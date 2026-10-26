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
    /// Profil privé (« Mon compte » → « Visibilité ») : introuvable dans « Rechercher un joueur », et aucune de ses pages
    /// n'est visible des autres, même par lien direct (<see cref="Services.PlayerService"/>).
    /// </summary>
    public bool ProfilePrivate { get; set; }

    /// <summary>Pages que les autres joueurs peuvent consulter, en lecture seule, quand le profil est public.</summary>
    public PublicPages PublicPages { get; set; } = PublicPages.All;
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
