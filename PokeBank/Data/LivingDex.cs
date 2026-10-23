namespace PokeBank.Data;

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
}

/// <summary>
/// Living dex d'un compte : un exemplaire de chaque Pokémon du Pokédex d'un jeu, compté dans une sauvegarde stockée
/// (équipe et boîtes). Au plus un par jeu et par compte, une sauvegarde ne sert qu'à un ; supprimé avec la sauvegarde.
/// </summary>
public class LivingDex
{
    public int Id { get; set; }

    public string OwnerId { get; set; } = "";
    public ApplicationUser? Owner { get; set; }

    public LivingDexGame Game { get; set; }

    /// <summary>Sauvegarde stockée où sont comptés les Pokémon.</summary>
    public int StoredSaveId { get; set; }
    public StoredSave? StoredSave { get; set; }

    public DateTime CreatedAt { get; set; }
}
