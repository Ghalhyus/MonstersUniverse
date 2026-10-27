using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MonsutaPotaru.Data;
using MonsutaPotaru.Services;

namespace MonsutaPotaru.Tests;

/// <summary>
/// Comptes : pseudo (règles, unicité, choix pour les anciens comptes), connexion par l'adresse mail, changement d'adresse,
/// rôle Standard et premier administrateur (pseudo de <c>Bank:AdminPseudo</c>).
/// </summary>
public sealed class AccountServiceTests : IDisposable
{
    private const string Invite = "pokeamis";
    private const string Password = "Pikachu-25";
    private const string AdminPseudo = "Pierre";

    private readonly TestDatabase _db = new();
    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;
    private readonly AccountService _accounts;
    private readonly UserManager<ApplicationUser> _users;

    public AccountServiceTests()
    {
        // Identity réglé comme le site (Program.cs), sur la base de test.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Bank:AdminPseudo"] = AdminPseudo }).Build());
        services.AddScoped(_ => _db.NewContext());
        services.AddSingleton(_db.Factory);
        services.AddIdentityCore<ApplicationUser>(AccountService.ConfigureIdentity)
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddErrorDescriber<FrenchIdentityErrorDescriber>();
        services.AddScoped<AccountService>();
        _provider = services.BuildServiceProvider();
        _scope = _provider.CreateScope();
        _accounts = _scope.ServiceProvider.GetRequiredService<AccountService>();
        _users = _scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        // Code d'invitation en base, comme après le premier démarrage (AdminService.InitializeAsync).
        using var db = _db.NewContext();
        db.SiteSettings.Add(new SiteSetting { Key = SiteSetting.InviteCodeKey, Value = Invite });
        db.SaveChanges();
    }

    private async Task<string[]> RolesOf(string userId)
    {
        await using var db = _db.NewContext();
        return await db.UserRoles.Where(r => r.UserId == userId).Select(r => r.RoleId).ToArrayAsync();
    }

    public void Dispose()
    {
        _scope.Dispose();
        _provider.Dispose();
        _db.Dispose();
    }

    [Theory]
    [InlineData("Sacha")]
    [InlineData("Ghal-81")]
    [InlineData("Le Grand.Ash_2")]
    [InlineData("Éloïse")]
    [InlineData("サトシ")]
    [InlineData("123")]
    public void CheckPseudo_Accepts(string pseudo) => Assert.Null(AccountService.CheckPseudo(pseudo));

    [Theory]
    [InlineData("ab", "de 3 à 20 caractères")]
    [InlineData("Dresseur de Kanto 151", "de 3 à 20 caractères")]
    [InlineData("sacha@kanto.fr", "« @ »")] // un pseudo n'a jamais d'arobase : c'est ce qui distingue les anciens comptes
    [InlineData("Sacha😀", "« 😀 »")]
    [InlineData("Sa/cha", "« / »")]
    [InlineData("-._", "au moins une lettre ou un chiffre")]
    public void CheckPseudo_Refuses(string pseudo, string expected) => Assert.Contains(expected, AccountService.CheckPseudo(pseudo));

    [Fact]
    public void NormalizePseudo_TrimsAndJoinsWordsWithOneSpace() =>
        Assert.Equal("Le Grand Ash", AccountService.NormalizePseudo("  Le   Grand\tAsh "));

    [Fact]
    public async Task Register_ThenLogIn_WithTheEmail_NotThePseudo()
    {
        Assert.Equal("Code d'invitation invalide.", (await _accounts.RegisterAsync("Sacha", "sacha@kanto.fr", Password, "mauvais")).Message);
        Assert.Equal("Code d'invitation invalide.", (await _accounts.RegisterAsync("Sacha", "sacha@kanto.fr", Password, "")).Message);

        var created = await _accounts.RegisterAsync("  Sacha  ", "sacha@kanto.fr", Password, " Poke-Amis "); // majuscules, tirets, espaces : peu importe
        Assert.True(created.Ok, created.Message);
        Assert.Equal(("Sacha", "sacha@kanto.fr"), (created.User!.UserName, created.User.Email));
        Assert.True(AccountService.HasPseudo(created.User.UserName));
        Assert.Equal([SiteRoles.StandardId], await RolesOf(created.User.Id));

        Assert.Equal(created.User.Id, (await _accounts.FindForLoginAsync(" SACHA@kanto.fr ", Password))?.Id);
        Assert.Null(await _accounts.FindForLoginAsync("Sacha", Password)); // le pseudo ne sert pas à se connecter
        Assert.Null(await _accounts.FindForLoginAsync("sacha@kanto.fr", "Mauvais-1"));
        Assert.Null(await _accounts.FindForLoginAsync("ondine@azuria.fr", Password));
    }

