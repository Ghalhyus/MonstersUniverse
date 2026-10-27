using Microsoft.Extensions.Logging.Abstractions;
using MonsutaPotaru.Data;
using MonsutaPotaru.Services;
using PKHeX.Core;

namespace MonsutaPotaru.Tests;

/// <summary>
/// Combats en direct (<see cref="BattleHub"/>, vrai simulateur) : demande, équipes, choix vérifiés, temps écoulé, abandon,
/// annulation ; et <see cref="BattleService"/> : sauvegardes qui peuvent combattre, Pokémon proposés, équipe vérifiée.
/// </summary>
public sealed class BattleHubTests : IDisposable
{
    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private const string Format = "gen3customgame";
    private readonly ManualTime _time = new();
    private readonly BattleHub _hub;
    private static readonly BattleParty Sacha = new("sacha", "Sacha", 1, "Vert Feuille — David");
    private static readonly BattleParty Ondine = new("ondine", "Ondine", 2, "Rouge Feu — Carfil");

    public BattleHubTests() => _hub = new BattleHub(_time, BattleTests.Showdown(), NullLogger.Instance, ticking: false);

    public void Dispose() => _hub.Dispose();

    private static BattlePlayer Team(string pseudo, string file)
    {
        Assert.True(SaveUtil.TryGetSaveFile(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Sauvegardes", file)), out var sav, file));
        return new BattlePlayer(pseudo, Enumerable.Range(0, sav!.PartyCount).Select(i => BattleTeam.ToSet(sav.GetPartySlotAtIndex(i), sav)).ToList());
    }

    /// <summary>Attend qu'une condition soit vraie : le simulateur répond sur son propre fil.</summary>
    private static void WaitUntil(Func<bool> condition, string what)
    {
        var limit = DateTime.UtcNow.AddSeconds(15);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < limit, $"délai dépassé : {what}");
            Thread.Sleep(20);
        }
    }

    private static bool ReadyToChoose(BattleSession s, string userId) => s.Snapshot(userId) is { Request.MustAct: true, Chosen: false };

    private BattleSession Fighting()
    {
        var (session, _) = _hub.Request(Sacha, Ondine, Format);
        Assert.NotNull(session);
        Assert.Null(_hub.Accept(session, "ondine"));
        Assert.Equal(BattleStage.TeamBuilding, session.Stage);
        Assert.Null(_hub.SubmitTeam(session, "sacha", Team("Sacha", "vert-feuille.sav"), 0));
        Assert.Equal(BattleStage.TeamBuilding, session.Stage); // l'équipe d'Ondine manque
        Assert.Null(_hub.SubmitTeam(session, "ondine", Team("Ondine", "rouge-feu.sav"), 2));
        Assert.Equal(BattleStage.Fighting, session.Stage);
        WaitUntil(() => ReadyToChoose(session, "sacha") && ReadyToChoose(session, "ondine"), "premières requêtes");
        return session;
    }

    [Fact]
    public void Battle_ChoicesAreChecked_ThenTheLatePlayerLoses()
    {
        var finished = new List<BattleSession>();
        _hub.Finished += finished.Add;
        var session = Fighting();
        Assert.Equal(2, session.OtherIllegal("sacha")); // montré à Sacha : l'équipe d'Ondine en a 2
        Assert.Contains("Go ! Piafou !", session.Steps("sacha", 0).Select(s => s.Text));
        Assert.Contains("Sacha envoie Piafou !", session.Steps("ondine", 0).Select(s => s.Text));

        // Choix vérifiés avant d'aller au simulateur.
        Assert.Equal("Cette capacité n'existe pas.", _hub.Choose(session, "sacha", BattleChoice.Move(9)));
        Assert.Equal("Ce Pokémon est déjà au combat.", _hub.Choose(session, "sacha", BattleChoice.SwitchTo(1)));
        Assert.Equal("Le combat n'est pas en cours.", _hub.Choose(session, "pierre", BattleChoice.Move(1)));

        // Un tour : chacun attaque ; le temps pour choisir commence après les étapes à jouer.
        Assert.Null(_hub.Choose(session, "sacha", BattleChoice.Move(1)));
        Assert.Equal("Ce n'est pas le moment de choisir.", _hub.Choose(session, "sacha", BattleChoice.Move(1))); // déjà choisi
        Assert.Null(_hub.Choose(session, "ondine", BattleChoice.Move(1)));
        WaitUntil(() => session.Snapshot("sacha").Scene is not null && ReadyToChoose(session, "sacha") && ReadyToChoose(session, "ondine"), "tour 2");
        Assert.Contains("Piafou utilise Picpic !", session.Steps("sacha", 0).Select(s => s.Text));
        var deadline = session.Snapshot("sacha").Deadline!.Value;
        Assert.True(deadline > _time.GetUtcNow().UtcDateTime + BattleTiming.Decision); // + la durée des étapes du tour

        // Personne ne choisit : à l'échéance, Sacha (le premier en retard) perd.
        _hub.Tick();
        Assert.Equal(BattleStage.Fighting, session.Stage);
        _time.Now = deadline.AddSeconds(1);
        _hub.Tick();
        WaitUntil(() => session.Stage == BattleStage.Done, "fin du combat");
        Assert.Equal((BattleOutcome.Lost, BattleOutcome.Won), (session.Snapshot("sacha").Outcome, session.Snapshot("ondine").Outcome));
        Assert.Contains("Temps écoulé ! Sacha n'a pas choisi à temps et perd le combat.", session.Steps("ondine", 0).Select(s => s.Text));
        Assert.Equal([session], finished);
        Assert.Null(_hub.Active("sacha"));
    }

