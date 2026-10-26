using Microsoft.EntityFrameworkCore;
using MonsutaPotaru.Data;
using MonsutaPotaru.Services;
using PKHeX.Core;

namespace MonsutaPotaru.Tests;

/// <summary>Échanges : sauvegardes compatibles, Pokémon proposés, échange des deux sauvegardes, évolutions, statistiques.</summary>
public sealed class TradeServiceTests : IDisposable
{
    private readonly TestDatabase _db = new("sacha", "ondine");
    private readonly TradeService _trades;
    private readonly StoredSaveService _saves;

    public TradeServiceTests()
    {
        _trades = _db.Trades();
        _saves = _db.StoredSaves();
    }

    public void Dispose() => _db.Dispose();

    /// <summary>Noire 2 de <paramref name="ot"/> : l'équipe, puis la boîte 1 (Pokémon propres au dresseur : pas de doublons).</summary>
    private static byte[] Bw2(string ot, ushort tid, ushort[] party, params ushort[] box)
    {
        var sav = new SAV5B2W2 { OT = ot, TID16 = tid, SID16 = 1, PlayedHours = 10 };
        var n = 0;
        for (int i = 0; i < party.Length; i++)
            sav.SetPartySlotAtIndex(SaveSessionTests.CreatePk5(party[i], 30, ot, n++), i);
        for (int i = 0; i < box.Length; i++)
            sav.SetBoxSlotAtIndex(SaveSessionTests.CreatePk5(box[i], 30, ot, n++), 0, i);
        return sav.Write().ToArray();
    }

    private async Task<int> Store(string owner, string file, byte[] data)
    {
        var result = await _saves.AddAsync(owner, file, data);
        Assert.True(result.Ok, result.Message);
        return (await _saves.ListAsync(owner)).Single(s => s.FileName == file).Id;
    }

    private async Task<SaveFile> Read(int saveId)
    {
        await using var db = _db.NewContext();
        var save = await db.StoredSaves.SingleAsync(s => s.Id == saveId);
        Assert.True(SaveUtil.TryGetSaveFile(save.Data.ToArray(), out var sav));
        return sav!;
    }

    [Fact]
    public async Task SavePairs_OnlySameGeneration()
    {
        var sachaB2 = await Store("sacha", "b2.sav", Bw2("Sacha", 1, [25]));
        await Store("sacha", "platine.sav", SaveSessionTests.CreatePlatinumSave(3));
        var ondineB2 = await Store("ondine", "b2.sav", Bw2("Ondine", 2, [133]));

        var pair = Assert.Single(await _trades.SavePairsAsync("sacha", "ondine")); // Platine (4e) ne va pas avec Noire 2 (5e)
        Assert.Equal((sachaB2, ondineB2), (pair.Mine.Id, pair.Theirs.Id));
    }