    [Fact]
    public async Task Login_LocksTheAccount_AfterFiveWrongPasswordsInARow()
    {
        var sacha = (await _accounts.RegisterAsync("Sacha", "sacha@kanto.fr", Password, Invite)).User!;
        async Task<int> Failures() => await _users.GetAccessFailedCountAsync((await _users.FindByIdAsync(sacha.Id))!);

        // 4 échecs, puis le bon mot de passe : le compteur repart à zéro.
        for (int i = 0; i < AccountService.MaxFailedLogins - 1; i++)
            Assert.Null(await _accounts.FindForLoginAsync("sacha@kanto.fr", "Mauvais-1"));
        Assert.Equal(AccountService.MaxFailedLogins - 1, await Failures());
        Assert.NotNull(await _accounts.FindForLoginAsync("sacha@kanto.fr", Password));
        Assert.Equal(0, await Failures());

        // 5 d'affilée : bloqué, même avec le bon mot de passe.
        for (int i = 0; i < AccountService.MaxFailedLogins; i++)
            Assert.Null(await _accounts.FindForLoginAsync("sacha@kanto.fr", "Mauvais-1"));
        Assert.Null(await _accounts.FindForLoginAsync("sacha@kanto.fr", Password));
        var lockedUntil = await _users.GetLockoutEndDateAsync((await _users.FindByIdAsync(sacha.Id))!);
        Assert.InRange(lockedUntil!.Value - DateTimeOffset.UtcNow, AccountService.LoginLockout - TimeSpan.FromMinutes(1), AccountService.LoginLockout);

        // Blocage passé : le bon mot de passe marche de nouveau.
        await _users.SetLockoutEndDateAsync((await _users.FindByIdAsync(sacha.Id))!, DateTimeOffset.UtcNow.AddSeconds(-1));
        Assert.Equal(sacha.Id, (await _accounts.FindForLoginAsync("sacha@kanto.fr", Password))?.Id);
    }

    [Fact]
    public void LoginLimiter_RefusesTheEleventhAttemptOfAMinute_PerAddress()
    {
        using var limiter = new LoginLimiter();
        for (int i = 0; i < LoginLimiter.AttemptsPerMinute; i++)
            Assert.True(limiter.TryAttempt("203.0.113.7"));
        Assert.False(limiter.TryAttempt("203.0.113.7"));
        Assert.True(limiter.TryAttempt("198.51.100.2")); // une autre adresse n'est pas gênée
        Assert.True(limiter.TryAttempt(null));
    }

    [Fact]
    public async Task Register_Refuses_ATakenPseudoOrEmail_AndAWeakPassword_InFrench()
    {
        Assert.True((await _accounts.RegisterAsync("Sacha", "sacha@kanto.fr", Password, Invite)).Ok);

        Assert.Equal("Le pseudo « sacha » est déjà pris.", (await _accounts.RegisterAsync("sacha", "autre@kanto.fr", Password, Invite)).Message);
        Assert.Equal("L'adresse mail « Sacha@Kanto.fr » a déjà un compte.",
            (await _accounts.RegisterAsync("Ondine", "Sacha@Kanto.fr", Password, Invite)).Message);
        var weak = await _accounts.RegisterAsync("Ondine", "ondine@azuria.fr", "psykokwak", Invite);
        Assert.False(weak.Ok);
        Assert.Contains("au moins un chiffre", weak.Message);
        Assert.Contains("au moins une majuscule", weak.Message);
        Assert.Contains("Le pseudo doit faire", (await _accounts.RegisterAsync("O", "ondine@azuria.fr", Password, Invite)).Message);
    }

