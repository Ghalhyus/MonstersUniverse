using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MonsutaPotaru.Data;

namespace MonsutaPotaru.Services;

/// <summary>Résultat d'une opération sur un compte, message prêt à afficher.</summary>
public sealed record AccountResult(bool Ok, string Message, ApplicationUser? User = null);

/// <summary>
/// Comptes : inscription (pseudo, adresse mail, mot de passe, code d'invitation), connexion par l'adresse mail, choix du
/// pseudo pour les comptes créés avant lui, changement d'adresse. Le pseudo est le nom d'utilisateur d'Identity : c'est lui
/// qu'affichent le menu et la 3DS. Il est définitif. Un compte d'avant le pseudo a son adresse pour nom d'utilisateur
/// (« @ », interdit dans un pseudo) : le site lui demande d'en choisir un (<see cref="HasPseudo"/>).
/// </summary>
public sealed class AccountService(UserManager<ApplicationUser> userManager, IDbContextFactory<ApplicationDbContext> dbFactory,
    IConfiguration configuration, ILogger<AccountService> logger)
{
    public const int PseudoMinLength = 3;
    public const int PseudoMaxLength = 20;

    /// <summary>Mauvais mots de passe d'affilée qui bloquent la connexion à un compte.</summary>
    public const int MaxFailedLogins = 5;

    /// <summary>Durée du blocage, levé tout seul.</summary>
    public static readonly TimeSpan LoginLockout = TimeSpan.FromMinutes(5);

    /// <summary>Message de tout échec de connexion : il ne dit pas si l'adresse a un compte, ni si ce compte est bloqué.</summary>
    public static readonly string LoginFailedMessage =
        $"Adresse mail ou mot de passe incorrect. Après {MaxFailedLogins} échecs, la connexion à ce compte est bloquée {LoginLockout.TotalMinutes:0} minutes.";

    /// <summary>Empreinte d'un mot de passe quelconque, vérifiée pour une adresse inconnue (voir <see cref="FindForLoginAsync"/>).</summary>
    private static string? unknownAccountHash;

    /// <summary>Réglages d'Identity du site (aussi ceux des tests).</summary>
    public static void ConfigureIdentity(IdentityOptions options)
    {
        options.SignIn.RequireConfirmedAccount = false; // le site n'envoie pas de mails
        options.User.RequireUniqueEmail = true; // on se connecte avec l'adresse
        options.User.AllowedUserNameCharacters = ""; // tous : le pseudo est vérifié par CheckPseudo (accents, espaces)
        options.Lockout.AllowedForNewUsers = true; // contre les essais de mots de passe (FindForLoginAsync)
        options.Lockout.MaxFailedAccessAttempts = MaxFailedLogins;
        options.Lockout.DefaultLockoutTimeSpan = LoginLockout;
    }

    /// <summary>Le compte a-t-il choisi son pseudo ? Sinon, son nom d'utilisateur est encore son adresse mail.</summary>
    public static bool HasPseudo(string? userName) => !string.IsNullOrEmpty(userName) && !userName.Contains('@');

    /// <summary>Pseudo tel qu'il sera gardé : sans espaces au début ni à la fin, une seule espace entre deux mots.</summary>
    public static string NormalizePseudo(string? pseudo) =>
        string.Join(' ', (pseudo ?? "").Normalize(NormalizationForm.FormC).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Ce qui ne va pas dans un pseudo (déjà normalisé), ou null s'il convient. L'unicité est vérifiée par Identity.</summary>
    public static string? CheckPseudo(string pseudo)
    {
        if (pseudo.Length < PseudoMinLength || pseudo.Length > PseudoMaxLength)
            return $"Le pseudo doit faire de {PseudoMinLength} à {PseudoMaxLength} caractères.";
        foreach (var rune in pseudo.EnumerateRunes())
        {
            if (!Rune.IsLetterOrDigit(rune) && rune.Value is not (' ' or '-' or '.' or '_'))
                return $"Le pseudo ne peut pas contenir « {rune} » : lettres, chiffres, espaces, tirets, points et tirets bas seulement.";
        }
        if (!pseudo.EnumerateRunes().Any(Rune.IsLetterOrDigit))
            return "Le pseudo doit contenir au moins une lettre ou un chiffre.";
        return null;
    }

    /// <summary>Crée un compte (rôle Standard), si le code d'invitation est le bon (en base : page « Administration »).</summary>
    public async Task<AccountResult> RegisterAsync(string pseudo, string email, string password, string inviteCode)
    {
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            if (!AdminService.InviteCodeMatches(await AdminService.ReadInviteCodeAsync(db), inviteCode))
                return new(false, "Code d'invitation invalide.");
        }

        pseudo = NormalizePseudo(pseudo);
        if (CheckPseudo(pseudo) is { } error)
            return new(false, error);

        var user = new ApplicationUser { UserName = pseudo, Email = email.Trim() };
        var result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
            return new(false, Describe(result));
        result = await userManager.AddToRoleAsync(user, SiteRoles.Standard);
        if (!result.Succeeded) // le compte existe : il recevra son rôle d'un administrateur
            logger.LogError("Compte {User} créé sans rôle : {Errors}", user.Id, Describe(result));
        logger.LogInformation("Compte {User} créé.", user.Id);
        await PromoteIfFirstAdminAsync(user, pseudo);
        return new(true, $"Bienvenue, {pseudo} !", user);
    }

    /// <summary>
    /// Le compte de cette adresse, si le mot de passe est le bon et que le compte n'est pas bloqué ; null sinon, sans dire
    /// pourquoi (<see cref="LoginFailedMessage"/>). Chaque mauvais mot de passe compte un échec : au
    /// <see cref="MaxFailedLogins"/>e d'affilée, le compte est bloqué <see cref="LoginLockout"/>, même pour le bon mot de
    /// passe. Un bon mot de passe remet le compteur à zéro. Le mot de passe est toujours vérifié (adresse inconnue
    /// comprise) : la durée de la réponse ne dit pas non plus si l'adresse a un compte.
    /// </summary>
    public async Task<ApplicationUser?> FindForLoginAsync(string email, string password)
    {
        var user = await userManager.FindByEmailAsync(email.Trim());
        if (user is null)
        {
            var unknown = new ApplicationUser();
            unknownAccountHash ??= userManager.PasswordHasher.HashPassword(unknown, Guid.NewGuid().ToString());
            userManager.PasswordHasher.VerifyHashedPassword(unknown, unknownAccountHash, password);
            return null;
        }

        var correct = await userManager.CheckPasswordAsync(user, password);
        if (await userManager.IsLockedOutAsync(user))
        {
            logger.LogWarning("Compte {User} bloqué : connexion refusée.", user.Id);
            return null;
        }
        if (!correct)
        {
            await userManager.AccessFailedAsync(user);
            if (await userManager.IsLockedOutAsync(user))
                logger.LogWarning("Compte {User} : {Count} mauvais mots de passe, connexion bloquée {Minutes} minutes.",
                    user.Id, MaxFailedLogins, LoginLockout.TotalMinutes);
            return null;
        }
        if (await userManager.GetAccessFailedCountAsync(user) > 0)
            await userManager.ResetAccessFailedCountAsync(user);
        return user;
    }

    /// <summary>Pseudo d'un compte créé avant le pseudo. Définitif : refusé si le compte en a déjà un.</summary>
    public async Task<AccountResult> ChoosePseudoAsync(ApplicationUser user, string pseudo)
    {
        if (HasPseudo(user.UserName))
            return new(false, "Ton pseudo est déjà choisi : il ne change plus.");
        pseudo = NormalizePseudo(pseudo);
        if (CheckPseudo(pseudo) is { } error)
            return new(false, error);
        // Vérifié avant : un refus d'Identity laisserait le nouveau nom sur l'objet du compte.
        if (await userManager.FindByNameAsync(pseudo) is not null)
            return new(false, userManager.ErrorDescriber.DuplicateUserName(pseudo).Description);

        var result = await userManager.SetUserNameAsync(user, pseudo);
        if (!result.Succeeded)
            return new(false, Describe(result));
        logger.LogInformation("Compte {User} : pseudo choisi.", user.Id);
        await PromoteIfFirstAdminAsync(user, pseudo);
        return new(true, $"Bienvenue, {pseudo} !", user);
    }

    /// <summary>Change l'adresse mail du compte, après vérification du mot de passe (le site n'envoie pas de lien de confirmation).</summary>
    public async Task<AccountResult> ChangeEmailAsync(ApplicationUser user, string newEmail, string password)
    {
        if (!await userManager.CheckPasswordAsync(user, password))
            return new(false, "Mot de passe incorrect.");
        newEmail = newEmail.Trim();
        if (string.Equals(newEmail, user.Email, StringComparison.OrdinalIgnoreCase))
            return new(false, "C'est déjà ton adresse mail.");
        // Vérifié avant : un refus d'Identity laisserait la nouvelle adresse sur l'objet du compte.
        if (await userManager.FindByEmailAsync(newEmail) is not null)
            return new(false, userManager.ErrorDescriber.DuplicateEmail(newEmail).Description);

        var result = await userManager.SetEmailAsync(user, newEmail);
        if (!result.Succeeded)
            return new(false, Describe(result));
        logger.LogInformation("Compte {User} : adresse mail changée.", user.Id);
        return new(true, "Ton adresse mail a été changée : utilise-la pour te connecter.", user);
    }

    /// <summary>Thème du compte (clé de <see cref="SiteThemes"/>), Classique sans compte ou sans choix.</summary>
    public async Task<string> ThemeAsync(string? userId)
    {
        if (string.IsNullOrEmpty(userId))
            return SiteThemes.Classic;
        await using var db = await dbFactory.CreateDbContextAsync();
        return SiteThemes.Normalize(await db.Users.Where(u => u.Id == userId).Select(u => u.Theme).FirstOrDefaultAsync());
    }

    /// <summary>Change le thème du compte (le thème par défaut n'est pas enregistré : il suivra un changement de défaut).</summary>
    public async Task<AccountResult> SetThemeAsync(string userId, string key)
    {
        if (SiteThemes.All.FirstOrDefault(t => t.Key == key) is not { } theme)
            return new(false, "Ce thème n'existe pas.");
        await using var db = await dbFactory.CreateDbContextAsync();
        var stored = theme.Key == SiteThemes.Classic ? null : theme.Key;
        if (await db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(u => u.SetProperty(x => x.Theme, stored)) == 0)
            return new(false, "Ce compte n'existe plus.");
        return new(true, $"Thème « {theme.Name} » appliqué.");
    }

    /// <summary>Premier administrateur du site, s'il porte le pseudo de <c>Bank:AdminPseudo</c> (voir <see cref="AdminService"/>).</summary>
    private async Task PromoteIfFirstAdminAsync(ApplicationUser user, string pseudo)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (await AdminService.PromoteFirstAdminAsync(db, configuration, user.Id, pseudo))
            logger.LogInformation("Compte {User} ({Pseudo}) : administrateur (Bank:AdminPseudo).", user.Id, pseudo);
    }

    public static string Describe(IdentityResult result) => string.Join(" ", result.Errors.Select(e => e.Description));

    /// <summary>Message d'erreur pour <c>StatusMessage</c> (rouge s'il commence par « Erreur ») : « Erreur : mot de passe incorrect. »</summary>
    public static string AsError(string message) =>
        "Erreur : " + (message.Length == 0 ? message : char.ToLowerInvariant(message[0]) + message[1..]);
}

