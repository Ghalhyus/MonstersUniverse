using PKHeX.Core;

namespace PokeBank.Services;

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
public sealed class PokemonInfo
{
    private const int French = (int)LanguageID.French;
    private readonly GameStrings _strings = GameInfo.GetStrings("fr");

    public string GameName(GameVersion version) => Get(_strings.gamelist, (int)version, version.ToString());

    /// <summary>Nom du jeu d'une sauvegarde ; certaines ne précisent pas la version exacte (« Any » pour PKHeX).</summary>
    public string GameName(SaveFile sav) =>
        sav.Version == GameVersion.Any ? $"Jeu de génération {sav.Generation}" : GameName(sav.Version);

    public static string SpriteUrl(ushort species, bool shiny) =>
        $"https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/{(shiny ? "shiny/" : "")}{species}.png";

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
            Get(_strings.natures, (int)pk.Nature),
            Get(_strings.abilitylist, pk.Ability),
            pk.HeldItem == 0 ? "—" : Get(_strings.itemlist, pk.HeldItem),
            new[] { pk.Move1, pk.Move2, pk.Move3, pk.Move4 }.Where(m => m != 0).Select(m => Get(_strings.movelist, m)).ToList(),
            pk.OriginalTrainerName,
            pk.DisplayTID.ToString("00000"),
            GameName(pk.Version),
            isLegal ?? IsLegal(pk),
            pk.Extension.ToUpperInvariant(),
            SpriteUrl(pk.Species, pk.IsShiny));
    }

    private static string Get(IReadOnlyList<string> list, int index, string fallback = "?") =>
        (uint)index < (uint)list.Count && !string.IsNullOrWhiteSpace(list[index]) ? list[index] : fallback;
}
