using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PKHeX.Core;
using PokeBank.Data;
using PokeBank.Services;

namespace PokeBank.Tests;

/// <summary>
/// Living dex : un exemplaire de chaque Pokémon du Pokédex d'un jeu, compté dans une sauvegarde stockée (1re génération :
/// Rouge, Bleu, Jaune). Vraies sauvegardes de Rouge et de Bleu (PokeBank.Tests/Sauvegardes).
/// </summary>
public sealed class LivingDexServiceTests : IDisposable
{
    private readonly TestDatabase _db = new("sacha", "ondine");
    private readonly StoredSaveService _saves;
    private readonly LivingDexService _dex;

    public LivingDexServiceTests()
    {
        _saves = _db.StoredSaves();
        _dex = new LivingDexService(_db.Factory, NullLogger<LivingDexService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    private static byte[] RealSave(string file) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Sauvegardes", file));

    private async Task<int> Store(string owner, string file)
    {
        var result = await _saves.AddAsync(owner, file, RealSave(file));
        Assert.True(result.Ok, result.Message);
        return (await _saves.ListAsync(owner)).Single(s => s.FileName == file).Id;
    }

    [Fact]
    public void FirstGeneration_OnlyHasTheNationalMode_Of151()
    {
        foreach (var game in LivingDexGames.All)
        {
            Assert.Equal([LivingDexMode.National], LivingDexGames.Modes(game)); // une région, aucune forme : Régional et Complet cachés
            Assert.Equal(151, LivingDexGames.Slots(game, LivingDexMode.National).Count);
            Assert.Equal(LivingDexGames.Slots(game, LivingDexMode.National), LivingDexGames.Slots(game, LivingDexMode.Complete));
        }
        Assert.Equal("images/livingdex/rouge-bleu/", LivingDexGames.Get(LivingDexGame.Blue).SpriteFolder);
        Assert.Equal("images/livingdex/jaune/", LivingDexGames.Get(LivingDexGame.Yellow).SpriteFolder);
        Assert.Equal("images/livingdex/jaquettes/rouge.png", LivingDexGames.Get(LivingDexGame.Red).Cover);
    }

    [Fact]
    public void Games_OfASave_RedAndBlueCannotBeTold()
    {
        foreach (var file in new[] { "rouge.sav", "bleu.sav" })
        {
            Assert.True(SaveUtil.TryGetSaveFile(RealSave(file), out var sav, file));
            Assert.Equal([LivingDexGame.Red, LivingDexGame.Blue], LivingDexGames.For(sav!)); // même format : au joueur de choisir
        }
        Assert.Equal([LivingDexGame.Yellow], LivingDexGames.For(new SAV1(LanguageID.French, GameVersion.YW)));
        Assert.Empty(LivingDexGames.For(new SAV2(LanguageID.French, GameVersion.GS)));
    }

    [Fact]
    public async Task Create_OnePerGame_OneSaveForOne()
    {
        var red = await Store("sacha", "rouge.sav");
        await Store("sacha", "argent.sav"); // 2e génération : pas de living dex

        var candidate = Assert.Single(await _dex.ListCandidatesAsync("sacha"));
        Assert.Equal(red, candidate.SaveId);
        Assert.Equal([LivingDexGame.Red, LivingDexGame.Blue], candidate.Games);

        Assert.Equal("Cette sauvegarde n'est pas une partie de Pokémon Jaune.", (await _dex.CreateAsync("sacha", red, LivingDexGame.Yellow)).Message);
        Assert.False((await _dex.CreateAsync("ondine", red, LivingDexGame.Red)).Ok); // pas la sienne
        var created = await _dex.CreateAsync("sacha", red, LivingDexGame.Red);
        Assert.True(created.Ok, created.Message);
        Assert.Equal("Cette sauvegarde a déjà un living dex.", (await _dex.CreateAsync("sacha", red, LivingDexGame.Blue)).Message);
        Assert.Empty(await _dex.ListCandidatesAsync("sacha")); // sauvegarde déjà liée

        // Une seconde partie de Rouge / Bleu : seul Bleu reste possible.
        var blue = await Store("sacha", "bleu.sav");
        Assert.Equal([LivingDexGame.Blue], Assert.Single(await _dex.ListCandidatesAsync("sacha")).Games);
        Assert.Equal("Tu as déjà un living dex Pokémon Rouge : supprime-le pour en créer un autre.",
            (await _dex.CreateAsync("sacha", blue, LivingDexGame.Red)).Message);
        Assert.True((await _dex.CreateAsync("sacha", blue, LivingDexGame.Blue)).Ok);

        Assert.Equal(["Pokémon Rouge", "Pokémon Bleu"], (await _dex.ListAsync("sacha")).Select(d => d.Game.Name));
        Assert.Empty(await _dex.ListAsync("ondine"));
        Assert.False(await _dex.DeleteAsync("ondine", created.Id!.Value));
        Assert.True(await _dex.DeleteAsync("sacha", created.Id!.Value));
        Assert.Equal([LivingDexGame.Red], Assert.Single(await _dex.ListCandidatesAsync("sacha")).Games); // Bleu reste pris
    }

    [Fact]
    public async Task Counts_ThePokemonOfTheSave_WithTheirPlace()
    {
        var red = await Store("sacha", "rouge.sav");
        var id = (await _dex.CreateAsync("sacha", red, LivingDexGame.Red)).Id!.Value;

        var view = await _dex.GetAsync("sacha", id, LivingDexMode.Complete); // pas proposé en 1re génération : National
        Assert.NotNull(view);
        Assert.Equal(LivingDexMode.National, view.Mode);
        Assert.Equal(151, view.Entries.Count);
        Assert.Equal(Enumerable.Range(1, 151), view.Entries.Select(e => (int)e.Species));

        List<(ushort Species, int Id, string? Location)> pokemon;
        using (var db = _db.NewContext())
            pokemon = db.BankPokemon.Where(p => p.StoredSaveId == red).OrderBy(p => p.Position)
                .Select(p => new { p.Species, p.Id, p.Location }).AsEnumerable().Select(p => (p.Species, p.Id, p.Location)).ToList();
        var species = pokemon.Select(p => p.Species).Distinct().ToList();
        Assert.Equal(species.Count, view.Owned);
        Assert.Equal(species.Count, Assert.Single(await _dex.ListAsync("sacha")).Owned);

        // Chaque espèce possédée : son premier exemplaire (équipe, puis boîtes), en couleur ; les autres manquent.
        var first = pokemon.First();
        var entry = view.Entries.Single(e => e.Species == first.Species);
        Assert.True(entry.Owned);
        Assert.Equal((first.Id, first.Location), (entry.BankId!.Value, entry.Location));
        Assert.Equal($"images/livingdex/rouge-bleu/{first.Species}.png", entry.Sprite);
        Assert.All(view.Entries.Where(e => !species.Contains(e.Species)), e => Assert.Null(e.BankId));
        Assert.Equal("Pikachu", view.Entries[24].Name);

        Assert.Null(await _dex.GetAsync("ondine", id));
    }

    [Fact]
    public async Task LivingDex_LeavesWithItsSave()
    {
        var red = await Store("sacha", "rouge.sav");
        await _dex.CreateAsync("sacha", red, LivingDexGame.Red);
        Assert.Equal("Pokémon Rouge", (await _dex.NamesBySaveAsync("sacha"))[red]);

        Assert.True((await _saves.DeleteWithoutDownloadAsync("sacha", red)).Ok);
        Assert.Empty(await _dex.ListAsync("sacha"));
        using var db = _db.NewContext();
        Assert.Empty(db.LivingDexes);
    }
}
