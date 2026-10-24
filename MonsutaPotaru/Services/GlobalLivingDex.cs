using PKHeX.Core;
using MonsutaPotaru.Data;

namespace MonsutaPotaru.Services;

/// <summary>
/// Case du living dex global. Une forme y est reconnue par son nom anglais chez PKHeX (<paramref name="FormKey"/>, dans
/// <paramref name="Context"/>), pas par son numéro : un même numéro n'est pas la même forme d'un jeu à l'autre (Pikachu 1
/// est Rockeur en 6e génération, à casquette Originale ensuite).
/// </summary>
/// <param name="Form">Numéro de la forme dans <paramref name="Context"/> (pour l'image).</param>
/// <param name="AnyForm">Remplie par n'importe quelle forme de l'espèce (Prismillon en Complet).</param>
/// <param name="AnyOfKeys">Remplie par l'une de ces formes (Pikachu Cosplayeur en Complet : n'importe quel costume).</param>
/// <param name="Gender">Mode Ultime : mâle (0) ou femelle (1), pour les espèces dont la femelle est différente.</param>
/// <param name="Sweet">Mode Ultime : décoration de Charmilly (0 Fraise à 6 Ruban, <c>FormArgument</c> de PKHeX).</param>
/// <param name="HeldItem">
/// Forme de combat due à un objet tenu (Méga-Gemme, Gemme Rouge / Bleue, Épée Rouillée, Ultranécrozélite) : remplie par
/// un exemplaire <b>de plus</b> qui le tient.
/// </param>
/// <param name="Move">Méga-Rayquaza : un Rayquaza de plus qui connaît Draco-Ascension.</param>
/// <param name="Gigamax">Remplie par un exemplaire de plus qui a le facteur Gigamax.</param>
public sealed record GlobalSlot(ushort Species, byte Form, EntityContext Context, string FormKey, string Name, bool AnyForm = false,
    IReadOnlyList<string>? AnyOfKeys = null, byte? Gender = null, byte? Sweet = null, ushort HeldItem = 0, ushort Move = 0,
    bool Gigamax = false)
{
    /// <summary>Case qui demande un exemplaire de plus que celui de l'espèce (Méga, Gigamax...).</summary>
    public bool ExtraCopy => HeldItem != 0 || Move != 0 || Gigamax;
}

/// <summary>
/// Living dex global : compté sur le stockage interne du compte (pas les sauvegardes), sans jeu, avec les seuls Pokémon
/// non chromatiques (un chromatique est ignoré, même s'il pourrait remplir une case). National : les 1 025
/// espèces. Complet : toutes les cases Complet des living dex par jeu réunies (formes régionales, Méga, Gigamax, formes
/// d'aspect, avec les mêmes choix). Ultime : chaque forme qui tient en boîte dans un jeu pris en charge (1re à 9e
/// génération, sans Légendes Z-A), même celles au même aspect, les formes de combat dues à un objet tenu et les Gigamax
/// (avec un exemplaire de plus), les femelles différentes, Charmilly avec chaque crème et chaque décoration ; Spinda n'a
/// qu'une case (motifs infinis). Le living dex global chromatique a les mêmes cases, sans ce qui n'a jamais existé en
/// chromatique (<see cref="ShinyLocked"/>), et ne compte que les chromatiques.
/// </summary>
public static class GlobalLivingDex
{
    public const int MaxSpecies = 1025;

    public static readonly LivingDexGameInfo Game = new(LivingDexGame.Global, "Living dex global", "", null, 0, "", [], MaxSpecies);

    public static readonly LivingDexGameInfo ShinyGame =
        new(LivingDexGame.GlobalShiny, "Living dex global chromatique", "", null, 0, "", [], MaxSpecies);

    /// <summary>Living dex global, normal ou chromatique (sans jeu ni sauvegarde).</summary>
    public static bool IsGlobal(LivingDexGame game) => game is LivingDexGame.Global or LivingDexGame.GlobalShiny;

    public static readonly IReadOnlyList<LivingDexMode> Modes = [LivingDexMode.National, LivingDexMode.Complete, LivingDexMode.Ultimate];

