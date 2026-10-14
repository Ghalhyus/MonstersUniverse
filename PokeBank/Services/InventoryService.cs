using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PokeBank.Data;

namespace PokeBank.Services;

/// <summary>Sauvegarde stockée telle que proposée pour un retrait de Pokédollars.</summary>
/// <param name="Money">Argent dans le fichier stocké ; null si PKHeX ne sait pas le lire pour ce jeu.</param>
public sealed record MoneySource(int SaveId, string Label, string TrainerId, long? Money);

public sealed record WalletOperationView(DateTime At, WalletOperationKind Kind, long Amount, long BalanceAfter, string SaveLabel, string Details);

public sealed record WithdrawResult(bool Ok, string Message, long Balance = 0);

public sealed record PurchaseResult(bool Ok, string Message, int Limit = 0, long Balance = 0);

/// <summary>
/// Inventaire du site : pour l'instant un porte-monnaie de Pokédollars, alimenté en retirant de l'argent des sauvegardes
/// stockées (seule la copie du serveur est modifiée). Chaque mouvement est inscrit dans un journal.
/// </summary>
public sealed class InventoryService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    StoredSaveService storedSaves,
    ILogger<InventoryService> logger)
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    public static string FormatMoney(long amount) => $"{amount.ToString("N0", French)} ₽";

    public async Task<long> GetBalanceAsync(string ownerId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Wallets.Where(w => w.OwnerId == ownerId).Select(w => w.Money).FirstOrDefaultAsync();
    }

    /// <summary>Journal des opérations, du plus récent au plus ancien.</summary>
    public async Task<List<WalletOperationView>> ListOperationsAsync(string ownerId, int take = 200)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.WalletOperations.AsNoTracking()
            .Where(o => o.OwnerId == ownerId)
            .OrderByDescending(o => o.At).ThenByDescending(o => o.Id)
            .Take(take)
            .Select(o => new WalletOperationView(o.At, o.Kind, o.Amount, o.BalanceAfter, o.SaveLabel, o.Details))
            .ToListAsync();
    }

    /// <summary>
    /// Achète <paramref name="lots"/> lots d'emplacements de collection (<see cref="CollectionCapacity.SlotsPerLot"/> chacun,
    /// <see cref="CollectionCapacity.LotPrice"/> l'un) : débit, nouvelle limite et ligne de journal en une seule opération.
    /// </summary>
    public async Task<PurchaseResult> BuyCollectionSlotsAsync(string ownerId, int lots)
    {
        if (lots <= 0)
            return new(false, "Choisis au moins un lot.");
        if (lots > 100_000)
            return new(false, "Trop de lots en une seule fois.");

        long cost = lots * CollectionCapacity.LotPrice;
        int slots = lots * CollectionCapacity.SlotsPerLot;

        await using var db = await dbFactory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        // Débit conditionnel et atomique : deux achats simultanés ne peuvent pas rendre le solde négatif.
        var debited = await db.Wallets.Where(w => w.OwnerId == ownerId && w.Money >= cost)
            .ExecuteUpdateAsync(w => w.SetProperty(x => x.Money, x => x.Money - cost));
        if (debited == 0)
        {
            var available = await db.Wallets.Where(w => w.OwnerId == ownerId).Select(w => w.Money).FirstOrDefaultAsync();
            return new(false, $"{lots} lot{(lots > 1 ? "s" : "")} coûte{(lots > 1 ? "nt" : "")} {FormatMoney(cost)}, ton porte-monnaie contient {FormatMoney(available)} : " +
                              $"il manque {FormatMoney(cost - available)}. Retire de l'argent d'une sauvegarde depuis « Inventaire ».");
        }

        var limitBefore = await db.Users.Where(u => u.Id == ownerId).Select(u => u.CollectionLimit).FirstAsync();
        await db.Users.Where(u => u.Id == ownerId)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.CollectionLimit, x => x.CollectionLimit + slots));
        var limit = limitBefore + slots;
        var balance = await db.Wallets.Where(w => w.OwnerId == ownerId).Select(w => w.Money).FirstAsync();

        db.WalletOperations.Add(new WalletOperation
        {
            OwnerId = ownerId,
            At = DateTime.UtcNow,
            Kind = WalletOperationKind.BuyCollectionSlots,
            Amount = -cost,
            BalanceAfter = balance,
            Details = $"+{CollectionCapacity.FormatCount(slots)} emplacements ({CollectionCapacity.FormatCount(limitBefore)} → {CollectionCapacity.FormatCount(limit)})",
        });
        await db.SaveChangesAsync();
        await tx.CommitAsync();

        logger.LogInformation("Utilisateur {User} : {Slots} emplacements achetés pour {Cost} ₽ (limite {Limit}).", ownerId, slots, cost, limit);
        return new(true, $"Collection agrandie : {CollectionCapacity.FormatCount(limit)} emplacements. Solde : {FormatMoney(balance)}.", limit, balance);
    }

    /// <summary>Sauvegardes stockées de l'utilisateur, avec l'argent que contient chacune (lu dans le fichier).</summary>
    public async Task<List<MoneySource>> ListMoneySourcesAsync(string ownerId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var saves = await db.StoredSaves.AsNoTracking()
            .Where(s => s.OwnerId == ownerId)
            .OrderBy(s => s.UploadedAt)
            .Select(s => new { s.Id, s.GameName, s.TrainerName, s.TrainerId, s.FileName, s.Data })
            .ToListAsync();
        return saves.Select(s => new MoneySource(s.Id, $"{s.GameName} — {s.TrainerName}", s.TrainerId,
                StoredSaveService.TryRead(s.Data, s.FileName, out var sav) ? sav.Money : null))
            .ToList();
    }

    /// <summary>
    /// Retire <paramref name="amount"/> Pokédollars de la sauvegarde stockée et les crédite sur le porte-monnaie,
    /// en une seule opération. La partie est marquée « modifiée sur le serveur » (voir <see cref="SaveModification"/>).
    /// </summary>
    public async Task<WithdrawResult> WithdrawFromSaveAsync(string ownerId, int saveId, long amount)
    {
        if (amount <= 0)
            return new(false, "Le montant doit être supérieur à 0.");

        await using var db = await dbFactory.CreateDbContextAsync();
        var save = await db.StoredSaves.FirstOrDefaultAsync(s => s.Id == saveId && s.OwnerId == ownerId);
        if (save is null)
            return new(false, "Cette sauvegarde n'existe plus.");
        if (!StoredSaveService.TryRead(save.Data, save.FileName, out var sav))
            return new(false, "Impossible de lire cette sauvegarde.");

        var label = $"{save.GameName} — {save.TrainerName}";
        long before = sav.Money;
        if (amount > before)
            return new(false, $"« {label} » ne contient que {FormatMoney(before)}.");

        // Modifie le fichier, puis vérifie en le relisant que le jeu a bien pris la nouvelle valeur.
        sav.Money = (uint)(before - amount);
        var newData = sav.Write().ToArray();
        if (!StoredSaveService.TryRead(newData, save.FileName, out var check) || check.Money != before - amount)
            return new(false, $"La modification de l'argent n'est pas prise en charge pour ce jeu ({save.GameName}).");

        await storedSaves.WriteBackupAsync(ownerId, save, "retrait-avant");

        var now = DateTime.UtcNow;
        await using var tx = await db.Database.BeginTransactionAsync();

        save.Data = newData;
        save.DownloadedSinceModification = false; // la version que le joueur a pu télécharger n'est plus la bonne
        save.Version++;

        // Trace rattachée à la partie (et non à la sauvegarde stockée) : elle survit à une suppression.
        var modification = await db.SaveModifications.FindAsync(save.IdentityKey);
        if (modification is null)
            db.SaveModifications.Add(modification = new SaveModification { IdentityKey = save.IdentityKey });
        modification.PlayTimeSeconds = save.PlayTimeSeconds; // un retrait ne change pas le temps de jeu
        modification.MoneyAfter = before - amount;
        modification.Withdrawn += amount;
        modification.ModifiedAt = now;
        try
        {
            await db.SaveChangesAsync(); // jeton de concurrence : échoue si le fichier a changé depuis sa lecture
        }
        catch (DbUpdateConcurrencyException)
        {
            return new(false, StoredSaveService.ConflictResult().Message);
        }

        // Crédit atomique (deux retraits simultanés depuis deux sauvegardes ne s'écrasent pas).
        var credited = await db.Wallets.Where(w => w.OwnerId == ownerId)
            .ExecuteUpdateAsync(w => w.SetProperty(x => x.Money, x => x.Money + amount));
        if (credited == 0)
        {
            db.Wallets.Add(new Wallet { OwnerId = ownerId, Money = amount });
            await db.SaveChangesAsync();
        }
        var balance = await db.Wallets.Where(w => w.OwnerId == ownerId).Select(w => w.Money).FirstAsync();

        db.WalletOperations.Add(new WalletOperation
        {
            OwnerId = ownerId,
            At = now,
            Kind = WalletOperationKind.WithdrawFromSave,
            Amount = amount,
            BalanceAfter = balance,
            StoredSaveId = save.Id,
            SaveLabel = label,
        });
        await db.SaveChangesAsync();
        await tx.CommitAsync();

        await storedSaves.WriteBackupAsync(ownerId, save, "retrait-apres");
        logger.LogInformation("Utilisateur {User} : {Amount} ₽ retirés de la sauvegarde {Id}.", ownerId, amount, save.Id);

        return new(true, $"{FormatMoney(amount)} retirés de « {label} ». Solde : {FormatMoney(balance)}.", balance);
    }
}
