using PKHeX.Core;
using PokeBank.Data;
using PokeBank.Services;

namespace PokeBank.Tests;

/// <summary>
/// Doublons : une sauvegarde dont un Pokémon est déjà ailleurs dans le compte (vieille copie d'avant un dépôt dans le
/// stockage interne...) est refusée, stockée ou chargée dans « Transfert ».
/// </summary>
public sealed class PokemonFingerprintTests : IDisposable
{
    private readonly TestDatabase _db = new("sacha", "ondine");
    private readonly StoredSaveService _saves;

    public PokemonFingerprintTests() => _saves = _db.StoredSaves();

    public void Dispose() => _db.Dispose();

    private const uint PikachuPid = 0x0A000001, EeveePid = 0x0A000002;

    private static PK5 Pk5(ushort species, uint pid)
    {
        var pk = SaveSessionTests.CreatePk5(species, 5);
        pk.PID = pid;
        pk.RefreshChecksum();
        return pk;
    }

    /// <summary>Sauvegarde Noir 2 / Blanc 2 de ce dresseur, avec ces Pokémon dans la boîte 1.</summary>
    private static byte[] Save(string ot, int hours, params PK5[] box)
    {
        var sav = new SAV5B2W2 { OT = ot, TID16 = 12345, SID16 = 54321, PlayedHours = hours };
        for (int i = 0; i < box.Length; i++)
            sav.SetBoxSlotAtIndex(box[i], 0, i);
        return sav.Write().ToArray();
    }

    /// <summary>Pokémon déposé dans le stockage interne de ce compte.</summary>
    private void Deposit(string owner, PKM pk)
    {
        using var db = _db.NewContext();
        db.BankPokemon.Add(BankService.CreateEntry(new PokemonInfo(), pk, owner, DateTime.UtcNow));
        db.SaveChanges();
    }

    [Fact]
    public void Fingerprint_FollowsThePokemon_EvenAfterEvolution()
    {
        var pichu = Pk5(172, PikachuPid);
        var raichu = Pk5(26, PikachuPid); // le même, deux évolutions plus tard
        Assert.Equal(PokemonFingerprint.Of(pichu), PokemonFingerprint.Of(raichu));
        Assert.NotEqual(PokemonFingerprint.Of(pichu), PokemonFingerprint.Of(Pk5(172, EeveePid)));
        var otherTrainer = Pk5(172, PikachuPid);
        otherTrainer.SID16 = 1;
        Assert.NotEqual(PokemonFingerprint.Of(pichu), PokemonFingerprint.Of(otherTrainer));
    }

    [Fact]
    public void Fingerprint_FirstAndSecondGeneration_UseIvsTrainerAndFamily()
    {
        static PK2 Gen2(ushort species, ushort dv) =>
            new() { Species = species, TID16 = 4242, OriginalTrainerName = "SACHA", DV16 = dv };
        var pichu = Gen2(172, 0xABCD);
        Assert.Equal(PokemonFingerprint.Of(pichu), PokemonFingerprint.Of(Gen2(26, 0xABCD))); // devenu Raichu
        Assert.NotEqual(PokemonFingerprint.Of(pichu), PokemonFingerprint.Of(Gen2(172, 0xABCE)));
        Assert.NotEqual(PokemonFingerprint.Of(pichu), PokemonFingerprint.Of(Gen2(1, 0xABCD))); // autre famille
        var red = new PK1 { Species = 25, TID16 = 4242, OriginalTrainerName = "SACHA", DV16 = 0xABCD };
        Assert.Equal(PokemonFingerprint.Of(pichu), PokemonFingerprint.Of(red)); // Rouge → Or : même Pokémon

        // Famille d'évolution connue pour chaque espèce des 1re et 2e génération, même absente d'Écarlate / Violet.
        Assert.All(Enumerable.Range(1, 251), s => Assert.NotEqual(0, ParkService.Family((ushort)s)));
        Assert.Equal(238, ParkService.Family(124)); // Lippoutou : Lippouti
        Assert.Equal(83, ParkService.Family(83)); // Canarticho
    }

    [Fact]
    public async Task StoringAnOldCopy_IsRefused()
    {
        Deposit("sacha", Pk5(25, PikachuPid)); // déposé depuis « Transfert »...

        // ... puis la vieille copie de la sauvegarde, qui l'a encore, est envoyée : refusée, rien n'est stocké.
        var refused = await _saves.AddAsync("sacha", "b2.sav", Save("Ludo", 10, Pk5(25, PikachuPid), Pk5(133, EeveePid)));
        Assert.Equal(StoreStatus.Duplicate, refused.Status);
        Assert.Equal("Sauvegarde refusée : 1 Pokémon de cette sauvegarde est déjà ailleurs dans ton compte : Pikachu N.5 "
            + "(stockage interne). C'est sans doute une vieille copie : envoie la sauvegarde actuelle de ton jeu.", refused.Message);
        Assert.Empty(await _saves.ListAsync("sacha"));

        // La sauvegarde actuelle (sans le Pikachu déposé) passe ; sa mise à jour ne se compare pas à sa version précédente.
        Assert.True((await _saves.AddAsync("sacha", "b2.sav", Save("Ludo", 11, Pk5(133, EeveePid)))).Ok);
        var id = Assert.Single(await _saves.ListAsync("sacha")).Id;
        var updated = await _saves.UpdateAsync("sacha", id, "b2.sav", Save("Ludo", 12, Pk5(133, EeveePid)));
        Assert.True(updated.Ok, updated.Message);

        // Vieille copie en mise à jour : refusée avant même la question du temps de jeu.
        var older = await _saves.UpdateAsync("sacha", id, "b2.sav", Save("Ludo", 10, Pk5(25, PikachuPid), Pk5(133, EeveePid)));
        Assert.Equal(StoreStatus.Duplicate, older.Status);

        // Un Pokémon d'une autre sauvegarde stockée du même compte compte aussi.
        var other = await _saves.AddAsync("sacha", "blanc2.sav", Save("Pierre", 3, Pk5(133, EeveePid)));
        Assert.Equal(StoreStatus.Duplicate, other.Status);
        Assert.Matches(@"^Sauvegarde refusée : 1 Pokémon .* : Évoli N\.5 \(.+ — Ludo\)\. ", other.Message);

        // Jamais entre comptes.
        Assert.True((await _saves.AddAsync("ondine", "ondine.sav", Save("Ondine", 5, Pk5(25, PikachuPid)))).Ok);
    }

