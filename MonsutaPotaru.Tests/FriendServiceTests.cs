using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MonsutaPotaru.Data;
using MonsutaPotaru.Services;

namespace MonsutaPotaru.Tests;

/// <summary>Amis : demandes (par pseudo ou par code), acceptation, refus, annulation, retrait, code ami.</summary>
public sealed class FriendServiceTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly FriendService _friends;

    public FriendServiceTests()
    {
        _friends = new FriendService(_db.Factory, NullLogger<FriendService>.Instance);
        using var db = _db.NewContext();
        db.Users.AddRange(
            User("sacha", "Sacha"),
            User("ondine", "Ondine"),
            User("pierre", "Pierre", ProfileAudience.Nobody),
            User("ancien", "ancien@kanto.fr"));
        db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private static ApplicationUser User(string id, string name, ProfileAudience audience = ProfileAudience.Everyone) =>
        new() { Id = id, UserName = name, NormalizedUserName = name.ToUpperInvariant(), Audience = audience };

    private async Task<int> SingleReceived(string userId) => Assert.Single((await _friends.ListAsync(userId)).Received).RequestId;

    [Fact]
    public async Task Request_ThenAccept_MakesFriendsBothWays()
    {
        Assert.Equal("Demande d'ami envoyée à Ondine.", (await _friends.RequestAsync("sacha", "ondine")).Message);
        Assert.False((await _friends.RequestAsync("sacha", "Ondine")).Ok); // déjà envoyée
        Assert.Equal(1, await _friends.PendingCountAsync("ondine"));
        Assert.Equal("Ondine", Assert.Single((await _friends.ListAsync("sacha")).Sent).Pseudo);

        Assert.True((await _friends.AcceptAsync("ondine", await SingleReceived("ondine"))).Ok);
        Assert.Equal("Ondine", Assert.Single((await _friends.ListAsync("sacha")).Friends).Pseudo);
        Assert.Equal("Sacha", Assert.Single((await _friends.ListAsync("ondine")).Friends).Pseudo);
        Assert.Equal(0, await _friends.PendingCountAsync("ondine"));
        Assert.Equal("Ondine est déjà ton ami.", (await _friends.RequestAsync("sacha", "Ondine")).Message);

        // Retirer : des deux côtés.
        Assert.True((await _friends.RemoveAsync("ondine", "sacha")).Ok);
        Assert.Empty((await _friends.ListAsync("sacha")).Friends);
        Assert.False((await _friends.RemoveAsync("ondine", "sacha")).Ok);
    }

    [Fact]
    public async Task CrossedRequests_AreAcceptedAtOnce()
    {
        Assert.True((await _friends.RequestAsync("sacha", "Ondine")).Ok);
        var crossed = await _friends.RequestAsync("ondine", "Sacha");
        Assert.True(crossed.Ok);
        Assert.Contains("amis", crossed.Message);
        Assert.Single((await _friends.ListAsync("ondine")).Friends);
        Assert.Empty((await _friends.ListAsync("sacha")).Received);
        Assert.Empty((await _friends.ListAsync("sacha")).Sent);
    }

    [Fact]
    public async Task Decline_AndCancel_OnlyByTheRightPlayer()
    {
        Assert.True((await _friends.RequestAsync("sacha", "Ondine")).Ok);
        var id = await SingleReceived("ondine");
        Assert.False((await _friends.DeclineAsync("sacha", id)).Ok); // pas la sienne à refuser
        Assert.False((await _friends.CancelAsync("ondine", id)).Ok); // pas la sienne à annuler
        Assert.False((await _friends.AcceptAsync("sacha", id)).Ok);
        Assert.True((await _friends.DeclineAsync("ondine", id)).Ok);
        Assert.Empty((await _friends.ListAsync("sacha")).Sent);

        Assert.True((await _friends.RequestAsync("sacha", "Ondine")).Ok); // on peut redemander
        Assert.True((await _friends.CancelAsync("sacha", await SingleReceived("ondine"))).Ok);
        Assert.Equal(0, await _friends.PendingCountAsync("ondine"));
    }

    [Fact]
    public async Task HiddenPlayer_OnlyByCode()
    {
        Assert.Equal(PlayerService.HiddenMessage, (await _friends.RequestAsync("sacha", "Pierre")).Message); // « Personne »
        Assert.Equal(PlayerService.HiddenMessage, (await _friends.RequestAsync("sacha", "ancien@kanto.fr")).Message);

        var code = await _friends.CodeAsync("pierre");
        Assert.Matches("^[A-Z2-9]{4}-[A-Z2-9]{4}$", code);
        Assert.Equal(code, await _friends.CodeAsync("pierre")); // permanent
        Assert.True((await _friends.RequestByCodeAsync("sacha", " " + code.ToLowerInvariant().Replace("-", " ") + " ")).Ok);
        Assert.Equal("Sacha", (await _friends.ListAsync("pierre")).Received.Single().Pseudo);

        Assert.False((await _friends.RequestByCodeAsync("pierre", code)).Ok); // son propre code
        Assert.Equal("Un code ami fait 8 caractères (ABCD-EFGH).", (await _friends.RequestByCodeAsync("sacha", "ABC")).Message);
        Assert.Equal("Aucun joueur n'a ce code ami.", (await _friends.RequestByCodeAsync("sacha", "AAAA-AAAA")).Message);

        // Code régénéré : l'ancien ne marche plus.
        Assert.True((await _friends.RegenerateCodeAsync("pierre")).Ok);
        Assert.NotEqual(code, await _friends.CodeAsync("pierre"));
        Assert.Equal("Aucun joueur n'a ce code ami.", (await _friends.RequestByCodeAsync("ondine", code)).Message);
    }

    [Fact]
    public async Task OldAccountWithoutPseudo_ShowsAsSuch()
    {
        Assert.True((await _friends.RequestByCodeAsync("ancien", await _friends.CodeAsync("sacha"))).Ok);
        Assert.Null(Assert.Single((await _friends.ListAsync("sacha")).Received).Pseudo); // l'adresse n'est jamais montrée
    }

    [Fact]
    public async Task DeletingAnAccount_RemovesItsFriendsAndRequests()
    {
        Assert.True((await _friends.RequestAsync("sacha", "Ondine")).Ok);
        Assert.True((await _friends.AcceptAsync("ondine", await SingleReceived("ondine"))).Ok);
        Assert.True((await _friends.RequestByCodeAsync("sacha", await _friends.CodeAsync("pierre"))).Ok);

        await using var db = _db.NewContext();
        db.Users.Remove(await db.Users.SingleAsync(u => u.Id == "sacha"));
        await db.SaveChangesAsync();
        Assert.Empty((await _friends.ListAsync("ondine")).Friends);
        Assert.Equal(0, await _friends.PendingCountAsync("pierre"));
    }
}
