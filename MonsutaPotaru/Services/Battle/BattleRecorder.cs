using Microsoft.EntityFrameworkCore;
using MonsutaPotaru.Data;

namespace MonsutaPotaru.Services;

/// <summary>
/// Enregistre chaque combat terminé (<see cref="BattleHub.Finished"/>) pour les statistiques (<see cref="BattleRecord"/>).
/// L'événement vient du fil du simulateur : l'écriture se fait à côté, sans le retenir.
/// </summary>
public sealed class BattleRecorder(BattleHub hub, IDbContextFactory<ApplicationDbContext> dbFactory, ILogger<BattleRecorder> logger)
    : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        hub.Finished += OnFinished;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        hub.Finished -= OnFinished;
        return Task.CompletedTask;
    }

    private void OnFinished(BattleSession session) => _ = Task.Run(async () =>
    {
        try
        {
            await RecordAsync(dbFactory, session, DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Combat {Id} : enregistrement du résultat impossible.", session.Id);
        }
    });

    /// <summary>Résultat de ce combat, vu par celui qui l'a demandé (rien si le combat n'a pas de vainqueur ni de nul).</summary>
    public static async Task RecordAsync(IDbContextFactory<ApplicationDbContext> dbFactory, BattleSession session, DateTime at)
    {
        var outcome = session.FirstView.Outcome;
        if (outcome == BattleOutcome.None)
            return;
        await using var db = await dbFactory.CreateDbContextAsync();
        db.BattleRecords.Add(new BattleRecord
        {
            At = at,
            FirstId = session.First.UserId,
            SecondId = session.Second.UserId,
            Format = session.Format,
            WinnerId = outcome switch
            {
                BattleOutcome.Won => session.First.UserId,
                BattleOutcome.Lost => session.Second.UserId,
                _ => null,
            },
        });
        await db.SaveChangesAsync();
    }
}
