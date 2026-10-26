using Microsoft.AspNetCore.Identity;

namespace MonsutaPotaru.Data;

/// <summary>
/// Réglage du site modifiable depuis la page « Administration » (le code d'invitation : <see cref="InviteCodeKey"/>),
/// gardé en base plutôt que dans la configuration du serveur.
/// </summary>
public class SiteSetting
{
    public const string InviteCodeKey = "InviteCode";

    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Rôles des comptes (Identity), exclusifs : Standard pour tous, Administrateur pour ceux qui gèrent le site (page
/// « Administration », qui peut tout ce que peut un Standard). Créés avec la base (identifiants fixes).
/// </summary>
public static class SiteRoles
{
    public const string Standard = "Standard";
    public const string Admin = "Administrateur";
    public const string StandardId = "standard";
    public const string AdminId = "administrateur";

    public static readonly IdentityRole[] All =
    [
        new() { Id = StandardId, Name = Standard, NormalizedName = "STANDARD", ConcurrencyStamp = StandardId },
        new() { Id = AdminId, Name = Admin, NormalizedName = "ADMINISTRATEUR", ConcurrencyStamp = AdminId },
    ];
}
