using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using PKHeX.Core;

namespace MonsutaPotaru.Services;

/// <summary>
/// Choix de l'image d'un Pokémon parmi celles du site (wwwroot/images/pokemon, rassemblées par outils/images.py) :
/// forme (19-alola.png), Méga-Évolution ou Primo-Résurgence quand il tient sa gemme ou son orbe (6-mega-x.png), femelle
/// différente du mâle (femelle/25.png), chromatique (chromatique/...). Une image absente laisse place à celle de l'espèce.
/// </summary>
public static partial class PokemonSprites
{
    /// <summary>Images présentes (« 19-alola.png », « chromatique/femelle/25.png »), lues au démarrage par <see cref="Load"/>.</summary>
    private static IReadOnlySet<string> _files = new HashSet<string>();

    /// <summary>Lit la liste des images du site (dossier wwwroot) ; sans elle, chaque Pokémon a l'image de son espèce.</summary>
    public static void Load(string webRoot)
    {
        _webRoot = webRoot;
        var folder = Path.Combine(webRoot, "images", "pokemon");
        _files = Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, "*.png", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(folder, f).Replace('\\', '/')).ToHashSet()
            : new HashSet<string>();
        var forms = Path.Combine(webRoot, "images", "donjon", "formes.json");
        _donjonForms = File.Exists(forms)
            ? System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(File.ReadAllText(forms))!
                .ToDictionary(e => e.Key, e => e.Value.GroupBy(f => Compact(f.Key)).ToDictionary(g => g.Key, g => g.First().Value))
            : [];
    }

    private static string? _webRoot;

    /// <summary>
    /// Formes de SpriteCollab par espèce (« 0026 » → nom compacté « alola » → dossier « 0001 »), lues dans
    /// images/donjon/formes.json (écrit par outils/images.py).
    /// </summary>
    private static Dictionary<string, Dictionary<string, string>> _donjonForms = [];

    /// <summary>L'espèce a des sprites Donjon Mystère (64 espèces récentes n'en ont pas encore chez SpriteCollab).</summary>
    public static bool HasDonjonSprites(ushort species) => DonjonExists($"{species:0000}");

    /// <summary>
    /// Dossier des sprites Donjon Mystère d'un Pokémon (SpriteCollab : « images/donjon/0026/0001/ » Raichu d'Alola,
    /// « .../0001/0001/ » sa version chromatique, « 0025/0000/0000/0002/ » Pikachu femelle) : celui de sa forme, ou de sa
    /// Méga-Évolution s'il tient sa gemme (comme son image dans la collection), sinon celui de l'espèce ; chromatique et
    /// femelle quand ils existent.
    /// </summary>
    public static string DonjonFolder(ushort species, byte form, EntityContext context, bool shiny, bool female, int heldItem)
    {
        var dex = $"{species:0000}";
        var (shown, names) = MegaForm(species, form, heldItem) is { } mega ? (mega, MegaContext) : (form, context);
        var key = shown != 0 && FormName(species, shown, names) is { } name ? DonjonForm(dex, name) : null;
        string?[] candidates =
        [
            .. key is null ? [] : new[] { shiny && female ? $"{dex}/{key}/0001/0002" : null, shiny ? $"{dex}/{key}/0001" : null,
                female ? $"{dex}/{key}/0000/0002" : null, $"{dex}/{key}" },
            shiny && female ? $"{dex}/0000/0001/0002" : null, shiny ? $"{dex}/0000/0001" : null,
            female ? $"{dex}/0000/0000/0002" : null, dex,
        ];
        return $"images/donjon/{candidates.OfType<string>().FirstOrDefault(DonjonExists) ?? dex}/";
    }

    private static bool DonjonExists(string folder) =>
        _webRoot is not null && File.Exists(Path.Combine(_webRoot, "images", "donjon", folder, "AnimData.xml"));

    /// <summary>
    /// Dossier SpriteCollab d'une forme d'après son nom anglais chez PKHeX : le nom lui-même (« Mega X » ↔ « Mega_X »),
    /// ceux de PokéAPI (« ! » ↔ « Exclamation »), en retirant des mots à la fin (« Paldea Combat » ↔ « Paldea »), puis le
    /// seul nom de SpriteCollab qui commence ainsi (« Dusk » ↔ « Dusk_Mane », « Crowned » ↔ « Crowned_Sword »).
    /// </summary>
    private static string? DonjonForm(string dex, string formName)
    {
        if (!_donjonForms.TryGetValue(dex, out var forms))
            return null;
        var words = formName.TrimStart('*').Split([' ', '-'], StringSplitOptions.RemoveEmptyEntries);
        IEnumerable<string> names = [formName, .. Candidates(formName),
            .. Enumerable.Range(1, Math.Max(0, words.Length - 1)).Select(n => string.Join(' ', words[..^n]))];
        var compact = names.Select(Compact).Where(n => n.Length > 0).ToList();
        return compact.Select(n => forms.GetValueOrDefault(n)).FirstOrDefault(f => f is not null)
            ?? compact.Where(n => n.Length >= 3)
                .Select(n => forms.Where(f => f.Key.StartsWith(n, StringComparison.Ordinal)).ToList())
                .FirstOrDefault(m => m.Count == 1)?[0].Value;
    }

    /// <summary>Nom réduit à ses lettres et chiffres, en minuscules : « Mega_X » et « Mega X » → megax.</summary>
    private static string Compact(string name) => new(Slug(name).Where(char.IsLetterOrDigit).ToArray());

    public static string Url(PKM pk) =>
        Url(pk.Species, pk.Form, pk.Context, pk.Gender == 1, pk.HeldItem, pk.IsShiny, pk.IsEgg);

    /// <summary>Image d'un Pokémon enregistré (sexe et objet tenu inconnus tant que le démarrage ne les a pas remplis).</summary>
    public static string Url(Data.BankPokemon p) =>
        Url(p.Species, p.Form, (EntityContext)p.Context, p.Gender == 1, p.HeldItem ?? 0, p.IsShiny, p.IsEgg);

    /// <summary>
    /// Image d'un Pokémon. Un œuf garde l'image de son espèce (comme avant les formes). <paramref name="context"/> donne
    /// les noms des formes (ils changent d'un jeu à l'autre : Pikachu Cosplayeur en 6e génération, casquettes ensuite).
    /// </summary>
    public static string Url(ushort species, byte form, EntityContext context, bool female, int heldItem, bool shiny, bool egg = false)
    {
        var folder = shiny ? "chromatique/" : "";
        if (!egg)
        {
            var (shown, names) = MegaForm(species, form, heldItem) is { } mega ? (mega, MegaContext) : (form, context);
            // Même la forme 0 : l'image de l'espèce est celle de la forme par défaut de PokéAPI, pas toujours la même que
            // celle de PKHeX (Prismillon : Bocage chez PokéAPI, Blizzard chez PKHeX).
            if (FormName(species, shown, names) is { } name)
                foreach (var slug in Candidates(name))
                    if (_files.Contains($"{folder}{species}-{slug}.png"))
                        return $"images/pokemon/{folder}{species}-{slug}.png";
            if (female && shown == 0 && _files.Contains($"{folder}femelle/{species}.png"))
                return $"images/pokemon/{folder}femelle/{species}.png";
        }
        return $"images/pokemon/{folder}{species}.png";
    }

    /// <summary>
    /// Image Gigamax d'une forme (« 892-rapid-strike-gmax.png », Shifours Mille Poings), sinon celle de l'espèce
    /// (« 6-gmax.png ») ; sans image Gigamax, celle de la forme.
    /// </summary>
    public static string GigamaxUrl(ushort species, byte form, EntityContext context, bool shiny = false)
    {
        var folder = shiny ? "chromatique/" : "";
        if (FormName(species, form, context) is { } name)
            foreach (var slug in Candidates(name))
                if (_files.Contains($"{folder}{species}-{slug}-gmax.png"))
                    return $"images/pokemon/{folder}{species}-{slug}-gmax.png";
        return _files.Contains($"{folder}{species}-gmax.png") ? $"images/pokemon/{folder}{species}-gmax.png"
            : Url(species, form, context, female: false, heldItem: 0, shiny);
    }

    /// <summary>
    /// Image d'un Charmilly avec sa crème et sa décoration (« 869-ruby-cream-star-sweet.png ») ; sinon celle de sa crème
    /// (la décoration Fraise de Lait Vanille est l'image de l'espèce).
    /// </summary>
    public static string AlcremieUrl(byte cream, string sweet, bool shiny = false)
    {
        var folder = shiny ? "chromatique/" : "";
        if (FormName((ushort)Species.Alcremie, cream, EntityContext.Gen8) is { } name
            && _files.Contains($"{folder}{(ushort)Species.Alcremie}-{Slug(name)}-{sweet}-sweet.png"))
            return $"images/pokemon/{folder}{(ushort)Species.Alcremie}-{Slug(name)}-{sweet}-sweet.png";
        return Url((ushort)Species.Alcremie, cream, EntityContext.Gen8, female: false, heldItem: 0, shiny);
    }

    /// <summary>Jeu dont PKHeX donne toutes les Méga-Évolutions (Légendes Z-A, qui ajoute les siennes à celles de X/Y).</summary>
    private const EntityContext MegaContext = EntityContext.Gen9a;

    /// <summary>
    /// Forme méga-évoluée (ou Primo-Résurgence) que prend le Pokémon avec l'objet qu'il tient : celle dont c'est la gemme
    /// (Dracaufite X → Méga-Dracaufeu X). Pour une espèce à plusieurs formes ayant la même gemme (Mistigrix mâle et
    /// femelle, Magearna, Nigirigon), celle qui prolonge sa forme actuelle (« F » → « F-Mega »). Méga-Rayquaza n'a pas de
    /// gemme : jamais choisi. PKHeX ne marque comme méga qu'une des formes jumelles (« M-Mega », pas « F-Mega ») : la
    /// jumelle est retrouvée par son nom.
    /// </summary>
    public static byte? MegaForm(ushort species, byte form, int heldItem)
    {
        if (heldItem <= 0 || !FormInfo.HasMegaForm(species) && species is not ((ushort)Species.Kyogre or (ushort)Species.Groudon))
            return null;
        var names = FormNames(species, MegaContext);
        var matching = Enumerable.Range(0, names.Length).Select(f => (byte)f)
            .Where(f => (FormInfo.IsMegaForm(species, f) || FormInfo.IsPrimalForm(species, f))
                        && ItemStorage9ZA.GetExpectedMegaStoneOrPrimalOrb(species, f) == heldItem)
            .ToList();
        if (matching.Count == 0)
            return null;
        var current = form < names.Length ? names[form] : "";
        var dash = names[matching[0]].IndexOf('-');
        var twin = dash > 0 ? Array.IndexOf(names, current + names[matching[0]][dash..]) : -1; // « M-Mega » → « F-Mega »
        return twin > 0 ? (byte)twin : matching[0];
    }

    private static string? FormName(ushort species, byte form, EntityContext context)
    {
        var names = FormNames(species, context);
        return form < names.Length ? names[form] : null;
    }

    private static readonly GameStrings English = GameInfo.GetStrings("en");

    private static string[] FormNames(ushort species, EntityContext context) =>
        FormConverter.GetFormList(species, English.types, English.forms, GameInfo.GenderSymbolASCII, context);

    /// <summary>
    /// Noms de fichier possibles d'une forme, du plus probable au moins probable : le nom anglais de PKHeX mis en forme
    /// (« Alola » → alola, « Mega X » → mega-x), puis les noms que PokéAPI donne autrement. Le premier présent l'emporte.
    /// </summary>
    public static IEnumerable<string> Candidates(string formName)
    {
        var name = Slug(formName.TrimStart('*')); // « *Teal » : Ogerpon téracristallisé
        yield return name;
        if (Aliases.TryGetValue(formName.TrimStart('*'), out var aliases))
            foreach (var alias in aliases)
                yield return alias;
        var meteor = MinorPattern().Match(name); // Météno : « C-Blue » (noyau) → blue, « M-Blue » (météore) → blue-meteor
        if (meteor.Success)
            yield return meteor.Groups[1].Value == "c" ? meteor.Groups[2].Value : $"{meteor.Groups[2].Value}-meteor";
        if (name.EndsWith("-mega", StringComparison.Ordinal))
            yield return name.StartsWith("f-", StringComparison.Ordinal) ? "mega-female"
                : name.StartsWith("m-", StringComparison.Ordinal) ? "mega-male" : "mega"; // « Curly-Mega », « F-Mega »
        foreach (var suffix in Suffixes)
            yield return name + suffix;
    }

    /// <summary>Noms de PKHeX que PokéAPI écrit autrement (sans tenir compte des suffixes de <see cref="Suffixes"/>).</summary>
    private static readonly Dictionary<string, string[]> Aliases = new()
    {
        ["!"] = ["exclamation"], ["?"] = ["question"], // Zarbi
        ["???"] = ["unknown"], // Arceus de type ??? (4e génération)
        ["F"] = ["female"], ["M"] = ["male"], // Mistigrix, Wimessir, Paragruel, Fragroin
        ["Large"] = ["totem", "totem-alola", "totem-disguised"], // Pokémon dominants
        ["Lord"] = ["hisui"], ["Lady"] = ["hisui"], // Pokémon Barons de Légendes Arceus
        ["Jumbo"] = ["super"], // Pitrouille, Banshitrouye
        ["Spiky"] = ["spiky-eared"], // Pichu Troizépi
        ["Dusk"] = ["own-tempo"], // Rocabot Tempo Perso
        ["Active"] = ["battle-bond"], // Amphinobi
        ["Noice Face"] = ["noice"], // Bekaglaçon
        ["Electric"] = ["shock"], ["Fire"] = ["burn"], ["Ice"] = ["chill"], ["Water"] = ["douse"], // Genesect
        ["10%-C"] = ["10-power-construct"], ["50%-C"] = ["50-power-construct"], // Zygarde
    };

    /// <summary>Fins de nom que PokéAPI ajoute (« Paldea Combat » → paldea-combat-breed, « Original » → original-cap...).</summary>
    private static readonly string[] Suffixes =
        ["-cap", "-breed", "-striped", "-standard", "-strawberry-sweet", "-plumage", "-build", "-mode", "-mask"];

    [GeneratedRegex("^([cm])-([a-z]+)$")]
    private static partial Regex MinorPattern();

    /// <summary>Nom anglais en nom de fichier : « Paldea Combat » → paldea-combat, « Flabébé » → flabebe.</summary>
    private static string Slug(string name)
    {
        var plain = new string(name.Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
        var slug = Regex.Replace(plain.ToLowerInvariant().Replace(' ', '-'), "[^a-z0-9-]", "");
        return Regex.Replace(slug, "-+", "-").Trim('-');
    }
}
