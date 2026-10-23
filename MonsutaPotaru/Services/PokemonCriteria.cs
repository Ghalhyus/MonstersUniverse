using PKHeX.Core;
using MonsutaPotaru.Data;

namespace MonsutaPotaru.Services;

/// <summary>
/// Filtres de « Ma collection » (pastilles « Type : Feu, Eau », « IV : PV ≥ 31 »...). Plusieurs valeurs d'un même filtre
/// s'additionnent (Feu ou Eau), les filtres se cumulent (Feu et femelle). Un filtre sans valeur ne filtre rien. Les œufs
/// (espèce cachée) sont exclus dès qu'un autre filtre qu'« Œuf » est actif, pour ne pas révéler ce qu'ils contiennent.
/// </summary>
public sealed record PokemonCriteria
{
    public static readonly PokemonCriteria None = new();

    /// <summary>Générations (1 à 9) : celle où l'espèce est apparue (numéro de Pokédex, voir <see cref="GenerationRanges"/>).</summary>
    public IReadOnlySet<byte> Generations { get; init; } = new HashSet<byte>();

    /// <summary>Types (index PKHeX) : l'un des deux types du Pokémon en fait partie.</summary>
    public IReadOnlySet<byte> Types { get; init; } = new HashSet<byte>();
    /// <summary>Sexes : 0 mâle, 1 femelle, 2 asexué.</summary>
    public IReadOnlySet<byte> Genders { get; init; } = new HashSet<byte>();
    public IReadOnlySet<byte> Natures { get; init; } = new HashSet<byte>();
    public IReadOnlySet<int> Abilities { get; init; } = new HashSet<int>();
    /// <summary>IV minimaux (PV, Attaque, Défense, Atq. Spé., Déf. Spé., Vitesse) ; 0 : pas de contrainte.</summary>
    public IReadOnlyList<byte> MinIvs { get; init; } = new byte[6];
    /// <summary>Niveaux minimal et maximal, compris (1 à 100) ; null : pas de borne. Voir <see cref="WithLevels"/>.</summary>
    public byte? MinLevel { get; init; }
    public byte? MaxLevel { get; init; }
    public IReadOnlySet<FormKind> Forms { get; init; } = new HashSet<FormKind>();
    /// <summary>Tient sa Méga-Gemme (affiché méga-évolué) ; null : peu importe.</summary>
    public bool? Mega { get; init; }
    public bool? Shiny { get; init; }
    /// <summary>A un surnom : nom affiché différent du nom de l'espèce (un surnom identique au nom de l'espèce ne compte pas).</summary>
    public bool? Nicknamed { get; init; }
    public bool? Egg { get; init; }

    /// <summary>Un filtre autre qu'« Œuf » est actif : les œufs sont exclus.</summary>
    public bool ExcludesEggs =>
        Generations.Count > 0 || Types.Count > 0 || Genders.Count > 0 || Natures.Count > 0 || Abilities.Count > 0 || MinIvs.Any(iv => iv > 0)
        || MinLevel is not null || MaxLevel is not null || Forms.Count > 0 || Mega is not null || Shiny is not null || Nicknamed is not null;

    /// <summary>Bornes de niveau ramenées à 1-100 (vide ou 0 : pas de borne), inversées si le minimum dépasse le maximum.</summary>
    public PokemonCriteria WithLevels(int? min, int? max)
    {
        byte? Clamp(int? level) => level is > 0 ? (byte)Math.Min(level.Value, 100) : null;
        var (low, high) = (Clamp(min), Clamp(max));
        return low > high ? this with { MinLevel = high, MaxLevel = low } : this with { MinLevel = low, MaxLevel = high };
    }

    public bool IsEmpty => !ExcludesEggs && Egg is null;

