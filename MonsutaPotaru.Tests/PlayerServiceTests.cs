using Microsoft.EntityFrameworkCore;
using MonsutaPotaru.Data;
using MonsutaPotaru.Services;

namespace MonsutaPotaru.Tests;

/// <summary>Multijoueurs : recherche par pseudo, profils privés, pages publiques.</summary>
public sealed class PlayerServiceTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly PlayerService _players;

    public PlayerServiceTests()
    {
        _players = _db.Players();
        using var db = _db.NewContext();
        // Comptes comme Identity les enregistre (nom normalisé) ; un ancien compte sans pseudo (son adresse).
        db.Users.AddRange(
            User("sacha", "Sacha du Bourg"),
            User("ondine", "Ondine"),
            User("pierre", "Pierre"),
            User("elo", "Éloïse"),
            User("cache", "Sachaprivé", isPrivate: true),
            User("ancien", "sacha@kanto.fr"));
        db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private static ApplicationUser User(string id, string name, bool isPrivate = false) =>
        new() { Id = id, UserName = name, NormalizedUserName = name.ToUpperInvariant(), ProfilePrivate = isPrivate };

    [Fact]
    public async Task Search_FindsPublicPseudos_ByAnyPart_IgnoringCaseAndAccents()
    {
        Assert.Empty(await _players.SearchAsync("s")); // trop court : pas d'annuaire de tous les joueurs
        Assert.Empty(await _players.SearchAsync("   "));
        Assert.Equal(["Sacha du Bourg"], await _players.SearchAsync("SACH")); // ni le profil privé, ni l'adresse d'un ancien compte
        Assert.Equal(["Sacha du Bourg"], await _players.SearchAsync(" du  bourg "));
        Assert.Equal(["Éloïse"], await _players.SearchAsync("eloi"));
        Assert.Empty(await _players.SearchAsync("kanto"));
    }

    [Fact]
    public async Task Search_SortsAlphabetically_IgnoringAccents()
    {
        await using (var db = _db.NewContext())
        {
            db.Users.AddRange(User("zeli", "Zeli"), User("elie", "Élie"));
            await db.SaveChangesAsync();
        }
        Assert.Equal(["Élie", "Zeli"], await _players.SearchAsync("LI")); // « Élie » rangée à « e », avant « Zeli »
    }

    [Fact]
    public async Task Find_OnlyPublicProfiles_AndPublicPages()
    {
        var sacha = await _players.FindAsync("sacha du bourg"); // majuscules indifférentes
        Assert.Equal(("sacha", "Sacha du Bourg", PublicPages.All), (sacha?.Id, sacha?.Pseudo, sacha?.Pages));

        Assert.Null(await _players.FindAsync("Sachaprivé"));
        Assert.Null(await _players.FindAsync("sacha@kanto.fr")); // ancien compte sans pseudo
        Assert.Null(await _players.FindAsync("Inconnu"));
        Assert.Null(await _players.FindAsync(""));

        Assert.True((await _players.SetVisibilityAsync("ondine", false, PublicPages.Park | PublicPages.LivingDex)).Ok);
        Assert.NotNull(await _players.FindAsync("Ondine", PublicPages.Park));
        Assert.Null(await _players.FindAsync("Ondine", PublicPages.Inventory)); // page non publique : comme un inconnu
        Assert.Equal(PublicPages.Park | PublicPages.LivingDex, (await _players.FindAsync("Ondine"))!.Pages);
    }

    [Fact]
    public async Task Visibility_IsSavedOnTheAccount()
    {
        Assert.Equal(new ProfileVisibility(false, PublicPages.All), await _players.VisibilityAsync("pierre")); // défaut

        var hidden = await _players.SetVisibilityAsync("pierre", true, PublicPages.Collection);
        Assert.True(hidden.Ok);
        Assert.Contains("privé", hidden.Message);
        Assert.Equal(new ProfileVisibility(true, PublicPages.Collection), await _players.VisibilityAsync("pierre"));
        Assert.Null(await _players.FindAsync("Pierre", PublicPages.Collection)); // privé : rien, même la page cochée
        Assert.Empty(await _players.SearchAsync("pierre"));

        Assert.True((await _players.SetVisibilityAsync("pierre", false, (PublicPages)0xFF)).Ok); // valeurs inconnues ignorées
        await using var db = _db.NewContext();
        Assert.Equal(PublicPages.All, await db.Users.Where(u => u.Id == "pierre").Select(u => u.PublicPages).SingleAsync());
        Assert.False((await _players.SetVisibilityAsync("personne", false, PublicPages.All)).Ok);
    }

    [Theory]
    [InlineData("Sacha du Bourg", null, "joueurs/Sacha%20du%20Bourg")]
    [InlineData("Éloïse", "parc", "joueurs/%C3%89lo%C3%AFse/parc")]
    public void Link_EscapesThePseudo(string pseudo, string? slug, string expected) =>
        Assert.Equal(expected, PlayerService.Link(pseudo, slug));
}
