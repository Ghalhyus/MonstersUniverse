using Microsoft.EntityFrameworkCore;
using PKHeX.Core;
using PokeBank.Data;

namespace PokeBank.Services;

/// <summary>Mode d'un living dex : quels Pokémon il demande.</summary>
public enum LivingDexMode
{
    /// <summary>Chaque Pokémon du Pokédex régional du jeu.</summary>
    Regional,
    /// <summary>Chaque Pokémon du Pokédex national que le jeu peut contenir.</summary>
    National,
    /// <summary>
    /// Chaque forme de chaque Pokémon du national : formes régionales, Méga-Évolutions, Gigamax, formes qui changent
    /// l'aspect (Sancoki / Tritosor, Zarbi...) ; pas le sexe. Seules les formes qu'une sauvegarde du jeu peut contenir
    /// comptent : aucune en 1re génération, les lettres de Zarbi en 2e et 3e (les formes de Morphéo n'existent qu'en
    /// combat ; Deoxys a la forme de son jeu, voir <see cref="LivingDexGameInfo.DeoxysForm"/>).
    /// </summary>
    Complete,
}

/// <summary>Un jeu de living dex : nom, jaquette, sprites, et Pokémon de chaque mode.</summary>
/// <param name="Cover">Jaquette officielle (outils/images.py, partie « livingdex »).</param>
/// <param name="SpriteFolder">Sprites du jeu, « images/livingdex/rouge-bleu/25.png » (Pikachu).</param>
/// <param name="Region">Région du Pokédex régional (« Kanto », « Johto », « Hoenn »).</param>
/// <param name="Regional">Pokédex régional (numéros nationaux, dans l'ordre du Pokédex du jeu).</param>
/// <param name="NationalMax">Dernier numéro national que le jeu peut contenir.</param>
/// <param name="DeoxysForm">
/// Forme de Deoxys dans ce jeu (3e génération) : un Pokémon de 3e génération n'enregistre pas sa forme, c'est le jeu qui
/// la donne (Normale dans Rubis / Saphir, Attaque dans Rouge Feu, Défense dans Vert Feuille, Vitesse dans Émeraude). Une
/// sauvegarde ne peut donc avoir que celle-là : une seule case, remplie par n'importe quel Deoxys.
/// </param>
public sealed record LivingDexGameInfo(LivingDexGame Game, string Name, string Cover, string SpriteFolder, byte Generation,
    string Region, IReadOnlyList<ushort> Regional, ushort NationalMax, byte DeoxysForm = 0);

/// <summary>Une case du living dex : un Pokémon (et sa forme) demandé par le mode.</summary>
/// <param name="AnyForm">Remplie par n'importe quelle forme de l'espèce, même en Complet (Deoxys de 3e génération).</param>
public sealed record LivingDexSlot(ushort Species, byte Form, bool AnyForm = false);

/// <summary>Case du living dex avec le Pokémon de la sauvegarde qui la remplit, s'il y en a un.</summary>
/// <param name="Number">Numéro affiché : celui du Pokédex régional en mode Régional (Germignon : 1 à Johto), sinon le national.</param>
/// <param name="Name">« Pikachu », « Zarbi B » (Complet), « Deoxys (forme Vitesse) » (Émeraude).</param>
/// <param name="Sprite">Sprite du jeu, propre à la forme.</param>
/// <param name="BankId">Pokémon de la sauvegarde (sa fiche), null : manquant.</param>
/// <param name="Location">« Boîte 3, case 12 », « Équipe 2 ».</param>
/// <param name="Fallback">Image de la même forme sur le site, si le sprite du jeu manque (images pas encore récupérées).</param>
public sealed record LivingDexEntry(int Number, ushort Species, byte Form, string Name, string Sprite, int? BankId, string? Location,
    string Fallback = "")
{
    public bool Owned => BankId is not null;
}

public sealed record LivingDexSummary(int Id, LivingDexGameInfo Game, string SaveLabel, int Owned, int Total);

