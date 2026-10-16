using PokeBank.Data;
using PokeBank.Services;

namespace PokeBank.Tests;

/// <summary>Lecture paginée de la banque (page « Ma collection »), sur une base SQLite en mémoire.</summary>
public sealed class BankServiceTests : IDisposable
{
    private readonly TestDatabase _db = new("sacha", "ondine");
    private readonly BankService _bank;

    public BankServiceTests()
    {
        using (var db = _db.NewContext())
        {
            var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            for (int i = 1; i <= 25; i++) // Id i déposé à start + i minutes
                db.BankPokemon.Add(Entry(i, "sacha", i % 5 == 0 ? "Pikachu" : "Évoli", start.AddMinutes(i)));
            db.BankPokemon.Add(Entry(26, "ondine", "Pikachu", start));
            db.SaveChanges();
        }
        _bank = _db.Bank();
    }

    private static BankPokemon Entry(int id, string owner, string species, DateTime at) =>
        new() { Id = id, OwnerId = owner, SpeciesName = species, Nickname = species, Level = 5, DepositedAt = at, Data = [1, 2, 3] };

    [Fact]
    public async Task Count_IsPerOwner()
    {
        Assert.Equal(25, await _bank.CountAsync("sacha"));
        Assert.Equal(1, await _bank.CountAsync("ondine"));
    }

    [Fact]
    public async Task GetRange_PagesFromMostRecent()
    {
        var page = await _bank.GetRangeAsync("sacha", null, skip: 10, take: 10);
        Assert.Equal(Enumerable.Range(6, 10).Reverse(), page.Select(p => p.Id)); // 15, 14 ... 6

        var last = await _bank.GetRangeAsync("sacha", null, skip: 20, take: 10);
        Assert.Equal([5, 4, 3, 2, 1], last.Select(p => p.Id));
    }

    [Fact]
    public async Task GetRange_AppliesSearch()
    {
        Assert.Equal(5, await _bank.CountAsync("sacha", "pika"));
        var page = await _bank.GetRangeAsync("sacha", "pika", 0, 10);
        Assert.Equal([25, 20, 15, 10, 5], page.Select(p => p.Id));
    }

    // ---------- Tri ----------

    /// <summary>Collection d'ondine pour les tris : 4 Pokémon et 1 œuf, déposés dans l'ordre des identifiants.</summary>
    private async Task<BankService> SortSample()
    {
        using var db = _db.NewContext();
        db.BankPokemon.RemoveRange(db.BankPokemon.Where(p => p.OwnerId == "ondine"));
        var start = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        BankPokemon Mon(int id, ushort species, string name, bool egg = false) => new()
        {
            Id = 100 + id, OwnerId = "ondine", Species = species, SpeciesName = name, SortName = BankPokemon.SortKey(name),
            Nickname = egg ? "Œuf" : name, IsEgg = egg, Level = 5, DepositedAt = start.AddMinutes(id), Data = [1],
        };
        db.BankPokemon.AddRange(Mon(1, 133, "Évoli"), Mon(2, 25, "Pikachu"), Mon(3, 1, "Bulbizarre", egg: true), Mon(4, 145, "Électhor"), Mon(5, 25, "Pikachu"));
        await db.SaveChangesAsync();
        return _bank;
    }

    private async Task<int[]> Ids(CollectionSort sort) =>
        (await (await SortSample()).GetRangeAsync("ondine", null, 0, 10, sort: sort)).Select(p => p.Id - 100).ToArray();

    [Fact]
    public async Task Sort_ByArrival_MostRecentFirst_EggsLast()
    {
        Assert.Equal(new[] { 5, 4, 2, 1, 3 }, await Ids(new(CollectionSortBy.Arrival)));
        Assert.Equal(new[] { 1, 2, 4, 5, 3 }, await Ids(new(CollectionSortBy.Arrival, Reversed: true)));
    }

    [Fact]
    public async Task Sort_ByName_IgnoresAccents_AndKeepsTheMostRecentFirstOnTies()
    {
        // Électhor et Évoli avec les « e », pas après Pikachu ; les deux Pikachu : le plus récent d'abord.
        Assert.Equal(new[] { 4, 1, 5, 2, 3 }, await Ids(new(CollectionSortBy.Name)));
        Assert.Equal(new[] { 5, 2, 1, 4, 3 }, await Ids(new(CollectionSortBy.Name, Reversed: true)));
    }

    [Fact]
    public async Task Sort_ByDex_HidesTheSpeciesOfEggs()
    {
        // L'œuf (Bulbizarre, n° 1) reste à la fin : le trier par numéro trahirait son espèce.
        Assert.Equal(new[] { 5, 2, 1, 4, 3 }, await Ids(new(CollectionSortBy.Dex)));
        Assert.Equal(new[] { 4, 1, 5, 2, 3 }, await Ids(new(CollectionSortBy.Dex, Reversed: true)));
    }

    [Fact]
    public async Task FillSortNames_FillsOnlyMissingNames()
    {
        Assert.Equal("evoli", BankPokemon.SortKey("Évoli"));
        using (var db = _db.NewContext())
        {
            await BankService.FillSortNamesAsync(db);
            Assert.All(db.BankPokemon, p => Assert.Equal(BankPokemon.SortKey(p.SpeciesName), p.SortName));
        }
    }

    [Fact]
    public async Task FillSpriteDetails_ReadsGenderAndHeldItem_FromTheStoredBytes()
    {
        var pk = SaveSessionTests.CreatePk5(25, 20);
        pk.Gender = 1;
        pk.HeldItem = 234; // Restes
        using (var db = _db.NewContext())
        {
            var entry = BankService.CreateEntry(new PokemonInfo(), pk, "ondine", DateTime.UtcNow);
            Assert.Equal(((byte?)1, (int?)234), (entry.Gender, entry.HeldItem)); // rempli dès l'enregistrement
            (entry.Gender, entry.HeldItem) = (null, null); // comme un Pokémon enregistré avant la migration SpriteDetails
            db.BankPokemon.Add(entry);
            db.SaveChanges();

            await BankService.FillSpriteDetailsAsync(db);
            var filled = db.BankPokemon.Single(p => p.Id == entry.Id);
            Assert.Equal(((byte?)1, (int?)234), (filled.Gender, filled.HeldItem));
            Assert.DoesNotContain(db.BankPokemon, p => p.Gender == null); // octets illisibles : sans sexe, sans objet
        }
    }

    [Fact]
    public async Task Get_RefusesSomeoneElsesPokemon()
    {
        Assert.NotNull(await _bank.GetAsync("ondine", 26));
        Assert.Null(await _bank.GetAsync("sacha", 26));
    }

    public void Dispose() => _db.Dispose();
}
