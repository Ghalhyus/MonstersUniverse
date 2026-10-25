using System.Buffers.Text;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using MonsutaPotaru.Data;
using PKHeX.Core;

namespace MonsutaPotaru.Services;

/// <summary>Console reliée, telle qu'affichée dans « Synchronisation 3DS ».</summary>
public sealed record SyncDeviceInfo(int Id, string Name, DateTime LinkedAt, DateTime? LastUsedAt);

/// <summary>Code de liaison à taper sur la 3DS (« ABCD-EFGH ») et sa fin de validité (UTC).</summary>
public sealed record SyncPairingInfo(string Code, DateTime ExpiresAt);

/// <summary>Résultat d'une liaison : le jeton à garder sur la console si elle a réussi, sinon un message à afficher.</summary>
public sealed record SyncLinkResult(bool Ok, string Message, string? Token = null, string? Account = null);

/// <summary>Console authentifiée par son jeton : à qui envoyer ses sauvegardes.</summary>
public sealed record SyncCaller(int DeviceId, string OwnerId, string DeviceName);

public enum SyncUploadStatus { Added, Updated, Unchanged, Refused }

/// <param name="Refusal">Pour <see cref="SyncUploadStatus.Refused"/> : la règle qui a refusé l'envoi.</param>
public sealed record SyncUploadResult(SyncUploadStatus Status, string Message, StoreStatus? Refusal = null);

/// <summary>
/// Sauvegarde stockée vue par une console : de quoi la présenter, savoir si elle a changé (empreinte SHA-256) et où la
/// remettre (<paramref name="Keys"/> : emplacements notés pour cette console).
/// </summary>
/// <param name="Console">« 3ds », « ds », « gba », « gb », ou « autre » (jeux d'autres consoles : non récupérables).</param>
/// <param name="Family">Jeu, pour trouver une ROM qui lui convient : « DP », « Pt », « HGSS », « B », « W », « BW »,
/// « B2 », « W2 », « B2W2 » en DS (le jeu exact quand la sauvegarde le dit) ; « RB » (Rouge, Bleu, Vert), « YW » (Jaune),
/// « GS » (Or, Argent), « C » (Cristal) en Game Boy, suivis de « -JP » ou « -KO » pour les versions japonaise et coréenne
/// (disposition différente) ; numéro de version PKHeX sinon.</param>
public sealed record SyncSaveEntry(int Id, string Game, string Trainer, string TrainerId, string FileName, string Hash,
    int PlayTimeSeconds, bool ModifiedOnServer, string Console, string Family, IReadOnlyList<string> Keys);

public enum SyncCompareVerdict { Retrievable, UpToDate, Refused }

/// <summary>Pourquoi la sauvegarde de la console ne peut pas être remplacée.</summary>
public enum SyncCompareRefusal { None, Missing, Unreadable, OtherGame, MoreAdvanced }

/// <summary>Faut-il remplacer la sauvegarde de la console par celle du site ? Message prêt à afficher sur la console.</summary>
/// <param name="ModifiedOnServer">Le site a modifié la partie (retrait d'argent, transfert d'objets) : la console ne l'a pas.</param>
/// <param name="Refusal">Pour un refus : autre partie (la console cherche alors ailleurs où la mettre), progression plus
/// avancée sur la console, fichier illisible, sauvegarde supprimée du site.</param>
public sealed record SyncCompareResult(SyncCompareVerdict Verdict, string Message, bool ModifiedOnServer = false,
    SyncCompareRefusal Refusal = SyncCompareRefusal.None);