    /// <summary>Image de la carte, à la place d'une jaquette : la Master Ball (objet n° 1), le Charme Chroma (n° 632) pour le chromatique.</summary>
    public static int CoverItem(LivingDexGame game) => game == LivingDexGame.GlobalShiny ? 632 : 1;

    public static IReadOnlyList<GlobalSlot> Slots(LivingDexMode mode, bool shiny = false) => (mode, shiny) switch
    {
        (LivingDexMode.Complete, false) => CompleteSlots.Value,
        (LivingDexMode.Ultimate, false) => UltimateSlots.Value,
        (_, false) => NationalSlots.Value,
        (LivingDexMode.Complete, true) => ShinyComplete.Value,
        (LivingDexMode.Ultimate, true) => ShinyUltimate.Value,
        _ => ShinyNational.Value,
    };

    /// <summary>
    /// Espèces jamais obtenables en chromatique dans un jeu pris en charge (« shiny lock » ; liste de Bulbapedia, « List
    /// of unobtainable Shiny Pokémon », distributions et cadeaux de HOME compris) : Victini, Hoopa, Volcanion (le
    /// chromatique de HOME vient de Légendes Z-A, qui ne transfère pas vers Épée / Bouclier ni Écarlate / Violet), Cosmog,
    /// Cosmovum, Magearna, Marshadow, Wushours, Shifours, Zarude, Blizzeval, Spectreval, Sylveroy, et les légendaires et
    /// Paradoxes de la 9e génération donnés une seule fois (Serpente-Eau à Pêchaminus).
    /// </summary>
    public static readonly ushort[] ShinyLockedSpecies =
        [494, 720, 721, 789, 790, 801, 802, 891, 892, 893, 896, 897, 898, 1009, 1010, 1014, 1015, 1016, 1017, 1020, 1021, 1022, 1023, 1024, 1025];

    /// <summary>
    /// Case jamais obtenable en chromatique : espèce de <see cref="ShinyLockedSpecies"/>, ou forme qui ne l'a jamais été
    /// (costumes et casquettes de Pikachu, sauf la Partenaire ; Pikachu et Évoli Partenaires ; Pichu Troizépi ; Amphinobi
    /// Lien Naturel ; motif Poké Ball ; Ursaking Lune Vermeille ; Melmetal Gigamax ; Pokémon Dominants, cadeaux fixes).
    /// </summary>
    public static bool ShinyLocked(GlobalSlot slot) =>
        ShinyLockedSpecies.Contains(slot.Species)
        || slot.Species == Pikachu && (slot.AnyOfKeys is not null || slot.Context == EntityContext.Gen6 && slot.Form > 0
                                       || slot.FormKey is "Original" or "Hoenn" or "Sinnoh" or "Unova" or "Kalos" or "Alola" or "World")
        || slot.FormKey == "Starter" && slot.Species is Pikachu or Eevee
        || slot.Species == 172 && slot.FormKey == "Spiky"
        || slot.Species == Greninja && slot.FormKey == "Ash"
        || slot.Species is Scatterbug or Spewpa or Vivillon && slot.FormKey == "Poké Ball"
        || slot.Species == 901 && slot.FormKey == "Bloodmoon"
        || slot.Species == 809 && slot.Gigamax
        || slot.FormKey == "Large";

    private static readonly Lazy<IReadOnlyList<GlobalSlot>> ShinyNational = new(() => NationalSlots.Value.Where(s => !ShinyLocked(s)).ToList());
    private static readonly Lazy<IReadOnlyList<GlobalSlot>> ShinyComplete = new(() => CompleteSlots.Value.Where(s => !ShinyLocked(s)).ToList());
    private static readonly Lazy<IReadOnlyList<GlobalSlot>> ShinyUltimate = new(() => UltimateSlots.Value.Where(s => !ShinyLocked(s)).ToList());