public sealed record LivingDexView(int Id, LivingDexGameInfo Game, string SaveLabel, IReadOnlyList<LivingDexMode> Modes,
    LivingDexMode Mode, IReadOnlyList<LivingDexEntry> Entries)
{
    public int Owned => Entries.Count(e => e.Owned);
}

/// <summary>Sauvegarde stockée qui peut recevoir un living dex, et les jeux possibles (Rouge ou Bleu, Or ou Argent : au choix).</summary>
public sealed record LivingDexCandidate(int SaveId, string Label, IReadOnlyList<LivingDexGame> Games);

public sealed record LivingDexResult(bool Ok, string Message, int? Id = null);

/// <summary>Jeux qui ont un living dex : 1re à 3e génération pour l'instant.</summary>
public static class LivingDexGames
{
    private static readonly ushort[] Kanto = Enumerable.Range(1, 151).Select(n => (ushort)n).ToArray();

    /// <summary>
    /// Pokédex de Johto d'Or, Argent et Cristal : les 251 Pokémon, dans l'ordre du jeu (Germignon d'abord, Mew et Celebi à
    /// la fin). Repris des données de PokéAPI (pokemon_dex_numbers.csv, Pokédex 3 « original-johto »).
    /// </summary>
    private static readonly ushort[] Johto =
    [
        152, 153, 154, 155, 156, 157, 158, 159, 160, 16, 17, 18, 21, 22, 163, 164, 19, 20, 161, 162, 172, 25, 26, 10, 11,
        12, 13, 14, 15, 165, 166, 167, 168, 74, 75, 76, 41, 42, 169, 173, 35, 36, 174, 39, 40, 175, 176, 27, 28, 23,
        24, 206, 179, 180, 181, 194, 195, 92, 93, 94, 201, 95, 208, 69, 70, 71, 187, 188, 189, 46, 47, 60, 61, 62, 186,
        129, 130, 118, 119, 79, 80, 199, 43, 44, 45, 182, 96, 97, 63, 64, 65, 132, 204, 205, 29, 30, 31, 32, 33, 34,
        193, 191, 192, 102, 103, 185, 202, 48, 49, 123, 212, 127, 214, 109, 110, 88, 89, 81, 82, 100, 101, 190, 209, 210, 37,
        38, 58, 59, 234, 183, 184, 50, 51, 56, 57, 52, 53, 54, 55, 66, 67, 68, 236, 106, 107, 237, 203, 128, 241, 240,
        126, 238, 124, 239, 125, 122, 235, 83, 177, 178, 211, 72, 73, 98, 99, 213, 120, 121, 90, 91, 222, 223, 224, 170, 171,
        86, 87, 108, 114, 133, 134, 135, 136, 196, 197, 116, 117, 230, 207, 225, 220, 221, 216, 217, 231, 232, 226, 227, 84, 85,
        77, 78, 104, 105, 115, 111, 112, 198, 228, 229, 218, 219, 215, 200, 137, 233, 113, 242, 131, 138, 139, 140, 141, 142, 143,
        1, 2, 3, 4, 5, 6, 7, 8, 9, 144, 145, 146, 243, 244, 245, 147, 148, 149, 246, 247, 248, 249, 250, 150, 151,
        251,
    ];

    private const ushort Unown = 201, Deoxys = 386;

