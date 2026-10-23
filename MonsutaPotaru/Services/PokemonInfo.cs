using PKHeX.Core;

namespace MonsutaPotaru.Services;

/// <summary>Vue d'un Pokémon prête à afficher (noms en français).</summary>
public sealed record PokemonView(
    ushort Species,
    byte Form,
    string SpeciesName,
    string Nickname,
    int Level,
    bool IsShiny,
    bool IsEgg,
    string Gender,
    string Nature,
    string Ability,
    string HeldItem,
    IReadOnlyList<string> Moves,
    string OriginalTrainer,
    string TrainerId,
    string OriginGame,
    bool IsLegal,
    string Format,
    string SpriteUrl);

/// <summary>Traduit un PKM de PKHeX en informations lisibles.</summary>
public sealed partial class PokemonInfo
{
    private const int French = (int)LanguageID.French;
    private readonly GameStrings _strings = GameInfo.GetStrings("fr");

    /// <summary>
    /// Noms français des objets, sans le suffixe de génération que PKHeX ajoute quand un nom existe dans plusieurs jeux
    /// (« Nouilles (G8) » → « Nouilles »).
    /// </summary>
    private readonly string[] _items = GameInfo.GetStrings("fr").itemlist.Select(n => GenerationSuffix().Replace(n, "")).ToArray();

    [System.Text.RegularExpressions.GeneratedRegex(@" \(G\d\)$")]
    private static partial System.Text.RegularExpressions.Regex GenerationSuffix();

    public string GameName(GameVersion version) =>
        GroupNames.TryGetValue(version, out var name) ? name : Get(_strings.gamelist, (int)version, version.ToString());

    /// <summary>
    /// Noms des groupes de jeux, absents des listes de PKHeX : une sauvegarde de Rouge/Bleu, d'Or/Argent, de Rubis/Saphir,
    /// de Diamant/Perle ou de HeartGold/SoulSilver ne dit pas lequel des deux, et un Pokémon de 1re ou 2e génération pas de
    /// quel jeu il vient. Noms des jeux tels que PKHeX les donne en français (« Or HeartGold »). À garder en accord avec les
    /// migrations GameNames et GameGroupNames.
    /// </summary>
    public static readonly IReadOnlyDictionary<GameVersion, string> GroupNames = new Dictionary<GameVersion, string>
    {
        [GameVersion.RB] = "Rouge / Bleu",
        [GameVersion.RBY] = "Rouge / Bleu / Jaune",
        [GameVersion.GS] = "Or / Argent",
        [GameVersion.GSC] = "Or / Argent / Cristal",
        [GameVersion.RS] = "Rubis / Saphir",
        [GameVersion.RSE] = "Rubis / Saphir / Émeraude",
        [GameVersion.FRLG] = "Rouge Feu / Vert Feuille",
        [GameVersion.COLO] = "Colosseum",
        [GameVersion.XD] = "XD : Le Souffle des Ténèbres",
        [GameVersion.RSBOX] = "Pokémon Box",
        [GameVersion.DP] = "Diamant / Perle",
        [GameVersion.DPPt] = "Diamant / Perle / Platine",
        [GameVersion.HGSS] = "Or HeartGold / Argent SoulSilver",
    };

    /// <summary>Nom du jeu d'une sauvegarde ; certaines ne précisent pas la version exacte (« Any » pour PKHeX).</summary>
    public string GameName(SaveFile sav) =>
        sav.Version == GameVersion.Any ? $"Jeu de génération {sav.Generation}" : GameName(sav.Version);

    /// <summary>Nom français d'un objet par sa clé (voir <see cref="SaveBag"/>) : « Super Ball », « CT/DT Séisme ».</summary>
    public string ItemName(int key) =>
        SaveBag.MoveOf(key) is { } move ? $"CT/DT {MoveName(move)}" : Get(_items, key, $"Objet n°{key}");

    /// <summary>Nom court d'un objet, sous le titre de sa catégorie : celui de la capacité pour une CT ou une DT (« Séisme »).</summary>
    public string ShortItemName(int key) => SaveBag.MoveOf(key) is { } move ? MoveName(move) : ItemName(key);

    public string MoveName(ushort move) => Get(_strings.movelist, move, $"Capacité n°{move}");

    /// <summary>Noms français des types, natures et talents (filtres de « Ma collection »), par index PKHeX.</summary>
    public string TypeName(byte type) => Get(_strings.types, type);
    public string NatureName(byte nature) => Get(_strings.natures, nature);
    public string AbilityName(int ability) => Get(_strings.abilitylist, ability, $"Talent n°{ability}");

    /// <summary>
    /// Image d'un objet, servie par le site (wwwroot/images/objets, rassemblées par outils/images.py) : par numéro
    /// d'objet (1.png) ; pour une CT ou une DT, la CT du type de la capacité (ct-ground.png). Un objet sans image affiche
    /// inconnu.png (composant ItemSprite).
    /// </summary>
    public static string ItemSpriteUrl(int key) =>
        SaveBag.MoveOf(key) is { } move ? $"images/objets/ct-{Get(EnglishTypes, MoveType(move), "normal").ToLowerInvariant()}.png"
        : $"images/objets/{key}.png";

    /// <summary>Type d'une capacité dans les jeux récents (Charme est de type Fée depuis la 6e génération).</summary>
    private static byte MoveType(ushort move) =>
        move < MoveInfo.GetTypeTable(EntityContext.Gen9).Length ? MoveInfo.GetType(move, EntityContext.Gen9)
        : move < MoveInfo.GetTypeTable(EntityContext.Gen9a).Length ? MoveInfo.GetType(move, EntityContext.Gen9a)
        : (byte)0;

    private static readonly IReadOnlyList<string> EnglishTypes = GameInfo.GetStrings("en").types;

    public static bool IsLegal(PKM pk) => new LegalityAnalysis(pk, StorageSlotType.Box).Valid;

    public PokemonView Describe(PKM pk, bool? isLegal = null)
    {
        var speciesName = SpeciesName.GetSpeciesName(pk.Species, French);
        return new PokemonView(
            pk.Species,
            pk.Form,
            speciesName,
            pk.IsEgg ? "Œuf" : pk.IsNicknamed ? pk.Nickname : speciesName,
            pk.CurrentLevel,
            pk.IsShiny,
            pk.IsEgg,
            pk.Gender switch { 0 => "♂", 1 => "♀", _ => "" },
            Get(_strings.natures, PokemonTraits.Nature(pk)), // 1re et 2e génération : celle de leur transfert
            Get(_strings.abilitylist, pk.Ability),
            pk.HeldItem == 0 ? "—" : Get(_items, pk.HeldItem),
            new[] { pk.Move1, pk.Move2, pk.Move3, pk.Move4 }.Where(m => m != 0).Select(m => Get(_strings.movelist, m)).ToList(),
            pk.OriginalTrainerName,
            pk.DisplayTID.ToString("00000"),
            GameName(pk.Version),
            isLegal ?? IsLegal(pk),
            pk.Extension.ToUpperInvariant(),
            PokemonSprites.Url(pk));
    }

    private static string Get(IReadOnlyList<string> list, int index, string fallback = "?") =>
        (uint)index < (uint)list.Count && !string.IsNullOrWhiteSpace(list[index]) ? list[index] : fallback;
}