/// <summary>Messages d'erreur d'Identity en français (ceux que le site peut afficher).</summary>
public sealed class FrenchIdentityErrorDescriber : IdentityErrorDescriber
{
    private static IdentityError Error(string code, string description) => new() { Code = code, Description = description };

    public override IdentityError DefaultError() => Error(nameof(DefaultError), "Une erreur inattendue s'est produite : réessaie.");
    public override IdentityError ConcurrencyFailure() =>
        Error(nameof(ConcurrencyFailure), "Ton compte vient d'être modifié ailleurs : recharge la page et recommence.");
    public override IdentityError PasswordMismatch() => Error(nameof(PasswordMismatch), "Mot de passe incorrect.");
    public override IdentityError InvalidToken() => Error(nameof(InvalidToken), "Lien invalide ou expiré.");
    public override IdentityError InvalidUserName(string? userName) => Error(nameof(InvalidUserName), $"Le pseudo « {userName} » n'est pas valide.");
    public override IdentityError InvalidEmail(string? email) => Error(nameof(InvalidEmail), $"L'adresse mail « {email} » n'est pas valide.");
    public override IdentityError DuplicateUserName(string userName) => Error(nameof(DuplicateUserName), $"Le pseudo « {userName} » est déjà pris.");
    public override IdentityError DuplicateEmail(string email) => Error(nameof(DuplicateEmail), $"L'adresse mail « {email} » a déjà un compte.");
    public override IdentityError UserAlreadyHasPassword() => Error(nameof(UserAlreadyHasPassword), "Ce compte a déjà un mot de passe.");
    public override IdentityError PasswordTooShort(int length) =>
        Error(nameof(PasswordTooShort), $"Le mot de passe doit faire au moins {length} caractères.");
    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) =>
        Error(nameof(PasswordRequiresUniqueChars), $"Le mot de passe doit contenir au moins {uniqueChars} caractères différents.");
    public override IdentityError PasswordRequiresNonAlphanumeric() =>
        Error(nameof(PasswordRequiresNonAlphanumeric), "Le mot de passe doit contenir au moins un caractère spécial (ni lettre ni chiffre).");
    public override IdentityError PasswordRequiresDigit() => Error(nameof(PasswordRequiresDigit), "Le mot de passe doit contenir au moins un chiffre.");
    public override IdentityError PasswordRequiresLower() => Error(nameof(PasswordRequiresLower), "Le mot de passe doit contenir au moins une minuscule.");
    public override IdentityError PasswordRequiresUpper() => Error(nameof(PasswordRequiresUpper), "Le mot de passe doit contenir au moins une majuscule.");
}