    /// <summary>
    /// Pokédex de Hoenn de Rubis, Saphir et Émeraude : 202 Pokémon, dans l'ordre du jeu (Arcko d'abord, Jirachi et Deoxys
    /// à la fin). Repris des données de PokéAPI (pokemon_dex_numbers.csv, Pokédex 4 « hoenn »).
    /// </summary>
    private static readonly ushort[] Hoenn =
    [
        252, 253, 254, 255, 256, 257, 258, 259, 260, 261, 262, 263, 264, 265, 266, 267, 268, 269, 270, 271, 272, 273, 274, 275, 276,
        277, 278, 279, 280, 281, 282, 283, 284, 285, 286, 287, 288, 289, 63, 64, 65, 290, 291, 292, 293, 294, 295, 296, 297, 118,
        119, 129, 130, 298, 183, 184, 74, 75, 76, 299, 300, 301, 41, 42, 169, 72, 73, 302, 303, 304, 305, 306, 66, 67, 68,
        307, 308, 309, 310, 311, 312, 81, 82, 100, 101, 313, 314, 43, 44, 45, 182, 84, 85, 315, 316, 317, 318, 319, 320,
        321, 322, 323, 218, 219, 324, 88, 89, 109, 110, 325, 326, 27, 28, 327, 227, 328, 329, 330, 331, 332, 333, 334, 335,
        336, 337, 338, 339, 340, 341, 342, 343, 344, 345, 346, 347, 348, 174, 39, 40, 349, 350, 351, 120, 121, 352, 353, 354,
        355, 356, 357, 358, 359, 37, 38, 172, 25, 26, 54, 55, 360, 202, 177, 178, 203, 231, 232, 127, 214, 111, 112, 361,
        362, 363, 364, 365, 366, 367, 368, 369, 222, 170, 171, 370, 116, 117, 230, 371, 372, 373, 374, 375, 376, 377, 378, 379,
        380, 381, 382, 383, 384, 385, 386,
    ];

    // Dossiers et fichiers : à garder en accord avec outils/images.py (LIVINGDEX_SPRITES, LIVINGDEX_COVERS).
    public static readonly IReadOnlyList<LivingDexGameInfo> All =
    [
        new(LivingDexGame.Red, "Pokémon Rouge", "images/livingdex/jaquettes/rouge.png", "images/livingdex/rouge-bleu/", 1, "Kanto", Kanto, 151),
        new(LivingDexGame.Blue, "Pokémon Bleu", "images/livingdex/jaquettes/bleu.png", "images/livingdex/rouge-bleu/", 1, "Kanto", Kanto, 151),
        new(LivingDexGame.Yellow, "Pokémon Jaune", "images/livingdex/jaquettes/jaune.png", "images/livingdex/jaune/", 1, "Kanto", Kanto, 151),
        new(LivingDexGame.Gold, "Pokémon Or", "images/livingdex/jaquettes/or.png", "images/livingdex/or/", 2, "Johto", Johto, 251),
        new(LivingDexGame.Silver, "Pokémon Argent", "images/livingdex/jaquettes/argent.png", "images/livingdex/argent/", 2, "Johto", Johto, 251),
        new(LivingDexGame.Crystal, "Pokémon Cristal", "images/livingdex/jaquettes/cristal.png", "images/livingdex/cristal/", 2, "Johto", Johto, 251),
        // 3e génération : Rubis et Saphir ont les mêmes sprites, Rouge Feu et Vert Feuille aussi (Deoxys à part).
        new(LivingDexGame.Ruby, "Pokémon Rubis", "images/livingdex/jaquettes/rubis.png", "images/livingdex/rubis-saphir/", 3, "Hoenn", Hoenn, 386),
        new(LivingDexGame.Sapphire, "Pokémon Saphir", "images/livingdex/jaquettes/saphir.png", "images/livingdex/rubis-saphir/", 3, "Hoenn", Hoenn, 386),
        new(LivingDexGame.Emerald, "Pokémon Émeraude", "images/livingdex/jaquettes/emeraude.png", "images/livingdex/emeraude/", 3, "Hoenn", Hoenn, 386,
            DeoxysForm: 3),
        new(LivingDexGame.FireRed, "Pokémon Rouge Feu", "images/livingdex/jaquettes/rouge-feu.png", "images/livingdex/rouge-feu-vert-feuille/", 3,
            "Kanto", Kanto, 386, DeoxysForm: 1),
        new(LivingDexGame.LeafGreen, "Pokémon Vert Feuille", "images/livingdex/jaquettes/vert-feuille.png", "images/livingdex/rouge-feu-vert-feuille/", 3,
            "Kanto", Kanto, 386, DeoxysForm: 2),
    ];

    public static LivingDexGameInfo Get(LivingDexGame game) => All.Single(g => g.Game == game);

