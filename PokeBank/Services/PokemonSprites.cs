using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using PKHeX.Core;

namespace PokeBank.Services;

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
        var folder = Path.Combine(webRoot, "images", "pokemon");
        _files = Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, "*.png", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(folder, f).Replace('\\', '/')).ToHashSet()
            : new HashSet<string>();
    }

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
