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
    private const int Potion = 17, SuperPotion = 26, RareCandy = 50, HealthFeather = 565; // Super Bonbon ; Plume Santé : 5e génération

    private static byte[] Save(uint money = 100_000, int hours = 10, string ot = "Ludo", params (int Item, int Count)[] items)
    {
        var sav = new SAV5B2W2 { OT = ot, TID16 = 12345, SID16 = 54321, PlayedHours = hours, Money = money };
        sav.SetBoxSlotAtIndex(SaveSessionTests.CreatePk5(25, 5), 0, 0);
        if (items.Length > 0)
            SaveSessionTests.GiveItems(sav, items);
        return sav.Write().ToArray();
    }

    private async Task<int> Store(string owner = "sacha", uint money = 100_000, string ot = "Ludo", params (int Item, int Count)[] items)
    {
        Assert.True((await _saves.AddAsync(owner, $"{ot}.sav", Save(money, ot: ot, items: items))).Ok);
        return (await _saves.ListAsync(owner)).Single(s => s.TrainerName == ot).Id;
    }

    private async Task<Dictionary<int, int>> StoredItems(int saveId)
    {
        using var db = _db.NewContext();
        var save = await db.StoredSaves.SingleAsync(s => s.Id == saveId);
        Assert.True(SaveUtil.TryGetSaveFile(save.Data, out var sav));
        return SaveBag.ReadItems(sav!);
    }

    private async Task<Dictionary<int, int>> InventoryItems(string owner = "sacha") =>
        (await _inventory.GetItemsAsync(owner)).ToDictionary(b => b.ItemId, b => b.Count);

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

    // ---------- Pokéballs (et objets en général) ----------

    [Fact]
    public async Task WithdrawBalls_MovesThemFromTheBagToTheInventory()
    {
        var id = await Store(items: [(PokeBall, 20), (HyperBall, 5)]);
        var source = Assert.Single(await _inventory.ListItemSourcesAsync("sacha"));
        Assert.Null(source.Unsupported);
        Assert.Equal((25, 999), (source.Total(ItemCategory.Balls), source.Capacity[PokeBall]));
        Assert.False(source.Capacity.ContainsKey(BeastBall));

        var result = await _inventory.WithdrawItemsAsync("sacha", id, new Dictionary<int, int> { [PokeBall] = 15, [HyperBall] = 5, [SuperBall] = 0 });
        Assert.True(result.Ok, result.Message);

        Assert.Equal(new Dictionary<int, int> { [PokeBall] = 5 }, await StoredItems(id)); // plus de Hyper Ball dans le sac
        Assert.Equal(new Dictionary<int, int> { [HyperBall] = 5, [PokeBall] = 15 }, await InventoryItems());
        Assert.Empty(await InventoryItems("ondine"));
        Assert.Equal(["Hyper Ball", "Poké Ball"], (await _inventory.GetItemsAsync("sacha")).Select(b => b.Name));

        var op = Assert.Single(await _inventory.ListOperationsAsync("sacha"));
        Assert.Equal((WalletOperationKind.WithdrawItemsFromSave, 0L, "5 × Hyper Ball, 15 × Poké Ball"), (op.Kind, op.Amount, op.Details));

        var info = Assert.Single(await _saves.ListAsync("sacha"));
        Assert.True(info.MustDownloadBeforeUpdate);
        Assert.True(info.ItemsChangedOnServer);
        Assert.Equal("un transfert d'objets", info.ServerChanges);
        Assert.Single(Directory.GetFiles(_db.BackupRoot, "*retrait-objets-avant*", SearchOption.AllDirectories));
        Assert.Single(Directory.GetFiles(_db.BackupRoot, "*retrait-objets-apres*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task WithdrawBalls_RefusesMoreThanTheBagHolds_AndChangesNothing()
    {
        var id = await Store(items: [(PokeBall, 10)]);
        Assert.False((await _inventory.WithdrawItemsAsync("sacha", id, new Dictionary<int, int> { [PokeBall] = 11 })).Ok);
        Assert.False((await _inventory.WithdrawItemsAsync("sacha", id, new Dictionary<int, int> { [PokeBall] = 5, [MasterBall] = 1 })).Ok);
        Assert.False((await _inventory.WithdrawItemsAsync("sacha", id, new Dictionary<int, int> { [PokeBall] = -1 })).Ok);
        Assert.False((await _inventory.WithdrawItemsAsync("sacha", id, new Dictionary<int, int>())).Ok);
        Assert.False((await _inventory.WithdrawItemsAsync("ondine", id, new Dictionary<int, int> { [PokeBall] = 1 })).Ok); // pas sa sauvegarde

        Assert.Equal(new Dictionary<int, int> { [PokeBall] = 10 }, await StoredItems(id));
        Assert.Empty(await InventoryItems());
        Assert.Empty(await _inventory.ListOperationsAsync("sacha"));
        Assert.False(Assert.Single(await _saves.ListAsync("sacha")).ModifiedOnServer);
    }

    [Fact]
    public async Task DepositBalls_MovesThemFromTheInventoryToAnotherGame()
    {
        // Retirées de Platine, déposées dans Noir 2 : une Super Ball est le même objet d'un jeu à l'autre.
        Assert.True((await _saves.AddAsync("sacha", "platine.sav", SaveSessionTests.CreatePlatinumSave())).Ok);
        var platine = (await _saves.ListAsync("sacha")).Single().Id;
        Assert.True((await _inventory.WithdrawItemsAsync("sacha", platine, new Dictionary<int, int> { [SuperBall] = 15, [PokeBall] = 10 })).Ok);
        var b2 = await Store(items: [(PokeBall, 3)]);

        var result = await _inventory.DepositItemsAsync("sacha", b2, new Dictionary<int, int> { [SuperBall] = 15, [PokeBall] = 4 });
        Assert.True(result.Ok, result.Message);

        Assert.Equal(new Dictionary<int, int> { [SuperBall] = 15, [PokeBall] = 7 }, await StoredItems(b2));
        Assert.Equal(new Dictionary<int, int> { [PokeBall] = 6 }, await InventoryItems()); // la ligne des Super Ball a disparu
        using (var db = _db.NewContext())
            Assert.Equal(1, await db.OwnedItems.CountAsync());

        Assert.Equal([WalletOperationKind.DepositItemsToSave, WalletOperationKind.WithdrawItemsFromSave],
            (await _inventory.ListOperationsAsync("sacha")).Select(o => o.Kind));
        var info = (await _saves.ListAsync("sacha")).Single(s => s.Id == b2);
        Assert.True(info.MustDownloadBeforeUpdate); // le joueur doit la télécharger pour avoir ses Balls
    }

    [Fact]
    public async Task DepositBalls_RefusesWhatTheGameCannotTake()
    {
        var source = await Store(items: [(PokeBall, 50)], ot: "Pierre");
        Assert.True((await _inventory.WithdrawItemsAsync("sacha", source, new Dictionary<int, int> { [PokeBall] = 50 })).Ok);
        using (var db = _db.NewContext())
        {
            db.OwnedItems.Add(new OwnedItem { OwnerId = "sacha", ItemId = BeastBall, Count = 3 }); // venue de Soleil / Lune
            await db.SaveChangesAsync();
        }
        var b2 = await Store(items: [(PokeBall, 990)]);

        var unknown = await _inventory.DepositItemsAsync("sacha", b2, new Dictionary<int, int> { [BeastBall] = 1 });
        Assert.False(unknown.Ok);
        Assert.Contains("n'existe pas", unknown.Message);
        var full = await _inventory.DepositItemsAsync("sacha", b2, new Dictionary<int, int> { [PokeBall] = 10 });
        Assert.False(full.Ok);
        Assert.Contains("999", full.Message);
        Assert.False((await _inventory.DepositItemsAsync("sacha", b2, new Dictionary<int, int> { [HyperBall] = 1 })).Ok); // pas dans l'inventaire
        Assert.False((await _inventory.DepositItemsAsync("sacha", b2, new Dictionary<int, int> { [PokeBall] = 51 })).Ok); // plus que l'inventaire

        Assert.Equal(new Dictionary<int, int> { [PokeBall] = 990 }, await StoredItems(b2));
        Assert.Equal(new Dictionary<int, int> { [PokeBall] = 50, [BeastBall] = 3 }, await InventoryItems());
        Assert.False((await _saves.ListAsync("sacha")).Single(s => s.Id == b2).ModifiedOnServer);

        Assert.True((await _inventory.DepositItemsAsync("sacha", b2, new Dictionary<int, int> { [PokeBall] = 9 })).Ok); // 999 tout juste
        Assert.Equal(new Dictionary<int, int> { [PokeBall] = 999 }, await StoredItems(b2));
    }

    [Fact]
    public async Task AfterABallWithdrawal_TheOriginalFileIsRefused_EvenWithTheSameMoney()
    {
        // L'ancienne règle (temps de jeu égal et pas plus d'argent) aurait laissé passer l'original : seule l'empreinte le distingue.
        var original = Save(items: [(PokeBall, 20)]);
        Assert.True((await _saves.AddAsync("sacha", "b2.sav", original)).Ok);
        var id = Assert.Single(await _saves.ListAsync("sacha")).Id;
        Assert.True((await _inventory.WithdrawItemsAsync("sacha", id, new Dictionary<int, int> { [PokeBall] = 20 })).Ok);

        var downloaded = (await _saves.DownloadAsync("sacha", id))!.Value.Data;
        var refused = await _saves.UpdateAsync("sacha", id, "b2.sav", original, acceptOlder: true);
        Assert.Equal(StoreStatus.ModifiedOnServer, refused.Status);
        Assert.Contains("transfert d'objets", refused.Message);
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
        var id = await Store(items: [(PokeBall, 5)]);
        await _inventory.WithdrawFromSaveAsync("sacha", id, 1_000);
        await _inventory.WithdrawItemsAsync("sacha", id, new Dictionary<int, int> { [PokeBall] = 5 });

        var info = Assert.Single(await _saves.ListAsync("sacha"));
        Assert.Equal("un retrait de 1 000 ₽ et un transfert d'objets", new string(info.ServerChanges.Select(c => char.IsWhiteSpace(c) ? ' ' : c).ToArray()));
        Assert.Empty(await StoredItems(id));
        Assert.Equal(99_000u, await StoredMoney(id));
    }

    [Fact]
    public void SaveBag_WithdrawLeavesNoGapInThePouch()
    {
        var sav = new SAV5B2W2();
        SaveSessionTests.GiveItems(sav, (MasterBall, 1), (HyperBall, 2), (SuperBall, 3));
        Assert.Null(SaveBag.Withdraw(sav, new Dictionary<int, int> { [MasterBall] = 1 }, i => $"{i}"));

        var slots = sav.Inventory.First(p => p.CanContain(HyperBall)).Items; // poche « Objets » en 5e génération
        Assert.Equal([HyperBall, SuperBall, 0], slots.Take(3).Select(s => s.Index));
    }

    // ---------- Médicaments ----------

    [Fact]
    public async Task BallsAndMedicine_MoveTogetherInOneOperation()
    {
        var id = await Store(items: [(PokeBall, 10), (Potion, 5), (RareCandy, 2)]);
        var source = Assert.Single(await _inventory.ListItemSourcesAsync("sacha"));
        Assert.Equal((10, 7), (source.Total(ItemCategory.Balls), source.Total(ItemCategory.Medicine)));

        var result = await _inventory.WithdrawItemsAsync("sacha", id, new Dictionary<int, int> { [RareCandy] = 1, [Potion] = 5, [PokeBall] = 4 });
        Assert.True(result.Ok, result.Message);

        Assert.Equal(new Dictionary<int, int> { [PokeBall] = 6, [RareCandy] = 1 }, await StoredItems(id));
        var items = await _inventory.GetItemsAsync("sacha");
        Assert.Equal([(PokeBall, ItemCategory.Balls), (Potion, ItemCategory.Medicine), (RareCandy, ItemCategory.Medicine)],
            items.Select(i => (i.ItemId, i.Category))); // Pokéballs d'abord, puis médicaments
        var op = Assert.Single(await _inventory.ListOperationsAsync("sacha"));
        Assert.Equal((WalletOperationKind.WithdrawItemsFromSave, "4 × Poké Ball, 5 × Potion, 1 × Super Bonbon"), (op.Kind, op.Details));
        Assert.Equal("un transfert d'objets", Assert.Single(await _saves.ListAsync("sacha")).ServerChanges);
    }

    [Fact]
    public async Task Medicine_GoesFromPlatinumToBlack2_ButNotTheOtherWayForUnknownItems()
    {
        Assert.True((await _saves.AddAsync("sacha", "platine.sav", SaveSessionTests.CreatePlatinumSave())).Ok);
        var platine = (await _saves.ListAsync("sacha")).Single().Id;
        Assert.True((await _inventory.WithdrawItemsAsync("sacha", platine, new Dictionary<int, int> { [Potion] = 10, [SuperPotion] = 5 })).Ok);

        var b2 = await Store(items: [(Potion, 1), (HealthFeather, 4)]);
        Assert.True((await _inventory.DepositItemsAsync("sacha", b2, new Dictionary<int, int> { [Potion] = 7, [SuperPotion] = 5 })).Ok);
        Assert.Equal(new Dictionary<int, int> { [Potion] = 8, [SuperPotion] = 5, [HealthFeather] = 4 }, await StoredItems(b2));

        // La Plume Santé n'existe qu'à partir de la 5e génération : Platine la refuse.
        Assert.True((await _inventory.WithdrawItemsAsync("sacha", b2, new Dictionary<int, int> { [HealthFeather] = 4 })).Ok);
        Assert.False((await _inventory.ListItemSourcesAsync("sacha")).Single(s => s.SaveId == platine).Capacity.ContainsKey(HealthFeather));
        var refused = await _inventory.DepositItemsAsync("sacha", platine, new Dictionary<int, int> { [HealthFeather] = 1, [Potion] = 1 });
        Assert.False(refused.Ok);
        Assert.Contains("n'existe pas", refused.Message);
        Assert.Equal(new Dictionary<int, int> { [Potion] = 3, [HealthFeather] = 4 }, await InventoryItems()); // rien n'a bougé
    }

    [Fact]
    public void SaveBag_RefusesItemsThatAreNeitherBallsNorMedicine()
    {
        var sav = new SAV5B2W2();
        const int BlackFlute = 68; // objet de la poche Objets, pas un médicament
        SaveSessionTests.GiveItems(sav, (BlackFlute, 1));
        Assert.Empty(SaveBag.ReadItems(sav));
        Assert.NotNull(SaveBag.Withdraw(sav, new Dictionary<int, int> { [BlackFlute] = 1 }, i => $"{i}"));
        Assert.NotNull(SaveBag.Deposit(sav, new Dictionary<int, int> { [BlackFlute] = 1 }, i => $"{i}", "Noir 2"));
    }

    [Fact]
    public void SaveBag_ConvertsItemNumbersForThirdGenerationGames()
    {
        // Émeraude numérote ses objets autrement : la Potion (17 à partir de la 4e génération) y porte le numéro 13.
        var sav = new SAV3E();
        Assert.Equal(99, SaveBag.Capacity(sav)[Potion]); // poche Objets de la 3e génération
        Assert.False(SaveBag.Capacity(sav).ContainsKey(HealthFeather));
        Assert.Null(SaveBag.Deposit(sav, new Dictionary<int, int> { [Potion] = 5, [PokeBall] = 3 }, i => $"{i}", "Émeraude"));

        // Vérifié en mémoire : comme en 4e génération, PKHeX ne sait pas écrire une sauvegarde de 3e génération créée vide.
        Assert.Contains(sav.Inventory.SelectMany(p => p.Items), s => s.Index == 13 && s.Count == 5);
        Assert.Equal(new Dictionary<int, int> { [Potion] = 5, [PokeBall] = 3 }, SaveBag.ReadItems(sav));

        Assert.Null(SaveBag.Withdraw(sav, new Dictionary<int, int> { [Potion] = 5 }, i => $"{i}"));
        Assert.Equal(new Dictionary<int, int> { [PokeBall] = 3 }, SaveBag.ReadItems(sav));
    }

    // ---------- CT et DT ----------

    private const ushort MegaPunch = 5, Cut = 15, Surf = 57, Earthquake = 89, Toxic = 92, RockSmash = 249, FocusPunch = 264, HoneClaws = 468;
    private static readonly int EarthquakeTM = SaveBag.MachineKey(Earthquake), ToxicTM = SaveBag.MachineKey(Toxic),
        FocusPunchTM = SaveBag.MachineKey(FocusPunch);
    private const int TM01 = 328, TM26 = 353, TM94 = 619, HM01 = 420, HM03 = 422;

    [Fact]
    public async Task Machines_AreStoredByMove_AndGoBackAsTheMachineOfTheTargetGame()
    {
        Assert.True((await _saves.AddAsync("sacha", "platine.sav", SaveSessionTests.CreatePlatinumSave())).Ok);
        var platine = (await _saves.ListAsync("sacha")).Single().Id;
        var source = (await _inventory.ListItemSourcesAsync("sacha")).Single();
        Assert.Equal(7, source.Total(ItemCategory.Machines)); // les CS n'apparaissent pas
        Assert.Equal(("CT26", "CT01"), (source.MachineLabels[EarthquakeTM], source.MachineLabels[FocusPunchTM]));

        var result = await _inventory.WithdrawItemsAsync("sacha", platine,
            new Dictionary<int, int> { [EarthquakeTM] = 1, [FocusPunchTM] = 2, [ToxicTM] = 3, [PokeBall] = 1 });
        Assert.True(result.Ok, result.Message);
        var items = await _inventory.GetItemsAsync("sacha");
        Assert.Equal([(PokeBall, "Poké Ball"), (FocusPunchTM, "Mitra-Poing"), (EarthquakeTM, "Séisme"), (ToxicTM, "Toxik")],
            items.Select(i => (i.ItemId, i.Name))); // Pokéballs, puis CT dans l'ordre alphabétique des capacités
        Assert.All(items.Skip(1), i => Assert.Equal(ItemCategory.Machines, i.Category));
        Assert.EndsWith("/tm-ground.png", items.Single(i => i.ItemId == EarthquakeTM).SpriteUrl);
        Assert.Equal("1 × Poké Ball, 2 × CT/DT Mitra-Poing, 1 × CT/DT Séisme, 3 × CT/DT Toxik",
            (await _inventory.ListOperationsAsync("sacha")).Single().Details);

        // Noir 2 : Séisme y est aussi la CT26 ; Mitra-Poing n'y est apprise par aucune CT.
        var b2 = await Store(items: [(TM01, 1)]); // CT01 de Noir 2 : Affûtage
        Assert.Equal(new Dictionary<int, int> { [SaveBag.MachineKey(HoneClaws)] = 1 }, await StoredItems(b2));
        Assert.True((await _inventory.DepositItemsAsync("sacha", b2, new Dictionary<int, int> { [EarthquakeTM] = 1 })).Ok);
        Assert.Equal(new Dictionary<int, int> { [SaveBag.MachineKey(HoneClaws)] = 1, [EarthquakeTM] = 1 }, await StoredItems(b2));

        var refused = await _inventory.DepositItemsAsync("sacha", b2, new Dictionary<int, int> { [FocusPunchTM] = 1 });
        Assert.False(refused.Ok);
        Assert.Contains("aucune CT ni DT", refused.Message);
        Assert.False((await _inventory.ListItemSourcesAsync("sacha")).Single(s => s.SaveId == b2).Capacity.ContainsKey(FocusPunchTM));
    }

    [Fact]
    public async Task ReusableMachines_AreLimitedToOnePerBag()
    {
        Assert.True((await _saves.AddAsync("sacha", "platine.sav", SaveSessionTests.CreatePlatinumSave())).Ok);
        var platine = (await _saves.ListAsync("sacha")).Single().Id;
        Assert.Equal(99, (await _inventory.ListItemSourcesAsync("sacha")).Single().Capacity[ToxicTM]); // CT à usage unique en 4e génération
        Assert.True((await _inventory.WithdrawItemsAsync("sacha", platine, new Dictionary<int, int> { [ToxicTM] = 3 })).Ok);

        var b2 = await Store();
        Assert.Equal(1, (await _inventory.ListItemSourcesAsync("sacha")).Single(s => s.SaveId == b2).Capacity[ToxicTM]); // réutilisable en 5e
        var refused = await _inventory.DepositItemsAsync("sacha", b2, new Dictionary<int, int> { [ToxicTM] = 2 });
        Assert.False(refused.Ok);
        Assert.Contains("1 au plus", refused.Message);
        Assert.True((await _inventory.DepositItemsAsync("sacha", b2, new Dictionary<int, int> { [ToxicTM] = 1 })).Ok);
        Assert.False((await _inventory.DepositItemsAsync("sacha", b2, new Dictionary<int, int> { [ToxicTM] = 1 })).Ok); // déjà dans le sac
        Assert.Equal(new Dictionary<int, int> { [ToxicTM] = 2 }, await InventoryItems());
    }

    [Fact]
    public void SaveBag_NeverTouchesHiddenMachines()
    {
        Assert.True(SaveUtil.TryGetSaveFile(SaveSessionTests.CreatePlatinumSave(), out var sav, "platine.sav"));
        Assert.DoesNotContain(SaveBag.MachineKey(Cut), SaveBag.ReadItems(sav!).Keys);
        Assert.DoesNotContain(SaveBag.MachineKey(Surf), SaveBag.Capacity(sav!).Keys); // Surf n'est qu'une CS dans Platine

        // Toutes les CT retirées : les CS restent, sans trou dans la poche.
        Assert.Null(SaveBag.Withdraw(sav!, SaveSessionTests.PlatinumSampleMoves, i => $"{i}"));
        var pouch = sav!.Inventory.Single(p => p.Type == InventoryType.TMHMs);
        Assert.Equal([(HM01, 1), (HM03, 1), (0, 0)], pouch.Items.Take(3).Select(s => ((int)s.Index, s.Count)));
        Assert.NotNull(SaveBag.Deposit(sav, new Dictionary<int, int> { [SaveBag.MachineKey(Surf)] = 1 }, i => $"{i}", "Platine"));

        // Éclate-Roc : CT94 de Noir 2 que l'on peut retirer, mais seulement une CS dans Platine.
        var b2 = new SAV5B2W2();
        SaveSessionTests.GiveItems(b2, (TM94, 1));
        Assert.Equal(new Dictionary<int, int> { [SaveBag.MachineKey(RockSmash)] = 1 }, SaveBag.ReadItems(b2));
        Assert.False(SaveBag.Capacity(sav).ContainsKey(SaveBag.MachineKey(RockSmash)));
    }

    [Fact]
    public void SaveBag_GivesEachMoveTheMachineOfEachGame()
    {
        // Séisme : CT26 de la 3e à la 7e génération, DT10 dans Épée/Bouclier, CT149 dans Écarlate/Violet.
        Assert.Equal("CT26", SaveBag.MachineLabels(new SAV3E())[EarthquakeTM]);
        Assert.Equal("CT26", SaveBag.MachineLabels(new SAV7SM())[EarthquakeTM]);
        Assert.Equal("DT10", SaveBag.MachineLabels(new SAV8SWSH())[EarthquakeTM]);
        Assert.Equal("CT149", SaveBag.MachineLabels(new SAV9SV())[EarthquakeTM]);
        // CT94 : Éclate-Roc dans X/Y, Force Cachée dans Rubis Oméga/Saphir Alpha.
        Assert.Equal("CT94", SaveBag.MachineLabels(new SAV6XY())[SaveBag.MachineKey(RockSmash)]);
        Assert.False(SaveBag.MachineLabels(new SAV6AO()).ContainsKey(SaveBag.MachineKey(RockSmash)));
        // Diamant Étincelant / Perle Scintillante : Surf est une CT (CT95), pas une CS.
        Assert.Equal("CT95", SaveBag.MachineLabels(new SAV8BS())[SaveBag.MachineKey(Surf)]);
        Assert.Equal(229, SaveBag.MachineLabels(new SAV9SV()).Values.Distinct().Count());
        Assert.Equal(200, SaveBag.MachineLabels(new SAV8SWSH()).Count);
    }

    [Fact]
    public void SaveBag_SwordShield_TakesTechnicalRecords_AndReusableMachines()
    {
        var sav = new SAV8SWSH();
        var capacity = SaveBag.Capacity(sav);
        Assert.Equal(999, capacity[EarthquakeTM]); // DT : à usage unique
        Assert.Equal(1, capacity[SaveBag.MachineKey(MegaPunch)]); // Ultimapoing : CT00, réutilisable
        Assert.Null(SaveBag.Deposit(sav, new Dictionary<int, int> { [EarthquakeTM] = 3 }, i => $"{i}", "Épée"));
        Assert.Equal(new Dictionary<int, int> { [EarthquakeTM] = 3 }, SaveBag.ReadItems(sav));
        Assert.Contains(sav.Inventory.SelectMany(p => p.Items), s => s.Index == 1140 && s.Count == 3); // objet DT10
    }

    [Fact]
    public void SaveBag_ConvertsMachinesForThirdGenerationGames()
    {
        var sav = new SAV3E();
        Assert.Equal(99, SaveBag.Capacity(sav)[EarthquakeTM]);
        Assert.Null(SaveBag.Deposit(sav, new Dictionary<int, int> { [EarthquakeTM] = 2, [FocusPunchTM] = 1 }, i => $"{i}", "Émeraude"));
        Assert.Contains(sav.Inventory.SelectMany(p => p.Items), s => s.Index == 314 && s.Count == 2); // CT26 en numérotation de la 3e génération
        Assert.Equal(new Dictionary<int, int> { [EarthquakeTM] = 2, [FocusPunchTM] = 1 }, SaveBag.ReadItems(sav));
        Assert.Null(SaveBag.Withdraw(sav, new Dictionary<int, int> { [EarthquakeTM] = 2 }, i => $"{i}"));
        Assert.Equal(new Dictionary<int, int> { [FocusPunchTM] = 1 }, SaveBag.ReadItems(sav));
    }

    [Fact]
    public void Money_IsFormattedTheFrenchWay()
    {
        Assert.Equal("125 000 ₽", InventoryService.FormatMoney(125_000).Replace(' ', ' ').Replace(' ', ' '));
    }

    public void Dispose() => _db.Dispose();
}
