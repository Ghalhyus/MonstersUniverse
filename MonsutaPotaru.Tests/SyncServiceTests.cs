using Microsoft.EntityFrameworkCore;
using PKHeX.Core;
using MonsutaPotaru.Data;
using MonsutaPotaru.Services;

namespace MonsutaPotaru.Tests;

/// <summary>Synchronisation 3DS : liaison par code, jeton, envoi des sauvegardes (ajout, mise à jour, refus).</summary>
public sealed class SyncServiceTests : IDisposable
{
    private readonly TestDatabase _db = new("sacha", "ondine");
    private readonly SyncService _sync;
    private readonly StoredSaveService _saves;

    public SyncServiceTests()
    {
        _sync = _db.Sync();
        _saves = _db.StoredSaves();
    }

    private static byte[] Save(int hours = 10, string ot = "Ludo", uint money = 1000, params ushort[] box)
    {
        var sav = new SAV5B2W2 { OT = ot, TID16 = 12345, SID16 = 54321, PlayedHours = hours, Money = money };
        for (int i = 0; i < box.Length; i++)
            sav.SetBoxSlotAtIndex(SaveSessionTests.CreatePk5(box[i], 5), 0, i);
        return sav.Write().ToArray();
    }

    /// <summary>Console reliée au compte, prête à envoyer.</summary>
    private async Task<SyncCaller> Linked(string owner = "sacha", string name = "3DS de test")
    {
        var code = await _sync.CreatePairingCodeAsync(owner);
        var link = await _sync.LinkAsync(code.Code, name);
        Assert.True(link.Ok, link.Message);
        return (await _sync.AuthenticateAsync(link.Token))!;
    }

    [Fact]
    public async Task PairingCode_LinksTheConsole_Once()
    {
        var code = await _sync.CreatePairingCodeAsync("sacha");
        Assert.Matches("^[A-Z2-9]{4}-[A-Z2-9]{4}$", code.Code);
        Assert.DoesNotMatch("[01ILO]", code.Code);

        // Tapé en minuscules, sans tiret : accepté.
        var link = await _sync.LinkAsync(code.Code.Replace("-", " ").ToLowerInvariant(), "  Console\nde Sacha  ");
        Assert.True(link.Ok, link.Message);
        Assert.Equal("sacha", link.Account);
        Assert.False(string.IsNullOrEmpty(link.Token));

        var device = Assert.Single(await _sync.ListDevicesAsync("sacha"));
        Assert.Equal("Consolede Sacha", device.Name); // caractères de contrôle retirés
        Assert.Null(device.LastUsedAt);
        Assert.Empty(await _sync.ListDevicesAsync("ondine"));

        // À usage unique.
        Assert.False((await _sync.LinkAsync(code.Code, "Autre")).Ok);
        Assert.Single(await _sync.ListDevicesAsync("sacha"));
    }

    [Fact]
    public async Task PairingCode_Refused_WhenWrongExpiredOrReplaced()
    {
        Assert.Contains("8 caractères", (await _sync.LinkAsync("ABC", null)).Message);
        Assert.Contains("inconnu", (await _sync.LinkAsync("ABCD-EFGH", null)).Message);

        var first = await _sync.CreatePairingCodeAsync("sacha");
        var second = await _sync.CreatePairingCodeAsync("sacha"); // remplace le premier
        Assert.False((await _sync.LinkAsync(first.Code, null)).Ok);

        using (var db = _db.NewContext())
            await db.SyncPairingCodes.ExecuteUpdateAsync(s => s.SetProperty(c => c.ExpiresAt, DateTime.UtcNow.AddSeconds(-1)));
        var expired = await _sync.LinkAsync(second.Code, null);
        Assert.False(expired.Ok);
        Assert.Contains("expiré", expired.Message);
        Assert.Empty(await _sync.ListDevicesAsync("sacha"));

        var cancelled = await _sync.CreatePairingCodeAsync("sacha");
        await _sync.CancelPairingCodeAsync("sacha");
        Assert.False((await _sync.LinkAsync(cancelled.Code, null)).Ok);
    }

