namespace PokeBank.Data;

/// <summary>
/// Objet de l'inventaire du site (Pokéball, médicament, CT ou DT), retiré du sac d'une sauvegarde stockée ou à y déposer.
/// Une ligne par objet et par compte ; elle disparaît quand la quantité retombe à 0.
/// </summary>
public class OwnedItem
{
    public string OwnerId { get; set; } = "";
    public ApplicationUser? Owner { get; set; }

    /// <summary>
    /// Clé de l'objet (voir <see cref="Services.SaveBag"/>) : son numéro dans PKHeX (le même d'un jeu à l'autre à partir de la
    /// 3e génération) ; pour une CT ou une DT, la capacité qu'elle apprend (<see cref="Services.SaveBag.MachineKey"/>).
    /// </summary>
    public int ItemId { get; set; }
    public int Count { get; set; }
}
