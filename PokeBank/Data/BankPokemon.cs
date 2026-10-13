namespace PokeBank.Data;

/// <summary>
/// Un Pokémon stocké dans la banque. <see cref="Data"/> contient les octets bruts (déchiffrés, format "boîte")
/// tels que fournis par PKHeX ; les autres colonnes ne servent qu'à l'affichage et à la recherche.
/// </summary>
public class BankPokemon
{
    public int Id { get; set; }

    public string OwnerId { get; set; } = "";
    public ApplicationUser? Owner { get; set; }

    public byte[] Data { get; set; } = [];
    /// <summary>PKHeX.Core.EntityContext du fichier (Gen5, Gen6...).</summary>
    public byte Context { get; set; }
    public string Extension { get; set; } = "";

    public ushort Species { get; set; }
    public byte Form { get; set; }
    public string SpeciesName { get; set; } = "";
    public string Nickname { get; set; } = "";
    public byte Level { get; set; }
    public bool IsShiny { get; set; }
    public bool IsEgg { get; set; }
    public bool IsLegal { get; set; }
    public string OriginalTrainer { get; set; } = "";
    public string OriginGame { get; set; } = "";

    public DateTime DepositedAt { get; set; }
}
