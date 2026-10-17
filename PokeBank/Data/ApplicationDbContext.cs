using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace PokeBank.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<BankPokemon> BankPokemon => Set<BankPokemon>();
    public DbSet<StoredSave> StoredSaves => Set<StoredSave>();
    public DbSet<SaveModification> SaveModifications => Set<SaveModification>();
    public DbSet<Wallet> Wallets => Set<Wallet>();
    public DbSet<WalletOperation> WalletOperations => Set<WalletOperation>();
    public DbSet<OwnedItem> OwnedItems => Set<OwnedItem>();
    public DbSet<ParkMember> ParkMembers => Set<ParkMember>();

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
            e.Property(s => s.Version).IsConcurrencyToken();
        });

        // Pas de lien vers StoredSave : la trace doit survivre à la suppression de la sauvegarde stockée.
        builder.Entity<SaveModification>(e => e.HasKey(m => m.IdentityKey));

        builder.Entity<Wallet>(e =>
        {
            e.HasKey(w => w.OwnerId);
            e.HasOne(w => w.Owner).WithOne().HasForeignKey<Wallet>(w => w.OwnerId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<WalletOperation>(e =>
        {
            e.HasIndex(o => new { o.OwnerId, o.At });
            e.HasOne(o => o.Owner).WithMany().HasForeignKey(o => o.OwnerId).OnDelete(DeleteBehavior.Cascade);
            // Supprimer une sauvegarde garde l'historique : le libellé reste dans SaveLabel.
            e.HasOne(o => o.StoredSave).WithMany().HasForeignKey(o => o.StoredSaveId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<OwnedItem>(e =>
        {
            e.HasKey(i => new { i.OwnerId, i.ItemId });
            e.HasOne(i => i.Owner).WithMany().HasForeignKey(i => i.OwnerId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ParkMember>(e =>
        {
            e.HasKey(m => m.BankPokemonId); // un Pokémon est au parc ou n'y est pas
            e.HasIndex(m => m.OwnerId);
            // Le Pokémon disparaît (retiré, sauvegarde mise à jour ou supprimée) : il quitte le parc.
            e.HasOne(m => m.BankPokemon).WithOne().HasForeignKey<ParkMember>(m => m.BankPokemonId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
