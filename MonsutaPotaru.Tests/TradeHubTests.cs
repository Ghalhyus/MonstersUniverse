using MonsutaPotaru.Services;

namespace MonsutaPotaru.Tests;

/// <summary>Échanges en direct : demande, acceptation, choix, validation (refus et double accord), annulation, expiration.</summary>
public sealed class TradeHubTests : IDisposable
{
    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private readonly ManualTime _time = new();
    private readonly TradeHub _hub;
    private static readonly TradeParty Sacha = new("sacha", "Sacha", 1, "Noire 2 — Sacha");
    private static readonly TradeParty Ondine = new("ondine", "Ondine", 2, "Blanche 2 — Ondine");
    private static readonly TradeParty Pierre = new("pierre", "Pierre", 3, "Noire — Pierre");

    public TradeHubTests() => _hub = new TradeHub(_time);

    public void Dispose() => _hub.Dispose();

    private static TradeCandidate Pokemon(ushort species) =>
        new(new TradeSlot(0, species), $"empreinte-{species}", species, $"n°{species}", $"n°{species}", 10, false, false, "", "Boîte 1");

    private TradeSession Started()
    {
        var (session, _) = _hub.Request(Sacha, Ondine);
        Assert.NotNull(session);
        Assert.Null(_hub.Accept(session, "ondine"));
        return session;
    }

    [Fact]
    public void Request_IsSeenByTheOther_UntilAccepted()
    {
        var changed = new List<string>();
        _hub.RequestsChanged += changed.Add;
        var (session, message) = _hub.Request(Sacha, Ondine);
        Assert.Contains("Ondine", message);
        Assert.Same(session, Assert.Single(_hub.Incoming("ondine")));
        Assert.Same(session, _hub.Active("sacha"));
        Assert.Null(_hub.Active("ondine")); // une demande reçue n'est pas encore un échange
        Assert.Equal(["ondine"], changed);

        Assert.Null(_hub.Request(Sacha, Pierre).Session); // un échange à la fois
        Assert.NotNull(_hub.Accept(session!, "sacha")); // pas la sienne à accepter
        Assert.Null(_hub.Accept(session!, "ondine"));
        Assert.Equal(TradeStage.Choosing, session!.Stage);
        Assert.Same(session, _hub.Active("ondine"));
        Assert.Empty(_hub.Incoming("ondine"));
        Assert.Null(_hub.Request(Pierre, Ondine).Session); // Ondine échange déjà
    }

    [Fact]
    public void BothChoose_ThenARefusal_GoesBackToChoosing()
    {
        var session = Started();
        _hub.Choose(session, "sacha", Pokemon(120));
        Assert.Equal(TradeStage.Choosing, session.Stage);
        _hub.Choose(session, "ondine", Pokemon(64));
        Assert.Equal(TradeStage.Confirming, session.Stage);
        Assert.Equal(64, session.WantedFromMe("sacha")!.Species);

        Assert.False(_hub.Confirm(session, "sacha", true));
        Assert.True(session.Confirmed("sacha"));
        Assert.False(_hub.Confirm(session, "ondine", false)); // refus
        Assert.Equal(TradeStage.Choosing, session.Stage);
        Assert.Null(session.FirstWants);
        Assert.False(session.Confirmed("sacha"));
        Assert.Contains("Ondine a refusé", session.Notice);
    }

    [Fact]
    public void BothConfirm_TheSecondExecutes_ThenItIsDone()
    {
        var session = Started();
        _hub.Choose(session, "sacha", Pokemon(120));
        _hub.Choose(session, "ondine", Pokemon(64));
        Assert.False(_hub.Confirm(session, "ondine", true));
        Assert.True(_hub.Confirm(session, "sacha", true)); // le second à valider fait l'échange
        Assert.Equal(TradeStage.Trading, session.Stage);
        _hub.Cancel(session, "ondine"); // trop tard : l'écriture est en cours
        Assert.Equal(TradeStage.Trading, session.Stage);

        _hub.Complete(session, new TradeOutcome(true, "reçu Kadabra", "reçu Stari"));
        Assert.Equal(TradeStage.Done, session.Stage);
        Assert.Null(_hub.Active("sacha"));
        Assert.NotNull(_hub.Request(Sacha, Ondine).Session); // un nouvel échange est possible
    }

    [Fact]
    public void FailedTrade_RetryGoesBackToChoosing_OtherwiseCancels()
    {
        var session = Started();
        _hub.Choose(session, "sacha", Pokemon(120));
        _hub.Choose(session, "ondine", Pokemon(64));
        _hub.Confirm(session, "sacha", true);
        _hub.Confirm(session, "ondine", true);
        _hub.Complete(session, TradeOutcome.Failed("a changé"));
        Assert.Equal(TradeStage.Choosing, session.Stage);
        Assert.Equal("a changé", session.Notice);
    }

    [Fact]
    public void Cancel_EndsItForBoth_AndRequestsExpire()
    {
        var session = Started();
        var notified = 0;
        session.Changed += () => notified++;
        _hub.Cancel(session, "ondine");
        Assert.Equal(TradeStage.Cancelled, session.Stage);
        Assert.Equal("Ondine a annulé l'échange.", session.Notice);
        Assert.Equal(1, notified);
        Assert.Null(_hub.Active("sacha"));

        var (request, _) = _hub.Request(Sacha, Ondine);
        _time.Now += TradeHub.RequestLifetime - TimeSpan.FromSeconds(1);
        _hub.ExpireRequests();
        Assert.Equal(TradeStage.Requested, request!.Stage);
        _time.Now += TimeSpan.FromSeconds(2);
        _hub.ExpireRequests();
        Assert.Equal(TradeStage.Cancelled, request.Stage);
        Assert.Contains("expiré", request.Notice);
        Assert.Empty(_hub.Incoming("ondine"));

        var (declined, _) = _hub.Request(Sacha, Ondine);
        _hub.Cancel(declined!, "ondine"); // refuser une demande reçue
        Assert.Equal("Ondine a refusé la demande d'échange.", declined!.Notice);
    }
}
