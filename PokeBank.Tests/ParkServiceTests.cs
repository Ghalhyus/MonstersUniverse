using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PokeBank.Data;
using PokeBank.Services;

namespace PokeBank.Tests;

/// <summary>« Parc » : choix des Pokémon (30 au plus, 1re génération), départ avec le Pokémon, sprites Donjon Mystère.</summary>
public sealed class ParkServiceTests : IDisposable
{
    private readonly TestDatabase _db = new("sacha", "ondine");
    private readonly ParkService _park;

    public ParkServiceTests() => _park = new ParkService(_db.Factory, NullLogger<ParkService>.Instance);

    public void Dispose() => _db.Dispose();

    private int Add(ushort species, string owner = "sacha", bool egg = false, bool shiny = false)
    {
        using var db = _db.NewContext();
        var entry = new BankPokemon
        {
            OwnerId = owner, Species = species, SpeciesName = $"Espèce {species}", Nickname = $"N{species}", Level = 10,
            IsEgg = egg, IsShiny = shiny, Nature = 0, DepositedAt = DateTime.UtcNow, Data = [1],
        };
        db.BankPokemon.Add(entry);
        db.SaveChanges();
        return entry.Id;
    }

    [Fact]
    public async Task Choice_IsLimitedToThirtyFirstGenerationPokemon_OfTheTrainer()
    {
        var pikachu = Add(25);
        var mew = Add(151);
        var chikorita = Add(152); // 2e génération
        var egg = Add(1, egg: true);
        var misty = Add(120, owner: "ondine");

        Assert.False((await _park.SetAsync("sacha", [pikachu, chikorita])).Ok);
        Assert.False((await _park.SetAsync("sacha", [egg])).Ok);
        Assert.False((await _park.SetAsync("sacha", [misty])).Ok); // pas le sien
        var many = Enumerable.Range(0, 31).Select(_ => Add(19)).ToList();
        Assert.Equal("Le parc accueille 30 Pokémon au plus : retires-en 1.", (await _park.SetAsync("sacha", many)).Message);
        Assert.Empty(await _park.ListAsync("sacha"));

        var result = await _park.SetAsync("sacha", [mew, pikachu]);
        Assert.True(result.Ok, result.Message);
        Assert.Equal("Parc mis à jour : 2 Pokémon.", result.Message);
        Assert.Equal(new[] { mew, pikachu }.Order(), (await _park.ListAsync("sacha")).Select(p => p.Id).Order());

        var candidates = await _park.ListCandidatesAsync("sacha");
        Assert.DoesNotContain(candidates, c => c.Id == chikorita || c.Id == egg || c.Id == misty);
        Assert.Equal([pikachu, mew], candidates.Where(c => c.InPark).Select(c => c.Id)); // par numéro de Pokédex
    }

    [Fact]
    public async Task NewChoice_KeepsThoseWhoStay_InTheirOrder()
    {
        var (a, b, c) = (Add(1), Add(4), Add(7));
        await _park.SetAsync("sacha", [a, b]);
        await Task.Delay(10);
        await _park.SetAsync("sacha", [c, b]);
        Assert.Equal([b, c], (await _park.ListAsync("sacha")).Select(p => p.Id)); // b était là avant c
        Assert.True(await _park.RemoveAsync("sacha", b));
        Assert.False(await _park.RemoveAsync("ondine", c)); // pas le sien
        Assert.Equal([c], (await _park.ListAsync("sacha")).Select(p => p.Id));
    }

    [Fact]
    public async Task PokemonLeavesThePark_WhenItLeavesTheCollection()
    {
        var pikachu = Add(25);
        await _park.SetAsync("sacha", [pikachu]);
        using (var db = _db.NewContext())
            await db.BankPokemon.Where(p => p.Id == pikachu).ExecuteDeleteAsync(); // comme une sauvegarde mise à jour ou supprimée
        Assert.Empty(await _park.ListAsync("sacha"));
        using (var db = _db.NewContext())
            Assert.Empty(db.ParkMembers);
    }

    [Fact]
    public void Sprites_AndFamilies()
    {
        PokemonSprites.Load(RealSavesTests.WebRootWithImages());
        Assert.Equal("images/donjon/0025/", PokemonSprites.DonjonFolder(25, false, false));
        Assert.Equal("images/donjon/0025/0000/0001/", PokemonSprites.DonjonFolder(25, true, false)); // chromatique
        Assert.Equal("images/donjon/0025/0000/0000/0002/", PokemonSprites.DonjonFolder(25, false, true)); // femelle
        Assert.Equal("images/donjon/0016/", PokemonSprites.DonjonFolder(16, true, false)); // Roucool : pas de chromatique
        Assert.Equal(172, ParkService.Family(26)); // Raichu : famille de Pichu
        Assert.Equal(ParkService.Family(1), ParkService.Family(3)); // Bulbizarre, Florizarre
    }
}
