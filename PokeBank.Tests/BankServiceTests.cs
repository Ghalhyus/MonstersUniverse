using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using PokeBank.Data;
using PokeBank.Services;

namespace PokeBank.Tests;

/// <summary>Lecture paginée de la banque (page « Ma collection »), sur une base SQLite en mémoire.</summary>
public sealed class BankServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly BankService _bank;

    public BankServiceTests()
    {
        _connection.Open(); // la base en mémoire vit tant que la connexion reste ouverte
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options;
        using (var db = new ApplicationDbContext(options))
        {
            db.Database.EnsureCreated();
            db.Users.AddRange(new ApplicationUser { Id = "sacha", UserName = "sacha" }, new ApplicationUser { Id = "ondine", UserName = "ondine" });

            var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            for (int i = 1; i <= 25; i++) // Id i déposé à start + i minutes
                db.BankPokemon.Add(Entry(i, "sacha", i % 5 == 0 ? "Pikachu" : "Évoli", start.AddMinutes(i)));
            db.BankPokemon.Add(Entry(26, "ondine", "Pikachu", start));
            db.SaveChanges();
        }

        _bank = new BankService(new Factory(options), new PokemonInfo(), new ConfigurationBuilder().Build(), null!, NullLogger<BankService>.Instance);
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

    [Fact]
    public async Task Get_RefusesSomeoneElsesPokemon()
    {
        Assert.NotNull(await _bank.GetAsync("ondine", 26));
        Assert.Null(await _bank.GetAsync("sacha", 26));
    }

    public void Dispose() => _connection.Dispose();

    private sealed class Factory(DbContextOptions<ApplicationDbContext> options) : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);
    }
}
