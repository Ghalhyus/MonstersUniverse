using System.Buffers.Binary;
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

    /// <summary>
    /// Sauvegarde Platine avec <paramref name="count"/> Pokémon d'espèces différentes dans les boîtes, à partir de Tortipouss
    /// (n° 387). Chacun vient d'une vraie rencontre de Platine générée par PKHeX (sauvage, don, échange) : ils sont donc
    /// légaux. Les espèces qu'on ne rencontre pas directement dans Platine (Boskara, qui s'obtient par évolution...) sont sautées.
    /// 50 000 ₽, des Pokéballs et des médicaments (<see cref="PlatinumSampleItems"/>) dans le sac, pour essayer l'inventaire.
    /// </summary>
    internal static byte[] CreatePlatinumSave(int count = 50)
    {
        // PKHeX ne sait pas écrire une sauvegarde Gen 4 créée vide (new SAV4Pt() plante dans Write) : on part d'un
        // fichier brut dont les blocs « général » et « boîtes » ont un pied valide, dans les deux copies du fichier.
        const int partition = 0x40000, storageSize = 0x121E4;
        var raw = new byte[SaveUtil.SIZE_G4RAW];
        raw.AsSpan().Fill(0xFF); // mémoire vierge : les blocs annexes (Panthéon, vidéos...) restent non initialisés
        foreach (var start in new[] { 0, partition })
        {
            InitBlock4(raw.AsSpan(start, SAV4Pt.GeneralSize));
            InitBlock4(raw.AsSpan(start + SAV4Pt.GeneralSize, storageSize));
        }

        var sav = new SAV4Pt(raw)
        {
            OT = "Lucas",
            TID16 = 24680,
            SID16 = 13579,
            Language = (int)LanguageID.French,
            Money = 50_000,
            PlayedHours = 12,
            PlayedMinutes = 34,
        };
        for (int b = 0; b < sav.BoxCount; b++)
        {
            sav.SetBoxName(b, $"BOÎTE {b + 1}");
            for (int s = 0; s < sav.BoxSlotCount; s++)
                sav.SetBoxSlotAtIndex(sav.BlankPKM, b, s); // des octets à zéro ne font pas un emplacement vide en Gen 4 (chiffrement)
        }

        int placed = 0;
        for (ushort species = 387; placed < count && species <= sav.MaxSpeciesID; species++)
        {
            var template = new PK4 { Species = species };
            // Les œufs passent en premier, mais ils donnent la forme de base au niveau 1 : on veut l'espèce elle-même.
            var encounter = EncounterMovesetGenerator.GenerateEncounters(template, sav, ReadOnlyMemory<ushort>.Empty, GameVersion.Pt)
                .FirstOrDefault(e => e.Species == species && !e.IsEgg);
            if (encounter is null)
                continue; // pas de rencontre directe dans Platine
            var pk = encounter.ConvertToPKM(sav);
            sav.SetBoxSlotAtIndex(pk, placed / sav.BoxSlotCount, placed % sav.BoxSlotCount);
            placed++;
        }
        GiveItems(sav, PlatinumSampleItems);

        // PKHeX n'écrit que la copie active : la seconde copie reçoit les mêmes blocs, pour que le jeu trouve les deux à jour.
        var data = sav.Write().ToArray();
        data.AsSpan(0, SAV4Pt.GeneralSize + storageSize).CopyTo(data.AsSpan(partition));
        return data;
    }

    /// <summary>
    /// Sac de la sauvegarde Platine : 30 Poké Ball, 15 Super Ball, 8 Hyper Ball, 1 Master Ball, 5 Luxe Ball, 3 Honor Ball ;
    /// 10 Potion, 5 Super Potion, 2 Total Soin, 3 Rappel, 4 Huile, 2 Super Bonbon.
    /// </summary>
    internal static readonly (int Item, int Count)[] PlatinumSampleItems =
        [(4, 30), (3, 15), (2, 8), (1, 1), (11, 5), (12, 3), (17, 10), (26, 5), (27, 2), (28, 3), (38, 4), (50, 2)];

    /// <summary>
    /// Ajoute des objets dans le sac, dans la poche où le jeu les range (« Balls » ou « Médicaments » en 4e génération, Balls
    /// avec les « Objets » en 5e). Numéros de 4e génération : pas pour les jeux de 3e génération (voir <see cref="SaveBag.Deposit"/>).
    /// </summary>
    internal static void GiveItems(SaveFile sav, params (int Item, int Count)[] items)
    {
        var pouches = sav.Inventory;
        foreach (var (item, count) in items)
        {
            var pouch = pouches.First(p => p.Type != InventoryType.PCItems && p.CanContain((ushort)item));
            Assert.True(pouch.GiveItem(sav, (ushort)item, count) > 0);
        }
        sav.Inventory = pouches;
    }

    /// <summary>Bloc Gen 4 vide avec son pied : compteurs de sauvegarde, taille du bloc et signature du jeu.</summary>
    private static void InitBlock4(Span<byte> block)
    {
        block.Clear();
        BinaryPrimitives.WriteUInt32LittleEndian(block[^0x14..], 1); // compteur principal
        BinaryPrimitives.WriteUInt32LittleEndian(block[^0x10..], 1); // compteur secondaire
        BinaryPrimitives.WriteUInt32LittleEndian(block[^0xC..], (uint)block.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(block[^0x8..], SAV4.MAGIC_JAPAN_INTL);
    }

    [Fact]
    public void PlatinumSample_Has50LegalPokemon()
    {
        Assert.True(SaveUtil.TryGetSaveFile(CreatePlatinumSave(), out var sav, "platine.sav"));
        Assert.IsType<SAV4Pt>(sav);
        Assert.True(sav.ChecksumsValid, sav.ChecksumInfo);
        Assert.Equal(50_000u, sav.Money);
        Assert.Equal(PlatinumSampleItems.ToDictionary(b => b.Item, b => b.Count), SaveBag.ReadItems(sav));
        Assert.True(new SaveSession().TryLoad(CreatePlatinumSave(), "platine.sav", out var error), error);
        var pokemon = StoredSaveService.ReadAllPokemon(sav).Select(p => p.Pokemon).ToList();
        Assert.Equal(50, pokemon.Count);
        Assert.All(pokemon, pk => Assert.True(PokemonInfo.IsLegal(pk), $"{pk.Species} illégal"));
    }

    /// <summary>Comme <see cref="WriteSampleSave_WhenRequested"/>, pour la sauvegarde Platine : POKEBANK_SAMPLE_PT_OUT.</summary>
    [Fact]
    public void WritePlatinumSampleSave_WhenRequested()
    {
        var path = Environment.GetEnvironmentVariable("POKEBANK_SAMPLE_PT_OUT");
        if (string.IsNullOrEmpty(path)) return;
        File.WriteAllBytes(path, CreatePlatinumSave());
    }

    /// <summary>Ajoute une heure de jeu à une sauvegarde, sans rien changer d'autre (« le joueur a rejoué un peu »).</summary>
    internal static byte[] AddOneHourOfPlay(byte[] data, string fileName = "")
    {
        // PKHeX écrit dans le tableau qu'on lui donne : une copie laisse l'original intact.
        Assert.True(SaveUtil.TryGetSaveFile(data.ToArray(), out var sav, fileName), "Fichier non reconnu comme une sauvegarde Pokémon.");
        sav!.PlayedHours += 1;
        return sav.Write().ToArray();
    }

    [Fact]
    public void AddOneHourOfPlay_KeepsEverythingElse()
    {
        var before = CreatePlatinumSave();
        Assert.True(SaveUtil.TryGetSaveFile(AddOneHourOfPlay(before), out var after));
        Assert.True(SaveUtil.TryGetSaveFile(before, out var original));
        Assert.Equal(SaveIdentity.Of(original!).PlayTimeSeconds + 3600, SaveIdentity.Of(after!).PlayTimeSeconds);
        Assert.Equal(SaveIdentity.Of(original!).Key, SaveIdentity.Of(after!).Key); // même partie
        Assert.Equal(SaveBag.ReadItems(original!), SaveBag.ReadItems(after!));
        Assert.Equal(50, StoredSaveService.ReadAllPokemon(after!).Count());
    }

    /// <summary>
    /// Ajoute une heure de jeu à un fichier existant, modifié sur place, pour passer la règle anti-copie de « Mes sauvegardes »
    /// (une version plus avancée est acceptée) : définir POKEBANK_ADD_PLAYTIME=chemin\vers\fichier.sav puis lancer les tests.
    /// </summary>
    [Fact]
    public void AddPlayTime_WhenRequested()
    {
        var path = Environment.GetEnvironmentVariable("POKEBANK_ADD_PLAYTIME");
        if (string.IsNullOrEmpty(path)) return;
        File.WriteAllBytes(path, AddOneHourOfPlay(File.ReadAllBytes(path), path));
    }
}