    private const ushort Pikachu = 25, Eevee = 133, Raticate = 20, Marowak = 105, Mothim = 414, Arceus = 493, Genesect = 649,
        Greninja = 658, Floette = 670, Furfrou = 676, Zygarde = 718, Rockruff = 744, Silvally = 773, Mimikyu = 778, Necrozma = 800,
        Toxtricity = 849, Alcremie = 869, Zacian = 888, Zamazenta = 889, Koraidon = 1007, Miraidon = 1008, Scatterbug = 664,
        Spewpa = 665, Vivillon = 666;

    /// <summary>Formes de combat dues à un objet tenu : Zacian et Zamazenta Suprêmes, Ultra-Necrozma.</summary>
    private static readonly Dictionary<(ushort Species, byte Form), (ushort Item, string Name)> HeldBattleForms = new()
    {
        [(Zacian, 1)] = (1103, "Zacian Suprême"), // Épée Rouillée
        [(Zamazenta, 1)] = (1104, "Zamazenta Suprême"), // Bouclier Rouillé
        [(Necrozma, 3)] = (923, "Ultra-Necrozma"), // Ultranécrozélite (Necrozma fusionné)
    };

    /// <summary>
    /// Espèces dont la femelle a un autre aspect (forme de base) : repris des données de PokéAPI (pokemon_species.csv,
    /// has_gender_differences), sans Mistigrix, Wimessir, Paragruel et Fragroin, dont le sexe est une forme chez PKHeX.
    /// </summary>
    public static readonly ushort[] GenderDifferences =
    [
        3, 12, 19, 20, 25, 26, 41, 42, 44, 45, 64, 65, 84, 85, 97, 111, 112, 118, 119, 123, 129, 130, 133, 154, 165,
        166, 178, 185, 186, 190, 194, 195, 198, 202, 203, 207, 208, 212, 214, 215, 217, 221, 224, 229, 232, 255, 256, 257, 267, 269,
        272, 274, 275, 307, 308, 315, 316, 317, 322, 323, 332, 350, 369, 396, 397, 398, 399, 400, 401, 402, 403, 404, 405, 407, 415,
        417, 418, 419, 424, 443, 444, 445, 449, 450, 453, 454, 456, 457, 459, 460, 461, 464, 465, 473, 521, 592, 593, 668,
    ];

    /// <summary>Décorations de Charmilly, dans l'ordre de PKHeX (<c>FormArgument</c>) : noms français et noms de fichier.</summary>
    private static readonly (string Name, string File)[] Sweets =
        [("Fraise", "strawberry"), ("Baie", "berry"), ("Cœur", "love"), ("Étoile", "star"), ("Trèfle", "clover"), ("Fleur", "flower"), ("Ruban", "ribbon")];

    /// <summary>Crèmes de Charmilly (PKHeX abrège en français : deux « Ruby »).</summary>
    private static readonly string[] Creams =
        ["Lait Vanille", "Lait Ruby", "Lait Matcha", "Lait Menthe", "Lait Citron", "Lait Salé", "Mélange Ruby", "Mélange Caramel", "Mélange Tricolore"];

    /// <summary>
    /// Boîtes des jeux pris en charge, une table de PKHeX par jeu (le nombre de formes change : Motisma n'en a qu'une en
    /// Diamant / Perle). À partir de la 8e génération, PKHeX dit aussi quelles formes existent dans le jeu.
    /// </summary>
    private static readonly (IPersonalTable Table, EntityContext Context, byte Generation)[] Games =
    [
        (PersonalTable.C, EntityContext.Gen2, 2), (PersonalTable.E, EntityContext.Gen3, 3), (PersonalTable.DP, EntityContext.Gen4, 4),
        (PersonalTable.Pt, EntityContext.Gen4, 4), (PersonalTable.HGSS, EntityContext.Gen4, 4), (PersonalTable.BW, EntityContext.Gen5, 5),
        (PersonalTable.B2W2, EntityContext.Gen5, 5), (PersonalTable.XY, EntityContext.Gen6, 6), (PersonalTable.AO, EntityContext.Gen6, 6),
        (PersonalTable.SM, EntityContext.Gen7, 7), (PersonalTable.USUM, EntityContext.Gen7, 7), (PersonalTable.GG, EntityContext.Gen7b, 7),
        (PersonalTable.SWSH, EntityContext.Gen8, 8), (PersonalTable.LA, EntityContext.Gen8a, 8), (PersonalTable.BDSP, EntityContext.Gen8b, 8),
        (PersonalTable.SV, EntityContext.Gen9, 9),
    ];

