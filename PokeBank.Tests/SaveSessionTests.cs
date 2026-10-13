using PKHeX.Core;
using PokeBank.Services;

namespace PokeBank.Tests;

public class SaveSessionTests
{
    /// <summary>Sauvegarde Noir 2 / Blanc 2 vierge avec un Pikachu dans la première case.</summary>
    internal static byte[] CreateBw2Save()
    {
        var sav = new SAV5B2W2 { OT = "Ludo" };
        sav.SetBoxSlotAtIndex(CreatePk5(25, 20), 0, 0);
        sav.SetBoxSlotAtIndex(CreatePk5(133, 15), 0, 1);
        return sav.Write().ToArray();
    }

    internal static PK5 CreatePk5(ushort species, byte level)
    {
        var pk = new PK5
        {
            Species = species,
            Version = GameVersion.B2,
            Language = (int)LanguageID.French,
            OriginalTrainerName = "Ludo",
            TID16 = 12345,
            PID = 0x12345678,
            MetLevel = level,
            Move1 = 33, // Charge
        };
        pk.CurrentLevel = level;
        pk.ClearNickname();
        pk.RefreshChecksum();
        return pk;
    }

    private static SaveSession LoadedSession(byte[]? data = null)
    {
        var session = new SaveSession();
        Assert.True(session.TryLoad(data ?? CreateBw2Save(), "test.sav", out var error), error);
        return session;
    }

    [Fact]
    public void Load_RecognizesBlack2White2()
    {
        var session = LoadedSession();
        Assert.IsType<SAV5B2W2>(session.Save);
        Assert.Equal(25, session.GetSlot(0, 0)!.Species);
        Assert.Null(session.GetSlot(0, 2));
    }

    [Fact]
    public void Load_RejectsGarbage()
    {
        var session = new SaveSession();
        Assert.False(session.TryLoad(new byte[1234], "nope.sav", out _));
        Assert.False(session.IsLoaded);
    }

    [Fact]
    public void Deposit_RemovesFromSave_AndSurvivesWrite()
    {
        var session = LoadedSession();
        Assert.True(session.TryDeposit(0, 0, out var error), error);

        Assert.Null(session.GetSlot(0, 0));
        var deposit = Assert.Single(session.Deposits);
        Assert.Equal(25, deposit.Pokemon.Species);

        // La sauvegarde réécrite doit être relisible et sans le Pikachu.
        var reloaded = LoadedSession(session.Save!.Write().ToArray());
        Assert.Null(reloaded.GetSlot(0, 0));
        Assert.Equal(133, reloaded.GetSlot(0, 1)!.Species);
    }

    [Fact]
    public void BankBytes_RoundTrip_ThroughEntityFormat()
    {
        var pk = CreatePk5(25, 20);
        var restored = EntityFormat.GetFromBytes(pk.DecryptedBoxData, EntityContext.Gen5);
        Assert.IsType<PK5>(restored);
        Assert.Equal(25, restored!.Species);
        Assert.Equal(20, restored.CurrentLevel);
    }

    [Fact]
    public void Withdraw_PlacesInFirstEmptySlot()
    {
        var session = LoadedSession();
        Assert.True(session.TryWithdraw(42, CreatePk5(1, 5), out var error), error);

        var w = Assert.Single(session.Withdrawals);
        Assert.Equal((0, 2), (w.Box, w.Slot));
        Assert.Equal(1, session.GetSlot(0, 2)!.Species);
        Assert.True(session.IsPendingWithdrawal(42));
        Assert.False(session.TryWithdraw(42, CreatePk5(1, 5), out _)); // pas deux fois le même
    }

    [Fact]
    public void Withdraw_ToChosenSlot()
    {
        var session = LoadedSession();
        Assert.True(session.TryWithdraw(42, CreatePk5(1, 5), 1, 5, out var error), error);

        var w = Assert.Single(session.Withdrawals);
        Assert.Equal((1, 5), (w.Box, w.Slot));
        Assert.Equal(1, session.GetSlot(1, 5)!.Species);
    }

    [Fact]
    public void Withdraw_ToOccupiedSlot_IsRefused()
    {
        var session = LoadedSession();
        Assert.False(session.TryWithdraw(42, CreatePk5(1, 5), 0, 0, out var error));
        Assert.NotEmpty(error);
        Assert.Empty(session.Withdrawals);
        Assert.Equal(25, session.GetSlot(0, 0)!.Species); // Pikachu toujours là
    }

    [Fact]
    public void FindEmptySlot_SkipsOccupied_AndWrapsAround()
    {
        var session = LoadedSession();
        Assert.Equal((0, 2), session.FindEmptySlot(0, 0)); // (0,0) et (0,1) occupés
        Assert.Equal((1, 5), session.FindEmptySlot(1, 5));

        var last = (session.Save!.BoxCount - 1, session.Save.BoxSlotCount - 1);
        session.TryWithdraw(1, CreatePk5(1, 5), last.Item1, last.Item2, out _);
        Assert.Equal((0, 2), session.FindEmptySlot(last.Item1, last.Item2)); // repart du début
    }

    [Fact]
    public void Withdraw_ConvertsGen4Pokemon()
    {
        var pk4 = new PK4 { Species = 393, Version = GameVersion.D, Language = (int)LanguageID.French, OriginalTrainerName = "Ludo", MetLevel = 5 };
        pk4.CurrentLevel = 5;
        pk4.ClearNickname();
        pk4.RefreshChecksum();

        var session = LoadedSession();
        Assert.True(session.TryWithdraw(1, pk4, out var error), error);
        Assert.IsType<PK5>(session.GetSlot(0, 2));
    }

    [Fact]
    public void Withdraw_RefusesSpeciesUnknownToTheGame()
    {
        var session = LoadedSession();
        var pk6 = new PK6 { Species = 700 }; // Nymphali, Gen 6
        Assert.False(session.TryWithdraw(1, pk6, out var error));
        Assert.NotEmpty(error);
        Assert.Empty(session.Withdrawals);
    }

    [Fact]
    public void Deposit_OfJustWithdrawn_CancelsTheWithdrawal()
    {
        var session = LoadedSession();
        session.TryWithdraw(7, CreatePk5(1, 5), out _);
        Assert.True(session.TryDeposit(0, 2, out _));

        Assert.Empty(session.Withdrawals);
        Assert.Empty(session.Deposits);
    }

    [Fact]
    public void Reset_RestoresOriginal()
    {
        var session = LoadedSession();
        session.TryDeposit(0, 0, out _);
        session.Reset();

        Assert.False(session.HasChanges);
        Assert.Equal(25, session.GetSlot(0, 0)!.Species);
    }

    [Fact]
    public void UndoDeposit_PutsPokemonBack()
    {
        var session = LoadedSession();
        session.TryDeposit(0, 0, out _);
        Assert.True(session.UndoDeposit(session.Deposits[0]));

        Assert.Empty(session.Deposits);
        Assert.Equal(25, session.GetSlot(0, 0)!.Species);
    }

    /// <summary>
    /// Génère un fichier de test utilisable dans le site :
    /// définir POKEBANK_SAMPLE_OUT=chemin\vers\fichier.sav puis lancer les tests.
    /// </summary>
    [Fact]
    public void WriteSampleSave_WhenRequested()
    {
        var path = Environment.GetEnvironmentVariable("POKEBANK_SAMPLE_OUT");
        if (string.IsNullOrEmpty(path)) return;
        File.WriteAllBytes(path, CreateBw2Save());
    }
}