    /// <summary>
    /// Jeux possibles pour une sauvegarde : PKHeX reconnaît Jaune, Cristal et Émeraude, mais pas Rouge de Bleu, Or d'Argent,
    /// Rubis de Saphir ni Rouge Feu de Vert Feuille (même format) : au joueur de choisir. Pour Rouge Feu / Vert Feuille,
    /// PKHeX répond « Rouge Feu » faute de mieux : les deux sont toujours proposés. Aucun pour une sauvegarde d'une autre
    /// génération, ni pour Colosseum / XD.
    /// </summary>
    public static IReadOnlyList<LivingDexGame> For(SaveFile sav) => sav.Generation switch
    {
        1 => sav.Version switch
        {
            GameVersion.YW => [LivingDexGame.Yellow],
            GameVersion.RD => [LivingDexGame.Red],
            GameVersion.BU or GameVersion.GN => [LivingDexGame.Blue], // Bleu japonais ou Vert : les sprites de Rouge / Bleu
            _ => [LivingDexGame.Red, LivingDexGame.Blue], // RB (ou version inconnue)
        },
        2 => sav.Version switch
        {
            GameVersion.C => [LivingDexGame.Crystal],
            GameVersion.GD => [LivingDexGame.Gold],
            GameVersion.SI => [LivingDexGame.Silver],
            GameVersion.GS => [LivingDexGame.Gold, LivingDexGame.Silver],
            _ => [LivingDexGame.Gold, LivingDexGame.Silver, LivingDexGame.Crystal], // version inconnue
        },
        3 => sav.Version switch
        {
            GameVersion.E => [LivingDexGame.Emerald],
            GameVersion.R => [LivingDexGame.Ruby],
            GameVersion.S => [LivingDexGame.Sapphire],
            GameVersion.RS => [LivingDexGame.Ruby, LivingDexGame.Sapphire],
            GameVersion.FR or GameVersion.LG or GameVersion.FRLG => [LivingDexGame.FireRed, LivingDexGame.LeafGreen],
            _ => [], // Colosseum, XD
        },
        _ => [],
    };

    /// <summary>
    /// Pokémon demandés par un mode : en Régional et National, un par espèce ; en Complet, chaque forme qu'une sauvegarde
    /// du jeu peut contenir (voir <see cref="LivingDexMode.Complete"/>), dans l'ordre du national puis des formes.
    /// </summary>
    public static IReadOnlyList<LivingDexSlot> Slots(LivingDexGameInfo game, LivingDexMode mode)
    {
        var national = Enumerable.Range(1, game.NationalMax).Select(n => (ushort)n);
        return mode switch
        {
            LivingDexMode.Regional => game.Regional.Select(n => Base(game, n)).ToList(),
            LivingDexMode.National => national.Select(n => Base(game, n)).ToList(),
            _ => national.SelectMany(n => Forms(game, n)).ToList(),
        };
    }

    /// <summary>Case d'une espèce : sa forme de base, sauf Deoxys en 3e génération (la forme de son jeu).</summary>
    private static LivingDexSlot Base(LivingDexGameInfo game, ushort species) =>
        species == Deoxys && game.Generation == 3 ? new(species, game.DeoxysForm, AnyForm: true) : new(species, 0);

    /// <summary>
    /// Formes d'une espèce en mode Complet, réduites à celles qu'une sauvegarde du jeu peut contenir : les lettres de Zarbi
    /// (26 en 2e génération, A à Z ; 28 en 3e, avec « ! » et « ? »), enregistrées dans chaque Zarbi. Rien d'autre jusqu'à
    /// la 3e génération : ni forme régionale, ni Méga-Évolution, ni Gigamax ; les formes de Morphéo n'existent qu'en combat,
    /// Deoxys a celle de son jeu (<see cref="Base"/>).
    /// </summary>
    private static IEnumerable<LivingDexSlot> Forms(LivingDexGameInfo game, ushort species) =>
        species == Unown && UnownForms(game) > 1
            ? Enumerable.Range(0, UnownForms(game)).Select(f => new LivingDexSlot(species, (byte)f))
            : [Base(game, species)];

    private static int UnownForms(LivingDexGameInfo game) => game.Generation switch { 2 => 26, 3 => 28, _ => 1 };

