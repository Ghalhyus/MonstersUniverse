namespace PokeBank.Data;

/// <summary>
/// Pokémon de la collection installé dans le « Parc » de son dresseur (30 au plus, voir <see cref="Services.ParkService"/>).
/// Supprimé avec le Pokémon : retiré de la collection, ou recréé par la mise à jour de sa sauvegarde stockée, il quitte le parc.
/// </summary>
public class ParkMember
{
    public int BankPokemonId { get; set; }
    public BankPokemon? BankPokemon { get; set; }

    /// <summary>Dresseur du Pokémon (celui de <see cref="BankPokemon"/>), pour compter et lister sans jointure.</summary>
    public string OwnerId { get; set; } = "";

    public DateTime AddedAt { get; set; }
}
