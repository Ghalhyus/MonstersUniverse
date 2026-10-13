using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace PokeBank.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<BankPokemon> BankPokemon => Set<BankPokemon>();
    public DbSet<StoredSave> StoredSaves => Set<StoredSave>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<BankPokemon>(e =>
        {
            e.HasIndex(p => new { p.OwnerId, p.DepositedAt });
            e.HasOne(p => p.Owner).WithMany().HasForeignKey(p => p.OwnerId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(p => p.StoredSave).WithMany().HasForeignKey(p => p.StoredSaveId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<StoredSave>(e =>
        {
            e.HasIndex(s => s.IdentityKey).IsUnique(); // une sauvegarde par serveur, tous comptes confondus
            e.HasIndex(s => s.OwnerId);
            e.HasOne(s => s.Owner).WithMany().HasForeignKey(s => s.OwnerId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
