namespace MonsutaPotaru.Data;

/// <summary>Jeu d'un living dex (jaquette, sprites, liste des Pokémon : <see cref="Services.LivingDexGames"/>).</summary>
public enum LivingDexGame
{
    Red = 1,
    Blue = 2,
    Yellow = 3,
    Gold = 4,
    Silver = 5,
    Crystal = 6,
    Ruby = 7,
    Sapphire = 8,
    Emerald = 9,
    FireRed = 10,
    LeafGreen = 11,
    Diamond = 12,
    Pearl = 13,
    Platinum = 14,
    HeartGold = 15,
    SoulSilver = 16,
    Black = 17,
    White = 18,
    Black2 = 19,
    White2 = 20,
    X = 21,
    Y = 22,
    OmegaRuby = 23,
    AlphaSapphire = 24,
    Sun = 25,
    Moon = 26,
    UltraSun = 27,
    UltraMoon = 28,
    LetsGoPikachu = 29,
    LetsGoEevee = 30,
    Sword = 31,
    Shield = 32,
    BrilliantDiamond = 33,
    ShiningPearl = 34,
    LegendsArceus = 35,
    Scarlet = 36,
    Violet = 37,
    /// <summary>Living dex global : sans jeu ni sauvegarde, compté sur le stockage interne (<see cref="Services.GlobalLivingDex"/>).</summary>
    Global = 100,
}

/// <summary>
/// Living dex d'un compte : un exemplaire de chaque Pokémon du Pokédex d'un jeu, compté dans une sauvegarde stockée
/// (équipe et boîtes). Au plus un par jeu et par compte, une sauvegarde ne sert qu'à un ; supprimé avec la sauvegarde.
/// Le living dex global (<see cref="LivingDexGame.Global"/>) n'a pas de sauvegarde : il compte le stockage interne.
/// </summary>
public class LivingDex
{
    public int Id { get; set; }

    public string OwnerId { get; set; } = "";
    public ApplicationUser? Owner { get; set; }

    public LivingDexGame Game { get; set; }

    /// <summary>Sauvegarde stockée où sont comptés les Pokémon ; null pour le living dex global (stockage interne).</summary>
    public int? StoredSaveId { get; set; }
    public StoredSave? StoredSave { get; set; }

    public DateTime CreatedAt { get; set; }
}
