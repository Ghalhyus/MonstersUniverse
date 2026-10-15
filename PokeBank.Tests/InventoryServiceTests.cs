using Microsoft.EntityFrameworkCore;
using PKHeX.Core;
using PokeBank.Data;
using PokeBank.Services;

namespace PokeBank.Tests;

/// <summary>Porte-monnaie : retrait de Pokédollars depuis une sauvegarde stockée, journal, encadrement de la mise à jour.</summary>
public sealed class InventoryServiceTests : IDisposable
{
    private readonly TestDatabase _db = new("sacha", "ondine");
    private readonly StoredSaveService _saves;
    private readonly InventoryService _inventory;

    public InventoryServiceTests()
    {
        _saves = _db.StoredSaves();
        _inventory = _db.Inventory();
    }

    private const int MasterBall = 1, HyperBall = 2, SuperBall = 3, PokeBall = 4, BeastBall = 851; // Ultra Ball des Ultra-Chimères : 7e génération

    private static byte[] Save(uint money = 100_000, int hours = 10, string ot = "Ludo", params (int Item, int Count)[] balls)
    {
        var sav = new SAV5B2W2 { OT = ot, TID16 = 12345, SID16 = 54321, PlayedHours = hours, Money = money };
        sav.SetBoxSlotAtIndex(SaveSessionTests.CreatePk5(25, 5), 0, 0);
        if (balls.Length > 0)
            SaveSessionTests.GiveBalls(sav, balls);
        return sav.Write().ToArray();
    }

    private async Task<int> Store(string owner = "sacha", uint money = 100_000, string ot = "Ludo", params (int Item, int Count)[] balls)
    {
        Assert.True((await _saves.AddAsync(owner, $"{ot}.sav", Save(money, ot: ot, balls: balls))).Ok);
        return (await _saves.ListAsync(owner)).Single(s => s.TrainerName == ot).Id;
    }

    private async Task<Dictionary<int, int>> StoredBalls(int saveId)
    {
        using var db = _db.NewContext();
        var save = await db.StoredSaves.SingleAsync(s => s.Id == saveId);
        Assert.True(SaveUtil.TryGetSaveFile(save.Data, out var sav));
        return SaveBag.ReadBalls(sav!);
    }

    private async Task<Dictionary<int, int>> InventoryBalls(string owner = "sacha") =>
        (await _inventory.GetBallsAsync(owner)).ToDictionary(b => b.ItemId, b => b.Count);

    private async Task<uint> StoredMoney(int saveId)
    {
        using var db = _db.NewContext();
        var save = await db.StoredSaves.SingleAsync(s => s.Id == saveId);
        Assert.True(SaveUtil.TryGetSaveFile(save.Data, out var sav));
        return sav!.Money;
    }

