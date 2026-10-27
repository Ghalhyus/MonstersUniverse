using PKHeX.Core;

namespace MonsutaPotaru.Services;

/// <summary>
/// Noms des combats : le simulateur (Pokémon Showdown) écrit les espèces, capacités, objets, talents et types en anglais ;
/// on retrouve leur numéro dans les noms anglais de PKHeX, puis leur nom français. Les noms sont comparés sans majuscules,
/// accents ni ponctuation (« King's Rock » / « King’s Rock », « Poke Ball » / « Poké Ball »), avec quelques alias pour les
/// noms qui diffèrent vraiment (« Stick », devenu « Leek »).
/// </summary>
public static class BattleNames
{
    private static readonly GameStrings English = GameInfo.GetStrings("en");
    private static readonly GameStrings French = GameInfo.GetStrings("fr");

    private const int HiddenPower = 237;

    private static readonly Dictionary<string, int> Species = Index(English.specieslist, new()
    {
        ["nidoranf"] = (int)PKHeX.Core.Species.NidoranF,
        ["nidoranm"] = (int)PKHeX.Core.Species.NidoranM,
    });
    private static readonly Dictionary<string, int> Moves = Index(English.movelist);
    private static readonly Dictionary<string, int> Items = Index(English.itemlist, new()
    {
        ["stick"] = 259, // « Leek » à partir de la 8e génération
        ["upgrade"] = 252,
    });
    private static readonly Dictionary<string, int> Abilities = Index(English.abilitylist);
    private static readonly Dictionary<string, int> Types = English.types.Select((name, i) => (name, i)).ToDictionary(t => Key(t.name), t => t.i);

    /// <summary>Nom comparable : minuscules, sans accents, espaces ni ponctuation.</summary>
    public static string Key(string name)
    {
        var plain = name.Normalize(System.Text.NormalizationForm.FormD);
        return new string(plain.Where(char.IsAsciiLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    }

    private static Dictionary<string, int> Index(IReadOnlyList<string> names, Dictionary<string, int>? aliases = null)
    {
        var index = new Dictionary<string, int>(aliases ?? []);
        for (var i = 1; i < names.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(names[i]))
                index.TryAdd(Key(names[i]), i);
        }
        return index;
    }

    /// <summary>
    /// Espèce d'une description de Showdown (« Deoxys-Speed, L50, M ») : numéro national, nom de la forme (« Speed ») et
    /// nom français (« Deoxys »). Une forme que PKHeX ne nomme pas à part est cherchée sur le nom avant le tiret
    /// (« Castform-Sunny » → Morphéo), sauf pour les noms qui ont eux-mêmes un tiret (« Ho-Oh »).
    /// </summary>
    public static (ushort Species, string Forme, string Name) SpeciesOf(string details)
    {
        var name = details.Split(',')[0].Trim();
        if (Species.TryGetValue(Key(name), out var number))
            return ((ushort)number, "", French.specieslist[number]);
        var dash = name.IndexOf('-');
        if (dash > 0 && Species.TryGetValue(Key(name[..dash]), out number))
            return ((ushort)number, name[(dash + 1)..], French.specieslist[number]);
        return (0, "", name);
    }

    /// <summary>Numéro d'une capacité (« Hidden Power Dark 70 », nom affiché par Showdown, compris), 0 si inconnue.</summary>
    public static int MoveNumber(string name) =>
        name.StartsWith("Hidden Power", StringComparison.OrdinalIgnoreCase) ? HiddenPower : Moves.GetValueOrDefault(Key(name));

    public static string Move(string name) => Get(French.movelist, MoveNumber(name), name);

    public static string Item(string name) => Get(French.itemlist, Items.GetValueOrDefault(Key(name)), name);

    public static string Ability(string name) => Get(French.abilitylist, Abilities.GetValueOrDefault(Key(name)), name);

    public static string Type(string name) => Types.TryGetValue(Key(name), out var type) ? French.types[type] : name;

    /// <summary>Type d'une capacité en 3e génération (Puissance Cachée : Normal ; son vrai type est dans le nom de Showdown).</summary>
    public static string MoveType(string name)
    {
        if (name.StartsWith("Hidden Power ", StringComparison.OrdinalIgnoreCase))
        {
            var type = name["Hidden Power ".Length..].Split(' ')[0];
            return Type(type);
        }
        var move = MoveNumber(name);
        return move == 0 ? "" : French.types[MoveInfo.GetType((ushort)move, EntityContext.Gen3)];
    }

    /// <summary>
    /// Type anglais d'une capacité, en minuscules (« electric ») : classe CSS de sa couleur dans le menu des capacités.
    /// Vide pour une capacité inconnue (Lutte, Repos forcé...).
    /// </summary>
    public static string MoveTypeKey(string name)
    {
        if (name.StartsWith("Hidden Power ", StringComparison.OrdinalIgnoreCase))
            return Key(name["Hidden Power ".Length..].Split(' ')[0]);
        var move = MoveNumber(name);
        return move == 0 ? "" : Key(English.types[MoveInfo.GetType((ushort)move, EntityContext.Gen3)]);
    }

    /// <summary>
    /// Numéro de forme de PKHeX (images) d'une forme de Showdown : Deoxys (Attaque, Défense, Vitesse), Morphéo, lettre de
    /// Zarbi (« B » : 1, « Exclamation » : 26, « Question » : 27).
    /// </summary>
    public static byte FormIndex(ushort species, string forme) => (species, forme) switch
    {
        (386, "Attack") or (351, "Sunny") => 1,
        (386, "Defense") or (351, "Rainy") => 2,
        (386, "Speed") or (351, "Snowy") => 3,
        (201, "Exclamation") => 26,
        (201, "Question") => 27,
        (201, [var letter and >= 'B' and <= 'Z']) => (byte)(letter - 'A'),
        _ => 0,
    };

    /// <summary>Forme de Showdown d'un Zarbi (numéro de forme de PKHeX) : « » (A), « B » à « Z », « Exclamation », « Question ».</summary>
    public static string UnownForme(byte form) => form switch
    {
        26 => "Exclamation",
        27 => "Question",
        > 0 and < 26 => ((char)('A' + form)).ToString(),
        _ => "",
    };

    private static string Get(IReadOnlyList<string> list, int index, string fallback) =>
        index > 0 && index < list.Count && !string.IsNullOrWhiteSpace(list[index]) ? list[index] : fallback;
}