    private static readonly GameStrings English = GameInfo.GetStrings("en");
    private static readonly GameStrings French = GameInfo.GetStrings("fr");

    /// <summary>
    /// Nom anglais d'une forme chez PKHeX (« Alola », « Rock Star ») : la clé qui la reconnaît d'un jeu à l'autre. La forme
    /// de base s'appelle « Normal » (PKHeX ne la nomme pas dans un jeu où l'espèce n'a qu'une forme).
    /// </summary>
    public static string FormKey(ushort species, byte form, EntityContext context) =>
        FormName(species, form, context) is { Length: > 0 } name ? name : form == 0 ? "Normal" : $"{form}";

    /// <summary>Nom anglais de la forme chez PKHeX, null si PKHeX ne la nomme pas dans ce jeu (Dominants de Let's Go).</summary>
    private static string? FormName(ushort species, byte form, EntityContext context)
    {
        var names = FormConverter.GetFormList(species, English.types, English.forms, GameInfo.GenderSymbolASCII, context);
        return form < names.Length ? names[form] : null;
    }

    private static string FrenchForm(ushort species, byte form, EntityContext context)
    {
        var names = FormConverter.GetFormList(species, French.types, French.forms, GameInfo.GenderSymbolUnicode, context);
        return form < names.Length ? names[form] : $"{form}";
    }

    private static string SpeciesName(ushort species) => French.specieslist[species];

    private static readonly Lazy<IReadOnlyList<GlobalSlot>> NationalSlots = new(() =>
        Enumerable.Range(1, MaxSpecies).Select(n => (ushort)n)
            .Select(s => new GlobalSlot(s, 0, EntityContext.Gen9, FormKey(s, 0, EntityContext.Gen9), SpeciesName(s), AnyForm: true))
            .ToList());

    /// <summary>Clé d'une case : l'espèce, son genre de case (forme, Méga, Gigamax), la forme.</summary>
    private static (ushort, int, string) Key(GlobalSlot s) => (s.Species, s.ExtraCopy ? s.Gigamax ? 2 : 1 : 0, s.AnyOfKeys is null ? s.FormKey : "*");

    /// <summary>
    /// Cases Complet de chaque jeu, réunies : une même case (espèce, forme) n'est comptée qu'une fois ; « n'importe quelle
    /// forme » seulement si tous les jeux le disent (Hoopa : n'importe lequel en 6e, la forme exacte en 7e). Les conditions
    /// propres à une sauvegarde disparaissent : objet rare dans le sac (Shaymin Céleste en 4e), gemme dans le sac (Let's Go :
    /// la gemme tenue, comme ailleurs), conseils. Dans l'ordre du national, puis formes, Méga, Gigamax.
    /// </summary>
    private static readonly Lazy<IReadOnlyList<GlobalSlot>> CompleteSlots = new(() =>
    {
        var slots = new Dictionary<(ushort, int, string), (GlobalSlot Slot, int Order)>();
        foreach (var game in LivingDexGames.All)
        {
            var context = LivingDexGames.ContextOf(game);
            foreach (var s in LivingDexGames.Slots(game, LivingDexMode.Complete))
            {
                IReadOnlyList<string>? anyOf = s.FormsFrom == 0 ? null
                    : Enumerable.Range(s.FormsFrom, s.Form - s.FormsFrom + 1).Select(f => FormKey(s.Species, (byte)f, context)).ToList();
                var slot = new GlobalSlot(s.Species, s.Form, context, FormKey(s.Species, s.Form, context),
                    LivingDexGames.Name(game, s, LivingDexMode.Complete), s.AnyForm, anyOf, HeldItem: s.MegaItem, Move: s.MegaMove,
                    Gigamax: s.Gigamax);
                var key = Key(slot);
                if (!slots.TryGetValue(key, out var known))
                    slots[key] = (slot, slots.Count);
                else if (known.Slot.AnyForm && !slot.AnyForm && !slot.ExtraCopy)
                    slots[key] = (slot, known.Order); // la forme exacte l'emporte
            }
        }
        return Ordered(slots.Values);
    });

