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
    /// une même sauvegarde ne peut être stockée qu'une fois, et elle est refusée dans « Transfert » tant qu'elle l'est.
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

    /// <summary>
    /// La version modifiée par le serveur (voir <see cref="SaveModification"/>) a été téléchargée depuis la dernière
    /// modification : « Mettre à jour » est bloqué tant que ce n'est pas le cas.
    /// </summary>
    public bool DownloadedSinceModification { get; set; }

    /// <summary>Jeton de concurrence : deux opérations simultanées (deux onglets) ne peuvent pas partir du même fichier.</summary>
    public int Version { get; set; }
}

/// <summary>
/// Trace d'une modification faite par le serveur sur une partie (retrait de Pokédollars, transfert de Pokéballs),
/// rattachée à l'empreinte de la partie et non à la sauvegarde stockée : elle survit à la suppression, pour qu'on ne
/// puisse pas renvoyer l'ancienne sauvegarde du jeu (qui n'a pas ces changements) en la supprimant puis en l'ajoutant
/// de nouveau. Levée dès qu'une version plus avancée de la partie (plus de temps de jeu) est envoyée.
/// </summary>
public class SaveModification
{
    /// <summary>Même valeur que <see cref="StoredSave.IdentityKey"/>.</summary>
    public string IdentityKey { get; set; } = "";
    /// <summary>Temps de jeu de la version modifiée (une modification du serveur ne le change pas).</summary>
    public int PlayTimeSeconds { get; set; }
    /// <summary>
    /// Argent restant dans la version modifiée. Ne sert que pour les traces antérieures à <see cref="DataHash"/> :
    /// à temps de jeu égal, un fichier qui contient plus d'argent est l'ancienne sauvegarde.
    /// </summary>
    public long MoneyAfter { get; set; }
    /// <summary>
    /// Empreinte SHA-256 du fichier modifié par le serveur : à temps de jeu égal, seul ce fichier exact est accepté
    /// (l'ancienne sauvegarde du jeu a encore l'argent ou les Balls retirés, ou pas les Balls déposées).
    /// </summary>
    public string? DataHash { get; set; }
    /// <summary>Total d'argent retiré depuis la dernière version envoyée par le joueur.</summary>
    public long Withdrawn { get; set; }
    /// <summary>Des Pokéballs ont été retirées ou déposées depuis la dernière version envoyée par le joueur.</summary>
    public bool BallsChanged { get; set; }
    public DateTime ModifiedAt { get; set; }

    public static string Hash(ReadOnlySpan<byte> data) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data));
}