    [Fact]
    public async Task Token_IsStoredHashed_AndStopsWorkingOnceUnlinked()
    {
        var code = await _sync.CreatePairingCodeAsync("sacha");
        var token = (await _sync.LinkAsync(code.Code, null)).Token!;

        using (var db = _db.NewContext())
            Assert.NotEqual(token, (await db.SyncDevices.SingleAsync()).TokenHash);

        var caller = await _sync.AuthenticateAsync(token);
        Assert.NotNull(caller);
        Assert.Equal(("sacha", "3DS"), (caller.OwnerId, caller.DeviceName)); // nom par défaut
        Assert.NotNull(Assert.Single(await _sync.ListDevicesAsync("sacha")).LastUsedAt);

        Assert.Null(await _sync.AuthenticateAsync("faux"));
        Assert.Null(await _sync.AuthenticateAsync(null));

        Assert.False(await _sync.UnlinkAsync("ondine", caller.DeviceId)); // pas sa console
        Assert.True(await _sync.UnlinkAsync("sacha", caller.DeviceId));
        Assert.Null(await _sync.AuthenticateAsync(token));
    }

    [Fact]
    public async Task Upload_AddsThenUpdates_AndSkipsIdenticalFiles()
    {
        var caller = await Linked();

        var added = await _sync.UploadAsync(caller, "sdmc:/roms/nds/saves/Pokemon Blanc 2.sav", Save(box: [25]));
        Assert.Equal(SyncUploadStatus.Added, added.Status);
        var stored = Assert.Single(await _saves.ListAsync("sacha"));
        Assert.Equal("Pokemon Blanc 2.sav", stored.FileName); // sans le dossier de la console
        Assert.Equal(1, stored.PokemonCount);

        var same = await _sync.UploadAsync(caller, "Pokemon Blanc 2.sav", Save(box: [25]));
        Assert.Equal(SyncUploadStatus.Unchanged, same.Status);

        // Même fichier sous un autre nom (version exacte reconnue par la console) : seul le nom change.
        var renamed = await _sync.UploadAsync(caller, "Pokémon Blanc 2 (FR).sav", Save(box: [25]));
        Assert.Equal(SyncUploadStatus.Unchanged, renamed.Status);
        Assert.Contains("renommée", renamed.Message);
        Assert.Equal("Pokémon Blanc 2 (FR).sav", Assert.Single(await _saves.ListAsync("sacha")).FileName);

        var updated = await _sync.UploadAsync(caller, "Pokemon Blanc 2.sav", Save(hours: 11, box: [25, 133]));
        Assert.Equal(SyncUploadStatus.Updated, updated.Status);
        Assert.Equal(2, Assert.Single(await _saves.ListAsync("sacha")).PokemonCount);
    }

    [Fact]
    public async Task Upload_NeverForcesAnOlderSave()
    {
        var caller = await Linked();
        Assert.Equal(SyncUploadStatus.Added, (await _sync.UploadAsync(caller, "b2.sav", Save(hours: 20, box: [25]))).Status);

        var older = await _sync.UploadAsync(caller, "b2.sav", Save(hours: 5, box: [25, 133]));
        Assert.Equal((SyncUploadStatus.Refused, StoreStatus.OlderPlayTime), (older.Status, older.Refusal));
        Assert.Contains("Mes sauvegardes", older.Message);
        Assert.Equal(20 * 3600, Assert.Single(await _saves.ListAsync("sacha")).PlayTimeSeconds);
    }

    [Fact]
    public async Task Upload_RefusesAnotherAccountsGame_AndNonSaves()
    {
        Assert.True((await _saves.AddAsync("ondine", "b2.sav", Save(box: [25]))).Ok);
        var caller = await Linked("sacha");

        var other = await _sync.UploadAsync(caller, "b2.sav", Save(hours: 30, box: [25]));
        Assert.Equal((SyncUploadStatus.Refused, StoreStatus.AlreadyStored), (other.Status, other.Refusal));
        Assert.Equal(StoredSaveService.OtherAccountMessage, other.Message);
        Assert.Empty(await _saves.ListAsync("sacha"));

        var junk = await _sync.UploadAsync(caller, "zelda.sav", new byte[0x8000]);
        Assert.Equal((SyncUploadStatus.Refused, StoreStatus.Invalid), (junk.Status, junk.Refusal));
    }