/// <summary>
/// Synchronisation depuis une 3DS (homebrew PokeBank Sync) : liaison de la console au compte par un code à usage unique,
/// puis envoi des sauvegardes Pokémon qu'elle trouve. Un envoi ajoute la partie aux sauvegardes stockées, ou met à jour
/// celle déjà stockée par ce compte, avec les règles de « Mes sauvegardes » (<see cref="StoredSaveService"/>). Rien n'est
/// jamais forcé : une version plus ancienne ou une partie modifiée sur le serveur est refusée avec le message du site.
/// </summary>
public sealed class SyncService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    StoredSaveService storedSaves,
    ILogger<SyncService> logger)
{
    /// <summary>Durée de validité d'un code de liaison.</summary>
    public static readonly TimeSpan PairingCodeLifetime = TimeSpan.FromMinutes(10);

    public const int PairingCodeLength = 8;

    /// <summary>Sans 0/O ni 1/I/L, qui se confondent à l'écran ; 31⁸ ≈ 850 milliards de codes.</summary>
    private const string PairingAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    public const int MaxDeviceNameLength = 40;
    public const int MaxFileNameLength = 100;
    public const int MaxKeyLength = 300;

    public const string UnknownTokenMessage =
        "Cette 3DS n'est plus reliée à ton compte : relie-la de nouveau depuis « Synchronisation 3DS » (menu « Sauvegardes ») sur le site.";

    /// <summary>
    /// Nouveau code de liaison pour ce compte. Remplace le précédent s'il y en avait un ; les codes expirés de tout le
    /// serveur sont effacés au passage.
    /// </summary>
    public async Task<SyncPairingInfo> CreatePairingCodeAsync(string ownerId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var now = DateTime.UtcNow;
        await db.SyncPairingCodes.Where(c => c.OwnerId == ownerId || c.ExpiresAt <= now).ExecuteDeleteAsync();

        for (int attempt = 0; ; attempt++)
        {
            var code = new string(RandomNumberGenerator.GetItems<char>(PairingAlphabet, PairingCodeLength));
            var entry = new SyncPairingCode { Code = code, OwnerId = ownerId, ExpiresAt = now + PairingCodeLifetime };
            db.SyncPairingCodes.Add(entry);
            try
            {
                await db.SaveChangesAsync();
                return new SyncPairingInfo(FormatCode(code), entry.ExpiresAt);
            }
            catch (DbUpdateException) when (attempt < 3) // code déjà donné à quelqu'un d'autre (très improbable) : un autre
            {
                db.Entry(entry).State = EntityState.Detached;
            }
        }
    }

    /// <summary>Annule le code de liaison en cours de ce compte (« Annuler » dans « Synchronisation 3DS »).</summary>
    public async Task CancelPairingCodeAsync(string ownerId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.SyncPairingCodes.Where(c => c.OwnerId == ownerId).ExecuteDeleteAsync();
    }

    /// <summary>« ABCDEFGH » → « ABCD-EFGH », plus facile à recopier.</summary>
    public static string FormatCode(string code) => $"{code[..4]}-{code[4..]}";

    /// <summary>Code tapé sur la console : majuscules, sans tiret ni espace (« abcd efgh » vaut « ABCD-EFGH »).</summary>
    public static string NormalizeCode(string? typed) =>
        new((typed ?? "").Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    /// <summary>
    /// Relie la console qui a tapé ce code au compte qui l'a demandé : le code est consommé, et la console reçoit un jeton
    /// longue durée (gardé sur sa carte SD) qu'elle présentera à chaque envoi.
    /// </summary>
    public async Task<SyncLinkResult> LinkAsync(string? typedCode, string? deviceName)
    {
        var code = NormalizeCode(typedCode);
        if (code.Length != PairingCodeLength)
            return new(false, $"Le code fait {PairingCodeLength} caractères (lettres et chiffres), comme affiché dans « Synchronisation 3DS » sur le site.");

        await using var db = await dbFactory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();
        var pairing = await db.SyncPairingCodes.FindAsync(code);
        if (pairing is null)
            return new(false, "Code inconnu : vérifie-le, ou demandes-en un nouveau dans « Synchronisation 3DS » sur le site.");
        db.SyncPairingCodes.Remove(pairing); // à usage unique, même expiré
        if (pairing.ExpiresAt <= DateTime.UtcNow)
        {
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return new(false, "Ce code a expiré : demandes-en un nouveau dans « Synchronisation 3DS » sur le site.");
        }

        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        var device = new SyncDevice
        {
            OwnerId = pairing.OwnerId,
            Name = CleanDeviceName(deviceName),
            TokenHash = HashToken(token),
            LinkedAt = DateTime.UtcNow,
        };
        db.SyncDevices.Add(device);
        await db.SaveChangesAsync();
        await tx.CommitAsync();

        var account = await AccountNameAsync(db, pairing.OwnerId);
        logger.LogInformation("Utilisateur {User} : 3DS « {Device} » reliée ({Id}).", pairing.OwnerId, device.Name, device.Id);
        return new(true, $"3DS reliée au compte {account}.", token, account);
    }

    /// <summary>Console qui présente ce jeton, ou null s'il est inconnu (jamais donné, ou console retirée du compte).</summary>
    public async Task<SyncCaller?> AuthenticateAsync(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;
        var hash = HashToken(token.Trim());
        await using var db = await dbFactory.CreateDbContextAsync();
        var device = await db.SyncDevices.AsNoTracking().FirstOrDefaultAsync(d => d.TokenHash == hash);
        if (device is null)
            return null;
        var now = DateTime.UtcNow;
        await db.SyncDevices.Where(d => d.Id == device.Id).ExecuteUpdateAsync(s => s.SetProperty(d => d.LastUsedAt, now));
        return new SyncCaller(device.Id, device.OwnerId, device.Name);
    }

    /// <summary>Nom du compte, que la console affiche pour qu'on sache où partent les sauvegardes.</summary>
    public async Task<string> AccountNameAsync(string ownerId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await AccountNameAsync(db, ownerId);
    }

    private static async Task<string> AccountNameAsync(ApplicationDbContext db, string ownerId) =>
        await db.Users.Where(u => u.Id == ownerId).Select(u => u.UserName).FirstOrDefaultAsync() ?? "";

    public async Task<List<SyncDeviceInfo>> ListDevicesAsync(string ownerId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.SyncDevices.AsNoTracking()
            .Where(d => d.OwnerId == ownerId)
            .OrderBy(d => d.LinkedAt)
            .Select(d => new SyncDeviceInfo(d.Id, d.Name, d.LinkedAt, d.LastUsedAt))
            .ToListAsync();
    }

    /// <summary>Retire une console du compte : son jeton ne vaut plus rien. Faux si elle n'existe plus.</summary>
    public async Task<bool> UnlinkAsync(string ownerId, int deviceId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var removed = await db.SyncDevices.Where(d => d.Id == deviceId && d.OwnerId == ownerId).ExecuteDeleteAsync();
        if (removed > 0)
            logger.LogInformation("Utilisateur {User} : 3DS {Id} retirée.", ownerId, deviceId);
        return removed > 0;
    }

    /// <summary>
    /// Sauvegarde envoyée par une console : nouvelle partie → ajoutée ; partie déjà stockée par ce compte → mise à jour,
    /// sauf si le fichier est identique (rien à faire) ; partie stockée par un autre compte → refusée. Si l'envoi aboutit,
    /// <paramref name="key"/> (emplacement du fichier sur la console) est noté pour la récupération.
    /// </summary>
    public async Task<SyncUploadResult> UploadAsync(SyncCaller caller, string? fileName, byte[] data, string? key = null)
    {
        var name = CleanFileName(fileName);
        if (!StoredSaveService.TryRead(data, name, out var sav))
            return new(SyncUploadStatus.Refused, "Fichier non reconnu comme une sauvegarde Pokémon.", StoreStatus.Invalid);
        var identity = SaveIdentity.Of(sav);

        SyncUploadResult result;
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var stored = await db.StoredSaves.AsNoTracking()
                .Where(s => s.IdentityKey == identity.Key)
                .Select(s => new { s.Id, s.OwnerId, s.Data, s.FileName })
                .FirstOrDefaultAsync();
            if (stored is null)
                result = FromStore(await storedSaves.AddAsync(caller.OwnerId, name, data), SyncUploadStatus.Added);
            else if (stored.OwnerId != caller.OwnerId)
                return new(SyncUploadStatus.Refused, StoredSaveService.OtherAccountMessage, StoreStatus.AlreadyStored);
            else if (SaveModification.Hash(stored.Data) == SaveModification.Hash(data))
            {
                if (stored.FileName == name)
                    result = new(SyncUploadStatus.Unchanged, "Déjà à jour sur le site : rien à envoyer.");
                else
                {
                    // Même fichier sous un nom plus précis (la console a reconnu la version exacte du jeu) : seul le nom change.
                    await db.StoredSaves.Where(s => s.Id == stored.Id)
                        .ExecuteUpdateAsync(u => u.SetProperty(s => s.FileName, name).SetProperty(s => s.Version, s => s.Version + 1));
                    result = new(SyncUploadStatus.Unchanged, $"Déjà à jour sur le site, renommée « {name} ».");
                }
            }
            else
            {
                var updated = await storedSaves.UpdateAsync(caller.OwnerId, stored.Id, name, data, acceptOlder: false);
                if (updated.Status == StoreStatus.OlderPlayTime) // sur le site, on demande confirmation ; ici, on ne force jamais
                    updated = updated with { Message = $"{updated.Message} Rien n'a été changé : si c'est voulu, mets-la à jour depuis « Mes sauvegardes » sur le site." };
                result = FromStore(updated, SyncUploadStatus.Updated);
            }

            if (result.Status != SyncUploadStatus.Refused)
            {
                var saveId = await db.StoredSaves.Where(s => s.IdentityKey == identity.Key && s.OwnerId == caller.OwnerId)
                    .Select(s => (int?)s.Id).FirstOrDefaultAsync();
                if (saveId is { } id)
                    await RecordLocationAsync(db, caller, id, key);
            }
        }
        return Log(caller, name, result);
    }

    /// <summary>
    /// Sauvegardes stockées du compte, telles que la console les présente dans « Récupérer » : jeu, dresseur, empreinte du
    /// fichier actuel du site, temps de jeu, modification par le site, console d'origine et emplacements connus sur cette
    /// console.
    /// </summary>
    public async Task<List<SyncSaveEntry>> ListForDeviceAsync(SyncCaller caller)
    {
        var infos = await storedSaves.ListAsync(caller.OwnerId);
        await using var db = await dbFactory.CreateDbContextAsync();
        var files = await db.StoredSaves.AsNoTracking().Where(s => s.OwnerId == caller.OwnerId)
            .Select(s => new { s.Id, s.Data }).ToDictionaryAsync(s => s.Id, s => s.Data);
        var keys = (await db.SyncSaveLocations.AsNoTracking().Where(l => l.DeviceId == caller.DeviceId)
                .Select(l => new { l.StoredSaveId, l.Key }).ToListAsync())
            .ToLookup(l => l.StoredSaveId, l => l.Key);

        var entries = new List<SyncSaveEntry>(infos.Count);
        foreach (var info in infos)
        {
            if (!files.TryGetValue(info.Id, out var data))
                continue; // supprimée entre les deux lectures
            var (console, family) = StoredSaveService.TryRead(data, info.FileName, out var sav) ? DescribeForConsole(sav) : ("autre", "");
            entries.Add(new SyncSaveEntry(info.Id, info.GameName, info.TrainerName, info.TrainerId, info.FileName,
                SaveModification.Hash(data), info.PlayTimeSeconds, info.ModifiedOnServer, console, family,
                keys[info.Id].OrderBy(k => k, StringComparer.Ordinal).ToList()));
        }
        return entries;
    }

    /// <summary>Console où se joue la sauvegarde, et jeu précis pour y trouver une ROM (voir <see cref="SyncSaveEntry"/>).</summary>
    public static (string Console, string Family) DescribeForConsole(SaveFile sav) => sav switch
    {
        SAV4DP => ("ds", "DP"),
        SAV4Pt => ("ds", "Pt"),
        SAV4HGSS => ("ds", "HGSS"),
        SAV5BW => ("ds", sav.Version switch { GameVersion.B => "B", GameVersion.W => "W", _ => "BW" }),
        SAV5B2W2 => ("ds", sav.Version switch { GameVersion.B2 => "B2", GameVersion.W2 => "W2", _ => "B2W2" }),
        SAV6XY or SAV6AO or SAV7SM or SAV7USUM => ("3ds", ((int)sav.Version).ToString()),
        SAV3 => ("gba", ((int)sav.Version).ToString()),
        SAV1 g1 => ("gb", (g1.Version == GameVersion.YW ? "YW" : "RB") + (g1.Japanese ? "-JP" : "")),
        SAV2 g2 => ("gb", (g2.Version == GameVersion.C ? "C" : "GS") + (g2.Japanese ? "-JP" : g2.Korean ? "-KO" : "")),
        _ => ("autre", ((int)sav.Version).ToString()),
    };

    /// <summary>
    /// Compare la sauvegarde de la console à celle du site, avant de la remplacer. Refusé si ce n'est pas la même partie
    /// (même dresseur) ou si celle de la console a plus de temps de jeu (sa progression serait perdue). À temps de jeu
    /// égal mais contenu différent, c'est que le site l'a modifiée (retrait d'argent, transfert d'objets) : récupérable.
    /// </summary>
    public async Task<SyncCompareResult> CompareAsync(SyncCaller caller, int saveId, byte[] local)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var stored = await db.StoredSaves.AsNoTracking()
            .Where(s => s.Id == saveId && s.OwnerId == caller.OwnerId)
            .Select(s => new { s.Data, s.IdentityKey, s.PlayTimeSeconds })
            .FirstOrDefaultAsync();
        if (stored is null)
            return new(SyncCompareVerdict.Refused, "Cette sauvegarde n'existe plus sur le site.", Refusal: SyncCompareRefusal.Missing);
        if (SaveModification.Hash(stored.Data) == SaveModification.Hash(local) || SameGameBoyMemory(stored.Data, local))
            return new(SyncCompareVerdict.UpToDate, "Identique à celle du site : rien à récupérer.");
        if (!StoredSaveService.TryRead(local, "", out var sav))
            return new(SyncCompareVerdict.Refused, "Le fichier de la console n'est pas une sauvegarde Pokémon reconnue : il ne sera pas écrasé.",
                Refusal: SyncCompareRefusal.Unreadable);

        var identity = SaveIdentity.Of(sav);
        if (identity.Key != stored.IdentityKey)
            return new(SyncCompareVerdict.Refused,
                $"Le fichier de la console est une autre partie (dresseur {identity.TrainerName}, {identity.TrainerId}) : il ne sera pas écrasé.",
                Refusal: SyncCompareRefusal.OtherGame);

        var modification = await db.SaveModifications.AsNoTracking().FirstOrDefaultAsync(m => m.IdentityKey == stored.IdentityKey);
        var changes = modification is null ? null : StoredSaveService.DescribeServerChanges(modification.Withdrawn, modification.ItemsChanged);
        var onConsole = SaveIdentity.FormatPlayTime(identity.PlayTimeSeconds);
        var site = SaveIdentity.FormatPlayTime(stored.PlayTimeSeconds);
        if (identity.PlayTimeSeconds > stored.PlayTimeSeconds)
            return new(SyncCompareVerdict.Refused,
                $"Ta partie sur la console est plus avancée ({onConsole} contre {site} sur le site) : la récupérer effacerait cette progression. "
                + (changes is null
                    ? "Envoie-la plutôt."
                    : $"Attention : {changes} a modifié cette partie sur le site, et la console ne l'a pas."),
                modification is not null, SyncCompareRefusal.MoreAdvanced);
        if (identity.PlayTimeSeconds == stored.PlayTimeSeconds)
            return new(SyncCompareVerdict.Retrievable,
                changes is null ? "Même temps de jeu, mais contenu différent de celui du site." : $"{Capitalize(changes)} a modifié cette partie sur le site.",
                modification is not null);
        return new(SyncCompareVerdict.Retrievable, $"Plus avancée sur le site ({onConsole} → {site}).", modification is not null);
    }

    /// <summary>
    /// Deux sauvegardes Game Boy dont la mémoire du jeu (les 32 premiers Ko) est identique : l'une vient d'un émulateur,
    /// qui ajoute quelques octets à la fin (32 792 octets pour une Rouge), l'autre de la Console virtuelle (32 Ko tout
    /// juste), où la console n'écrit que ces 32 Ko.
    /// </summary>
    public static bool SameGameBoyMemory(byte[] a, byte[] b)
    {
        const int memory = 0x8000;
        static bool GameBoySize(int length) => length is >= memory and <= 0x8100;
        return GameBoySize(a.Length) && GameBoySize(b.Length) && a.AsSpan(0, memory).SequenceEqual(b.AsSpan(0, memory));
    }

    /// <summary>
    /// Fichier actuel d'une sauvegarde stockée, pour la console (lève le blocage de « Mettre à jour » comme un
    /// téléchargement depuis le site). <paramref name="key"/> : où la console va l'écrire. Null si elle n'existe plus.
    /// </summary>
    public async Task<(string FileName, byte[] Data)?> DownloadAsync(SyncCaller caller, int saveId, string? key)
    {
        if (await storedSaves.DownloadAsync(caller.OwnerId, saveId) is not { } file)
            return null;
        await using (var db = await dbFactory.CreateDbContextAsync())
            await RecordLocationAsync(db, caller, saveId, key);
        logger.LogInformation("Utilisateur {User} : sauvegarde {Id} récupérée par la 3DS {Device} ({Key}).", caller.OwnerId, saveId, caller.DeviceId, key);
        return file;
    }

    /// <summary>Note que ce fichier de la console correspond à cette sauvegarde stockée (remplace l'ancienne correspondance).</summary>
    private static async Task RecordLocationAsync(ApplicationDbContext db, SyncCaller caller, int saveId, string? key)
    {
        key = key?.Trim();
        if (string.IsNullOrEmpty(key) || key.Length > MaxKeyLength || key.Any(char.IsControl))
            return;
        var location = await db.SyncSaveLocations.FirstOrDefaultAsync(l => l.DeviceId == caller.DeviceId && l.Key == key);
        if (location is null)
            db.SyncSaveLocations.Add(location = new SyncSaveLocation { DeviceId = caller.DeviceId, Key = key });
        location.StoredSaveId = saveId;
        location.UpdatedAt = DateTime.UtcNow;
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException) // notée au même moment par une autre requête : la sienne vaut autant
        {
        }
    }

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private static SyncUploadResult FromStore(StoreResult result, SyncUploadStatus success) =>
        result.Ok ? new(success, result.Message) : new(SyncUploadStatus.Refused, result.Message, result.Status);

    private SyncUploadResult Log(SyncCaller caller, string fileName, SyncUploadResult result)
    {
        logger.LogInformation("Utilisateur {User} : envoi de « {File} » depuis la 3DS {Device} : {Status} {Refusal}.",
            caller.OwnerId, fileName, caller.DeviceId, result.Status, result.Refusal);
        return result;
    }

    internal static string HashToken(string token) => SaveModification.Hash(System.Text.Encoding.UTF8.GetBytes(token));

    private static string CleanDeviceName(string? name)
    {
        var clean = new string((name ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
        return clean.Length == 0 ? "3DS" : clean[..Math.Min(clean.Length, MaxDeviceNameLength)];
    }

    /// <summary>Nom affiché à côté de « Jeu — Dresseur » : sans dossier ni caractère de contrôle, extension gardée.</summary>
    public static string CleanFileName(string? fileName)
    {
        var clean = Path.GetFileName((fileName ?? "").Replace('\\', '/'));
        clean = new string(clean.Where(c => !char.IsControl(c) && !"<>:\"|?*".Contains(c)).ToArray()).Trim(); // interdits sous Windows (téléchargement)
        if (clean.Length == 0)
            return "sauvegarde.sav";
        if (clean.Length <= MaxFileNameLength)
            return clean;
        var ext = Path.GetExtension(clean);
        return ext.Length < 10 ? clean[..(MaxFileNameLength - ext.Length)] + ext : clean[..MaxFileNameLength];
    }
}
