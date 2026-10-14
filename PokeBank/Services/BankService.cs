using Microsoft.EntityFrameworkCore;
using PKHeX.Core;
using PokeBank.Data;

namespace PokeBank.Services;

/// <summary>Ce qu'il faut pour afficher un Pokémon de la banque dans une grille.</summary>
/// <param name="StoredSaveId">Renseigné pour un Pokémon d'une sauvegarde stockée (lecture seule).</param>
public sealed record BankListItem(int Id, ushort Species, string SpeciesName, string Nickname, byte Level, bool IsShiny, bool IsEgg, bool IsLegal, int? StoredSaveId);

/// <summary>Filtre de « Ma collection » : tout, la banque seule, ou une sauvegarde stockée.</summary>
public readonly record struct CollectionFilter(bool BankOnly = false, int? StoredSaveId = null)
{
    public static readonly CollectionFilter All = new();
    public static readonly CollectionFilter Bank = new(BankOnly: true);
    public static CollectionFilter Save(int id) => new(StoredSaveId: id);
}

public sealed class BankService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    PokemonInfo info,
    IConfiguration config,
    IWebHostEnvironment env,
    ILogger<BankService> logger)
{
    /// <summary>Pokémon de la banque (hors sauvegardes stockées) : ceux que « Transfert » peut retirer.</summary>
    public async Task<List<BankPokemon>> ListAsync(string ownerId, string? search = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await Query(db, ownerId, search, CollectionFilter.Bank).ToListAsync();
    }

    public async Task<int> CountAsync(string ownerId, string? search = null, CollectionFilter filter = default, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await Query(db, ownerId, search, filter).CountAsync(ct);
    }

    /// <summary>
    /// Une tranche de la collection, sans les octets du Pokémon (affichage seulement) :
    /// la page « Ma collection » charge ainsi au fur et à mesure du défilement.
    /// </summary>
    public async Task<List<BankListItem>> GetRangeAsync(string ownerId, string? search, int skip, int take, CollectionFilter filter = default, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await Query(db, ownerId, search, filter)
            .Skip(skip)
            .Take(take)
            .Select(p => new BankListItem(p.Id, p.Species, p.SpeciesName, p.Nickname, p.Level, p.IsShiny, p.IsEgg, p.IsLegal, p.StoredSaveId))
            .ToListAsync(ct);
    }

    /// <summary>Occupation de la collection (banque + sauvegardes stockées) et sa limite.</summary>
    public async Task<CollectionUsage> GetUsageAsync(string ownerId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await CollectionCapacity.GetUsageAsync(db, ownerId);
    }

    public async Task<BankPokemon?> GetAsync(string ownerId, int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.BankPokemon.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id && p.OwnerId == ownerId);
    }

    /// <summary>
    /// Pokémon d'un utilisateur, filtrés par origine et par espèce / surnom, du plus récent au plus ancien
    /// (et, dans une même sauvegarde stockée, dans l'ordre équipe → boîtes → reste).
    /// </summary>
    private static IQueryable<BankPokemon> Query(ApplicationDbContext db, string ownerId, string? search, CollectionFilter filter)
    {
        var query = db.BankPokemon.AsNoTracking().Where(p => p.OwnerId == ownerId);
        if (filter.BankOnly)
            query = query.Where(p => p.StoredSaveId == null);
        else if (filter.StoredSaveId is { } saveId)
            query = query.Where(p => p.StoredSaveId == saveId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = $"%{search.Trim()}%";
            query = query.Where(p => EF.Functions.Like(p.SpeciesName, s) || EF.Functions.Like(p.Nickname, s));
        }
        return query.OrderByDescending(p => p.DepositedAt).ThenBy(p => p.Position).ThenByDescending(p => p.Id);
    }

    public static PKM? ToPkm(BankPokemon entry) =>
        EntityFormat.GetFromBytes(entry.Data.ToArray(), (EntityContext)entry.Context);

    /// <summary>Ligne de base pour un Pokémon : octets bruts + métadonnées d'affichage et de recherche.</summary>
    public static BankPokemon CreateEntry(PokemonInfo info, PKM pk, string ownerId, DateTime now)
    {
        var view = info.Describe(pk);
        return new BankPokemon
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
        };
    }

    /// <summary>
    /// Enregistre en base les dépôts et retraits en attente, garde une copie de la sauvegarde
    /// avant/après, puis renvoie le fichier modifié à télécharger.
    /// </summary>
    public async Task<byte[]> CommitAsync(string ownerId, SaveSession session)
    {
        var sav = session.Save ?? throw new InvalidOperationException("Aucune sauvegarde chargée.");

        await using var db = await dbFactory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        // La sauvegarde a pu être stockée (depuis un autre onglet) après son chargement dans « Transfert ».
        var key = SaveIdentity.Of(sav).Key;
        if (await db.StoredSaves.AnyAsync(s => s.IdentityKey == key))
            throw new InvalidOperationException("Cette sauvegarde est maintenant stockée sur le serveur : elle ne peut plus être utilisée dans « Transfert ».");

        foreach (var w in session.Withdrawals)
        {
            // Jamais un Pokémon de sauvegarde stockée : ils sont en lecture seule.
            var deleted = await db.BankPokemon.Where(p => p.Id == w.BankId && p.OwnerId == ownerId && p.StoredSaveId == null).ExecuteDeleteAsync();
            if (deleted == 0)
                throw new InvalidOperationException("Un des Pokémon retirés n'est plus dans le stockage interne (retiré depuis un autre onglet ?). Annule et recommence.");
        }

        // Taille de la collection, vérifiée dans la transaction : les retraits viennent de libérer leur place.
        var afterWithdrawals = await CollectionCapacity.GetUsageAsync(db, ownerId);
        var before = afterWithdrawals with { Used = afterWithdrawals.Used + session.Withdrawals.Count };
        if (CollectionCapacity.Exceeds(before, afterWithdrawals.Used + session.Deposits.Count))
            throw new InvalidOperationException(CollectionCapacity.BankFullMessage(before, session.Deposits.Count - session.Withdrawals.Count));

        var now = DateTime.UtcNow;
        foreach (var d in session.Deposits)
            db.BankPokemon.Add(CreateEntry(info, d.Pokemon, ownerId, now));

        await db.SaveChangesAsync();
        var newData = sav.Write().ToArray();
        await WriteBackupsAsync(ownerId, session, newData);
        await tx.CommitAsync();

        logger.LogInformation("Utilisateur {User} : {Deposits} dépôt(s), {Withdrawals} retrait(s).",
            ownerId, session.Deposits.Count, session.Withdrawals.Count);

        session.MarkCommitted(newData);
        return newData;
    }

    /// <summary>Dossier des copies de sécurité d'un utilisateur (créé au besoin).</summary>
    public static string BackupDirectory(IConfiguration config, IWebHostEnvironment env, string ownerId)
    {
        var root = Path.Combine(env.ContentRootPath, config["Bank:BackupPath"] ?? "Data/backups"); // Combine ignore ContentRootPath si le chemin configuré est absolu
        var dir = Path.Combine(root, ownerId);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Copie de sécurité de la sauvegarde avant et après chaque validation.</summary>
    private async Task WriteBackupsAsync(string ownerId, SaveSession session, byte[] newData)
    {
        var dir = BackupDirectory(config, env, ownerId);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var name = Path.GetFileNameWithoutExtension(session.FileName);
        var ext = Path.GetExtension(session.FileName);
        if (session.OriginalData is { } original)
            await File.WriteAllBytesAsync(Path.Combine(dir, $"{stamp}_{name}_avant{ext}"), original);
        await File.WriteAllBytesAsync(Path.Combine(dir, $"{stamp}_{name}_apres{ext}"), newData);
    }
}