    private static List<GlobalSlot> Ordered(IEnumerable<(GlobalSlot Slot, int Order)> slots) =>
        slots.OrderBy(s => s.Slot.Species).ThenBy(s => Key(s.Slot).Item2).ThenBy(s => s.Order).Select(s => s.Slot).ToList();

    /// <summary>
    /// Mode Ultime : les cases du Complet, chaque forme y ayant sa case (plus de « n'importe quelle forme », sauf pour les
    /// Méga et le Gigamax de Charmilly), puis chaque autre forme qu'une boîte d'un jeu pris en charge peut garder.
    /// Exclues : les formes de combat sans objet tenu, les coupes de Couafarel (en équipe seulement), les formes de
    /// monture de Koraidon et Miraidon, Floette Éternelle (jamais distribuée), les formes Roi de Légendes Arceus
    /// (impossibles à capturer), Arceus « ??? » (sans Plaque en 4e génération). Mâle et femelle à part quand la femelle
    /// est différente ; Charmilly avec chaque décoration.
    /// </summary>
    private static readonly Lazy<IReadOnlyList<GlobalSlot>> UltimateSlots = new(() =>
    {
        var slots = new Dictionary<(ushort, int, string), (GlobalSlot Slot, int Order)>();
        void Add(GlobalSlot slot)
        {
            if (!slots.ContainsKey(Key(slot)))
                slots[Key(slot)] = (slot, slots.Count);
        }

        foreach (var s in CompleteSlots.Value)
        {
            if (s.AnyOfKeys is not null)
                continue; // Pikachu Cosplayeur : chaque costume a sa case plus bas
            if (s.Gigamax && s.Species == Toxtricity)
                continue; // les deux Gigamax de Salarsen, plus bas
            var exact = s.ExtraCopy ? s : s with { AnyForm = false };
            Add(exact with { Name = UltimateName(exact, s.Name) });
        }

        foreach (var (table, context, generation) in Games)
        {
            for (ushort species = 1; species <= Math.Min((int)table.MaxSpeciesID, MaxSpecies); species++)
            {
                for (byte form = 0; form < table[species].FormCount; form++)
                {
                    if (generation >= 8 && !table.IsPresentInGame(species, form))
                        continue;
                    if (form > 0 && FormName(species, form, context) is null)
                        continue; // forme sans nom dans ce jeu (Dominants de Let's Go) : elle n'y existe pas
                    var key = FormKey(species, form, context);
                    if (FormInfo.IsMegaForm(species, form) || FormInfo.IsPrimalForm(species, form) || Excluded(species, form, key))
                        continue; // Méga et Primo : déjà dans le Complet
                    if (HeldBattleForms.TryGetValue((species, form), out var held))
                        Add(new GlobalSlot(species, form, context, held.Name, held.Name, AnyForm: true, HeldItem: held.Item));
                    else if (!FormInfo.IsBattleOnlyForm(species, form, generation))
                        Add(new GlobalSlot(species, form, context, key, UltimateName(species, form, context, key)));
                }
            }
        }
        // Salarsen : un Gigamax par forme (même aspect).
        foreach (var form in new byte[] { 0, 1 })
            Add(new GlobalSlot(Toxtricity, form, EntityContext.Gen8, FormKey(Toxtricity, form, EntityContext.Gen8),
                $"{SpeciesName(Toxtricity)} Gigamax ({(form == 0 ? "forme Aigüe" : "forme Grave")})", Gigamax: true));

        // Mâle et femelle : la case de la forme de base se dédouble ; Charmilly : une case par crème et par décoration.
        var result = new List<(GlobalSlot Slot, int Order)>();
        foreach (var (slot, order) in slots.Values)
        {
            if (!slot.ExtraCopy && slot.Form == 0 && GenderDifferences.Contains(slot.Species))
            {
                var name = SpeciesName(slot.Species);
                result.Add((slot with { Gender = 0, Name = $"{name} ♂" }, order));
                result.Add((slot with { Gender = 1, Name = $"{name} ♀" }, order));
            }
            else if (!slot.ExtraCopy && slot.Species == Alcremie)
                result.AddRange(Sweets.Select((sweet, i) =>
                    (slot with { Sweet = (byte)i, Name = $"{SpeciesName(Alcremie)} ({Creams[Math.Min(slot.Form, (byte)8)]}, {sweet.Name})" }, order)));
            else
                result.Add((slot, order));
        }
        return Ordered(result);
    });

