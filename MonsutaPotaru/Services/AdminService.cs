using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using MonsutaPotaru.Data;

namespace MonsutaPotaru.Services;

/// <summary>Compte vu par la page « Administration » : pseudo et rôle seulement (l'adresse mail reste cachée).</summary>
/// <param name="Pseudo">Null pour un compte qui n'a pas encore choisi son pseudo.</param>
public sealed record AdminAccount(string Id, string? Pseudo, bool IsAdmin);

/// <summary>Résultat d'une action d'administration, message prêt à afficher.</summary>
public sealed record AdminResult(bool Ok, string Message);

/// <summary>
/// Administration du site : rôles des comptes (<see cref="SiteRoles"/>, exclusifs) et code d'invitation (gardé en base,
/// <see cref="SiteSetting.InviteCodeKey"/>). Chaque action revérifie en base que l'appelant est administrateur : le cookie
/// de connexion peut garder un rôle retiré jusqu'à sa prochaine vérification. Un changement de rôle renouvelle le
/// tampon de sécurité du compte : il est déconnecté à la prochaine vérification (30 minutes au plus) et retrouve ses
/// nouveaux droits en se reconnectant.
/// </summary>
public sealed class AdminService(IDbContextFactory<ApplicationDbContext> dbFactory, ILogger<AdminService> logger)
{
    public const string NotAdminMessage = "Tu n'es pas administrateur.";

