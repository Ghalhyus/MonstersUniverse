using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MonsutaPotaru.Data;
using MonsutaPotaru.Services;

namespace MonsutaPotaru.Tests;

/// <summary>Comptes : pseudo (règles, unicité, choix pour les anciens comptes), connexion par l'adresse mail, changement d'adresse.</summary>
public sealed class AccountServiceTests : IDisposable
{
    private const string Invite = "pokeamis";
    private const string Password = "Pikachu-25";

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
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Bank:InviteCode"] = Invite }).Build());
        services.AddScoped(_ => _db.NewContext());
        services.AddIdentityCore<ApplicationUser>(AccountService.ConfigureIdentity)
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddErrorDescriber<FrenchIdentityErrorDescriber>();
        services.AddScoped<AccountService>();
        _provider = services.BuildServiceProvider();
        _scope = _provider.CreateScope();
        _accounts = _scope.ServiceProvider.GetRequiredService<AccountService>();
        _users = _scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
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

        var created = await _accounts.RegisterAsync("  Sacha  ", "sacha@kanto.fr", Password, $" {Invite} ");
        Assert.True(created.Ok, created.Message);
        Assert.Equal(("Sacha", "sacha@kanto.fr"), (created.User!.UserName, created.User.Email));
        Assert.True(AccountService.HasPseudo(created.User.UserName));

        Assert.Equal(created.User.Id, (await _accounts.FindForLoginAsync(" SACHA@kanto.fr ", Password))?.Id);
        Assert.Null(await _accounts.FindForLoginAsync("Sacha", Password)); // le pseudo ne sert pas à se connecter
        Assert.Null(await _accounts.FindForLoginAsync("sacha@kanto.fr", "Mauvais-1"));
        Assert.Null(await _accounts.FindForLoginAsync("ondine@azuria.fr", Password));
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
}