    /// <summary>Formes jamais dans une boîte, ou jamais obtenables (voir <see cref="UltimateSlots"/>).</summary>
    private static bool Excluded(ushort species, byte form, string key) => (species, form) switch
    {
        (Furfrou, > 0) => true,
        (Koraidon or Miraidon, > 0) => true,
        (Floette, 5) => true,
        (Greninja, 2) => true, // Amphinobi Sacha : en combat seulement
        (Arceus, _) when key == "???" => true,
        _ => key == "Lord",
    };

    /// <summary>Nom d'une case du Complet dans l'Ultime : celui de la forme quand l'espèce a plusieurs cases (« Prismillon (motif Blizzard) »).</summary>
    private static string UltimateName(GlobalSlot slot, string completeName)
    {
        if (slot.ExtraCopy || completeName != SpeciesName(slot.Species))
            return completeName;
        var french = FrenchForm(slot.Species, slot.Form, slot.Context);
        return french is "" or "Normal" ? completeName : UltimateName(slot.Species, slot.Form, slot.Context, slot.FormKey);
    }

    /// <summary>
    /// Nom d'une forme qui n'est pas dans le Complet : « Pikachu Rockeur », « Pikachu (casquette Originale) », « Prismillon
    /// (motif Banquise) », « Arceus (type Feu) », « Rattatac Dominant », « Mistigrix ♀ »...
    /// </summary>
    private static string UltimateName(ushort species, byte form, EntityContext context, string key)
    {
        var name = SpeciesName(species);
        var french = FrenchForm(species, form, context);
        if (key is "M" or "F")
            return $"{name} {(key == "M" ? "♂" : "♀")}";
        if (key == "Large")
            return $"{name} Dominant";
        if (key == "Starter")
            return $"{name} Partenaire";
        return (species, form) switch
        {
            (Pikachu, _) when context == EntityContext.Gen6 => $"{name} {french}", // costumes de Rubis Oméga / Saphir Alpha
            (Pikachu, _) => french.StartsWith("Casquette") ? $"{name} ({char.ToLower(french[0])}{french[1..]})" : $"{name} (casquette {french})",
            (Scatterbug or Spewpa or Vivillon, _) => $"{name} (motif {french})",
            (Arceus, _) when key == "Legend" => $"{name} (Plaque Légende)", // Légendes Arceus
            (Arceus or Silvally, _) => $"{name} (type {french})",
            (Genesect, _) => $"{name} (module {french})",
            (Mothim, _) => $"{name} (cape {french})",
            (Greninja, 1) => $"{name} (Lien Naturel)",
            (Zygarde, 2) => $"{name} (forme 10 %, Rassemblement)",
            (Zygarde, 3) => $"{name} (forme 50 %, Rassemblement)",
            (Rockruff, _) => $"{name} (forme Crépusculaire)",
            _ when french is "" or "Normal" => name,
            _ => $"{name} ({french})",
        };
    }

    /// <summary>Image d'une case : celle de la collection (forme, femelle, Méga par la gemme, Gigamax, décoration de Charmilly).</summary>
    public static string Sprite(GlobalSlot slot, bool shiny = false) => slot switch
    {
        { Gigamax: true } => PokemonSprites.GigamaxUrl(slot.Species, slot.Form, slot.Context, shiny),
        { HeldItem: not 0 } when HeldBattleForms.ContainsKey((slot.Species, slot.Form)) =>
            PokemonSprites.Url(slot.Species, slot.Form, slot.Context, female: false, heldItem: 0, shiny),
        { HeldItem: not 0 } => PokemonSprites.Url(slot.Species, 0, slot.Context, female: false, heldItem: slot.HeldItem, shiny),
        { Sweet: { } sweet } => PokemonSprites.AlcremieUrl(slot.Form, Sweets[sweet].File, shiny),
        _ => PokemonSprites.Url(slot.Species, slot.Form, slot.Context, female: slot.Gender == 1, heldItem: 0, shiny),
    };