    [Fact]
    public async Task Upload_IsBlocked_WhileTheServerVersionWasNotDownloaded()
    {
        var caller = await Linked();
        Assert.Equal(SyncUploadStatus.Added, (await _sync.UploadAsync(caller, "b2.sav", Save(box: [25]))).Status);
        var id = Assert.Single(await _saves.ListAsync("sacha")).Id;
        Assert.True((await _db.Inventory().WithdrawFromSaveAsync("sacha", id, 500)).Ok);

        var blocked = await _sync.UploadAsync(caller, "b2.sav", Save(hours: 12, box: [25]));
        Assert.Equal((SyncUploadStatus.Refused, StoreStatus.ModifiedOnServer), (blocked.Status, blocked.Refusal));
        Assert.Contains("télécharge", blocked.Message);
    }

    [Theory]
    [InlineData(null, "sauvegarde.sav")]
    [InlineData("  ", "sauvegarde.sav")]
    [InlineData("sdmc:/3ds/Pokémon X/main", "main")]
    [InlineData("a\\b\\Pokémon <Noir>?.sav", "Pokémon Noir.sav")]
    public void FileName_IsCleaned(string? sent, string expected) => Assert.Equal(expected, SyncService.CleanFileName(sent));

    [Fact]
    public void FileName_IsShortened_KeepingItsExtension()
    {
        var name = SyncService.CleanFileName(new string('a', 300) + ".sav");
        Assert.Equal(SyncService.MaxFileNameLength, name.Length);
        Assert.EndsWith(".sav", name);
    }

    // --- Récupération depuis la console ---------------------------------------------------------------------------

    private const string Cle = "sd:sdmc:/saves/Pokemon - Version Noire 2.sav";

    [Fact]
    public async Task Upload_RecordsWhereTheFileIs_ForThisConsoleOnly()
    {
        var console = await Linked();
        var other = await Linked(name: "Autre 3DS");
        var data = Save(box: [25]);
        Assert.Equal(SyncUploadStatus.Added, (await _sync.UploadAsync(console, "noire2.sav", data, Cle)).Status);

        var entry = Assert.Single(await _sync.ListForDeviceAsync(console));
        Assert.Equal([Cle], entry.Keys);
        Assert.Equal(("ds", "B2W2"), (entry.Console, entry.Family)); // version absente d'une sauvegarde créée par PKHeX
        Assert.Equal(SaveModification.Hash(data), entry.Hash);
        Assert.Equal(("Ludo", "noire2.sav", 10 * 3600, false), (entry.Trainer, entry.FileName, entry.PlayTimeSeconds, entry.ModifiedOnServer));

        Assert.Empty(Assert.Single(await _sync.ListForDeviceAsync(other)).Keys);

        // Même fichier envoyé depuis un autre endroit (déplacé sur la carte SD) : les deux emplacements sont connus.
        Assert.Equal(SyncUploadStatus.Unchanged, (await _sync.UploadAsync(console, "noire2.sav", data, "sd:sdmc:/roms/nds/saves/noire2.sav")).Status);
        Assert.Equal(2, Assert.Single(await _sync.ListForDeviceAsync(console)).Keys.Count);

        // Un refus ne note rien.
        Assert.Equal(SyncUploadStatus.Refused, (await _sync.UploadAsync(console, "x.sav", new byte[0x8000], "sd:sdmc:/x.sav")).Status);
        Assert.Equal(2, Assert.Single(await _sync.ListForDeviceAsync(console)).Keys.Count);
    }

