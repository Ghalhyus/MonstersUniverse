using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PKHeX.Core;
using MonsutaPotaru.Data;
using MonsutaPotaru.Services;

namespace MonsutaPotaru.Tests;

/// <summary>
/// « Parc » : choix des Pokémon (25 au plus, avec des sprites), agrandissements payés avec le porte-monnaie, départ avec le
/// Pokémon, sprites Donjon Mystère.
/// </summary>
public sealed class ParkServiceTests : IDisposable
{
    private readonly TestDatabase _db = new("sacha", "ondine");
    private readonly ParkService _park;
    private readonly InventoryService _inventory;

    public ParkServiceTests()
    {
        PokemonSprites.Load(RealSavesTests.WebRootWithImages()); // sprites Donjon Mystère : qui peut aller au parc
        _park = new ParkService(_db.Factory, NullLogger<ParkService>.Instance);
        _inventory = _db.Inventory();
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

    private async Task SetWallet(string owner, long money)
    {
        using var db = _db.NewContext();
        db.Wallets.Add(new Wallet { OwnerId = owner, Money = money });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Choice_IsLimitedToTwentyFivePokemonWithSprites_OfTheTrainer()
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
        var many = Enumerable.Range(0, 26).Select(_ => Add(19)).ToList();
        Assert.Equal("Le parc accueille 25 Pokémon au plus : retires-en 1.", (await _park.SetAsync("sacha", many)).Message);
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
    public async Task Expanding_DebitsTheWallet_AndAddsTwentyFivePokemon_ThreeTimesAtMost()
    {
        await SetWallet("sacha", 160_000);
        await SetWallet("ondine", 1_000_000);
        var many = Enumerable.Range(0, 51).Select(_ => Add(19)).ToList();

        var first = await _park.ExpandAsync("sacha");
        Assert.True(first.Ok, first.Message);
        Assert.Equal((1, 110_000L), (first.Expansions, first.Balance));
        Assert.StartsWith("Parc agrandi : il accueille maintenant 50 Pokémon.", first.Message);
        Assert.True((await _park.SetAsync("sacha", many[..50])).Ok);
        Assert.Equal("Le parc accueille 50 Pokémon au plus : retires-en 1.", (await _park.SetAsync("sacha", many)).Message);

        Assert.True((await _park.ExpandAsync("sacha")).Ok);
        var third = await _park.ExpandAsync("sacha");
        Assert.Equal((true, 3, 10_000L), (third.Ok, third.Expansions, third.Balance));
        Assert.Equal(100, ParkService.CapacityOf(await _park.GetExpansionsAsync("sacha")));

        var fourth = await _park.ExpandAsync("sacha"); // la plus grande taille pour l'instant
        Assert.False(fourth.Ok);
        Assert.Equal("Ton parc a déjà sa plus grande taille (100 Pokémon).", fourth.Message);
        Assert.Equal(3, await _park.GetExpansionsAsync("sacha"));
        Assert.Equal(10_000, await _inventory.GetBalanceAsync("sacha"));

        var operations = await _inventory.ListOperationsAsync("sacha"); // les plus récentes d'abord
        Assert.All(operations, op => Assert.Equal((WalletOperationKind.ExpandPark, -50_000L), (op.Kind, op.Amount)));
        Assert.Equal(["Parc : 75 → 100 Pokémon", "Parc : 50 → 75 Pokémon", "Parc : 25 → 50 Pokémon"], operations.Select(o => o.Details));
        Assert.Equal(0, await _park.GetExpansionsAsync("ondine")); // l'achat de Sacha ne touche pas Ondine
        Assert.Equal(1_000_000, await _inventory.GetBalanceAsync("ondine"));
    }

    [Theory]
    [InlineData(0L)]      // pas de porte-monnaie
    [InlineData(49_999L)]
    public async Task Expanding_IsRefused_WithoutEnoughMoney(long money)
    {
        if (money > 0) await SetWallet("sacha", money);

        var result = await _park.ExpandAsync("sacha");
        Assert.False(result.Ok);
        Assert.Contains($"il manque {InventoryService.FormatMoney(50_000 - money)}", result.Message);
        Assert.Equal(0, await _park.GetExpansionsAsync("sacha")); // taille remise comme avant (transaction annulée)
        Assert.Equal(money, await _inventory.GetBalanceAsync("sacha"));
        Assert.Empty(await _inventory.ListOperationsAsync("sacha"));
    }

    [Fact]
    public async Task ParkAboveItsCapacity_KeepsItsPokemon_ButTakesNoNewOne()
    {
        // 30 Pokémon : la capacité d'avant les agrandissements
        var old = Enumerable.Range(0, 30).Select(_ => Add(19)).ToList();
        using (var db = _db.NewContext())
        {
            db.ParkMembers.AddRange(old.Select(id => new ParkMember { BankPokemonId = id, OwnerId = "sacha", AddedAt = DateTime.UtcNow }));
            await db.SaveChangesAsync();
        }
        var newcomer = Add(25);

        Assert.True((await _park.SetAsync("sacha", old[..29])).Ok); // en retirer un : permis, même au-dessus de 25
        Assert.Equal("Le parc accueille 25 Pokémon au plus : retires-en 4.", (await _park.SetAsync("sacha", [.. old[..28], newcomer])).Message);
        Assert.False((await _park.SetAsync("sacha", old)).Ok); // celui retiré ne revient pas tant que le parc dépasse
        Assert.Equal(29, (await _park.ListAsync("sacha")).Count);
        Assert.True((await _park.SetAsync("sacha", [.. old[..24], newcomer])).Ok); // revenu à 25 : de nouveau ouvert
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
