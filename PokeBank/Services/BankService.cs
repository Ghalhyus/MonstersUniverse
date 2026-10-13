using Microsoft.EntityFrameworkCore;
using PKHeX.Core;
using PokeBank.Data;

namespace PokeBank.Services;

/// <summary>Ce qu'il faut pour afficher un Pokémon de la banque dans une grille.</summary>
public sealed record BankListItem(int Id, ushort Species, string SpeciesName, string Nickname, byte Level, bool IsShiny, bool IsEgg, bool IsLegal);

public sealed class BankService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    PokemonInfo info,
    IConfiguration config,
    IWebHostEnvironment env,
    ILogger<BankService> logger)
{
    public async Task<List<BankPokemon>> ListAsync(string ownerId, string? search = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await Query(db, ownerId, search).ToListAsync();
    }

    public async Task<int> CountAsync(string ownerId, string? search = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await Query(db, ownerId, search).CountAsync(ct);
    }

    /// <summary>
    /// Une tranche de la banque, sans les octets du Pokémon (affichage seulement) :
    /// la page « Ma collection » charge ainsi au fur et à mesure du défilement.
    /// </summary>
    public async Task<List<BankListItem>> GetRangeAsync(string ownerId, string? search, int skip, int take, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await Query(db, ownerId, search)
            .Skip(skip)
            .Take(take)
            .Select(p => new BankListItem(p.Id, p.Species, p.SpeciesName, p.Nickname, p.Level, p.IsShiny, p.IsEgg, p.IsLegal))
            .ToListAsync(ct);
    }

    public async Task<BankPokemon?> GetAsync(string ownerId, int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.BankPokemon.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id && p.OwnerId == ownerId);
    }

    /// <summary>Pokémon d'un utilisateur, filtrés par espèce / surnom, du plus récent au plus ancien.</summary>
    private static IQueryable<BankPokemon> Query(ApplicationDbContext db, string ownerId, string? search)
    {
        var query = db.BankPokemon.AsNoTracking().Where(p => p.OwnerId == ownerId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = $"%{search.Trim()}%";
            query = query.Where(p => EF.Functions.Like(p.SpeciesName, s) || EF.Functions.Like(p.Nickname, s));
        }
        return query.OrderByDescending(p => p.DepositedAt).ThenByDescending(p => p.Id);
    }

    public static PKM? ToPkm(BankPokemon entry) =>
        EntityFormat.GetFromBytes(entry.Data.ToArray(), (EntityContext)entry.Context);

    /// <summary>
    /// Enregistre en base les dépôts et retraits en attente, garde une copie de la sauvegarde
    /// avant/après, puis renvoie le fichier modifié à télécharger.
    /// </summary>
    public async Task<byte[]> CommitAsync(string ownerId, SaveSession session)
    {
        var sav = session.Save ?? throw new InvalidOperationException("Aucune sauvegarde chargée.");

        await using var db = await dbFactory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        foreach (var w in session.Withdrawals)
        {
            var deleted = await db.BankPokemon.Where(p => p.Id == w.BankId && p.OwnerId == ownerId).ExecuteDeleteAsync();
            if (deleted == 0)
                throw new InvalidOperationException("Un des Pokémon retirés n'est plus dans la banque (retiré depuis un autre onglet ?). Annule et recommence.");
        }

        var now = DateTime.UtcNow;
        foreach (var d in session.Deposits)
        {
            var pk = d.Pokemon;
            var view = info.Describe(pk);
            db.BankPokemon.Add(new BankPokemon
            {
                OwnerId = ownerId,
                Data = pk.DecryptedBoxData,
                Context = (byte)pk.Context,
                Extension = pk.Extension,
                Species = pk.Species,
                Form = pk.Form,
                SpeciesName = view.SpeciesName,
                Nickname = view.Nickname,
                Level = pk.CurrentLevel,
                IsShiny = pk.IsShiny,
                IsEgg = pk.IsEgg,
                IsLegal = view.IsLegal,
                OriginalTrainer = pk.OriginalTrainerName,
                OriginGame = view.OriginGame,
                DepositedAt = now,
            });
        }

        await db.SaveChangesAsync();
        var newData = sav.Write().ToArray();
        await WriteBackupsAsync(ownerId, session, newData);
        await tx.CommitAsync();

        logger.LogInformation("Utilisateur {User} : {Deposits} dépôt(s), {Withdrawals} retrait(s).",
            ownerId, session.Deposits.Count, session.Withdrawals.Count);

        session.MarkCommitted(newData);
        return newData;
    }

    /// <summary>Copie de sécurité de la sauvegarde avant et après chaque validation.</summary>
    private async Task WriteBackupsAsync(string ownerId, SaveSession session, byte[] newData)
    {
        var root = Path.Combine(env.ContentRootPath, config["Bank:BackupPath"] ?? "Data/backups"); // Combine ignore ContentRootPath si le chemin configuré est absolu
        var dir = Path.Combine(root, ownerId);
        Directory.CreateDirectory(dir);

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var name = Path.GetFileNameWithoutExtension(session.FileName);
        var ext = Path.GetExtension(session.FileName);
        if (session.OriginalData is { } original)
            await File.WriteAllBytesAsync(Path.Combine(dir, $"{stamp}_{name}_avant{ext}"), original);
        await File.WriteAllBytesAsync(Path.Combine(dir, $"{stamp}_{name}_apres{ext}"), newData);
    }
}
