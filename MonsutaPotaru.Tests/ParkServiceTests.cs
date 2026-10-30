using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PKHeX.Core;
using MonsutaPotaru.Data;
using MonsutaPotaru.Services;

namespace MonsutaPotaru.Tests;

/// <summary>« Parc » : choix des Pokémon (30 au plus, avec des sprites), départ avec le Pokémon, sprites Donjon Mystère.</summary>
public sealed class ParkServiceTests : IDisposable
{
    private readonly TestDatabase _db = new("sacha", "ondine");
    private readonly ParkService _park;

    public ParkServiceTests()
    {
        PokemonSprites.Load(RealSavesTests.WebRootWithImages()); // sprites Donjon Mystère : qui peut aller au parc
        _park = new ParkService(_db.Factory, NullLogger<ParkService>.Instance);
    }

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
    public async Task GetMember_OnlyAPokemonOfThatPark()
    {
        var pikachu = Add(25);
        var mew = Add(151); // dans la collection de Sacha, pas dans son parc
        var misty = Add(120, owner: "ondine");
        Assert.True((await _park.SetAsync("sacha", [pikachu])).Ok);
        Assert.True((await _park.SetAsync("ondine", [misty])).Ok);

        Assert.Equal(pikachu, (await _park.GetMemberAsync("sacha", pikachu))?.Id);
        Assert.Null(await _park.GetMemberAsync("sacha", mew)); // numéro envoyé par un navigateur modifié
        Assert.Null(await _park.GetMemberAsync("sacha", misty)); // dans un autre parc
        Assert.Null(await _park.GetMemberAsync("ondine", pikachu));
    }

    [Fact]
    public async Task Choice_IsLimitedToThirtyPokemonWithSprites_OfTheTrainer()
    {
        var pikachu = Add(25);
        var mew = Add(151);
        var chikorita = Add(152); // 2e génération : bienvenue aussi
        var simisear = Add(514); // Flamoutan : pas encore de sprites chez SpriteCollab
        var egg = Add(1, egg: true);
        var misty = Add(120, owner: "ondine");

        Assert.Equal("Espèce 514 n'a pas encore de sprites Donjon Mystère : il ne peut pas aller au parc.",
            (await _park.SetAsync("sacha", [pikachu, simisear])).Message);
        Assert.False((await _park.SetAsync("sacha", [egg])).Ok);
        Assert.False((await _park.SetAsync("sacha", [misty])).Ok); // pas le sien
        var many = Enumerable.Range(0, ParkService.Capacity + 1).Select(_ => Add(19)).ToList();
        Assert.Equal($"Le parc accueille {ParkService.Capacity} Pokémon au plus : retires-en 1.", (await _park.SetAsync("sacha", many)).Message);
        Assert.Empty(await _park.ListAsync("sacha"));

        var result = await _park.SetAsync("sacha", [mew, pikachu, chikorita]);
        Assert.True(result.Ok, result.Message);
        Assert.Equal("Parc mis à jour : 3 Pokémon.", result.Message);
        Assert.Equal(new[] { mew, pikachu, chikorita }.Order(), (await _park.ListAsync("sacha")).Select(p => p.Id).Order());

        var candidates = await _park.ListCandidatesAsync("sacha");
        Assert.DoesNotContain(candidates, c => c.Id == egg || c.Id == misty);
        Assert.Equal([pikachu, mew, chikorita], candidates.Where(c => c.InPark).Select(c => c.Id)); // par numéro de Pokédex
        Assert.False(candidates.Single(c => c.Id == simisear).HasSprites); // grisé dans la fenêtre

        // Les pastilles de « Ma collection », dont la génération.
        var second = await _park.ListCandidatesAsync("sacha", new PokemonCriteria { Generations = new HashSet<byte> { 2 } });
        Assert.Equal([chikorita], second.Select(c => c.Id));
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
    public async Task Clear_EmptiesOnlyThatPark_AndKeepsTheCollection()
    {
        var (a, b) = (Add(1), Add(4));
        var misty = Add(120, owner: "ondine");
        await _park.SetAsync("sacha", [a, b]);
        await _park.SetAsync("ondine", [misty]);

        var result = await _park.ClearAsync("sacha");
        Assert.True(result.Ok, result.Message);
        Assert.Equal("Le parc est vide : ses 2 Pokémon restent dans ta collection.", result.Message);
        Assert.Empty(await _park.ListAsync("sacha"));
        Assert.Equal([misty], (await _park.ListAsync("ondine")).Select(p => p.Id)); // le parc d'Ondine ne bouge pas
        using (var db = _db.NewContext())
            Assert.Equal(2, db.BankPokemon.Count(p => p.OwnerId == "sacha"));
        Assert.Equal("Le parc est déjà vide.", (await _park.ClearAsync("sacha")).Message);
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
        static string Folder(ushort species, byte form = 0, EntityContext context = EntityContext.Gen9, bool shiny = false,
            bool female = false, int heldItem = 0) =>
            PokemonSprites.DonjonFolder(species, form, context, shiny, female, heldItem).Replace("images/donjon/", "");
        Assert.Equal("0025/", Folder(25));
        Assert.Equal("0025/0000/0001/", Folder(25, shiny: true)); // chromatique
        Assert.Equal("0025/0000/0000/0002/", Folder(25, female: true)); // femelle
        Assert.Equal("0016/", Folder(16, shiny: true)); // Roucool : pas de chromatique
        Assert.Equal("0026/0001/", Folder(26, 1, EntityContext.Gen7)); // Raichu d'Alola
        Assert.Equal("0077/0001/", Folder(77, 1, EntityContext.Gen8)); // Ponyta de Galar
        Assert.Equal("0128/0001/", Folder(128, 1)); // Tauros de Paldea, race Combative : « Paldea » chez SpriteCollab
        Assert.Equal("0201/0001/", Folder(201, 1, EntityContext.Gen4)); // Zarbi B
        Assert.Equal("0006/0001/", Folder(6, heldItem: ItemStorage9ZA.GetExpectedMegaStoneOrPrimalOrb(6, 1))); // Méga-Dracaufeu X
        Assert.Equal("0006/", Folder(6, heldItem: ItemStorage9ZA.GetExpectedMegaStoneOrPrimalOrb(6, 2))); // Méga Y : pas de sprite
        Assert.True(PokemonSprites.HasDonjonSprites(152));
        Assert.False(PokemonSprites.HasDonjonSprites(514));
        Assert.Equal(172, ParkService.Family(26)); // Raichu : famille de Pichu
        Assert.Equal(ParkService.Family(1), ParkService.Family(3)); // Bulbizarre, Florizarre
    }
}