    /// <summary>Applique les filtres à une requête de la collection (traduite en SQL).</summary>
    public IQueryable<BankPokemon> Apply(IQueryable<BankPokemon> query)
    {
        if (ExcludesEggs)
            query = query.Where(p => !p.IsEgg);
        if (Egg is { } egg)
            query = query.Where(p => p.IsEgg == egg);
        if (Generations.Count > 0)
        {
            var species = Generations.SelectMany(SpeciesOf).ToList();
            query = query.Where(p => species.Contains(p.Species));
        }
        if (Types.Count > 0)
        {
            var types = Types.ToList();
            query = query.Where(p => types.Contains(p.Type1) || types.Contains(p.Type2));
        }
        if (Genders.Count > 0)
        {
            var genders = Genders.ToList();
            query = query.Where(p => p.Gender != null && genders.Contains(p.Gender.Value));
        }
        if (Natures.Count > 0)
        {
            var natures = Natures.ToList();
            query = query.Where(p => p.Nature != null && natures.Contains(p.Nature.Value));
        }
        if (Abilities.Count > 0)
        {
            var abilities = Abilities.ToList();
            query = query.Where(p => abilities.Contains(p.Ability));
        }
        if (Forms.Count > 0)
        {
            var forms = Forms.ToList();
            query = query.Where(p => forms.Contains(p.FormKind));
        }
        var iv = MinIvs;
        if (iv[0] > 0) query = query.Where(p => p.IvHp >= iv[0]);
        if (iv[1] > 0) query = query.Where(p => p.IvAtk >= iv[1]);
        if (iv[2] > 0) query = query.Where(p => p.IvDef >= iv[2]);
        if (iv[3] > 0) query = query.Where(p => p.IvSpA >= iv[3]);
        if (iv[4] > 0) query = query.Where(p => p.IvSpD >= iv[4]);
        if (iv[5] > 0) query = query.Where(p => p.IvSpe >= iv[5]);
        if (MinLevel is { } minLevel)
            query = query.Where(p => p.Level >= minLevel);
        if (MaxLevel is { } maxLevel)
            query = query.Where(p => p.Level <= maxLevel);
        if (Nicknamed is { } nicknamed)
            query = query.Where(p => (p.Nickname != p.SpeciesName) == nicknamed);
        if (Mega is { } mega)
            query = query.Where(p => p.MegaReady == mega);
        if (Shiny is { } shiny)
            query = query.Where(p => p.IsShiny == shiny);
        return query;
    }

    /// <summary>Premier et dernier numéro de Pokédex de chaque génération (index 0 : 1re génération, Bulbizarre à Mew).</summary>
    public static readonly (ushort First, ushort Last)[] GenerationRanges =
        [(1, 151), (152, 251), (252, 386), (387, 493), (494, 649), (650, 721), (722, 809), (810, 905), (906, 1025)];

    /// <summary>Génération où une espèce est apparue (1 à 9) ; 0 hors de ces numéros.</summary>
    public static byte GenerationOf(ushort species) =>
        (byte)(Array.FindIndex(GenerationRanges, r => species >= r.First && species <= r.Last) + 1);

    private static IEnumerable<ushort> SpeciesOf(byte generation) =>
        generation is >= 1 and <= 9
            ? Enumerable.Range(GenerationRanges[generation - 1].First, GenerationRanges[generation - 1].Last - GenerationRanges[generation - 1].First + 1).Select(s => (ushort)s)
            : [];

    // ---------- Adresse de la page (?type=feu,eau&sexe=f&iv=pv31,vit25&niveau=20-40...) ----------

    /// <summary>Paramètres d'adresse des filtres, dans l'ordre des pastilles.</summary>
    public static readonly string[] QueryNames = ["generation", "type", "sexe", "nature", "talent", "iv", "niveau", "forme", "mega", "chromatique", "surnom", "oeuf"];

    private static readonly GameStrings French = GameInfo.GetStrings("fr");

    /// <summary>Types proposés (sans le type Stellaire, qui n'est celui d'aucune espèce) : index PKHeX et nom dans l'adresse.</summary>
    public static readonly IReadOnlyList<(byte Id, string Slug)> TypeSlugs =
        Enumerable.Range(0, 18).Select(i => ((byte)i, BankPokemon.SortKey(French.types[i]))).ToList();

    public static readonly IReadOnlyList<(byte Id, string Slug)> NatureSlugs =
        Enumerable.Range(0, 25).Select(i => ((byte)i, BankPokemon.SortKey(French.natures[i]))).ToList();

    public static readonly IReadOnlyList<(byte Id, string Slug)> GenderSlugs = [(0, "m"), (1, "f"), (2, "aucun")];

    public static readonly IReadOnlyList<(FormKind Kind, string Slug)> FormSlugs =
    [
        (FormKind.Standard, "standard"), (FormKind.Alola, "alola"), (FormKind.Galar, "galar"),
        (FormKind.Hisui, "hisui"), (FormKind.Paldea, "paldea"), (FormKind.Other, "autre"),
    ];

