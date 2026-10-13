using Microsoft.EntityFrameworkCore;
using PKHeX.Core;
using PokeBank.Data;

namespace PokeBank.Services;

/// <summary>Sauvegarde stockée, telle qu'affichée dans « Mes sauvegardes » (sans le fichier).</summary>
public sealed record StoredSaveInfo(
    int Id, string FileName, string GameName, byte Generation, string TrainerName, string TrainerId,
    int PlayTimeSeconds, int PokemonCount, DateTime UploadedAt, DateTime UpdatedAt)
{
    public string Label => $"{GameName} — {TrainerName}";
}

public enum StoreStatus { Ok, Invalid, AlreadyStored, NotFound, WrongTrainer, OlderPlayTime }

/// <param name="OldPlayTime">Pour <see cref="StoreStatus.OlderPlayTime"/> : temps de jeu de la version stockée (secondes).</param>
/// <param name="NewPlayTime">Pour <see cref="StoreStatus.OlderPlayTime"/> : temps de jeu du fichier envoyé (secondes).</param>
public sealed record StoreResult(StoreStatus Status, string Message, int OldPlayTime = 0, int NewPlayTime = 0)
{
    public bool Ok => Status == StoreStatus.Ok;
}

/// <summary>Personne qui a déjà stocké une sauvegarde donnée.</summary>
public sealed record SaveHolder(string OwnerId, string FileName);