    /// <summary>La case est remplie par ce Pokémon : même espèce ; en Complet, même forme (sauf <see cref="LivingDexSlot.AnyForm"/>).</summary>
    public static bool Fills(LivingDexSlot slot, ushort species, byte form, LivingDexMode mode) =>
        species == slot.Species && (mode != LivingDexMode.Complete || slot.AnyForm || form == slot.Form);

    /// <summary>
    /// Sprite du jeu d'une case : « 201.png » (Zarbi A), « 201-b.png », « 201-question.png », « 386-speed.png » (Deoxys
    /// Vitesse) : noms de PokéAPI, à garder en accord avec outils/images.py (LIVINGDEX_FORMS).
    /// </summary>
    public static string Sprite(LivingDexGameInfo game, LivingDexSlot slot) =>
        $"{game.SpriteFolder}{slot.Species}{FormSuffix(slot)}.png";

    private static string FormSuffix(LivingDexSlot slot) => (slot.Species, slot.Form) switch
    {
        (_, 0) => "",
        (Unown, 26) => "-exclamation",
        (Unown, 27) => "-question",
        (Unown, var letter) => $"-{(char)('a' + letter)}",
        (Deoxys, 1) => "-attack",
        (Deoxys, 2) => "-defense",
        (Deoxys, 3) => "-speed",
        _ => $"-{slot.Form}",
    };

    /// <summary>
    /// Nom d'une case : celui de l'espèce, avec la lettre de Zarbi en Complet (« Zarbi B ») et la forme de Deoxys quand ce
    /// n'est pas la Normale (« Deoxys (forme Vitesse) »). Noms de formes de PKHeX, en français.
    /// </summary>
    public static string Name(LivingDexGameInfo game, LivingDexSlot slot, LivingDexMode mode)
    {
        var species = FrenchStrings.specieslist[slot.Species];
        var forms = FormConverter.GetFormList(slot.Species, FrenchStrings.types, FrenchStrings.forms, GameInfo.GenderSymbolUnicode, Context(game));
        var form = slot.Form < forms.Length ? forms[slot.Form] : $"{slot.Form}";
        return slot.Species switch
        {
            Unown when mode == LivingDexMode.Complete && UnownForms(game) > 1 => $"{species} {form}",
            Deoxys when slot.Form != 0 => $"{species} (forme {form})",
            _ => species,
        };
    }

    /// <summary>Image de la même forme sur le site (formes des jeux récents), si le sprite du jeu manque.</summary>
    public static string Fallback(LivingDexGameInfo game, LivingDexSlot slot) =>
        PokemonSprites.Url(slot.Species, slot.Form, Context(game), female: false, heldItem: 0, shiny: false);

    private static EntityContext Context(LivingDexGameInfo game) => game.Generation switch
    {
        1 => EntityContext.Gen1,
        2 => EntityContext.Gen2,
        _ => EntityContext.Gen3,
    };

    private static readonly GameStrings FrenchStrings = GameInfo.GetStrings("fr");

    /// <summary>
    /// Modes proposés, dans l'ordre Régional, National, Complet : un mode qui demande exactement les mêmes Pokémon
    /// qu'un autre, dans le même ordre, est caché. En 1re génération, il ne reste que le National ; en 2e, le Pokédex de
    /// Johto a les mêmes 251 Pokémon que le national mais dans un autre ordre, et Zarbi ses 26 lettres : les trois modes ;
    /// en 3e, le Pokédex de Hoenn ou de Kanto, le national et les 28 lettres de Zarbi : les trois.
    /// </summary>
    public static IReadOnlyList<LivingDexMode> Modes(LivingDexGameInfo game)
    {
        var national = Slots(game, LivingDexMode.National);
        var modes = new List<LivingDexMode>();
        if (!Slots(game, LivingDexMode.Regional).SequenceEqual(national))
            modes.Add(LivingDexMode.Regional);
        modes.Add(LivingDexMode.National);
        if (!Slots(game, LivingDexMode.Complete).SequenceEqual(national))
            modes.Add(LivingDexMode.Complete);
        return modes;
    }

