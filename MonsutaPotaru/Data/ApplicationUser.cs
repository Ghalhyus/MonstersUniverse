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
}
