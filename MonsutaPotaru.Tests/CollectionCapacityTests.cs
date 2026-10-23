using Microsoft.EntityFrameworkCore;
using PKHeX.Core;
using MonsutaPotaru.Data;
using MonsutaPotaru.Services;

namespace MonsutaPotaru.Tests;

/// <summary>Taille maximale de la collection : blocages (banque, sauvegardes stockées) et achat d'emplacements.</summary>
public sealed class CollectionCapacityTests : IDisposable
{
    private readonly TestDatabase _db = new("sacha", "ondine");
    private readonly StoredSaveService _saves;
    private readonly InventoryService _inventory;
    private readonly BankService _bank;

    public CollectionCapacityTests()
    {
        _saves = _db.StoredSaves();
        _inventory = _db.Inventory();
        _bank = _db.Bank();
    }

    /// <summary>Sauvegarde Noir 2 / Blanc 2 avec <paramref name="count"/> Pokémon dans les boîtes.</summary>
    private static byte[] Save(int count, int hours = 10, string ot = "Ludo")
    {
        var sav = new SAV5B2W2 { OT = ot, TID16 = 12345, SID16 = 54321, PlayedHours = hours };
        for (int i = 0; i < count; i++)
            sav.SetBoxSlotAtIndex(SaveSessionTests.CreatePk5((ushort)(i % 600 + 1), 5, ot, i), i / 30, i % 30);
        return sav.Write().ToArray();
    }

    private async Task SetLimit(string owner, int limit)
    {
        using var db = _db.NewContext();
        await db.Users.Where(u => u.Id == owner).ExecuteUpdateAsync(u => u.SetProperty(x => x.CollectionLimit, limit));
    }

    private async Task SetWallet(string owner, long money)
    {
        using var db = _db.NewContext();
        db.Wallets.Add(new Wallet { OwnerId = owner, Money = money });
        await db.SaveChangesAsync();
    }

    private async Task<int> AddBankPokemon(string owner, int count)
    {
        using var db = _db.NewContext();
        // Chacun son PID : d'autres Pokémon que ceux des sauvegardes (CreatePk5), sinon leur dépôt serait refusé (doublon).
        var entries = Enumerable.Range(0, count).Select(i =>
        {
            var pk = SaveSessionTests.CreatePk5(25, 5);
            pk.PID = 0x0B000000u + (uint)i;
            return BankService.CreateEntry(new PokemonInfo(), pk, owner, DateTime.UtcNow);
        }).ToList();
        db.BankPokemon.AddRange(entries);
        await db.SaveChangesAsync();
        return entries[0].Id;
    }

    [Fact]
    public async Task NewAccounts_Get1000Slots()
    {
        Assert.Equal(new CollectionUsage(0, 1000), await _bank.GetUsageAsync("sacha"));
    }

    [Fact]
    public async Task Usage_CountsBankAndStoredSaves()
    {
        await AddBankPokemon("sacha", 2);
        Assert.True((await _saves.AddAsync("sacha", "b2.sav", Save(3))).Ok);
        Assert.Equal(5, (await _bank.GetUsageAsync("sacha")).Used);
    }

    [Fact]
    public async Task AddingASave_ThatDoesNotFit_IsRefusedEntirely()
    {
        await AddBankPokemon("sacha", 1);
        await SetLimit("sacha", 4);

        var result = await _saves.AddAsync("sacha", "b2.sav", Save(5));
        Assert.Equal(StoreStatus.CollectionFull, result.Status);
        Assert.Contains("5 Pokémon", result.Message);
        Assert.Contains("3 emplacements libres (1 / 4)", result.Message);
        Assert.Contains("il manque 2 emplacements", result.Message);
        Assert.Empty(await _saves.ListAsync("sacha"));
        Assert.Equal(1, (await _bank.GetUsageAsync("sacha")).Used);

        Assert.True((await _saves.AddAsync("sacha", "b2.sav", Save(3))).Ok); // pile la place restante
    }

    [Fact]
    public async Task UpdatingASave_CountsOnlyTheDifference()
    {
        await SetLimit("sacha", 5);
        Assert.True((await _saves.AddAsync("sacha", "b2.sav", Save(4))).Ok);
        var id = Assert.Single(await _saves.ListAsync("sacha")).Id;

        Assert.True((await _saves.UpdateAsync("sacha", id, "b2.sav", Save(5, hours: 11))).Ok); // +1 : juste assez

        var refused = await _saves.UpdateAsync("sacha", id, "b2.sav", Save(7, hours: 12));
        Assert.Equal(StoreStatus.CollectionFull, refused.Status);
        Assert.Contains("2 de plus que la version stockée", refused.Message);
        Assert.Equal(5, Assert.Single(await _saves.ListAsync("sacha")).PokemonCount);
    }