    /// <summary>
    /// Au démarrage : code d'invitation en base (repris de <c>Bank:InviteCode</c> la première fois, sinon tiré au hasard),
    /// et premier administrateur (le compte du pseudo <c>Bank:AdminPseudo</c>, seulement s'il n'y en a encore aucun : un
    /// rôle retiré depuis le site ne revient pas au redémarrage). Si ce compte n'existe pas encore, ou n'a pas encore
    /// choisi son pseudo, il le devient à son inscription ou au choix du pseudo (<see cref="PromoteFirstAdminAsync"/>).
    /// </summary>
    public static async Task InitializeAsync(ApplicationDbContext db, IConfiguration configuration, ILogger logger)
    {
        if (!await db.SiteSettings.AnyAsync(s => s.Key == SiteSetting.InviteCodeKey))
        {
            var configured = configuration["Bank:InviteCode"]?.Trim();
            db.SiteSettings.Add(new SiteSetting
            {
                Key = SiteSetting.InviteCodeKey,
                Value = string.IsNullOrEmpty(configured) ? NewInviteCode() : configured,
                UpdatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
            logger.LogInformation(string.IsNullOrEmpty(configured)
                ? "Code d'invitation tiré au hasard (visible dans « Administration »)."
                : "Code d'invitation repris de la configuration (Bank:InviteCode), puis gardé en base.");
        }

        if (await db.UserRoles.AnyAsync(r => r.RoleId == SiteRoles.AdminId))
            return;
        var pseudo = AccountService.NormalizePseudo(configuration["Bank:AdminPseudo"]);
        if (pseudo.Length == 0)
        {
            logger.LogWarning("Aucun administrateur : indique le pseudo d'un compte dans Bank:AdminPseudo (ADMIN_PSEUDO dans .env).");
            return;
        }
        var normalized = pseudo.ToUpperInvariant(); // comme le NormalizedUserName d'Identity
        var userId = await db.Users.Where(u => u.NormalizedUserName == normalized).Select(u => u.Id).FirstOrDefaultAsync();
        if (userId is null)
        {
            logger.LogWarning("Aucun administrateur, et aucun compte n'a le pseudo « {Pseudo} » (Bank:AdminPseudo).", pseudo);
            return;
        }
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await SetRoleAsync(db, userId, SiteRoles.AdminId);
            await tx.CommitAsync();
        }
        logger.LogInformation("Compte {User} ({Pseudo}) : administrateur (Bank:AdminPseudo).", userId, pseudo);
    }

    /// <summary>
    /// Compte qui vient de prendre son pseudo (inscription, choix du pseudo) : administrateur s'il porte celui de
    /// <c>Bank:AdminPseudo</c> et que le site n'en a encore aucun. Sans renouveler son tampon de sécurité : il est en train
    /// de se connecter, et sa connexion lira son rôle.
    /// </summary>
    public static async Task<bool> PromoteFirstAdminAsync(ApplicationDbContext db, IConfiguration configuration, string userId, string pseudo)
    {
        var wanted = AccountService.NormalizePseudo(configuration["Bank:AdminPseudo"]);
        if (wanted.Length == 0 || wanted.ToUpperInvariant() != AccountService.NormalizePseudo(pseudo).ToUpperInvariant())
            return false;
        await using var tx = await db.Database.BeginTransactionAsync();
        if (await db.UserRoles.AnyAsync(r => r.RoleId == SiteRoles.AdminId))
            return false;
        await SetRoleAsync(db, userId, SiteRoles.AdminId, renewSecurityStamp: false);
        await tx.CommitAsync();
        return true;
    }

    /// <summary>Nouveau code d'invitation au hasard : « ABCD-EFGH », sans caractères ambigus (comme le code de liaison 3DS).</summary>
    public static string NewInviteCode() =>
        SyncService.FormatCode(new string(RandomNumberGenerator.GetItems<char>(SyncService.PairingAlphabet, 8)));

    /// <summary>Le code tapé est-il le code d'invitation ? Sans tenir compte des majuscules, tirets et espaces.</summary>
    public static bool InviteCodeMatches(string? expected, string? typed)
    {
        var code = SyncService.NormalizeCode(expected);
        return code.Length > 0 && code == SyncService.NormalizeCode(typed);
    }

    /// <summary>Code d'invitation actuel (null s'il n'a pas encore été initialisé).</summary>
    public static Task<string?> ReadInviteCodeAsync(ApplicationDbContext db) =>
        db.SiteSettings.Where(s => s.Key == SiteSetting.InviteCodeKey).Select(s => s.Value).FirstOrDefaultAsync();

    public async Task<bool> IsAdminAsync(string userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await IsAdminAsync(db, userId);
    }

    private static Task<bool> IsAdminAsync(ApplicationDbContext db, string userId) =>
        db.UserRoles.AnyAsync(r => r.UserId == userId && r.RoleId == SiteRoles.AdminId);

    /// <summary>Code d'invitation, pour un administrateur (null sinon).</summary>
    public async Task<string?> InviteCodeAsync(string callerId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await IsAdminAsync(db, callerId) ? await ReadInviteCodeAsync(db) : null;
    }

    /// <summary>Tire un nouveau code d'invitation : l'ancien ne permet plus de s'inscrire.</summary>
    public async Task<AdminResult> RegenerateInviteCodeAsync(string callerId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (!await IsAdminAsync(db, callerId))
            return new(false, NotAdminMessage);
        var code = NewInviteCode();
        var changed = await db.SiteSettings.Where(s => s.Key == SiteSetting.InviteCodeKey)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.Value, code).SetProperty(s => s.UpdatedAt, DateTime.UtcNow));
        if (changed == 0)
            db.SiteSettings.Add(new SiteSetting { Key = SiteSetting.InviteCodeKey, Value = code, UpdatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        logger.LogInformation("Compte {User} : nouveau code d'invitation.", callerId);
        return new(true, $"Nouveau code d'invitation : {code}. L'ancien ne marche plus.");
    }

    /// <summary>Comptes du site, par pseudo (ceux sans pseudo à la fin), pour un administrateur (liste vide sinon).</summary>
    public async Task<List<AdminAccount>> ListAccountsAsync(string callerId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (!await IsAdminAsync(db, callerId))
            return [];
        var admins = (await db.UserRoles.Where(r => r.RoleId == SiteRoles.AdminId).Select(r => r.UserId).ToListAsync()).ToHashSet();
        var users = await db.Users.Select(u => new { u.Id, u.UserName }).ToListAsync();
        return users
            .Select(u => new AdminAccount(u.Id, AccountService.HasPseudo(u.UserName) ? u.UserName : null, admins.Contains(u.Id)))
            .OrderBy(a => a.Pseudo is null)
            .ThenBy(a => a.Pseudo, StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("fr-FR"), ignoreCase: true))
            .ToList();
    }