    /// <summary>
    /// Clé de forme d'un Pokémon pour le Complet, qui suit les règles des jeux : un Pokémon Dominant compte pour sa forme
    /// ordinaire, Zygarde avec Rassemblement pour son pourcentage.
    /// </summary>
    public static string CompleteKey(ushort species, byte form, EntityContext context)
    {
        if (species == Zygarde && form is 2 or 3)
            form = (byte)(form == 2 ? 1 : 0);
        else if (FormInfo.IsTotemForm(species, form))
            form = FormInfo.GetTotemBaseForm(species, form);
        return FormKey(species, form, context);
    }

    /// <summary>Pokémon du stockage interne, vu par le living dex global.</summary>
    /// <param name="Sweet">Décoration (Charmilly), sinon null.</param>
    public sealed record Copy(int Id, ushort Species, byte Form, EntityContext Context, byte? Gender, int HeldItem, byte? Sweet,
        bool CanGigantamax, bool KnowsDragonAscent);

    /// <summary>
    /// Exemplaire de chaque case, chacun ne servant qu'une fois : d'abord les cases qui demandent un exemplaire de plus
    /// (Méga, Gigamax...), puis les formes exactes, puis les cases « n'importe quelle forme ».
    /// </summary>
    public static Copy?[] Assign(IReadOnlyList<GlobalSlot> slots, IReadOnlyList<Copy> copies, LivingDexMode mode)
    {
        var result = new Copy?[slots.Count];
        var used = new HashSet<int>();
        var keys = copies.ToDictionary(c => c.Id, c => mode == LivingDexMode.Complete
            ? CompleteKey(c.Species, c.Form, c.Context) : FormKey(c.Species, c.Form, c.Context));
        var bySpecies = copies.GroupBy(c => c.Species).ToDictionary(g => g.Key, g => g.ToList());
        void Pass(Func<GlobalSlot, bool> which, Func<GlobalSlot, Copy, bool> fills)
        {
            for (var i = 0; i < slots.Count; i++)
            {
                if (result[i] is not null || !which(slots[i]))
                    continue;
                var copy = bySpecies.GetValueOrDefault(slots[i].Species)?.FirstOrDefault(c => !used.Contains(c.Id) && fills(slots[i], c));
                if (copy is null)
                    continue;
                result[i] = copy;
                used.Add(copy.Id);
            }
        }
        Pass(s => s.ExtraCopy, (s, c) =>
            s.Gigamax ? c.CanGigantamax && (s.AnyForm || keys[c.Id] == s.FormKey)
            : s.Move != 0 ? c.KnowsDragonAscent
            : c.HeldItem == s.HeldItem);
        Pass(s => !s.ExtraCopy && !s.AnyForm, (s, c) =>
            (s.AnyOfKeys?.Contains(keys[c.Id]) ?? keys[c.Id] == s.FormKey)
            && (s.Gender is null || c.Gender == s.Gender) && (s.Sweet is null || c.Sweet == s.Sweet));
        Pass(s => !s.ExtraCopy && s.AnyForm, (_, _) => true);
        return result;
    }

    /// <summary>Infobulle d'une case à exemplaire de plus : « avec Dracaufite X » ; manquante, « il faut un Dracaufeu de plus avec... ».</summary>
    public static string? Note(GlobalSlot slot, bool filled)
    {
        if (!slot.ExtraCopy)
            return null;
        var condition = slot.Gigamax ? "avec le facteur Gigamax"
            : slot.Move != 0 ? $"qui connaît {French.movelist[slot.Move]}"
            : $"avec {French.itemlist[slot.HeldItem]}";
        return filled ? condition : $"il faut un {SpeciesName(slot.Species)} de plus {condition}";
    }
}
