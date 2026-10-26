using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MonsutaPotaru.Data;
using MonsutaPotaru.Services;

namespace MonsutaPotaru.Tests;

/// <summary>Administration : code d'invitation en base, premier administrateur, rôles (garde-fous), liste des comptes.</summary>
public sealed class AdminServiceTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly AdminService _admin;

    public AdminServiceTests()
    {
        _admin = _db.Admin();
        using var db = _db.NewContext();
        // Comptes comme Identity les enregistre (nom normalisé) ; « ancien » n'a pas encore choisi son pseudo.
        foreach (var (id, name) in new[] { ("sacha", "Sacha"), ("ondine", "Ondine"), ("pierre", "Pierre"), ("ancien", "ancien@kanto.fr") })
        {
            db.Users.Add(new ApplicationUser { Id = id, UserName = name, NormalizedUserName = name.ToUpperInvariant(), SecurityStamp = "TAMPON" });
            db.UserRoles.Add(new() { UserId = id, RoleId = SiteRoles.StandardId });
        }
        db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private static IConfiguration Config(string? invite = null, string? admin = null) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Bank:InviteCode"] = invite, ["Bank:AdminPseudo"] = admin }).Build();

    private async Task Initialize(IConfiguration config)
    {
        await using var db = _db.NewContext();
        await AdminService.InitializeAsync(db, config, NullLogger.Instance);
    }

    private async Task<string[]> RolesOf(string userId)
    {
        await using var db = _db.NewContext();
        return await db.UserRoles.Where(r => r.UserId == userId).Select(r => r.RoleId).ToArrayAsync();
    }

    [Fact]
    public async Task Initialize_TakesTheConfiguredInviteCode_ThenKeepsTheOneInTheDatabase()
    {
        await Initialize(Config(invite: " pokeamis ", admin: "SACHA"));
        Assert.Equal("pokeamis", await _admin.InviteCodeAsync("sacha"));
        Assert.Equal([SiteRoles.AdminId], await RolesOf("sacha")); // majuscules indifférentes

        await Initialize(Config(invite: "autre", admin: "Ondine")); // redémarrage avec d'autres réglages
        Assert.Equal("pokeamis", await _admin.InviteCodeAsync("sacha"));
        Assert.Equal([SiteRoles.StandardId], await RolesOf("ondine")); // il y a déjà un administrateur
    }

    [Fact]
    public async Task Initialize_WithoutConfiguration_DrawsAnInviteCode_AndNamesNoAdmin()
    {
        await Initialize(Config());
        await using var db = _db.NewContext();
        Assert.Matches("^[A-Z2-9]{4}-[A-Z2-9]{4}$", await AdminService.ReadInviteCodeAsync(db));
        Assert.False(await db.UserRoles.AnyAsync(r => r.RoleId == SiteRoles.AdminId));

        await Initialize(Config(admin: "Inconnu")); // aucun compte de ce pseudo : rien
        Assert.False(await db.UserRoles.AnyAsync(r => r.RoleId == SiteRoles.AdminId));
    }

    [Theory]
    [InlineData("ABCD-EFGH", "abcd efgh", true)]
    [InlineData("ABCD-EFGH", "ABCDEFGH", true)]
    [InlineData("pokeamis", "PokeAmis", true)]
    [InlineData("ABCD-EFGH", "ABCD-EFGJ", false)]
    [InlineData("", "", false)]
    [InlineData(null, "", false)]
    public void InviteCodeMatches(string? expected, string typed, bool matches) =>
        Assert.Equal(matches, AdminService.InviteCodeMatches(expected, typed));

    [Fact]
    public async Task OnlyAnAdmin_SeesAndRegeneratesTheInviteCode()
    {
        await Initialize(Config(invite: "pokeamis", admin: "Sacha"));

        Assert.Null(await _admin.InviteCodeAsync("ondine"));
        Assert.Equal(AdminService.NotAdminMessage, (await _admin.RegenerateInviteCodeAsync("ondine")).Message);
        Assert.Empty(await _admin.ListAccountsAsync("ondine"));

        var regenerated = await _admin.RegenerateInviteCodeAsync("sacha");
        Assert.True(regenerated.Ok, regenerated.Message);
        var code = await _admin.InviteCodeAsync("sacha");
        Assert.Matches("^[A-Z2-9]{4}-[A-Z2-9]{4}$", code);
        Assert.Contains(code!, regenerated.Message);
    }

    [Fact]
    public async Task ListAccounts_ShowsPseudoAndRole_WithoutEmail()
    {
        await Initialize(Config(admin: "Sacha"));
        var accounts = await _admin.ListAccountsAsync("sacha");
        Assert.Equal([("Ondine", false), ("Pierre", false), ("Sacha", true), (null, false)],
            accounts.Select(a => (a.Pseudo, a.IsAdmin)).ToArray()); // par pseudo, ceux sans pseudo à la fin
    }

    [Fact]
    public async Task SetAdmin_GivesAndRemovesTheRole_WithItsSafeguards()
    {
        await Initialize(Config(admin: "Sacha"));

        Assert.Equal(AdminService.NotAdminMessage, (await _admin.SetAdminAsync("ondine", "ondine", true)).Message);
        Assert.False((await _admin.SetAdminAsync("sacha", "sacha", false)).Ok); // pas soi-même
        Assert.False((await _admin.SetAdminAsync("sacha", "personne", true)).Ok);

        Assert.True((await _admin.SetAdminAsync("sacha", "ondine", true)).Ok);
        Assert.Equal([SiteRoles.AdminId], await RolesOf("ondine")); // rôles exclusifs
        Assert.False((await _admin.SetAdminAsync("sacha", "ondine", true)).Ok); // déjà
        await using (var db = _db.NewContext())
            Assert.NotEqual("TAMPON", await db.Users.Where(u => u.Id == "ondine").Select(u => u.SecurityStamp).SingleAsync()); // reconnexion

        // Ondine retire le rôle à Sacha ; Sacha, qui ne l'a plus, ne peut plus rien.
        Assert.True((await _admin.SetAdminAsync("ondine", "sacha", false)).Ok);
        Assert.Equal([SiteRoles.StandardId], await RolesOf("sacha"));
        Assert.Equal(AdminService.NotAdminMessage, (await _admin.SetAdminAsync("sacha", "ondine", false)).Message);
        Assert.False((await _admin.SetAdminAsync("ondine", "pierre", false)).Ok); // Pierre n'est pas administrateur
        Assert.True(await _admin.IsAdminAsync("ondine"));
    }
}
