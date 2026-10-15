using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using PokeBank.Data;
using PokeBank.Services;

namespace PokeBank.Tests;

/// <summary>Base SQLite en mémoire + dossier de copies de sécurité temporaire, pour tester les services.</summary>
public sealed class TestDatabase : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    public DbContextOptions<ApplicationDbContext> Options { get; }
    public IDbContextFactory<ApplicationDbContext> Factory { get; }
    public string BackupRoot { get; } = Path.Combine(Path.GetTempPath(), "pokebank-tests", Guid.NewGuid().ToString("N"));

    public TestDatabase(params string[] users)
    {
        _connection.Open(); // la base en mémoire vit tant que la connexion reste ouverte
        Options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options;
        Factory = new DbFactory(Options);
        using var db = NewContext();
        db.Database.EnsureCreated();
        db.Users.AddRange(users.Select(u => new ApplicationUser { Id = u, UserName = u }));
        db.SaveChanges();
    }

    public ApplicationDbContext NewContext() => new(Options);

    public IConfiguration Config => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Bank:BackupPath"] = BackupRoot })
        .Build();

    public BankService Bank() =>
        new(Factory, new PokemonInfo(), Config, new FakeEnvironment(), NullLogger<BankService>.Instance);

    public StoredSaveService StoredSaves() =>
        new(Factory, new PokemonInfo(), Config, new FakeEnvironment(), NullLogger<StoredSaveService>.Instance);

    public InventoryService Inventory() =>
        new(Factory, StoredSaves(), new PokemonInfo(), NullLogger<InventoryService>.Instance);

    public void Dispose()
    {
        _connection.Dispose();
        if (Directory.Exists(BackupRoot))
            Directory.Delete(BackupRoot, recursive: true);
    }

    private sealed class DbFactory(DbContextOptions<ApplicationDbContext> options) : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);
    }

    /// <summary>Seul ContentRootPath sert (chemin des copies de sécurité, absolu ici de toute façon).</summary>
    private sealed class FakeEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "PokeBank.Tests";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public string EnvironmentName { get; set; } = "Test";
        public string WebRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
