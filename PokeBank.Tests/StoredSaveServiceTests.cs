using Microsoft.EntityFrameworkCore;
using PKHeX.Core;
using PokeBank.Services;

namespace PokeBank.Tests;

/// <summary>Sauvegardes stockées : unicité sur le serveur, mise à jour, suppression, refus dans la banque.</summary>
public sealed class StoredSaveServiceTests : IDisposable
{
    private readonly TestDatabase _db = new("sacha", "ondine");
    private readonly StoredSaveService _saves;

    public StoredSaveServiceTests() => _saves = _db.StoredSaves();

    /// <summary>Sauvegarde Noir 2 / Blanc 2 : un Pokémon dans l'équipe (s'il y en a) puis les autres dans la boîte 1.</summary>
    private static byte[] Save(string ot = "Ludo", ushort sid = 54321, int hours = 10, ushort[]? party = null, params ushort[] box)
    {
        var sav = new SAV5B2W2 { OT = ot, TID16 = 12345, SID16 = sid, PlayedHours = hours };
        var p = 0;
        foreach (var species in party ?? [])
            sav.SetPartySlotAtIndex(SaveSessionTests.CreatePk5(species, 10), p++);
        for (int i = 0; i < box.Length; i++)
            sav.SetBoxSlotAtIndex(SaveSessionTests.CreatePk5(box[i], 5), 0, i);
        return sav.Write().ToArray();
    }

    private async Task<int> StoredId(string owner) => (await _saves.ListAsync(owner)).Single().Id;

    [Fact]
    public void Identity_DependsOnTrainer()
    {
        static string Key(byte[] data)
        {
            Assert.True(SaveUtil.TryGetSaveFile(data, out var sav));
            return SaveIdentity.Of(sav!).Key;
        }
        Assert.Equal(Key(Save(box: 25)), Key(Save(hours: 99, box: 133))); // même partie, plus tard
        Assert.NotEqual(Key(Save()), Key(Save(sid: 1)));
        Assert.NotEqual(Key(Save()), Key(Save(ot: "Autre")));
    }

    [Fact]
    public async Task Add_StoresAllPokemon_ReadOnly()
    {
        var result = await _saves.AddAsync("sacha", "b2.sav", Save(party: [6], box: [25, 133]));
        Assert.True(result.Ok, result.Message);

        var info = Assert.Single(await _saves.ListAsync("sacha"));
        Assert.Equal(3, info.PokemonCount);
        Assert.Equal(10 * 3600, info.PlayTimeSeconds);

        var bank = _db.Bank();
        var items = await bank.GetRangeAsync("sacha", null, 0, 10, CollectionFilter.Save(info.Id));
        Assert.Equal([6, 25, 133], items.Select(p => (int)p.Species)); // équipe puis boîtes
        Assert.All(items, p => Assert.Equal(info.Id, p.StoredSaveId));

        using var db = _db.NewContext();
        Assert.Equal(["Équipe 1", "Boîte 1, case 1", "Boîte 1, case 2"],
            db.BankPokemon.OrderBy(p => p.Position).Select(p => p.Location!).ToList());

        // Lecture seule : absents de « Ma banque », présents dans « Tout ».
        Assert.Empty(await bank.ListAsync("sacha"));
        Assert.Equal(0, await bank.CountAsync("sacha", filter: CollectionFilter.Bank));
        Assert.Equal(3, await bank.CountAsync("sacha"));
    }

    [Fact]
    public async Task Add_SameSaveTwice_IsRefused_EvenForAnotherAccount()
    {
        Assert.True((await _saves.AddAsync("sacha", "b2.sav", Save(box: 25))).Ok);

        var again = await _saves.AddAsync("sacha", "copie.sav", Save(hours: 20, box: 25));
        Assert.Equal(StoreStatus.AlreadyStored, again.Status);
        Assert.Contains("b2.sav", again.Message);

        var other = await _saves.AddAsync("ondine", "b2.sav", Save(box: 25));
        Assert.Equal(StoreStatus.AlreadyStored, other.Status);
        Assert.Equal(StoredSaveService.OtherAccountMessage, other.Message); // sans dévoiler qui
        Assert.Empty(await _saves.ListAsync("ondine"));
    }

    [Fact]
    public async Task Add_RejectsGarbage()
    {
        var result = await _saves.AddAsync("sacha", "x.sav", new byte[1234]);
        Assert.Equal(StoreStatus.Invalid, result.Status);
    }