    [Fact]
    public async Task AnAccountAboveTheLimit_KeepsEverything_ButCanOnlyShrink()
    {
        Assert.True((await _saves.AddAsync("sacha", "b2.sav", Save(6))).Ok);
        var id = Assert.Single(await _saves.ListAsync("sacha")).Id;
        await SetLimit("sacha", 3); // compte existant déjà au-dessus

        Assert.Equal(StoreStatus.CollectionFull, (await _saves.AddAsync("sacha", "autre.sav", Save(1, ot: "Pierre"))).Status);
        Assert.True((await _saves.UpdateAsync("sacha", id, "b2.sav", Save(5, hours: 11))).Ok); // réduit : toujours permis
        Assert.Equal(StoreStatus.CollectionFull, (await _saves.UpdateAsync("sacha", id, "b2.sav", Save(6, hours: 12))).Status);
        Assert.NotNull(await _saves.DeleteAsync("sacha", id));
    }

    [Fact]
    public async Task BankCommit_ThatDoesNotFit_IsRefusedEntirely()
    {
        await AddBankPokemon("sacha", 2);
        await SetLimit("sacha", 3);
        var session = new SaveSession();
        Assert.True(session.TryLoad(Save(2, ot: "Pierre"), "p.sav", out var error), error);
        Assert.True(session.TryDeposit(0, 0, out error), error);
        Assert.True(session.TryDeposit(0, 1, out error), error);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _bank.CommitAsync("sacha", session));
        Assert.Contains("2 Pokémon à déposer, 1 emplacement libre (2 / 3)", ex.Message);
        Assert.Equal(2, (await _bank.GetUsageAsync("sacha")).Used); // rien n'a été déposé
    }

    [Fact]
    public async Task BankCommit_WithdrawalsFreeTheirSlots()
    {
        var bankId = await AddBankPokemon("sacha", 3);
        await SetLimit("sacha", 3); // pleine
        var session = new SaveSession();
        Assert.True(session.TryLoad(Save(1, ot: "Pierre"), "p.sav", out var error), error);
        Assert.True(session.TryDeposit(0, 0, out error), error);                                  // +1
        Assert.True(session.TryWithdraw(bankId, SaveSessionTests.CreatePk5(25, 5), out error), error); // −1

        await _bank.CommitAsync("sacha", session);
        Assert.Equal(new CollectionUsage(3, 3), await _bank.GetUsageAsync("sacha"));
    }

    [Fact]
    public async Task BuyingSlots_DebitsTheWallet_AndRaisesTheLimit()
    {
        await SetWallet("sacha", 25_000);

        var result = await _inventory.BuyCollectionSlotsAsync("sacha", 2);
        Assert.True(result.Ok, result.Message);
        Assert.Equal((1_200, 5_000L), (result.Limit, result.Balance));
        Assert.Equal(1_200, (await _bank.GetUsageAsync("sacha")).Limit);
        Assert.Equal(5_000, await _inventory.GetBalanceAsync("sacha"));

        var op = Assert.Single(await _inventory.ListOperationsAsync("sacha"));
        Assert.Equal((WalletOperationKind.BuyCollectionSlots, -20_000L, 5_000L), (op.Kind, op.Amount, op.BalanceAfter));
        Assert.Contains("1 200", op.Details.Replace(' ', ' ').Replace(' ', ' '));
    }

    [Theory]
    [InlineData(0L, 1)]       // porte-monnaie vide
    [InlineData(15_000L, 2)]  // pas assez pour 2 lots
    [InlineData(50_000L, 0)]  // aucun lot
    [InlineData(50_000L, -3)]
    public async Task BuyingSlots_IsRefused_WithoutEnoughMoneyOrLots(long money, int lots)
    {
        if (money > 0) await SetWallet("sacha", money);

        var result = await _inventory.BuyCollectionSlotsAsync("sacha", lots);
        Assert.False(result.Ok);
        Assert.NotEmpty(result.Message);
        Assert.Equal(1_000, (await _bank.GetUsageAsync("sacha")).Limit);
        Assert.Equal(money, await _inventory.GetBalanceAsync("sacha"));
        Assert.Empty(await _inventory.ListOperationsAsync("sacha"));
    }

    [Fact]
    public void FullMessages_AreReadable()
    {
        var message = CollectionCapacity.BankFullMessage(new CollectionUsage(999, 1000), 3);
        Assert.Equal("Pas assez de place dans ta collection : 3 Pokémon à déposer, 1 emplacement libre (999 / 1 000). Agrandis-la depuis « Ma collection ».",
            message.Replace(' ', ' ').Replace(' ', ' '));
        Assert.Contains("aucun emplacement libre (1 050 / 1 000)",
            CollectionCapacity.BankFullMessage(new CollectionUsage(1050, 1000), 1).Replace(' ', ' ').Replace(' ', ' '));
    }

    public void Dispose() => _db.Dispose();
}