    /// <summary>Statistiques des IV dans l'adresse (« pv31 »), dans l'ordre de <see cref="MinIvs"/>.</summary>
    public static readonly string[] IvSlugs = ["pv", "atq", "def", "atqspe", "defspe", "vit"];

    /// <summary>Valeurs à mettre dans l'adresse (null : paramètre retiré).</summary>
    public IReadOnlyDictionary<string, object?> ToQuery() => new Dictionary<string, object?>
    {
        ["generation"] = Join(Generations.Order().Select(g => g.ToString())),
        ["type"] = Join(TypeSlugs.Where(t => Types.Contains(t.Id)).Select(t => t.Slug)),
        ["sexe"] = Join(GenderSlugs.Where(g => Genders.Contains(g.Id)).Select(g => g.Slug)),
        ["nature"] = Join(NatureSlugs.Where(n => Natures.Contains(n.Id)).Select(n => n.Slug)),
        ["talent"] = Join(Abilities.Order().Select(a => a.ToString())),
        ["iv"] = Join(MinIvs.Select((v, i) => v > 0 ? $"{IvSlugs[i]}{v}" : null).OfType<string>()),
        ["niveau"] = MinLevel is null && MaxLevel is null ? null : $"{MinLevel}-{MaxLevel}", // « 20-40 », « -29 », « 51- »
        ["forme"] = Join(FormSlugs.Where(f => Forms.Contains(f.Kind)).Select(f => f.Slug)),
        ["mega"] = YesNo(Mega),
        ["chromatique"] = YesNo(Shiny),
        ["surnom"] = YesNo(Nicknamed),
        ["oeuf"] = YesNo(Egg),
    };

    /// <summary>Forme écrite des filtres (celle de l'adresse), pour savoir si deux filtres sont les mêmes.</summary>
    public string Key => string.Join("&", ToQuery().Where(p => p.Value is not null).Select(p => $"{p.Key}={p.Value}"));

    /// <summary>Filtres lus dans l'adresse ; une valeur inconnue est ignorée.</summary>
    public static PokemonCriteria FromQuery(Func<string, string?> get)
    {
        var ivs = new byte[6];
        foreach (var part in Split(get("iv")))
            // La statistique au nom le plus long d'abord : « atqspe31 » n'est pas « atq » suivi de « spe31 ».
            foreach (var (slug, index) in IvSlugs.Select((s, i) => (s, i)).OrderByDescending(s => s.s.Length))
                if (part.StartsWith(slug, StringComparison.Ordinal) && byte.TryParse(part[slug.Length..], out var value))
                {
                    ivs[index] = Math.Min((byte)31, value);
                    break;
                }
        var levels = (get("niveau") ?? "").Split('-');
        int? Level(int i) => levels.Length == 2 && int.TryParse(levels[i], out var level) ? level : null;
        return new PokemonCriteria
        {
            Generations = Split(get("generation")).Select(g => byte.TryParse(g, out var n) ? n : (byte)0).Where(n => n is >= 1 and <= 9).ToHashSet(),
            Types = Ids(get("type"), TypeSlugs),
            Genders = Ids(get("sexe"), GenderSlugs),
            Natures = Ids(get("nature"), NatureSlugs),
            Abilities = Split(get("talent")).Select(t => int.TryParse(t, out var id) ? id : 0).Where(id => id > 0).ToHashSet(),
            MinIvs = ivs,
            Forms = Split(get("forme")).SelectMany(s => FormSlugs.Where(f => f.Slug == s).Select(f => f.Kind)).ToHashSet(),
            Mega = ReadYesNo(get("mega")),
            Shiny = ReadYesNo(get("chromatique")),
            Nicknamed = ReadYesNo(get("surnom")),
            Egg = ReadYesNo(get("oeuf")),
        }.WithLevels(Level(0), Level(1));
    }

    private static HashSet<byte> Ids(string? value, IReadOnlyList<(byte Id, string Slug)> slugs) =>
        Split(value).SelectMany(s => slugs.Where(x => x.Slug == s).Select(x => x.Id)).ToHashSet();

    private static IEnumerable<string> Split(string? value) =>
        (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string? Join(IEnumerable<string> values) => string.Join(',', values) is { Length: > 0 } joined ? joined : null;

    private static string? YesNo(bool? value) => value switch { true => "oui", false => "non", null => null };

    private static bool? ReadYesNo(string? value) => value switch { "oui" => true, "non" => false, _ => null };
}