    public static string ModeName(LivingDexMode mode) => mode switch
    {
        LivingDexMode.Regional => "Régional",
        LivingDexMode.National => "National",
        _ => "Complet",
    };
}

/// <summary>
/// Living dex : un exemplaire de chaque Pokémon du Pokédex d'un jeu, compté dans une sauvegarde stockée (équipe et
/// boîtes, telle qu'envoyée sur le site). Au plus un par jeu et par compte ; supprimé avec sa sauvegarde.
/// </summary>
public sealed class LivingDexService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    ILogger<LivingDexService> logger)
{
    public async Task<List<LivingDexSummary>> ListAsync(string ownerId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var dexes = await db.LivingDexes.AsNoTracking().Where(d => d.OwnerId == ownerId)
            .Select(d => new { d.Id, d.Game, d.StoredSaveId, d.StoredSave!.TrainerName, d.StoredSave.FileName })
            .ToListAsync();
        var summaries = new List<LivingDexSummary>();
        foreach (var d in dexes.OrderBy(d => d.Game))
        {
            var game = LivingDexGames.Get(d.Game);
            var slots = LivingDexGames.Slots(game, LivingDexMode.National);
            var owned = await OwnedAsync(db, d.StoredSaveId);
            summaries.Add(new(d.Id, game, SaveLabel(d.TrainerName, d.FileName),
                slots.Count(s => Find(owned, s, LivingDexMode.National) is not null), slots.Count));
        }
        return summaries;
    }

    /// <summary>Sauvegardes stockées qui peuvent recevoir un living dex : d'un jeu qui en a, pas encore liées, avec un jeu libre.</summary>
    public async Task<List<LivingDexCandidate>> ListCandidatesAsync(string ownerId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var taken = await db.LivingDexes.Where(d => d.OwnerId == ownerId).Select(d => d.Game).ToListAsync();
        var linked = db.LivingDexes.Select(d => d.StoredSaveId);
        var saves = await db.StoredSaves.AsNoTracking()
            .Where(s => s.OwnerId == ownerId && s.Generation <= 3 && !linked.Contains(s.Id))
            .OrderBy(s => s.UploadedAt)
            .Select(s => new { s.Id, s.GameName, s.TrainerName, s.FileName, s.Data })
            .ToListAsync();
        var candidates = new List<LivingDexCandidate>();
        foreach (var s in saves)
        {
            if (!StoredSaveService.TryRead(s.Data, s.FileName, out var sav))
                continue;
            var games = LivingDexGames.For(sav).Except(taken).ToList();
            if (games.Count > 0)
                candidates.Add(new(s.Id, $"{s.GameName} — {SaveLabel(s.TrainerName, s.FileName)}", games));
        }
        return candidates;
    }

    public async Task<LivingDexResult> CreateAsync(string ownerId, int saveId, LivingDexGame game)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var save = await db.StoredSaves.AsNoTracking().Where(s => s.Id == saveId && s.OwnerId == ownerId)
            .Select(s => new { s.FileName, s.Data }).FirstOrDefaultAsync();
        if (save is null)
            return new(false, "Cette sauvegarde n'existe plus.");
        var name = LivingDexGames.Get(game).Name;
        if (!StoredSaveService.TryRead(save.Data, save.FileName, out var sav) || !LivingDexGames.For(sav).Contains(game))
            return new(false, $"Cette sauvegarde n'est pas une partie de {name}.");
        if (await db.LivingDexes.AnyAsync(d => d.OwnerId == ownerId && d.Game == game))
            return new(false, $"Tu as déjà un living dex {name} : supprime-le pour en créer un autre.");
        if (await db.LivingDexes.AnyAsync(d => d.StoredSaveId == saveId))
            return new(false, "Cette sauvegarde a déjà un living dex.");

