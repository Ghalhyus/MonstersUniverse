using Microsoft.AspNetCore.Identity;

namespace MonsutaPotaru.Data;

// Add profile data for application users by adding properties to the ApplicationUser class
public class ApplicationUser : IdentityUser
{
    /// <summary>
    /// Nombre maximal de Pokémon dans la collection (banque + sauvegardes stockées), agrandi en achetant des
    /// emplacements avec les Pokédollars du porte-monnaie (voir <see cref="Services.CollectionCapacity"/>).
    /// </summary>
    public int CollectionLimit { get; set; } = Services.CollectionCapacity.DefaultLimit;

    /// <summary>Thème choisi dans « Mon compte » → « Thèmes » (clé de <see cref="Services.SiteThemes"/>) ; null : le thème par défaut.</summary>
    [System.ComponentModel.DataAnnotations.MaxLength(32)]
    public string? Theme { get; set; }

    /// <summary>
    /// Qui peut voir les pages cochées (<see cref="PublicPages"/>), « Mon compte » → « Visibilité » : tout le monde, les amis
    /// seulement (trouvable dans la recherche pour recevoir des demandes), ou personne (introuvable, ami par code
    /// seulement). Voir <see cref="Services.PlayerService"/>.
    /// </summary>
    public ProfileAudience Audience { get; set; }

    /// <summary>Pages que les autres joueurs peuvent consulter, en lecture seule, selon <see cref="Audience"/>.</summary>
    public PublicPages PublicPages { get; set; } = PublicPages.All;

    /// <summary>
    /// Code ami (« ABCD-EFGH », <see cref="Services.FriendService"/>) : tapé par un autre joueur, il m'envoie une demande
    /// d'ami, même si mon profil est introuvable. Tiré au premier besoin, régénérable ; null tant qu'il n'a pas servi.
    /// </summary>
    [System.ComponentModel.DataAnnotations.MaxLength(16)]
    public string? FriendCode { get; set; }
}

/// <summary>Pages d'un joueur que les autres peuvent consulter (profil public), en lecture seule.</summary>
[Flags]
public enum PublicPages
{
    None = 0,
    Collection = 1,
    Park = 2,
    Inventory = 4,
    LivingDex = 8,
    All = Collection | Park | Inventory | LivingDex,
}

/// <summary>Qui peut voir les pages d'un joueur (« Mon compte » → « Visibilité »).</summary>
public enum ProfileAudience
{
    /// <summary>Tout le monde : trouvable, pages cochées visibles de tous.</summary>
    Everyone = 0,
    /// <summary>Amis seulement : trouvable (demandes d'ami possibles), pages cochées visibles de ses amis.</summary>
    Friends = 1,
    /// <summary>Personne : introuvable, aucune page visible, même des amis ; ami par code seulement.</summary>
    Nobody = 2,
}
