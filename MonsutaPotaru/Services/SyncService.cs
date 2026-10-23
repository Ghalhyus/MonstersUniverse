using System.Buffers.Text;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using MonsutaPotaru.Data;

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
    /// sauf si le fichier est identique (rien à faire) ; partie stockée par un autre compte → refusée.
    /// </summary>
    public async Task<SyncUploadResult> UploadAsync(SyncCaller caller, string? fileName, byte[] data)
    {
        var name = CleanFileName(fileName);
        if (!StoredSaveService.TryRead(data, name, out var sav))
            return new(SyncUploadStatus.Refused, "Fichier non reconnu comme une sauvegarde Pokémon.", StoreStatus.Invalid);
        var identity = SaveIdentity.Of(sav);

        int saveId;
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var stored = await db.StoredSaves.AsNoTracking()
                .Where(s => s.IdentityKey == identity.Key)
                .Select(s => new { s.Id, s.OwnerId, s.Data, s.FileName })
                .FirstOrDefaultAsync();
            if (stored is null)
                return Log(caller, name, FromStore(await storedSaves.AddAsync(caller.OwnerId, name, data), SyncUploadStatus.Added));
            if (stored.OwnerId != caller.OwnerId)
                return new(SyncUploadStatus.Refused, StoredSaveService.OtherAccountMessage, StoreStatus.AlreadyStored);
            if (SaveModification.Hash(stored.Data) == SaveModification.Hash(data))
            {
                if (stored.FileName == name)
                    return new(SyncUploadStatus.Unchanged, "Déjà à jour sur le site : rien à envoyer.");
                // Même fichier sous un nom plus précis (la console a reconnu la version exacte du jeu) : seul le nom change.
                await db.StoredSaves.Where(s => s.Id == stored.Id)
                    .ExecuteUpdateAsync(u => u.SetProperty(s => s.FileName, name).SetProperty(s => s.Version, s => s.Version + 1));
                return Log(caller, name, new(SyncUploadStatus.Unchanged, $"Déjà à jour sur le site, renommée « {name} »."));
            }
            saveId = stored.Id;
        }

        var result = await storedSaves.UpdateAsync(caller.OwnerId, saveId, name, data, acceptOlder: false);
        if (result.Status == StoreStatus.OlderPlayTime) // sur le site, on demande confirmation ; ici, on ne force jamais
            result = result with { Message = $"{result.Message} Rien n'a été changé : si c'est voulu, mets-la à jour depuis « Mes sauvegardes » sur le site." };
        return Log(caller, name, FromStore(result, SyncUploadStatus.Updated));
    }

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
