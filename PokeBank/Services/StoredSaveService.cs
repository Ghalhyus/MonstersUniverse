using Microsoft.EntityFrameworkCore;
using PKHeX.Core;
using PokeBank.Data;

namespace PokeBank.Services;

/// <summary>Sauvegarde stockée, telle qu'affichée dans « Mes sauvegardes » (sans le fichier).</summary>
/// <param name="ModifiedOnServer">Le serveur a modifié le fichier (retrait de Pokédollars) depuis le dernier envoi.</param>
/// <param name="MustDownloadBeforeUpdate">Modifiée sur le serveur et pas encore téléchargée : « Mettre à jour » est bloqué.</param>
public sealed record StoredSaveInfo(
    int Id, string FileName, string GameName, byte Generation, string TrainerName, string TrainerId,
    int PlayTimeSeconds, int PokemonCount, DateTime UploadedAt, DateTime UpdatedAt,
    bool ModifiedOnServer, bool MustDownloadBeforeUpdate, long WithdrawnOnServer)
{
    public string Label => $"{GameName} — {TrainerName}";
}

public enum StoreStatus { Ok, Invalid, AlreadyStored, NotFound, WrongTrainer, OlderPlayTime, ModifiedOnServer, Conflict }

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
        return await (
                from s in db.StoredSaves.AsNoTracking()
                where s.OwnerId == ownerId
                join m in db.SaveModifications on s.IdentityKey equals m.IdentityKey into mods
                from m in mods.DefaultIfEmpty()
                orderby s.UploadedAt
                select new StoredSaveInfo(s.Id, s.FileName, s.GameName, s.Generation, s.TrainerName, s.TrainerId,
                    s.PlayTimeSeconds, s.PokemonCount, s.UploadedAt, s.UpdatedAt,
                    m != null, m != null && !s.DownloadedSinceModification, m != null ? m.Withdrawn : 0))
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

        // Partie déjà modifiée par le serveur, puis supprimée : on ne peut la renvoyer que dans la version rendue ou plus avancée.
        var modification = await db.SaveModifications.FindAsync(identity.Key);
        if (CheckServerModification(modification, identity, sav) is { } refused)
            return refused;

        var now = DateTime.UtcNow;
        var save = new StoredSave { OwnerId = ownerId, IdentityKey = identity.Key, UploadedAt = now };
        Fill(save, sav, identity, fileName, data, now);
        save.DownloadedSinceModification = modification is not null; // c'est la version rendue par le serveur
        ClearIfMoreAdvanced(db, modification, identity, save);
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
    /// Si le serveur a modifié la partie (retrait de Pokédollars), il faut l'avoir téléchargée, et le fichier doit être
    /// plus avancé que la version modifiée, sans exception (voir <see cref="CheckServerModification"/>).
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
        var modification = await db.SaveModifications.FindAsync(save.IdentityKey);
        if (modification is not null)
        {
            if (!save.DownloadedSinceModification)
                return new(StoreStatus.ModifiedOnServer,
                    $"Un retrait de {InventoryService.FormatMoney(modification.Withdrawn)} a modifié cette sauvegarde sur le serveur : télécharge-la et remplace celle de ton jeu avant de la mettre à jour.");
            if (CheckServerModification(modification, identity, sav) is { } refused)
                return refused;
        }
        else if (identity.PlayTimeSeconds < save.PlayTimeSeconds && !acceptOlder)
            return new(StoreStatus.OlderPlayTime,
                $"Ce fichier a moins de temps de jeu que la version stockée ({SaveIdentity.FormatPlayTime(save.PlayTimeSeconds)} → {SaveIdentity.FormatPlayTime(identity.PlayTimeSeconds)}) : c'est peut-être une ancienne copie.",
                save.PlayTimeSeconds, identity.PlayTimeSeconds);

        await WriteBackupAsync(ownerId, save, "remplacee");

        await using var tx = await db.Database.BeginTransactionAsync();
        await db.BankPokemon.Where(p => p.StoredSaveId == save.Id).ExecuteDeleteAsync();
        var now = DateTime.UtcNow;
        Fill(save, sav, identity, fileName, data, now);
        ClearIfMoreAdvanced(db, modification, identity, save);
        AddPokemon(db, save, sav, ownerId, now);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException) // modifiée au même moment (retrait depuis un autre onglet...)
        {
            return ConflictResult();
        }
        await tx.CommitAsync();

        logger.LogInformation("Utilisateur {User} : sauvegarde {Id} mise à jour ({Count} Pokémon).", ownerId, save.Id, save.PokemonCount);
        return new(StoreStatus.Ok, $"Sauvegarde mise à jour : {save.PokemonCount} Pokémon.");
    }

    /// <summary>
    /// Fichier actuel d'une sauvegarde stockée (avec les éventuelles modifications du serveur), sans la supprimer.
    /// Lève le blocage de « Mettre à jour » posé après un retrait. Null si elle n'existe pas.
    /// </summary>
    public async Task<(string FileName, byte[] Data)?> DownloadAsync(string ownerId, int saveId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var save = await db.StoredSaves.FirstOrDefaultAsync(s => s.Id == saveId && s.OwnerId == ownerId);
        if (save is null)
            return null;
        if (!save.DownloadedSinceModification && await db.SaveModifications.AnyAsync(m => m.IdentityKey == save.IdentityKey))
        {
            save.DownloadedSinceModification = true;
            save.Version++;
            await db.SaveChangesAsync(); // jeton de concurrence : échoue si un retrait vient de modifier le fichier
        }
        return (save.FileName, save.Data);
    }

    /// <summary>
    /// Refuse le fichier d'une partie modifiée par le serveur s'il n'est pas plus avancé que la version modifiée :
    /// c'est alors l'ancienne sauvegarde du jeu, qui contient encore l'argent retiré. Seule exception, la version
    /// modifiée elle-même (même temps de jeu, pas plus d'argent), rendue au joueur par « Télécharger » ou la suppression.
    /// </summary>
    private static StoreResult? CheckServerModification(SaveModification? modification, SaveIdentity identity, SaveFile sav)
    {
        if (modification is null || identity.PlayTimeSeconds > modification.PlayTimeSeconds)
            return null;
        if (identity.PlayTimeSeconds == modification.PlayTimeSeconds && sav.Money <= modification.MoneyAfter)
            return null;
        return new(StoreStatus.ModifiedOnServer,
            $"Un retrait de {InventoryService.FormatMoney(modification.Withdrawn)} a modifié cette partie sur le serveur, et ce fichier n'est pas plus avancé " +
            $"({SaveIdentity.FormatPlayTime(modification.PlayTimeSeconds)} → {SaveIdentity.FormatPlayTime(identity.PlayTimeSeconds)}) : c'est sans doute l'ancienne " +
            "sauvegarde de ton jeu, qui contient encore l'argent retiré. Utilise la version rendue par PokeBank (« Télécharger » ou suppression), joue un peu, puis réessaie.");
    }

    /// <summary>Le joueur a envoyé une partie plus avancée que la version modifiée : la trace est levée, tout redevient normal.</summary>
    private static void ClearIfMoreAdvanced(ApplicationDbContext db, SaveModification? modification, SaveIdentity identity, StoredSave save)
    {
        if (modification is null || identity.PlayTimeSeconds <= modification.PlayTimeSeconds)
            return;
        db.SaveModifications.Remove(modification);
        save.DownloadedSinceModification = false;
    }

    public static StoreResult ConflictResult() =>
        new(StoreStatus.Conflict, "Cette sauvegarde vient d'être modifiée ailleurs (autre onglet ?) : recharge la page et recommence.");

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

    internal static bool TryRead(byte[] data, string fileName, out SaveFile sav)
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
        save.Version++;
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

    internal async Task WriteBackupAsync(string ownerId, StoredSave save, string suffix)
    {
        var dir = BankService.BackupDirectory(config, env, ownerId);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var name = Path.GetFileNameWithoutExtension(save.FileName);
        var ext = Path.GetExtension(save.FileName);
        await File.WriteAllBytesAsync(Path.Combine(dir, $"{stamp}_{name}_stockee-{suffix}{ext}"), save.Data);
    }
}