    [Fact]
    public async Task Compare_FollowsPlayTime_AndNeverOverwritesAnotherGame()
    {
        var console = await Linked();
        Assert.True((await _saves.AddAsync("sacha", "b2.sav", Save(hours: 10, box: [25]))).Ok);
        var id = Assert.Single(await _saves.ListAsync("sacha")).Id;

        Assert.Equal(SyncCompareVerdict.UpToDate, (await _sync.CompareAsync(console, id, Save(hours: 10, box: [25]))).Verdict);

        var older = await _sync.CompareAsync(console, id, Save(hours: 8, box: [25]));
        Assert.Equal(SyncCompareVerdict.Retrievable, older.Verdict);
        Assert.Contains("Plus avancée sur le site", older.Message);

        var newer = await _sync.CompareAsync(console, id, Save(hours: 12, box: [25]));
        Assert.Equal((SyncCompareVerdict.Refused, SyncCompareRefusal.MoreAdvanced), (newer.Verdict, newer.Refusal));
        Assert.Contains("plus avancée", newer.Message);

        var otherGame = await _sync.CompareAsync(console, id, Save(ot: "Autre", box: [25]));
        Assert.Equal((SyncCompareVerdict.Refused, SyncCompareRefusal.OtherGame), (otherGame.Verdict, otherGame.Refusal));
        Assert.Contains("autre partie", otherGame.Message);

        Assert.Equal(SyncCompareRefusal.Unreadable, (await _sync.CompareAsync(console, id, new byte[0x80000])).Refusal);
        Assert.Equal(SyncCompareVerdict.Refused, (await _sync.CompareAsync(await Linked("ondine"), id, Save(hours: 8))).Verdict); // pas son compte
    }

    [Fact]
    public async Task AfterAWithdrawal_TheOldConsoleSave_IsRetrievable_ThenDownloadUnblocksUpdates()
    {
        var console = await Linked();
        var old = Save(hours: 10, money: 1000, box: [25]);
        Assert.Equal(SyncUploadStatus.Added, (await _sync.UploadAsync(console, "b2.sav", old, Cle)).Status);
        var id = Assert.Single(await _saves.ListAsync("sacha")).Id;
        Assert.True((await _db.Inventory().WithdrawFromSaveAsync("sacha", id, 500)).Ok);

        var entry = Assert.Single(await _sync.ListForDeviceAsync(console));
        Assert.True(entry.ModifiedOnServer);
        Assert.NotEqual(SaveModification.Hash(old), entry.Hash);

        // Même temps de jeu, contenu différent : c'est la modification du site.
        var compared = await _sync.CompareAsync(console, id, old);
        Assert.Equal(SyncCompareVerdict.Retrievable, compared.Verdict);
        Assert.True(compared.ModifiedOnServer);
        Assert.Contains("retrait", compared.Message);

        var file = await _sync.DownloadAsync(console, id, "sd:sdmc:/saves/autre-nom.sav");
        Assert.NotNull(file);
        Assert.Equal(entry.Hash, SaveModification.Hash(file.Value.Data));
        Assert.False(Assert.Single(await _saves.ListAsync("sacha")).MustDownloadBeforeUpdate); // comme « Télécharger »
        Assert.Contains("sd:sdmc:/saves/autre-nom.sav", Assert.Single(await _sync.ListForDeviceAsync(console)).Keys);

        // La console a maintenant la version du site : à jour.
        Assert.Equal(SyncCompareVerdict.UpToDate, (await _sync.CompareAsync(console, id, file.Value.Data)).Verdict);
        Assert.Null(await _sync.DownloadAsync(await Linked("ondine"), id, null)); // pas son compte
    }

    [Theory]
    [InlineData("rouge.sav", "gb", "RB")]
    [InlineData("bleu.sav", "gb", "RB")]
    [InlineData("argent.sav", "gb", "GS")]
    [InlineData("noir.sav", "ds", "B")]
    [InlineData("rubis-omega.sav", "3ds", null)]
    public void DescribeForConsole_GivesTheGameToLookFor(string file, string console, string? family)
    {
        var data = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Sauvegardes", file));
        Assert.True(SaveUtil.TryGetSaveFile(data, out var sav));
        var described = SyncService.DescribeForConsole(sav!);
        Assert.Equal(console, described.Console);
        if (family is not null)
            Assert.Equal(family, described.Family);
    }

    [Fact]
    public async Task Locations_DisappearWithTheSave()
    {
        var console = await Linked();
        Assert.Equal(SyncUploadStatus.Added, (await _sync.UploadAsync(console, "b2.sav", Save(box: [25]), Cle)).Status);
        var id = Assert.Single(await _saves.ListAsync("sacha")).Id;
        Assert.NotNull(await _saves.DeleteAsync("sacha", id));

        using var db = _db.NewContext();
        Assert.Empty(db.SyncSaveLocations);
    }

    public void Dispose() => _db.Dispose();
}