    [Fact]
    public async Task Update_ReplacesThePokemon_AndKeepsABackup()
    {
        await _saves.AddAsync("sacha", "b2.sav", Save(box: [25, 133]));
        var id = await StoredId("sacha");

        var result = await _saves.UpdateAsync("sacha", id, "b2.sav", Save(hours: 12, box: [1, 4, 7]));
        Assert.True(result.Ok, result.Message);

        var items = await _db.Bank().GetRangeAsync("sacha", null, 0, 10, CollectionFilter.Save(id));
        Assert.Equal([1, 4, 7], items.Select(p => (int)p.Species));
        Assert.Equal(3, Assert.Single(await _saves.ListAsync("sacha")).PokemonCount);
        Assert.Single(Directory.GetFiles(_db.BackupRoot, "*stockee-remplacee*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Update_WithAnotherTrainer_IsRefused()
    {
        await _saves.AddAsync("sacha", "b2.sav", Save(box: 25));
        var result = await _saves.UpdateAsync("sacha", await StoredId("sacha"), "autre.sav", Save(sid: 1, box: 1));
        Assert.Equal(StoreStatus.WrongTrainer, result.Status);
    }

    [Fact]
    public async Task Update_WithLessPlayTime_NeedsConfirmation()
    {
        await _saves.AddAsync("sacha", "b2.sav", Save(hours: 10, box: 25));
        var id = await StoredId("sacha");

        var result = await _saves.UpdateAsync("sacha", id, "vieux.sav", Save(hours: 3, box: 1));
        Assert.Equal(StoreStatus.OlderPlayTime, result.Status);
        Assert.Equal((10 * 3600, 3 * 3600), (result.OldPlayTime, result.NewPlayTime));
        Assert.Equal("b2.sav", Assert.Single(await _saves.ListAsync("sacha")).FileName); // rien n'a changé

        Assert.True((await _saves.UpdateAsync("sacha", id, "vieux.sav", Save(hours: 3, box: 1), acceptOlder: true)).Ok);
        Assert.Equal("vieux.sav", Assert.Single(await _saves.ListAsync("sacha")).FileName);
    }

    [Fact]
    public async Task Delete_ReturnsTheFile_RemovesThePokemon_AndFreesTheSave()
    {
        var original = Save(box: [25, 133]);
        await _saves.AddAsync("sacha", "b2.sav", original);
        var id = await StoredId("sacha");

        Assert.Null(await _saves.DeleteAsync("ondine", id)); // pas la sienne

        var file = await _saves.DeleteAsync("sacha", id);
        Assert.NotNull(file);
        Assert.Equal("b2.sav", file.Value.FileName);
        Assert.Equal(original, file.Value.Data);
        Assert.Empty(await _saves.ListAsync("sacha"));
        using (var db = _db.NewContext())
            Assert.Equal(0, await db.BankPokemon.CountAsync());
        Assert.Single(Directory.GetFiles(_db.BackupRoot, "*stockee-supprimee*", SearchOption.AllDirectories));

        Assert.True((await _saves.AddAsync("ondine", "b2.sav", original)).Ok); // de nouveau libre
    }

    [Fact]
    public async Task DeleteWithoutDownload_RemovesTheSave_AndKeepsABackup()
    {
        await _saves.AddAsync("sacha", "b2.sav", Save(box: [25, 133]));
        var id = await StoredId("sacha");

        Assert.Equal(StoreStatus.NotFound, (await _saves.DeleteWithoutDownloadAsync("ondine", id)).Status); // pas la sienne

        var result = await _saves.DeleteWithoutDownloadAsync("sacha", id);
        Assert.True(result.Ok, result.Message);
        Assert.Equal("Sauvegarde « b2.sav » retirée du serveur (2 Pokémon).", result.Message);
        Assert.Empty(await _saves.ListAsync("sacha"));
        using (var db = _db.NewContext())
            Assert.Equal(0, await db.BankPokemon.CountAsync());
        Assert.Single(Directory.GetFiles(_db.BackupRoot, "*stockee-supprimee*", SearchOption.AllDirectories)); // copie gardée
        Assert.Equal(StoreStatus.NotFound, (await _saves.DeleteWithoutDownloadAsync("sacha", id)).Status);
    }

    [Fact]
    public async Task BankCommit_IsRefused_OnceTheSaveIsStored()
    {
        var data = Save(box: 25);
        var session = new SaveSession();
        Assert.True(session.TryLoad(data, "b2.sav", out var error), error);
        Assert.True(session.TryDeposit(0, 0, out error), error);

        await _saves.AddAsync("ondine", "b2.sav", data); // stockée entre-temps, depuis un autre compte

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _db.Bank().CommitAsync("sacha", session));
        Assert.Contains("stockée", ex.Message);
        Assert.Equal(0, await _db.Bank().CountAsync("sacha", filter: CollectionFilter.Bank));
    }

    public void Dispose() => _db.Dispose();
}