    [Fact]
    public void Forfeit_TheOtherWins()
    {
        var session = Fighting();
        _hub.Forfeit(session, "ondine");
        WaitUntil(() => session.Stage == BattleStage.Done, "fin du combat");
        Assert.Equal(BattleOutcome.Won, session.Snapshot("sacha").Outcome);
        Assert.Contains("Ondine abandonne le combat.", session.Steps("sacha", 0).Select(s => s.Text));
        Assert.Contains("Tu as battu Ondine !", session.Steps("sacha", 0).Select(s => s.Text));

        // Quitter la page pendant le combat, c'est abandonner.
        var again = Fighting();
        _hub.Leave(again, "sacha", "Sacha a quitté le combat.");
        WaitUntil(() => again.Stage == BattleStage.Done, "fin du combat");
        Assert.Equal(BattleOutcome.Lost, again.Snapshot("sacha").Outcome);
    }

    [Fact]
    public void Request_RefusedCancelledOrExpired_AndOneBattleAtATime()
    {
        var changed = new List<string>();
        _hub.RequestsChanged += changed.Add;
        var (session, message) = _hub.Request(Sacha, Ondine, Format);
        Assert.Equal("Demande envoyée à Ondine : en attente de sa réponse.", message);
        Assert.Equal([session], _hub.Incoming("ondine"));
        Assert.Equal(["ondine"], changed);
        Assert.Equal("Tu as déjà un combat en cours.", _hub.Request(Sacha, Ondine, Format).Message);

        _hub.Leave(session!, "ondine"); // refus
        Assert.Equal((BattleStage.Cancelled, "Ondine a refusé le combat."), (session!.Stage, session.Notice));
        Assert.Empty(_hub.Incoming("ondine"));

        var (late, _) = _hub.Request(Sacha, Ondine, Format);
        _time.Now += BattleHub.RequestLifetime;
        _hub.Tick();
        Assert.Equal((BattleStage.Cancelled, "Ondine n'a pas répondu à temps : la demande a expiré."), (late!.Stage, late.Notice));

        var (building, _) = _hub.Request(Sacha, Ondine, Format);
        Assert.Null(_hub.Accept(building!, "ondine"));
        _hub.Leave(building!, "sacha");
        Assert.Equal((BattleStage.Cancelled, "Sacha a annulé le combat."), (building!.Stage, building.Notice));
        Assert.Equal("Ce combat n'attend plus d'équipe.", _hub.SubmitTeam(building, "ondine", Team("Ondine", "rouge-feu.sav"), 0));
    }

    // ---------- BattleService ----------