        var dex = new LivingDex { OwnerId = ownerId, Game = game, StoredSaveId = saveId, CreatedAt = DateTime.UtcNow };
        db.LivingDexes.Add(dex);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException) // créé au même moment depuis un autre onglet (index uniques)
        {
            return new(false, $"Tu as déjà un living dex {name}, ou cette sauvegarde en a déjà un.");
        }
        logger.LogInformation("Utilisateur {User} : living dex {Game} créé sur la sauvegarde {Save}.", ownerId, game, saveId);
        return new(true, $"Living dex {name} créé.", dex.Id);
    }

    /// <summary>Living dex dans un mode (le National si celui demandé n'est pas proposé pour ce jeu), null s'il n'existe pas.</summary>
    public async Task<LivingDexView?> GetAsync(string ownerId, int id, LivingDexMode mode = LivingDexMode.National)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var dex = await db.LivingDexes.AsNoTracking().Where(d => d.Id == id && d.OwnerId == ownerId)
            .Select(d => new { d.Id, d.Game, d.StoredSaveId, d.StoredSave!.TrainerName, d.StoredSave.FileName })
            .FirstOrDefaultAsync();
        if (dex is null)
            return null;
        var game = LivingDexGames.Get(dex.Game);
        var modes = LivingDexGames.Modes(game);
        if (!modes.Contains(mode))
            mode = LivingDexMode.National;
        var owned = await OwnedAsync(db, dex.StoredSaveId);
        var entries = LivingDexGames.Slots(game, mode).Select((slot, index) =>
        {
            var copy = Find(owned, slot, mode);
            return new LivingDexEntry(mode == LivingDexMode.Regional ? index + 1 : slot.Species, slot.Species, slot.Form,
                LivingDexGames.Name(game, slot, mode), LivingDexGames.Sprite(game, slot), copy?.Id, copy?.Location,
                LivingDexGames.Fallback(game, slot));
        }).ToList();
        return new(dex.Id, game, SaveLabel(dex.TrainerName, dex.FileName), modes, mode, entries);
    }

    public async Task<bool> DeleteAsync(string ownerId, int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var deleted = await db.LivingDexes.Where(d => d.Id == id && d.OwnerId == ownerId).ExecuteDeleteAsync();
        if (deleted > 0)
            logger.LogInformation("Utilisateur {User} : living dex {Id} supprimé.", ownerId, id);
        return deleted > 0;
    }

    /// <summary>Living dex de chaque sauvegarde (nom du jeu), pour prévenir qu'il partira avec elle (« Supprimer »).</summary>
    public async Task<Dictionary<int, string>> NamesBySaveAsync(string ownerId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return (await db.LivingDexes.Where(d => d.OwnerId == ownerId).Select(d => new { d.StoredSaveId, d.Game }).ToListAsync())
            .ToDictionary(d => d.StoredSaveId, d => LivingDexGames.Get(d.Game).Name);
    }

    /// <summary>Pokémon de la sauvegarde qui remplit une case.</summary>
    private sealed record OwnedCopy(int Id, ushort Species, byte Form, string? Location);

    /// <summary>Pokémon de la sauvegarde (les œufs ne comptent pas), dans son ordre : équipe, puis boîtes.</summary>
    private static async Task<List<OwnedCopy>> OwnedAsync(ApplicationDbContext db, int saveId) =>
        await db.BankPokemon.AsNoTracking()
            .Where(p => p.StoredSaveId == saveId && !p.IsEgg)
            .OrderBy(p => p.Position)
            .Select(p => new OwnedCopy(p.Id, p.Species, p.Form, p.Location))
            .ToListAsync();

    /// <summary>
    /// Premier Pokémon de la sauvegarde qui remplit la case : la même espèce, sous n'importe quelle forme en Régional et
    /// National (un Raichu d'Alola est un Raichu), la forme exacte en Complet (<see cref="LivingDexGames.Fills"/>).
    /// </summary>
    private static OwnedCopy? Find(List<OwnedCopy> owned, LivingDexSlot slot, LivingDexMode mode) =>
        owned.FirstOrDefault(p => LivingDexGames.Fills(slot, p.Species, p.Form, mode));

    private static string SaveLabel(string trainer, string fileName) => $"{trainer} · {fileName}";
}