    [Fact]
    public async Task Trade_SwapsThePokemon_EvolvesKadabra_AndCountsStats()
    {
        var sachaSave = await Store("sacha", "b2.sav", Bw2("Sacha", 1, [25, 4], 64)); // Pikachu, Salamèche ; Kadabra en boîte
        var ondineSave = await Store("ondine", "b2.sav", Bw2("Ondine", 2, [120], 7));   // Stari ; Carapuce en boîte

        // Ondine veut le Kadabra de Sacha ; Sacha veut le Stari d'Ondine.
        var sachaOffers = await _trades.CandidatesAsync("sacha", sachaSave, "ondine", ondineSave);
        Assert.Equal([25, 4, 64], sachaOffers.Select(c => (int)c.Species));
        var kadabra = sachaOffers.Single(c => c.Species == 64);
        Assert.Equal("Boîte 1, case 1", kadabra.Location);
        var staryu = (await _trades.CandidatesAsync("ondine", ondineSave, "sacha", sachaSave)).Single(c => c.Species == 120);

        var outcome = await _trades.ExecuteAsync(new TradeSide("sacha", sachaSave, kadabra), new TradeSide("ondine", ondineSave, staryu));
        Assert.True(outcome.Ok, outcome.FirstMessage);
        Assert.Contains("Stari", outcome.FirstMessage);
        Assert.Contains("tu as reçu Kadabra, qui a évolué en Alakazam", outcome.SecondMessage);

        var sacha = await Read(sachaSave);
        Assert.Equal(120, sacha.GetBoxSlotAtIndex(0, 0).Species); // le Stari à la place du Kadabra
        var ondine = await Read(ondineSave);
        var alakazam = ondine.GetPartySlotAtIndex(0);
        Assert.Equal(65, alakazam.Species); // Kadabra a évolué en arrivant, à la place du Stari dans l'équipe
        Assert.Equal("Ludo", alakazam.OriginalTrainerName); // dresseur d'origine inchangé (CreatePk5 : « Ludo »), pas celui d'Ondine
        Assert.True(ondine.GetCaught(65));

        // Collection relue et sauvegardes « modifiées sur le serveur », à télécharger.
        await using (var db = _db.NewContext())
        {
            Assert.True(await db.BankPokemon.AnyAsync(p => p.StoredSaveId == ondineSave && p.Species == 65));
            Assert.False(await db.BankPokemon.AnyAsync(p => p.StoredSaveId == sachaSave && p.Species == 64));
        }
        var info = (await _saves.ListAsync("ondine")).Single();
        Assert.True(info.MustDownloadBeforeUpdate);
        Assert.Equal("un échange de Pokémon", info.ServerChanges);

        Assert.Equal(new TradeStats(1, 1), await _trades.StatsAsync("sacha"));
        Assert.Equal(new TradeStats(1, 1), await _trades.StatsAsync("ondine"));
    }

    [Fact]
    public async Task Trade_RefusesAPokemonThatChanged_OrAMissingSave()
    {
        var sachaSave = await Store("sacha", "b2.sav", Bw2("Sacha", 1, [25], 4));
        var ondineSave = await Store("ondine", "b2.sav", Bw2("Ondine", 2, [133], 7));
        var pikachu = (await _trades.CandidatesAsync("sacha", sachaSave, "ondine", ondineSave)).Single(c => c.Species == 25);
        var eevee = (await _trades.CandidatesAsync("ondine", ondineSave, "sacha", sachaSave)).Single(c => c.Species == 133);

        // Empreinte d'un autre Pokémon : la sauvegarde a changé depuis le choix.
        var stale = await _trades.ExecuteAsync(new TradeSide("sacha", sachaSave, pikachu with { Fingerprint = "autre" }), new TradeSide("ondine", ondineSave, eevee));
        Assert.False(stale.Ok);
        Assert.True(stale.Retry);
        Assert.Equal(new TradeStats(0, 0), await _trades.StatsAsync("sacha"));
        Assert.Equal(25, (await Read(sachaSave)).GetPartySlotAtIndex(0).Species); // rien n'a bougé

        var missing = await _trades.ExecuteAsync(new TradeSide("sacha", sachaSave, pikachu), new TradeSide("ondine", 999, eevee));
        Assert.False(missing.Ok);
        Assert.False(missing.Retry);
    }

    [Theory]
    [InlineData(95, 233, 0, 208, true)]   // Onix + Peau Métal → Steelix, objet consommé
    [InlineData(95, 0, 0, null, false)]    // Onix sans objet : rien
    [InlineData(64, 229, 0, null, false)]  // Kadabra + Pierre Stase : rien
    [InlineData(64, 0, 0, 65, false)]      // Kadabra → Alakazam
    [InlineData(588, 0, 616, 589, false)]  // Carabing contre Escargaume → Lançargot
    [InlineData(588, 0, 25, null, false)]  // Carabing contre autre chose : rien
    public void EvolveByTrade_FollowsTheGameRules(ushort species, int item, ushort partner, int? expected, bool itemUsed)
    {
        var pk = SaveSessionTests.CreatePk5(species, 30);
        pk.HeldItem = item;
        Assert.Equal(expected, (int?)TradeService.EvolveByTrade(pk, partner));
        if (expected is { } evolved)
        {
            Assert.Equal(evolved, pk.Species);
            Assert.Equal(itemUsed ? 0 : item, pk.HeldItem);
            Assert.Equal(new PokemonInfo().Describe(pk).SpeciesName, pk.Nickname); // sans surnom : le nom de la nouvelle espèce
        }
    }
}