    [Fact]
    public async Task Transfer_RefusesAnOldCopy_AndItsSecondDeposit()
    {
        Deposit("sacha", Pk5(25, PikachuPid));
        var bank = _db.Bank();
        var oldCopy = Save("Ludo", 10, Pk5(25, PikachuPid), Pk5(133, EeveePid));

        // Au chargement dans « Transfert » : refusée (la page ferme la sauvegarde et affiche ce message).
        Assert.True(SaveUtil.TryGetSaveFile(oldCopy.ToArray(), out var sav));
        var duplicates = await bank.FindDuplicatesAsync("sacha", StoredSaveService.ReadAllPokemon(sav!).Select(p => p.Pokemon));
        Assert.Equal("Pikachu N.5 (stockage interne)", Assert.Single(duplicates).Describe());
        Assert.EndsWith("C'est sans doute une vieille copie : charge la sauvegarde actuelle de ton jeu.",
            PokemonFingerprint.RefusalMessage(duplicates, PokemonFingerprint.TransferAdvice));
        Assert.Empty(await bank.FindDuplicatesAsync("ondine", StoredSaveService.ReadAllPokemon(sav!).Select(p => p.Pokemon)));

        // Validation : refusée si la copie est dans les dépôts (déposée entre-temps depuis un autre onglet).
        var session = new SaveSession();
        Assert.True(session.TryLoad(oldCopy, "b2.sav", out var error), error);
        Assert.True(session.TryDeposit(0, 0, out error), error);
        Assert.True(session.TryDeposit(0, 1, out error), error);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => bank.CommitAsync("sacha", session));
        Assert.Equal(BankService.AlreadyInStorageMessage("Pikachu") + " Annule son dépôt, puis valide de nouveau.", ex.Message);
        Assert.Equal(1, (await bank.GetUsageAsync("sacha")).Used);

        // Sans la copie, l'Évoli passe.
        Assert.True(session.UndoDeposit(session.Deposits.Single(d => d.Pokemon.Species == 25)));
        await bank.CommitAsync("sacha", session);
        Assert.Equal(2, (await bank.GetUsageAsync("sacha")).Used);
    }

    [Fact]
    public void Refusal_NamesAFewCopies()
    {
        var copies = Enumerable.Range(1, 7).Select(i => new DuplicateCopy($"f{i}", "Évoli", (byte)i, false, null)).ToList();
        copies[1] = copies[1] with { IsEgg = true, StoredSaveLabel = "Noire — Ghal" };
        Assert.Equal("Sauvegarde refusée : 7 Pokémon de cette sauvegarde sont déjà ailleurs dans ton compte : "
            + "Évoli N.1 (stockage interne), Œuf (Noire — Ghal), Évoli N.3 (stockage interne), Évoli N.4 (stockage interne), "
            + "Évoli N.5 (stockage interne) et 2 autres. C'est sans doute une vieille copie : envoie la sauvegarde actuelle de ton jeu.",
            PokemonFingerprint.RefusalMessage(copies, PokemonFingerprint.StoreAdvice));
        Assert.Null(PokemonFingerprint.RefusalMessage([], PokemonFingerprint.StoreAdvice));
    }

    [Fact]
    public async Task OlderPokemon_GetTheirFingerprintAtStartup()
    {
        using (var db = _db.NewContext())
        {
            var old = BankService.CreateEntry(new PokemonInfo(), Pk5(25, PikachuPid), "sacha", DateTime.UtcNow);
            old.Fingerprint = null; // enregistré avant l'empreinte
            var unreadable = BankService.CreateEntry(new PokemonInfo(), Pk5(133, EeveePid), "sacha", DateTime.UtcNow);
            unreadable.Fingerprint = null;
            unreadable.Data = [1, 2, 3];
            db.BankPokemon.AddRange(old, unreadable);
            db.SaveChanges();
        }
        using (var db = _db.NewContext())
            await PokemonFingerprint.FillAsync(db);
        using (var db = _db.NewContext())
            Assert.Equal([PokemonFingerprint.Of(Pk5(25, PikachuPid)), ""], db.BankPokemon.OrderBy(p => p.Id).Select(p => p.Fingerprint!).ToList());
    }
}