    [Fact]
    public async Task AnOldAccount_ChoosesItsPseudo_Once()
    {
        // Compte d'avant le pseudo : l'adresse comme nom d'utilisateur.
        var old = new ApplicationUser { UserName = "pierre@argenta.fr", Email = "pierre@argenta.fr" };
        Assert.True((await _users.CreateAsync(old, Password)).Succeeded);
        Assert.True((await _accounts.RegisterAsync("Sacha", "sacha@kanto.fr", Password, Invite)).Ok);
        Assert.False(AccountService.HasPseudo(old.UserName));

        Assert.Equal("Le pseudo « SACHA » est déjà pris.", (await _accounts.ChoosePseudoAsync(old, "SACHA")).Message);
        var chosen = await _accounts.ChoosePseudoAsync(old, "Pierre");
        Assert.True(chosen.Ok, chosen.Message);
        Assert.Equal([SiteRoles.AdminId], await RolesOf(old.Id)); // le pseudo de Bank:AdminPseudo, et pas encore d'administrateur
        Assert.Equal("Pierre", (await _users.FindByIdAsync(old.Id))!.UserName);
        Assert.Equal(old.Id, (await _accounts.FindForLoginAsync("pierre@argenta.fr", Password))?.Id); // toujours par l'adresse

        Assert.False((await _accounts.ChoosePseudoAsync(old, "Pierre2")).Ok); // définitif
        Assert.Equal("Pierre", (await _users.FindByIdAsync(old.Id))!.UserName);
    }

    [Fact]
    public async Task ChangeEmail_NeedsThePassword_AndAFreeAddress()
    {
        var sacha = (await _accounts.RegisterAsync("Sacha", "sacha@kanto.fr", Password, Invite)).User!;
        Assert.True((await _accounts.RegisterAsync("Ondine", "ondine@azuria.fr", Password, Invite)).Ok);

        Assert.Equal("Mot de passe incorrect.", (await _accounts.ChangeEmailAsync(sacha, "sacha@johto.fr", "Mauvais-1")).Message);
        Assert.Equal("L'adresse mail « ondine@azuria.fr » a déjà un compte.",
            (await _accounts.ChangeEmailAsync(sacha, "ondine@azuria.fr", Password)).Message);
        Assert.False((await _accounts.ChangeEmailAsync(sacha, "SACHA@kanto.fr", Password)).Ok); // la même

        var changed = await _accounts.ChangeEmailAsync(sacha, " sacha@johto.fr ", Password);
        Assert.True(changed.Ok, changed.Message);
        Assert.Equal(sacha.Id, (await _accounts.FindForLoginAsync("sacha@johto.fr", Password))?.Id);
        Assert.Null(await _accounts.FindForLoginAsync("sacha@kanto.fr", Password));
        Assert.Equal("Sacha", (await _users.FindByIdAsync(sacha.Id))!.UserName); // le pseudo ne bouge pas
    }
    [Fact]
    public async Task TheAdminPseudo_BecomesAdmin_OnlyWhileThereIsNone()
    {
        var pierre = (await _accounts.RegisterAsync("pierre", "pierre@argenta.fr", Password, Invite)).User!; // majuscules indifférentes
        Assert.Equal([SiteRoles.AdminId], await RolesOf(pierre.Id));
        Assert.True(await _db.Admin().IsAdminAsync(pierre.Id));

        var sacha = (await _accounts.RegisterAsync("Sacha", "sacha@kanto.fr", Password, Invite)).User!;
        Assert.Equal([SiteRoles.StandardId], await RolesOf(sacha.Id));

        // Un administrateur existe déjà : le pseudo de la configuration ne donne plus rien.
        await using var db = _db.NewContext();
        Assert.False(await AdminService.PromoteFirstAdminAsync(db, _scope.ServiceProvider.GetRequiredService<IConfiguration>(), sacha.Id, AdminPseudo));
        Assert.Equal([SiteRoles.StandardId], await RolesOf(sacha.Id));
    }
    [Fact]
    public async Task Theme_IsClassicByDefault_AndFollowsTheAccount()
    {
        var sacha = (await _accounts.RegisterAsync("Sacha", "sacha@kanto.fr", Password, Invite)).User!;
        Assert.Equal(SiteThemes.Classic, await _accounts.ThemeAsync(sacha.Id));
        Assert.Equal(SiteThemes.Classic, await _accounts.ThemeAsync(null)); // pas connecté

        Assert.True((await _accounts.SetThemeAsync(sacha.Id, SiteThemes.Pokedex)).Ok);
        Assert.Equal(SiteThemes.Pokedex, await _accounts.ThemeAsync(sacha.Id));
        Assert.False((await _accounts.SetThemeAsync(sacha.Id, "inconnu")).Ok);
        Assert.Equal(SiteThemes.Pokedex, await _accounts.ThemeAsync(sacha.Id));

        Assert.True((await _accounts.SetThemeAsync(sacha.Id, SiteThemes.Classic)).Ok);
        await using var db = _db.NewContext();
        Assert.Null(await db.Users.Where(u => u.Id == sacha.Id).Select(u => u.Theme).SingleAsync()); // le défaut n'est pas enregistré
    }
}
