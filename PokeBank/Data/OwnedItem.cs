namespace PokeBank.Data;

/// <summary>
/// Objet de l'inventaire du site (pour l'instant des Pokéballs), retiré du sac d'une sauvegarde stockée ou à y déposer.
/// Une ligne par objet et par compte ; elle disparaît quand la quantité retombe à 0.
/// </summary>
public class OwnedItem
{
    public string OwnerId { get; set; } = "";
    public ApplicationUser? Owner { get; set; }

    /// <summary>Numéro de l'objet dans PKHeX (le même d'un jeu à l'autre à partir de la 3e génération).</summary>
    public int ItemId { get; set; }
    public int Count { get; set; }
}
