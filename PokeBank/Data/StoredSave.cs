namespace PokeBank.Data;

/// <summary>
/// Sauvegarde de jeu conservée sur le serveur pour consultation : ses Pokémon sont visibles dans « Ma collection »
/// en lecture seule (<see cref="BankPokemon.StoredSaveId"/>) et ne peuvent pas être retirés vers un jeu.
/// </summary>
public class StoredSave
{
    public int Id { get; set; }

    public string OwnerId { get; set; } = "";
    public ApplicationUser? Owner { get; set; }

    /// <summary>
    /// Empreinte du dresseur (type de sauvegarde, jeu, ID dresseur + ID secret, nom), unique sur tout le serveur :
    /// une même sauvegarde ne peut être stockée qu'une fois, et elle est refusée dans « Ma banque » tant qu'elle l'est.
    /// </summary>
    public string IdentityKey { get; set; } = "";

    public string FileName { get; set; } = "";
    public byte[] Data { get; set; } = [];

    public string GameName { get; set; } = "";
    public byte Generation { get; set; }
    public string TrainerName { get; set; } = "";
    public string TrainerId { get; set; } = "";
    public int PlayTimeSeconds { get; set; }
    public int PokemonCount { get; set; }

    public DateTime UploadedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