    [Fact]
    public async Task Service_PairsOfFriends_Candidates_AndCheckedTeam()
    {
        using var db = new TestDatabase("sacha", "ondine", "pierre");
        await using (var context = db.NewContext())
        {
            context.Friendships.AddRange(
                new Friendship { UserId = "sacha", FriendId = "ondine", Since = DateTime.UtcNow },
                new Friendship { UserId = "ondine", FriendId = "sacha", Since = DateTime.UtcNow });
            await context.SaveChangesAsync();
        }
        var saves = db.StoredSaves();
        async Task<int> Store(string owner, string file)
        {
            var result = await saves.AddAsync(owner, file, File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Sauvegardes", file)));
            Assert.True(result.Ok, result.Message);
            return (await saves.ListAsync(owner)).Single(s => s.FileName == file).Id;
        }
        var leafGreen = await Store("sacha", "vert-feuille.sav");
        await Store("sacha", "noire-2.sav"); // 5e génération : pas encore de combats
        var fireRed = await Store("ondine", "rouge-feu.sav");
        var service = new BattleService(db.Factory, saves, new PokemonInfo());

        var pair = Assert.Single(await service.SavePairsAsync("sacha", "ondine"));
        Assert.Equal((leafGreen, fireRed, "gen3customgame"), (pair.Mine.Id, pair.Theirs.Id, pair.Format));
        Assert.Empty(await service.SavePairsAsync("sacha", "pierre")); // pas son ami

        var candidates = await service.CandidatesAsync("sacha", leafGreen);
        Assert.Equal(9, candidates.Count);
        Assert.Equal(("Piafou", "Équipe 1"), (candidates[0].Name, candidates[0].Location));
        Assert.Empty(await service.CandidatesAsync("ondine", leafGreen)); // pas la sienne
        Assert.Equal(6, BattleService.TeamSize(candidates.Count));
        Assert.Equal(4, BattleService.TeamSize(4));

        Assert.Equal("Choisis 6 Pokémon différents.", (await service.TeamAsync("sacha", leafGreen, "Sacha", candidates.Take(5).ToList())).Error);
        Assert.Equal("Choisis 6 Pokémon différents.", (await service.TeamAsync("sacha", leafGreen, "Sacha", [.. candidates.Take(5), candidates[0]])).Error);
        var changed = candidates.Take(6).Select((c, i) => i == 2 ? c with { Fingerprint = "autre" } : c).ToList();
        Assert.Contains("a changé", (await service.TeamAsync("sacha", leafGreen, "Sacha", changed)).Error);

        var chosen = candidates.Skip(3).Take(6).ToList(); // l'ordre choisi : le premier entre en combat
        var team = await service.TeamAsync("sacha", leafGreen, "Sacha", chosen);
        Assert.Null(team.Error);
        Assert.Equal("Sacha", team.Team!.Name);
        Assert.Equal(chosen.Select(c => (int)c.Species), team.Team.Team.Select(t => t.Species));
        Assert.Equal(chosen.Count(c => !c.IsLegal), team.Illegal);
        Assert.Equal("images/livingdex/rouge-feu-vert-feuille/", team.Sprites!.Folder); // ceux de son jeu
    }

    [Fact]
    public async Task Results_RecordedForBothPlayers_InTheirStatistics()
    {
        using var db = new TestDatabase("sacha", "ondine", "pierre");
        var service = new BattleService(db.Factory, db.StoredSaves(), new PokemonInfo());
        var at = DateTime.UtcNow;

        var won = Fighting();
        _hub.Forfeit(won, "ondine");
        WaitUntil(() => won.Stage == BattleStage.Done, "fin du combat");
        await BattleRecorder.RecordAsync(db.Factory, won, at);

        var lost = Fighting();
        _hub.Leave(lost, "sacha", "Sacha a quitté le combat."); // perdu pour Sacha
        WaitUntil(() => lost.Stage == BattleStage.Done, "fin du combat");
        await BattleRecorder.RecordAsync(db.Factory, lost, at);

        await using (var context = db.NewContext())
        {
            context.BattleRecords.Add(new BattleRecord { At = at, FirstId = "pierre", SecondId = "sacha", Format = Format }); // nul
            await context.SaveChangesAsync();
        }

        Assert.Equal(new BattleRecordStats(1, 1, 1, 2), await service.StatsAsync("sacha"));
        Assert.Equal(new BattleRecordStats(1, 1, 0, 1), await service.StatsAsync("ondine"));
        Assert.Equal(new BattleRecordStats(0, 0, 0, 0), await service.StatsAsync("personne"));
        await using (var context = db.NewContext())
        {
            var first = context.BattleRecords.OrderBy(b => b.Id).First();
            Assert.Equal(("sacha", "ondine", "sacha", Format), (first.FirstId, first.SecondId, first.WinnerId, first.Format));
        }
    }

    [Fact]
    public async Task Recorder_WritesEachFinishedBattle()
    {
        using var db = new TestDatabase("sacha", "ondine");
        var recorder = new BattleRecorder(_hub, db.Factory, NullLogger<BattleRecorder>.Instance);
        await recorder.StartAsync(CancellationToken.None);
        var session = Fighting();
        _hub.Forfeit(session, "sacha");
        WaitUntil(() =>
        {
            using var context = db.NewContext();
            return context.BattleRecords.Any();
        }, "combat enregistré");
        await recorder.StopAsync(CancellationToken.None);
        await using var check = db.NewContext();
        Assert.Equal("ondine", check.BattleRecords.Single().WinnerId);
    }
}