/// <summary>
/// Sauvegardes conservées sur le serveur pour consultation. Leurs Pokémon sont copiés en base (lecture seule)
/// pour apparaître dans « Ma collection ». Une même partie ne peut être stockée qu'une fois sur tout le serveur.
/// </summary>
public sealed class StoredSaveService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    PokemonInfo info,
    IConfiguration config,
    IWebHostEnvironment env,
    ILogger<StoredSaveService> logger)
{
    public const string OtherAccountMessage = "Cette sauvegarde est déjà enregistrée sur le serveur par un autre compte.";

    public async Task<List<StoredSaveInfo>> ListAsync(string ownerId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.StoredSaves.AsNoTracking()
            .Where(s => s.OwnerId == ownerId)
            .OrderBy(s => s.UploadedAt)
            .Select(s => new StoredSaveInfo(s.Id, s.FileName, s.GameName, s.Generation, s.TrainerName, s.TrainerId,
                s.PlayTimeSeconds, s.PokemonCount, s.UploadedAt, s.UpdatedAt))
            .ToListAsync();
    }

    /// <summary>Qui a stocké la sauvegarde de cette identité, s'il y en a une (tous comptes confondus).</summary>
    public async Task<SaveHolder?> FindHolderAsync(string identityKey)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.StoredSaves.AsNoTracking()
            .Where(s => s.IdentityKey == identityKey)
            .Select(s => new SaveHolder(s.OwnerId, s.FileName))
            .FirstOrDefaultAsync();
    }

    /// <summary>Message à afficher quand une sauvegarde est refusée parce qu'elle est déjà stockée.</summary>
    public static string AlreadyStoredMessage(SaveHolder holder, string userId, string ownAdvice) =>
        holder.OwnerId == userId
            ? $"Cette sauvegarde est déjà stockée dans ta collection (« {holder.FileName} »). {ownAdvice}"
            : OtherAccountMessage; // jamais le nom ni l'adresse de l'autre compte

    public async Task<StoreResult> AddAsync(string ownerId, string fileName, byte[] data)
    {
        if (!TryRead(data, fileName, out var sav))
            return Invalid();
        var identity = SaveIdentity.Of(sav);

        await using var db = await dbFactory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        var holder = await db.StoredSaves.Where(s => s.IdentityKey == identity.Key)
            .Select(s => new SaveHolder(s.OwnerId, s.FileName)).FirstOrDefaultAsync();
        if (holder is not null)
            return new(StoreStatus.AlreadyStored, AlreadyStoredMessage(holder, ownerId, "Utilise « Mettre à jour » pour la remplacer."));

        var now = DateTime.UtcNow;
        var save = new StoredSave { OwnerId = ownerId, IdentityKey = identity.Key, UploadedAt = now };
        Fill(save, sav, identity, fileName, data, now);
        db.StoredSaves.Add(save);
        AddPokemon(db, save, sav, ownerId, now);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException) // index unique : stockée au même moment par quelqu'un d'autre
        {
            return new(StoreStatus.AlreadyStored, OtherAccountMessage);
        }
        await tx.CommitAsync();

        logger.LogInformation("Utilisateur {User} : sauvegarde stockée {File} ({Count} Pokémon).", ownerId, fileName, save.PokemonCount);
        return new(StoreStatus.Ok, $"Sauvegarde « {fileName} » stockée : {save.PokemonCount} Pokémon ajoutés à ta collection.");
    }

    /// <summary>
    /// Remplace une sauvegarde stockée par une version plus récente de la même partie, et ses Pokémon par ceux du fichier.
    /// Si le fichier a moins de temps de jeu (vieille copie ?), rien n'est fait sans <paramref name="acceptOlder"/>.
    /// </summary>
    public async Task<StoreResult> UpdateAsync(string ownerId, int saveId, string fileName, byte[] data, bool acceptOlder = false)
    {
        if (!TryRead(data, fileName, out var sav))
            return Invalid();
        var identity = SaveIdentity.Of(sav);

        await using var db = await dbFactory.CreateDbContextAsync();
        var save = await db.StoredSaves.FirstOrDefaultAsync(s => s.Id == saveId && s.OwnerId == ownerId);
        if (save is null)
            return new(StoreStatus.NotFound, "Cette sauvegarde n'existe plus.");
        if (identity.Key != save.IdentityKey)
            return new(StoreStatus.WrongTrainer,
                $"Ce fichier n'est pas la même partie : dresseur {identity.TrainerName} ({identity.TrainerId}) au lieu de {save.TrainerName} ({save.TrainerId}).");
        if (identity.PlayTimeSeconds < save.PlayTimeSeconds && !acceptOlder)
            return new(StoreStatus.OlderPlayTime,
                $"Ce fichier a moins de temps de jeu que la version stockée ({SaveIdentity.FormatPlayTime(save.PlayTimeSeconds)} → {SaveIdentity.FormatPlayTime(identity.PlayTimeSeconds)}) : c'est peut-être une ancienne copie.",
                save.PlayTimeSeconds, identity.PlayTimeSeconds);

        await WriteBackupAsync(ownerId, save, "remplacee");

        await using var tx = await db.Database.BeginTransactionAsync();
        await db.BankPokemon.Where(p => p.StoredSaveId == save.Id).ExecuteDeleteAsync();
        var now = DateTime.UtcNow;
        Fill(save, sav, identity, fileName, data, now);
        AddPokemon(db, save, sav, ownerId, now);
        await db.SaveChangesAsync();
        await tx.CommitAsync();

        logger.LogInformation("Utilisateur {User} : sauvegarde {Id} mise à jour ({Count} Pokémon).", ownerId, save.Id, save.PokemonCount);
        return new(StoreStatus.Ok, $"Sauvegarde mise à jour : {save.PokemonCount} Pokémon.");
    }

    /// <summary>
    /// Retire une sauvegarde stockée et tous ses Pokémon, après en avoir gardé une copie de sécurité.
    /// Renvoie le fichier pour que l'utilisateur le récupère (null si elle n'existe pas).
    /// </summary>
    public async Task<(string FileName, byte[] Data)?> DeleteAsync(string ownerId, int saveId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var save = await db.StoredSaves.FirstOrDefaultAsync(s => s.Id == saveId && s.OwnerId == ownerId);
        if (save is null)
            return null;

        await WriteBackupAsync(ownerId, save, "supprimee");

        await using var tx = await db.Database.BeginTransactionAsync();
        await db.BankPokemon.Where(p => p.StoredSaveId == save.Id).ExecuteDeleteAsync();
        db.StoredSaves.Remove(save);
        await db.SaveChangesAsync();
        await tx.CommitAsync();

        logger.LogInformation("Utilisateur {User} : sauvegarde {Id} supprimée.", ownerId, save.Id);
        return (save.FileName, save.Data);
    }

    /// <summary>
    /// Tous les Pokémon que PKHeX trouve dans la sauvegarde, avec leur emplacement :
    /// équipe, boîtes, puis le reste (pension, Boîte Combat... selon le jeu).
    /// </summary>
    public static IEnumerable<(PKM Pokemon, string Location)> ReadAllPokemon(SaveFile sav)
    {
        if (sav.HasParty)
        {
            for (int i = 0; i < sav.PartyCount; i++)
                if (Valid(sav.GetPartySlotAtIndex(i)) is { } pk)
                    yield return (pk, $"Équipe {i + 1}");
        }

        if (sav.HasBox)
        {
            var named = sav as IBoxDetailName;
            for (int b = 0; b < sav.BoxCount; b++)
            {
                var name = named?.GetBoxName(b);
                var boxName = string.IsNullOrWhiteSpace(name) ? $"Boîte {b + 1}" : name;
                for (int s = 0; s < sav.BoxSlotCount; s++)
                    if (Valid(sav.GetBoxSlotAtIndex(b, s)) is { } pk)
                        yield return (pk, $"{boxName}, case {s + 1}");
            }
        }

        var counts = new Dictionary<StorageSlotType, int>();
        foreach (var slot in sav.GetExtraSlots(true))
        {
            if (slot.Type is StorageSlotType.Party or StorageSlotType.Box)
                continue; // déjà lus ci-dessus
            if (Valid(slot.Read(sav)) is not { } pk)
                continue;
            counts[slot.Type] = counts.GetValueOrDefault(slot.Type) + 1;
            yield return (pk, $"{SlotTypeName(slot.Type)} {counts[slot.Type]}");
        }

        static PKM? Valid(PKM? pk) => pk is null || pk.Species == 0 ? null : pk;
    }

    private static string SlotTypeName(StorageSlotType type) => type switch
    {
        StorageSlotType.Daycare => "Pension",
        StorageSlotType.BattleBox => "Boîte Combat",
        StorageSlotType.GTS => "GTS",
        _ => type.ToString(),
    };

    private static bool TryRead(byte[] data, string fileName, out SaveFile sav)
    {
        // PKHeX peut modifier le tableau : on lui passe une copie pour stocker le fichier d'origine intact.
        if (SaveUtil.TryGetSaveFile(data.ToArray(), out var parsed, fileName) && parsed is not null)
        {
            sav = parsed;
            return true;
        }
        sav = null!;
        return false;
    }

    private static StoreResult Invalid() => new(StoreStatus.Invalid, "Fichier non reconnu comme une sauvegarde Pokémon.");

    private void Fill(StoredSave save, SaveFile sav, SaveIdentity identity, string fileName, byte[] data, DateTime now)
    {
        save.FileName = fileName;
        save.Data = data;
        save.GameName = info.GameName(sav);
        save.Generation = identity.Generation;
        save.TrainerName = identity.TrainerName;
        save.TrainerId = identity.TrainerId;
        save.PlayTimeSeconds = identity.PlayTimeSeconds;
        save.UpdatedAt = now;
    }

    private void AddPokemon(ApplicationDbContext db, StoredSave save, SaveFile sav, string ownerId, DateTime now)
    {
        int position = 0;
        foreach (var (pk, location) in ReadAllPokemon(sav))
        {
            var entry = BankService.CreateEntry(info, pk, ownerId, now);
            entry.StoredSave = save;
            entry.Location = location;
            entry.Position = position++;
            db.BankPokemon.Add(entry);
        }
        save.PokemonCount = position;
    }

    private async Task WriteBackupAsync(string ownerId, StoredSave save, string suffix)
    {
        var dir = BankService.BackupDirectory(config, env, ownerId);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var name = Path.GetFileNameWithoutExtension(save.FileName);
        var ext = Path.GetExtension(save.FileName);
        await File.WriteAllBytesAsync(Path.Combine(dir, $"{stamp}_{name}_stockee-{suffix}{ext}"), save.Data);
    }
}
