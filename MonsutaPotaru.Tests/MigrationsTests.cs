using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MonsutaPotaru.Data;

namespace MonsutaPotaru.Tests;

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

    [Fact]
    public async Task GameGroupNames_RenamesTheCodesOfGameGroups()
    {
        const string before = "20261022190816_LivingDexes"; // juste avant GameGroupNames
        using (var db = NewContext())
        {
            await db.GetService<IMigrator>().MigrateAsync(before);
            db.Users.Add(new ApplicationUser { Id = "sacha", UserName = "sacha" });
            db.StoredSaves.AddRange(
                new StoredSave { OwnerId = "sacha", IdentityKey = "a", FileName = "soulsilver.sav", GameName = "HGSS", TrainerName = "Ghal" },
                new StoredSave { OwnerId = "sacha", IdentityKey = "b", FileName = "rubis.sav", GameName = "RS", TrainerName = "Ghal" },
                new StoredSave { OwnerId = "sacha", IdentityKey = "c", FileName = "platine.sav", GameName = "Platine", TrainerName = "Lucas" });
            db.WalletOperations.AddRange(
                new WalletOperation { OwnerId = "sacha", SaveLabel = "HGSS — Ghal\nsoulsilver.sav" },
                new WalletOperation { OwnerId = "sacha", SaveLabel = "RSE — Ghal" }, // « RS — » ne doit pas le prendre
                new WalletOperation { OwnerId = "sacha", SaveLabel = "Platine — Lucas" });
            await db.SaveChangesAsync();
        }

        using (var db = NewContext())
        {
            await db.Database.MigrateAsync();
            Assert.Equal(["Or HeartGold / Argent SoulSilver", "Rubis / Saphir", "Platine"],
                await db.StoredSaves.OrderBy(s => s.Id).Select(s => s.GameName).ToListAsync());
            Assert.Equal(["Or HeartGold / Argent SoulSilver — Ghal\nsoulsilver.sav", "Rubis / Saphir / Émeraude — Ghal", "Platine — Lucas"],
                await db.WalletOperations.OrderBy(o => o.Id).Select(o => o.SaveLabel).ToListAsync());

            await db.GetService<IMigrator>().MigrateAsync(before); // et retour
            Assert.Equal(["HGSS", "RS", "Platine"], await db.StoredSaves.OrderBy(s => s.Id).Select(s => s.GameName).ToListAsync());
            Assert.Equal(["HGSS — Ghal\nsoulsilver.sav", "RSE — Ghal", "Platine — Lucas"],
                await db.WalletOperations.OrderBy(o => o.Id).Select(o => o.SaveLabel).ToListAsync());
        }
    }
    [Fact]
    public async Task RemovePhoneAndTwoFactor_ClearsPhoneNumbers_AndTwoFactorData()
    {
        const string before = "20261025202449_SyncSaveLocations"; // juste avant RemovePhoneAndTwoFactor
        using (var db = NewContext())
        {
            await db.GetService<IMigrator>().MigrateAsync(before);
            db.Users.AddRange(
                new ApplicationUser { Id = "sacha", UserName = "sacha@kanto.fr", PhoneNumber = "0612345678", PhoneNumberConfirmed = true, TwoFactorEnabled = true },
                new ApplicationUser { Id = "ondine", UserName = "ondine@azuria.fr" });
            db.UserTokens.AddRange(
                new IdentityUserToken<string> { UserId = "sacha", LoginProvider = "[AspNetUserStore]", Name = "AuthenticatorKey", Value = "cle" },
                new IdentityUserToken<string> { UserId = "sacha", LoginProvider = "[AspNetUserStore]", Name = "RecoveryCodes", Value = "codes" },
                new IdentityUserToken<string> { UserId = "sacha", LoginProvider = "Autre", Name = "AuthenticatorKey", Value = "garde" });
            await db.SaveChangesAsync();
        }

        using (var db = NewContext())
        {
            await db.Database.MigrateAsync();
            var sacha = await db.Users.SingleAsync(u => u.Id == "sacha");
            Assert.Equal((null, false, false), (sacha.PhoneNumber, sacha.PhoneNumberConfirmed, sacha.TwoFactorEnabled));
            Assert.Equal("sacha@kanto.fr", sacha.UserName); // le reste ne bouge pas
            Assert.Equal("garde", Assert.Single(await db.UserTokens.ToListAsync()).Value);

            await db.GetService<IMigrator>().MigrateAsync(before); // retour : rien à remettre
            Assert.Equal(2, await db.Users.CountAsync());
        }
    }
    [Fact]
    public async Task RolesAndInviteCode_GivesTheStandardRole_ToExistingAccounts()
    {
        const string before = "20261026171901_RemovePhoneAndTwoFactor"; // juste avant RolesAndInviteCode
        using (var db = NewContext())
        {
            await db.GetService<IMigrator>().MigrateAsync(before);
            db.Users.AddRange(new ApplicationUser { Id = "sacha", UserName = "Sacha" }, new ApplicationUser { Id = "ondine", UserName = "ondine@azuria.fr" });
            await db.SaveChangesAsync();
        }

        using (var db = NewContext())
        {
            await db.Database.MigrateAsync();
            Assert.Equal([SiteRoles.Admin, SiteRoles.Standard], await db.Roles.OrderBy(r => r.Name).Select(r => r.Name!).ToListAsync());
            Assert.Equal([("ondine", SiteRoles.StandardId), ("sacha", SiteRoles.StandardId)],
                (await db.UserRoles.OrderBy(r => r.UserId).ToListAsync()).Select(r => (r.UserId, r.RoleId)).ToList());

            await db.GetService<IMigrator>().MigrateAsync(before); // et retour
            Assert.Empty(await db.UserRoles.ToListAsync());
            Assert.Empty(await db.Roles.ToListAsync());
        }
    }
}
