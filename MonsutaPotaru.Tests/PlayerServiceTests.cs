using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MonsutaPotaru.Data;
using MonsutaPotaru.Services;

namespace MonsutaPotaru.Tests;

/// <summary>Multijoueurs : recherche par pseudo, visibilité (tout le monde, amis seulement, personne), pages visibles.</summary>
public sealed class PlayerServiceTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly PlayerService _players;
    private readonly FriendService _friends;

    public PlayerServiceTests()
    {
        _players = _db.Players();
        _friends = new FriendService(_db.Factory, NullLogger<FriendService>.Instance);
        using var db = _db.NewContext();
        // Comptes comme Identity les enregistre (nom normalisé) ; un ancien compte sans pseudo (son adresse).
        db.Users.AddRange(
            User("sacha", "Sacha du Bourg"),
            User("ondine", "Ondine"),
            User("pierre", "Pierre"),
            User("elo", "Éloïse"),
            User("cache", "Sachaprivé", ProfileAudience.Nobody),
            User("amie", "Sachamie", ProfileAudience.Friends),
            User("ancien", "sacha@kanto.fr"));
        db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private static ApplicationUser User(string id, string name, ProfileAudience audience = ProfileAudience.Everyone) =>
        new() { Id = id, UserName = name, NormalizedUserName = name.ToUpperInvariant(), Audience = audience };

    [Fact]
    public async Task Search_FindsVisiblePseudos_ByAnyPart_IgnoringCaseAndAccents()
    {
        Assert.Empty(await _players.SearchAsync("s")); // trop court : pas d'annuaire de tous les joueurs
        Assert.Empty(await _players.SearchAsync("   "));
        // « Amis seulement » est trouvable (pour recevoir des demandes) ; ni « Personne », ni l'adresse d'un ancien compte.
        Assert.Equal(["Sacha du Bourg", "Sachamie"], await _players.SearchAsync("SACH"));
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
    public async Task Find_EveryoneProfiles_AndTheirCheckedPages()
    {
        var sacha = await _players.FindAsync("ondine", "sacha du bourg"); // majuscules indifférentes
        Assert.Equal(("sacha", "Sacha du Bourg", PublicPages.All, PlayerRelation.None), (sacha?.Id, sacha?.Pseudo, sacha?.Pages, sacha?.Relation));
        Assert.Equal(PlayerRelation.Self, (await _players.FindAsync("sacha", "Sacha du Bourg"))?.Relation);

        Assert.Null(await _players.FindAsync("ondine", "sacha@kanto.fr")); // ancien compte sans pseudo
        Assert.Null(await _players.FindAsync("ondine", "Inconnu"));
        Assert.Null(await _players.FindAsync("ondine", ""));

        Assert.True((await _players.SetVisibilityAsync("ondine", ProfileAudience.Everyone, PublicPages.Park | PublicPages.LivingDex)).Ok);
        Assert.NotNull(await _players.FindAsync("sacha", "Ondine", PublicPages.Park));
        Assert.Null(await _players.FindAsync("sacha", "Ondine", PublicPages.Inventory)); // page non cochée : comme un inconnu
    }

    [Fact]
    public async Task FriendsOnly_ShowsThePagesToFriends_AndNobody_HidesEverything()
    {
        // « Amis seulement » : trouvable, sans pages pour un inconnu ; ses amis (et lui-même) voient les pages cochées.
        var stranger = await _players.FindAsync("ondine", "Sachamie");
        Assert.Equal((PublicPages.None, true), (stranger?.Pages, stranger?.FriendsOnly));
        Assert.Null(await _players.FindAsync("ondine", "Sachamie", PublicPages.Collection));
        Assert.Equal(PublicPages.All, (await _players.FindAsync("amie", "Sachamie"))?.Pages);

        await BeFriends("ondine", "amie");
        Assert.Equal(PublicPages.All, (await _players.FindAsync("ondine", "Sachamie"))?.Pages);
        Assert.NotNull(await _players.FindAsync("ondine", "Sachamie", PublicPages.Collection));

        // « Personne » : introuvable, même par ses amis.
        await BeFriends("ondine", "cache");
        Assert.Null(await _players.FindAsync("ondine", "Sachaprivé"));
        Assert.Empty(await _players.SearchAsync("prive"));
    }

    private async Task BeFriends(string a, string b)
    {
        var code = await _friends.CodeAsync(b);
        Assert.True((await _friends.RequestByCodeAsync(a, code)).Ok);
        var request = Assert.Single((await _friends.ListAsync(b)).Received);
        Assert.True((await _friends.AcceptAsync(b, request.RequestId)).Ok);
    }

    [Fact]
    public async Task Visibility_IsSavedOnTheAccount()
    {
        Assert.Equal(new ProfileVisibility(ProfileAudience.Everyone, PublicPages.All), await _players.VisibilityAsync("pierre")); // défaut

        var hidden = await _players.SetVisibilityAsync("pierre", ProfileAudience.Nobody, PublicPages.Collection);
        Assert.True(hidden.Ok);
        Assert.Contains("code", hidden.Message);
        Assert.Equal(new ProfileVisibility(ProfileAudience.Nobody, PublicPages.Collection), await _players.VisibilityAsync("pierre"));
        Assert.Null(await _players.FindAsync("sacha", "Pierre", PublicPages.Collection)); // personne : rien, même la page cochée
        Assert.Empty(await _players.SearchAsync("pierre"));

        Assert.True((await _players.SetVisibilityAsync("pierre", ProfileAudience.Everyone, (PublicPages)0xFF)).Ok); // valeurs inconnues ignorées
        await using var db = _db.NewContext();
        Assert.Equal(PublicPages.All, await db.Users.Where(u => u.Id == "pierre").Select(u => u.PublicPages).SingleAsync());
        Assert.False((await _players.SetVisibilityAsync("pierre", (ProfileAudience)7, PublicPages.All)).Ok);
        Assert.False((await _players.SetVisibilityAsync("personne", ProfileAudience.Everyone, PublicPages.All)).Ok);
    }

    [Theory]
    [InlineData("Sacha du Bourg", null, "joueurs/Sacha%20du%20Bourg")]
    [InlineData("Éloïse", "parc", "joueurs/%C3%89lo%C3%AFse/parc")]
    public void Link_EscapesThePseudo(string pseudo, string? slug, string expected) =>
        Assert.Equal(expected, PlayerService.Link(pseudo, slug));
}
