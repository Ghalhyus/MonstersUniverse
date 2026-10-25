namespace MonsutaPotaru.Data;

/// <summary>
/// Console 3DS reliée à un compte (homebrew PokeBank Sync) : elle envoie les sauvegardes Pokémon qu'elle trouve, qui
/// deviennent des sauvegardes stockées (<see cref="StoredSave"/>). Reliée avec un code à usage unique
/// (<see cref="SyncPairingCode"/>), elle s'authentifie ensuite avec un jeton longue durée, dont seule l'empreinte est gardée.
/// </summary>
public class SyncDevice
{
    public int Id { get; set; }

    public string OwnerId { get; set; } = "";
    public ApplicationUser? Owner { get; set; }

    /// <summary>Nom donné par la console à la liaison (nom d'utilisateur de la 3DS), pour la reconnaître dans la liste.</summary>
    public string Name { get; set; } = "";

    /// <summary>Empreinte SHA-256 du jeton (jamais le jeton lui-même : une fuite de la base ne permet pas d'envoyer).</summary>
    public string TokenHash { get; set; } = "";

    public DateTime LinkedAt { get; set; }
    /// <summary>Dernier appel de la console (vérification du compte ou envoi), null si elle n'a encore rien fait.</summary>
    public DateTime? LastUsedAt { get; set; }
}

/// <summary>
/// Emplacement d'une sauvegarde stockée sur une console reliée : la clé que la console donne à ce fichier
/// (« sd:sdmc:/saves/Pokemon - Version Noire 2.sav », « titre:0004000000055D00:1 »). Noté à chaque envoi ou récupération,
/// pour que la console sache où remettre la version du site. Supprimé avec la console ou la sauvegarde.
/// </summary>
public class SyncSaveLocation
{
    public int Id { get; set; }

    public int DeviceId { get; set; }
    public SyncDevice? Device { get; set; }

    public int StoredSaveId { get; set; }
    public StoredSave? StoredSave { get; set; }

    /// <summary>Clé de la sauvegarde sur la console (une seule sauvegarde stockée par clé et par console).</summary>
    public string Key { get; set; } = "";

    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Code de liaison affiché dans « Synchronisation 3DS » et tapé sur la 3DS : valable quelques minutes, une seule fois,
/// un seul à la fois par compte (en demander un autre remplace le précédent).
/// </summary>
public class SyncPairingCode
{
    /// <summary>8 caractères sans ambiguïté (ni 0/O, ni 1/I/L), sans le tiret affiché au milieu.</summary>
    public string Code { get; set; } = "";

    public string OwnerId { get; set; } = "";
    public ApplicationUser? Owner { get; set; }

    public DateTime ExpiresAt { get; set; }
}
