using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PokeBank.Data;

namespace PokeBank.Tests;

/// <summary>Migrations qui modifient des données (pas seulement le schéma), passées sur une vraie base SQLite en mémoire.</summary>
public sealed class MigrationsTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<ApplicationDbContext> _options;

    public MigrationsTests()
    {
        _connection.Open();
        _options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options;
    }

    public void Dispose() => _connection.Dispose();

    private ApplicationDbContext NewContext() => new(_options);

    [Fact]
    public async Task JournalFileNames_AddsTheFileName_OfSavesThatStillExist()
    {
        using (var db = NewContext())
        {
            await db.GetService<IMigrator>().MigrateAsync("20261016182506_SpriteDetails"); // juste avant JournalFileNames
            db.Users.Add(new ApplicationUser { Id = "sacha", UserName = "sacha" });
            var save = new StoredSave { OwnerId = "sacha", IdentityKey = "cle", FileName = "rouge.sav", GameName = "Rouge / Bleu", TrainerName = "Red" };
            db.StoredSaves.Add(save);
            await db.SaveChangesAsync();
            db.WalletOperations.AddRange(
                new WalletOperation { OwnerId = "sacha", StoredSaveId = save.Id, SaveLabel = "Rouge / Bleu — Red" },
                new WalletOperation { OwnerId = "sacha", SaveLabel = "Noire — Ghal" }, // sauvegarde supprimée depuis
                new WalletOperation { OwnerId = "sacha" }, // achat d'emplacements : pas de sauvegarde
                new WalletOperation { OwnerId = "sacha", StoredSaveId = save.Id, SaveLabel = "Rouge / Bleu — Red\nancien.sav" });
            await db.SaveChangesAsync();
        }

        using (var db = NewContext())
        {
            await db.Database.MigrateAsync();
            Assert.Equal(["Rouge / Bleu — Red\nrouge.sav", "Noire — Ghal", "", "Rouge / Bleu — Red\nancien.sav"],
                await db.WalletOperations.OrderBy(o => o.Id).Select(o => o.SaveLabel).ToListAsync());

            await db.GetService<IMigrator>().MigrateAsync("20261016182506_SpriteDetails"); // et retour
            Assert.Equal(["Rouge / Bleu — Red", "Noire — Ghal", "", "Rouge / Bleu — Red"],
                await db.WalletOperations.OrderBy(o => o.Id).Select(o => o.SaveLabel).ToListAsync());
        }
    }
}