    [Fact]
    public async Task Withdraw_MovesMoneyFromTheStoredSaveToTheWallet()
    {
        var id = await Store();
        Assert.Equal(100_000, (await _inventory.ListMoneySourcesAsync("sacha")).Single().Money);

        var result = await _inventory.WithdrawFromSaveAsync("sacha", id, 30_000);
        Assert.True(result.Ok, result.Message);
        Assert.Equal(30_000, result.Balance);

        Assert.Equal(70_000u, await StoredMoney(id));
        Assert.Equal(30_000, await _inventory.GetBalanceAsync("sacha"));
        Assert.Equal(0, await _inventory.GetBalanceAsync("ondine"));

        var op = Assert.Single(await _inventory.ListOperationsAsync("sacha"));
        Assert.Equal((WalletOperationKind.WithdrawFromSave, 30_000L, 30_000L), (op.Kind, op.Amount, op.BalanceAfter));

        var info = Assert.Single(await _saves.ListAsync("sacha"));
        Assert.True(info.ModifiedOnServer);
        Assert.True(info.MustDownloadBeforeUpdate);
        Assert.Equal(30_000, info.WithdrawnOnServer);
        Assert.Single(Directory.GetFiles(_db.BackupRoot, "*retrait-avant*", SearchOption.AllDirectories));
        Assert.Single(Directory.GetFiles(_db.BackupRoot, "*retrait-apres*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Withdrawals_Accumulate_AcrossSaves()
    {
        var first = await Store(money: 50_000);
        var second = await Store(money: 80_000, ot: "Pierre");

        Assert.True((await _inventory.WithdrawFromSaveAsync("sacha", first, 10_000)).Ok);
        Assert.True((await _inventory.WithdrawFromSaveAsync("sacha", second, 80_000)).Ok);
        Assert.True((await _inventory.WithdrawFromSaveAsync("sacha", first, 5_000)).Ok);

        Assert.Equal(95_000, await _inventory.GetBalanceAsync("sacha"));
        Assert.Equal((35_000u, 0u), (await StoredMoney(first), await StoredMoney(second)));
        Assert.Equal([95_000L, 90_000L, 10_000L], (await _inventory.ListOperationsAsync("sacha")).Select(o => o.BalanceAfter)); // plus récent d'abord
        Assert.Equal(15_000, (await _saves.ListAsync("sacha")).Single(s => s.Id == first).WithdrawnOnServer);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(100_001)]
    public async Task Withdraw_RefusesInvalidAmounts(long amount)
    {
        var id = await Store();
        var result = await _inventory.WithdrawFromSaveAsync("sacha", id, amount);
        Assert.False(result.Ok);
        Assert.Equal(100_000u, await StoredMoney(id));
        Assert.Equal(0, await _inventory.GetBalanceAsync("sacha"));
        Assert.Empty(await _inventory.ListOperationsAsync("sacha"));
        Assert.False(Assert.Single(await _saves.ListAsync("sacha")).ModifiedOnServer);
    }

    [Fact]
    public async Task Withdraw_RefusesSomeoneElsesSave()
    {
        var id = await Store(owner: "ondine");
        Assert.False((await _inventory.WithdrawFromSaveAsync("sacha", id, 1_000)).Ok);
        Assert.Equal(100_000u, await StoredMoney(id));
        Assert.Equal(0, await _inventory.GetBalanceAsync("sacha"));
    }

    [Fact]
    public async Task AfterAWithdrawal_UpdateNeedsADownload_ThenMorePlayTime()
    {
        var id = await Store();
        await _inventory.WithdrawFromSaveAsync("sacha", id, 40_000);

        // 1. Pas encore téléchargée : bloqué, même avec une partie plus avancée.
        var blocked = await _saves.UpdateAsync("sacha", id, "b2.sav", Save(money: 100_000, hours: 12));
        Assert.Equal(StoreStatus.ModifiedOnServer, blocked.Status);
        Assert.Contains("télécharge", blocked.Message);

        // 2. Téléchargée : on récupère bien la version modifiée.
        var file = await _saves.DownloadAsync("sacha", id);
        Assert.True(SaveUtil.TryGetSaveFile(file!.Value.Data, out var downloaded));
        Assert.Equal(60_000u, downloaded!.Money);
        Assert.False(Assert.Single(await _saves.ListAsync("sacha")).MustDownloadBeforeUpdate);

        // 3. L'ancienne sauvegarde du jeu (même temps de jeu, ou moins) est refusée, sans possibilité de forcer.
        Assert.Equal(StoreStatus.ModifiedOnServer, (await _saves.UpdateAsync("sacha", id, "b2.sav", Save(money: 100_000, hours: 10))).Status);
        Assert.Equal(StoreStatus.ModifiedOnServer, (await _saves.UpdateAsync("sacha", id, "b2.sav", Save(money: 100_000, hours: 3), acceptOlder: true)).Status);
        Assert.Equal(60_000u, await StoredMoney(id));

        // 4. Une partie plus avancée passe, et tout redevient normal.
        Assert.True((await _saves.UpdateAsync("sacha", id, "b2.sav", Save(money: 65_000, hours: 11))).Ok);
        var info = Assert.Single(await _saves.ListAsync("sacha"));
        Assert.False(info.ModifiedOnServer);
        Assert.Equal(0, info.WithdrawnOnServer);
        Assert.Equal(65_000u, await StoredMoney(id));
    }

    [Fact]
    public async Task DeletingThenReAddingTheOriginal_IsRefused()
    {
        var original = Save(money: 100_000, hours: 10);
        Assert.True((await _saves.AddAsync("sacha", "b2.sav", original)).Ok);
        var id = Assert.Single(await _saves.ListAsync("sacha")).Id;
        await _inventory.WithdrawFromSaveAsync("sacha", id, 40_000);
        var returned = (await _saves.DeleteAsync("sacha", id))!.Value.Data; // version modifiée, rendue au joueur

        // L'original (même temps de jeu, argent intact) est refusé, pour ce compte comme pour un autre ; une copie plus ancienne aussi.
        Assert.Equal(StoreStatus.ModifiedOnServer, (await _saves.AddAsync("sacha", "b2.sav", original)).Status);
        Assert.Equal(StoreStatus.ModifiedOnServer, (await _saves.AddAsync("ondine", "b2.sav", original)).Status);
        Assert.Equal(StoreStatus.ModifiedOnServer, (await _saves.AddAsync("sacha", "b2.sav", Save(money: 100_000, hours: 8))).Status);
        Assert.Empty(await _saves.ListAsync("sacha"));

        // La version rendue est acceptée... mais la trace reste : l'original ne passe toujours pas en mise à jour.
        Assert.True((await _saves.AddAsync("sacha", "b2.sav", returned)).Ok);
        var info = Assert.Single(await _saves.ListAsync("sacha"));
        Assert.True(info.ModifiedOnServer);
        Assert.False(info.MustDownloadBeforeUpdate); // il a déjà ce fichier
        Assert.Equal(StoreStatus.ModifiedOnServer, (await _saves.UpdateAsync("sacha", info.Id, "b2.sav", original)).Status);
        Assert.Equal(60_000u, await StoredMoney(info.Id));

        // Une partie plus avancée lève la trace.
        Assert.True((await _saves.UpdateAsync("sacha", info.Id, "b2.sav", Save(money: 60_500, hours: 11))).Ok);
        Assert.False(Assert.Single(await _saves.ListAsync("sacha")).ModifiedOnServer);
        using var db = _db.NewContext();
        Assert.Empty(db.SaveModifications);
    }

    [Fact]
    public async Task ANewWithdrawal_AfterDownload_BlocksTheUpdateAgain()
    {
        var id = await Store();
        await _inventory.WithdrawFromSaveAsync("sacha", id, 10_000);
        await _saves.DownloadAsync("sacha", id);
        await _inventory.WithdrawFromSaveAsync("sacha", id, 10_000);

        var info = Assert.Single(await _saves.ListAsync("sacha"));
        Assert.True(info.MustDownloadBeforeUpdate); // la version téléchargée n'est plus la bonne
        Assert.Equal(20_000, info.WithdrawnOnServer);
    }

    [Fact]
    public async Task DeletingTheSave_KeepsTheJournalAndTheWallet()
    {
        var id = await Store();
        await _inventory.WithdrawFromSaveAsync("sacha", id, 25_000);
        Assert.NotNull(await _saves.DeleteAsync("sacha", id));

        Assert.Equal(25_000, await _inventory.GetBalanceAsync("sacha"));
        var op = Assert.Single(await _inventory.ListOperationsAsync("sacha"));
        Assert.Contains("Ludo", op.SaveLabel);
        using var db = _db.NewContext();
        Assert.Null((await db.WalletOperations.SingleAsync()).StoredSaveId);
    }

    [Fact]
    public async Task ConcurrentChange_IsDetected()
    {
        var id = await Store();
        // Simule un autre onglet : la sauvegarde change entre la lecture et l'écriture du retrait.
        using (var db = _db.NewContext())
        {
            var save = await db.StoredSaves.SingleAsync();
            await _inventory.WithdrawFromSaveAsync("sacha", id, 1_000); // « l'autre onglet » passe en premier
            save.FileName = "autre.sav";
            save.Version++;
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db.SaveChangesAsync());
        }
        Assert.Equal(99_000u, await StoredMoney(id));
    }

    // ---------- Pokéballs ----------

    [Fact]
    public async Task WithdrawBalls_MovesThemFromTheBagToTheInventory()
    {
        var id = await Store(balls: [(PokeBall, 20), (HyperBall, 5)]);
        var source = Assert.Single(await _inventory.ListBallSourcesAsync("sacha"));
        Assert.Null(source.Unsupported);
        Assert.Equal((25, 999), (source.Total, source.Capacity[PokeBall]));
        Assert.False(source.Capacity.ContainsKey(BeastBall));

        var result = await _inventory.WithdrawBallsAsync("sacha", id, new Dictionary<int, int> { [PokeBall] = 15, [HyperBall] = 5, [SuperBall] = 0 });
        Assert.True(result.Ok, result.Message);

        Assert.Equal(new Dictionary<int, int> { [PokeBall] = 5 }, await StoredBalls(id)); // plus de Hyper Ball dans le sac
        Assert.Equal(new Dictionary<int, int> { [HyperBall] = 5, [PokeBall] = 15 }, await InventoryBalls());
        Assert.Empty(await InventoryBalls("ondine"));
        Assert.Equal(["Hyper Ball", "Poké Ball"], (await _inventory.GetBallsAsync("sacha")).Select(b => b.Name));

        var op = Assert.Single(await _inventory.ListOperationsAsync("sacha"));
        Assert.Equal((WalletOperationKind.WithdrawBallsFromSave, 0L, "5 × Hyper Ball, 15 × Poké Ball"), (op.Kind, op.Amount, op.Details));

        var info = Assert.Single(await _saves.ListAsync("sacha"));
        Assert.True(info.MustDownloadBeforeUpdate);
        Assert.True(info.BallsChangedOnServer);
        Assert.Equal("un transfert de Pokéballs", info.ServerChanges);
        Assert.Single(Directory.GetFiles(_db.BackupRoot, "*retrait-balls-avant*", SearchOption.AllDirectories));
        Assert.Single(Directory.GetFiles(_db.BackupRoot, "*retrait-balls-apres*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task WithdrawBalls_RefusesMoreThanTheBagHolds_AndChangesNothing()
    {
        var id = await Store(balls: [(PokeBall, 10)]);
        Assert.False((await _inventory.WithdrawBallsAsync("sacha", id, new Dictionary<int, int> { [PokeBall] = 11 })).Ok);
        Assert.False((await _inventory.WithdrawBallsAsync("sacha", id, new Dictionary<int, int> { [PokeBall] = 5, [MasterBall] = 1 })).Ok);
        Assert.False((await _inventory.WithdrawBallsAsync("sacha", id, new Dictionary<int, int> { [PokeBall] = -1 })).Ok);
        Assert.False((await _inventory.WithdrawBallsAsync("sacha", id, new Dictionary<int, int>())).Ok);
        Assert.False((await _inventory.WithdrawBallsAsync("ondine", id, new Dictionary<int, int> { [PokeBall] = 1 })).Ok); // pas sa sauvegarde

        Assert.Equal(new Dictionary<int, int> { [PokeBall] = 10 }, await StoredBalls(id));
        Assert.Empty(await InventoryBalls());
        Assert.Empty(await _inventory.ListOperationsAsync("sacha"));
        Assert.False(Assert.Single(await _saves.ListAsync("sacha")).ModifiedOnServer);
    }

    [Fact]
    public async Task DepositBalls_MovesThemFromTheInventoryToAnotherGame()
    {
        // Retirées de Platine, déposées dans Noir 2 : une Super Ball est le même objet d'un jeu à l'autre.
        Assert.True((await _saves.AddAsync("sacha", "platine.sav", SaveSessionTests.CreatePlatinumSave())).Ok);
        var platine = (await _saves.ListAsync("sacha")).Single().Id;
        Assert.True((await _inventory.WithdrawBallsAsync("sacha", platine, new Dictionary<int, int> { [SuperBall] = 15, [PokeBall] = 10 })).Ok);
        var b2 = await Store(balls: [(PokeBall, 3)]);

        var result = await _inventory.DepositBallsAsync("sacha", b2, new Dictionary<int, int> { [SuperBall] = 15, [PokeBall] = 4 });
        Assert.True(result.Ok, result.Message);

        Assert.Equal(new Dictionary<int, int> { [SuperBall] = 15, [PokeBall] = 7 }, await StoredBalls(b2));
        Assert.Equal(new Dictionary<int, int> { [PokeBall] = 6 }, await InventoryBalls()); // la ligne des Super Ball a disparu
        using (var db = _db.NewContext())
            Assert.Equal(1, await db.OwnedItems.CountAsync());

        Assert.Equal([WalletOperationKind.DepositBallsToSave, WalletOperationKind.WithdrawBallsFromSave],
            (await _inventory.ListOperationsAsync("sacha")).Select(o => o.Kind));
        var info = (await _saves.ListAsync("sacha")).Single(s => s.Id == b2);
        Assert.True(info.MustDownloadBeforeUpdate); // le joueur doit la télécharger pour avoir ses Balls
    }

    [Fact]
    public async Task DepositBalls_RefusesWhatTheGameCannotTake()
    {
        var source = await Store(balls: [(PokeBall, 50)], ot: "Pierre");
        Assert.True((await _inventory.WithdrawBallsAsync("sacha", source, new Dictionary<int, int> { [PokeBall] = 50 })).Ok);
        using (var db = _db.NewContext())
        {
            db.OwnedItems.Add(new OwnedItem { OwnerId = "sacha", ItemId = BeastBall, Count = 3 }); // venue de Soleil / Lune
            await db.SaveChangesAsync();
        }
        var b2 = await Store(balls: [(PokeBall, 990)]);

        var unknown = await _inventory.DepositBallsAsync("sacha", b2, new Dictionary<int, int> { [BeastBall] = 1 });
        Assert.False(unknown.Ok);
        Assert.Contains("n'existe pas", unknown.Message);
        var full = await _inventory.DepositBallsAsync("sacha", b2, new Dictionary<int, int> { [PokeBall] = 10 });
        Assert.False(full.Ok);
        Assert.Contains("999", full.Message);
        Assert.False((await _inventory.DepositBallsAsync("sacha", b2, new Dictionary<int, int> { [HyperBall] = 1 })).Ok); // pas dans l'inventaire
        Assert.False((await _inventory.DepositBallsAsync("sacha", b2, new Dictionary<int, int> { [PokeBall] = 51 })).Ok); // plus que l'inventaire

        Assert.Equal(new Dictionary<int, int> { [PokeBall] = 990 }, await StoredBalls(b2));
        Assert.Equal(new Dictionary<int, int> { [PokeBall] = 50, [BeastBall] = 3 }, await InventoryBalls());
        Assert.False((await _saves.ListAsync("sacha")).Single(s => s.Id == b2).ModifiedOnServer);

        Assert.True((await _inventory.DepositBallsAsync("sacha", b2, new Dictionary<int, int> { [PokeBall] = 9 })).Ok); // 999 tout juste
        Assert.Equal(new Dictionary<int, int> { [PokeBall] = 999 }, await StoredBalls(b2));
    }

    [Fact]
    public async Task AfterABallWithdrawal_TheOriginalFileIsRefused_EvenWithTheSameMoney()
    {
        // L'ancienne règle (temps de jeu égal et pas plus d'argent) aurait laissé passer l'original : seule l'empreinte le distingue.
        var original = Save(balls: [(PokeBall, 20)]);
        Assert.True((await _saves.AddAsync("sacha", "b2.sav", original)).Ok);
        var id = Assert.Single(await _saves.ListAsync("sacha")).Id;
        Assert.True((await _inventory.WithdrawBallsAsync("sacha", id, new Dictionary<int, int> { [PokeBall] = 20 })).Ok);

        var downloaded = (await _saves.DownloadAsync("sacha", id))!.Value.Data;
        var refused = await _saves.UpdateAsync("sacha", id, "b2.sav", original, acceptOlder: true);
        Assert.Equal(StoreStatus.ModifiedOnServer, refused.Status);
        Assert.Contains("transfert de Pokéballs", refused.Message);
        Assert.True((await _saves.UpdateAsync("sacha", id, "b2.sav", downloaded)).Ok); // la version rendue passe

        // Supprimer puis renvoyer l'original ne marche pas non plus ; une partie plus avancée lève la trace.
        Assert.NotNull(await _saves.DeleteAsync("sacha", id));
        Assert.Equal(StoreStatus.ModifiedOnServer, (await _saves.AddAsync("sacha", "b2.sav", original)).Status);
        Assert.True((await _saves.AddAsync("sacha", "b2.sav", Save(hours: 11))).Ok);
        Assert.False(Assert.Single(await _saves.ListAsync("sacha")).ModifiedOnServer);
    }

    [Fact]
    public async Task MoneyAndBalls_AreBothDescribed()
    {
        var id = await Store(balls: [(PokeBall, 5)]);
        await _inventory.WithdrawFromSaveAsync("sacha", id, 1_000);
        await _inventory.WithdrawBallsAsync("sacha", id, new Dictionary<int, int> { [PokeBall] = 5 });

        var info = Assert.Single(await _saves.ListAsync("sacha"));
        Assert.Equal("un retrait de 1 000 ₽ et un transfert de Pokéballs", new string(info.ServerChanges.Select(c => char.IsWhiteSpace(c) ? ' ' : c).ToArray()));
        Assert.Empty(await StoredBalls(id));
        Assert.Equal(99_000u, await StoredMoney(id));
    }

    [Fact]
    public void SaveBag_WithdrawLeavesNoGapInThePouch()
    {
        var sav = new SAV5B2W2();
        SaveSessionTests.GiveBalls(sav, (MasterBall, 1), (HyperBall, 2), (SuperBall, 3));
        Assert.Null(SaveBag.Withdraw(sav, new Dictionary<int, int> { [MasterBall] = 1 }, i => $"{i}"));

        var slots = sav.Inventory.First(p => p.CanContain(HyperBall)).Items; // poche « Objets » en 5e génération
        Assert.Equal([HyperBall, SuperBall, 0], slots.Take(3).Select(s => s.Index));
    }

    [Fact]
    public void Money_IsFormattedTheFrenchWay()
    {
        Assert.Equal("125 000 ₽", InventoryService.FormatMoney(125_000).Replace(' ', ' ').Replace(' ', ' '));
    }

    public void Dispose() => _db.Dispose();
}