    /// <summary>
    /// Donne ou retire le rôle administrateur. On ne se le retire pas soi-même, et il reste toujours au moins un
    /// administrateur (vérifié dans la même requête que le retrait : deux administrateurs qui se le retirent en même temps
    /// n'en laissent pas zéro).
    /// </summary>
    public async Task<AdminResult> SetAdminAsync(string callerId, string targetId, bool admin)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (!await IsAdminAsync(db, callerId))
            return new(false, NotAdminMessage);
        var target = await db.Users.Where(u => u.Id == targetId).Select(u => new { u.UserName }).FirstOrDefaultAsync();
        if (target is null)
            return new(false, "Ce compte n'existe plus.");
        var name = AccountService.HasPseudo(target.UserName) ? target.UserName : "Ce compte";
        var isAdmin = await IsAdminAsync(db, targetId);

        if (admin)
        {
            if (isAdmin)
                return new(false, $"{name} est déjà administrateur.");
            await using (var promote = await db.Database.BeginTransactionAsync())
            {
                await SetRoleAsync(db, targetId, SiteRoles.AdminId);
                await promote.CommitAsync();
            }
            logger.LogInformation("Compte {User} : rôle administrateur donné au compte {Target}.", callerId, targetId);
            return new(true, $"{name} est maintenant administrateur (effectif à sa prochaine connexion).");
        }

        if (targetId == callerId)
            return new(false, "Tu ne peux pas te retirer ce rôle toi-même : demande à un autre administrateur.");
        if (!isAdmin)
            return new(false, $"{name} n'est pas administrateur.");
        await using var tx = await db.Database.BeginTransactionAsync();
        var removed = await db.UserRoles
            .Where(r => r.UserId == targetId && r.RoleId == SiteRoles.AdminId
                && db.UserRoles.Any(c => c.UserId == callerId && c.RoleId == SiteRoles.AdminId)
                && db.UserRoles.Count(c => c.RoleId == SiteRoles.AdminId) > 1)
            .ExecuteDeleteAsync();
        if (removed == 0)
            return new(false, "Impossible : il doit toujours rester au moins un administrateur.");
        await SetRoleAsync(db, targetId, SiteRoles.StandardId);
        await tx.CommitAsync();
        logger.LogInformation("Compte {User} : rôle administrateur retiré au compte {Target}.", callerId, targetId);
        return new(true, $"{name} n'est plus administrateur (effectif à sa prochaine connexion, 30 minutes au plus).");
    }

    /// <summary>Remplace le rôle du compte (rôles exclusifs) et renouvelle son tampon de sécurité (reconnexion).</summary>
    private static async Task SetRoleAsync(ApplicationDbContext db, string userId, string roleId, bool renewSecurityStamp = true)
    {
        await db.UserRoles.Where(r => r.UserId == userId).ExecuteDeleteAsync();
        db.UserRoles.Add(new() { UserId = userId, RoleId = roleId });
        await db.SaveChangesAsync();
        if (!renewSecurityStamp)
            return;
        var securityStamp = Guid.NewGuid().ToString("N").ToUpperInvariant(); // calculés ici : EF ne traduit pas NewGuid
        var concurrencyStamp = Guid.NewGuid().ToString();
        await db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(u => u
            .SetProperty(x => x.SecurityStamp, securityStamp)
            .SetProperty(x => x.ConcurrencyStamp, concurrencyStamp));
    }
}
