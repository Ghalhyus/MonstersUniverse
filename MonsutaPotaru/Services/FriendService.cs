using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using MonsutaPotaru.Data;

namespace MonsutaPotaru.Services;

/// <summary>Un ami, une demande reçue ou envoyée : pseudo (« Pseudo pas encore choisi » pour un ancien compte) et date.</summary>
public sealed record FriendEntry(string UserId, string? Pseudo, DateTime At, int RequestId = 0);

/// <summary>Page « Amis » : mes amis, les demandes reçues et celles que j'ai envoyées.</summary>
public sealed record FriendsOverview(IReadOnlyList<FriendEntry> Friends, IReadOnlyList<FriendEntry> Received, IReadOnlyList<FriendEntry> Sent);

/// <summary>
/// Amis (Multijoueurs → Amis) : demandes d'ami par le profil d'un joueur trouvable ou par son code ami (même introuvable),
/// acceptées ou refusées par l'autre ; une demande qui croise celle de l'autre est acceptée d'emblée. Chaque compte a un
/// code ami permanent, régénérable (<see cref="ApplicationUser.FriendCode"/>). Les amitiés sont enregistrées dans les deux
/// sens (<see cref="Friendship"/>).
/// </summary>
public sealed class FriendService(IDbContextFactory<ApplicationDbContext> dbFactory, ILogger<FriendService> logger)
{
    public const string NoPseudo = "Pseudo pas encore choisi";

    /// <summary>
    /// Demandes ou amis changés dans ce circuit (une action de la page « Amis » ou d'un profil) : le menu relit son
    /// compteur de demandes reçues.
    /// </summary>
    public event Action? Changed;

    /// <summary>Prévient le menu d'un changement fait par la page (résultat renvoyé tel quel).</summary>
    public AccountResult Notify(AccountResult result)
    {
        Changed?.Invoke();
        return result;
    }

    /// <summary>Lien entre deux comptes (l'appelant d'abord).</summary>
    public static async Task<PlayerRelation> RelationAsync(ApplicationDbContext db, string? userId, string otherId)
    {
        if (string.IsNullOrEmpty(userId))
            return PlayerRelation.None;
        if (userId == otherId)
            return PlayerRelation.Self;
        if (await db.Friendships.AnyAsync(f => f.UserId == userId && f.FriendId == otherId))
            return PlayerRelation.Friend;
        if (await db.FriendRequests.AnyAsync(r => r.FromId == userId && r.ToId == otherId))
            return PlayerRelation.RequestSent;
        if (await db.FriendRequests.AnyAsync(r => r.FromId == otherId && r.ToId == userId))
            return PlayerRelation.RequestReceived;
        return PlayerRelation.None;
    }

    /// <summary>Code ami du compte, tiré au premier besoin (« ABCD-EFGH »).</summary>
    public async Task<string> CodeAsync(string userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var code = await db.Users.Where(u => u.Id == userId).Select(u => u.FriendCode).FirstOrDefaultAsync();
        return code ?? await NewCodeAsync(db, userId);
    }

    /// <summary>Nouveau code ami : l'ancien ne permet plus de m'envoyer de demande.</summary>
    public async Task<AccountResult> RegenerateCodeAsync(string userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var code = await NewCodeAsync(db, userId);
        return new(true, $"Nouveau code ami : {code}. L'ancien ne marche plus.");
    }

    private static async Task<string> NewCodeAsync(ApplicationDbContext db, string userId)
    {
        while (true)
        {
            var code = SyncService.FormatCode(new string(RandomNumberGenerator.GetItems<char>(SyncService.PairingAlphabet, 8)));
            if (await db.Users.AnyAsync(u => u.FriendCode == code))
                continue; // déjà pris (très improbable) : un autre
            await db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(u => u.SetProperty(x => x.FriendCode, code));
            return code;
        }
    }

    /// <summary>Demande d'ami depuis le profil d'un joueur trouvable (pas « Personne »).</summary>
    public async Task<AccountResult> RequestAsync(string userId, string? pseudo)
    {
        var normalized = AccountService.NormalizePseudo(pseudo).ToUpperInvariant();
        await using var db = await dbFactory.CreateDbContextAsync();
        var target = normalized.Length == 0 ? null : await db.Users
            .Where(u => u.NormalizedUserName == normalized && u.Audience != ProfileAudience.Nobody)
            .Select(u => new { u.Id, u.UserName }).FirstOrDefaultAsync();
        if (target is null || !AccountService.HasPseudo(target.UserName))
            return new(false, PlayerService.HiddenMessage);
        return await CreateRequestAsync(db, userId, target.Id, target.UserName!);
    }

    /// <summary>Demande d'ami par le code ami de l'autre (majuscules, tirets et espaces ignorés), même s'il est introuvable.</summary>
    public async Task<AccountResult> RequestByCodeAsync(string userId, string? code)
    {
        var typed = SyncService.NormalizeCode(code);
        if (typed.Length != 8)
            return new(false, "Un code ami fait 8 caractères (ABCD-EFGH).");
        await using var db = await dbFactory.CreateDbContextAsync();
        var target = await db.Users.Where(u => u.FriendCode == SyncService.FormatCode(typed))
            .Select(u => new { u.Id, u.UserName }).FirstOrDefaultAsync();
        if (target is null)
            return new(false, "Aucun joueur n'a ce code ami.");
        return await CreateRequestAsync(db, userId, target.Id, AccountService.HasPseudo(target.UserName) ? target.UserName! : "Ce joueur");
    }

    private async Task<AccountResult> CreateRequestAsync(ApplicationDbContext db, string userId, string targetId, string name)
    {
        switch (await RelationAsync(db, userId, targetId))
        {
            case PlayerRelation.Self:
                return new(false, "C'est ton propre code : donne-le à tes amis.");
            case PlayerRelation.Friend:
                return new(false, $"{name} est déjà ton ami.");
            case PlayerRelation.RequestSent:
                return new(false, $"Tu as déjà envoyé une demande à {name}.");
            case PlayerRelation.RequestReceived: // les deux le veulent : amis tout de suite
                var reverse = await db.FriendRequests.FirstAsync(r => r.FromId == targetId && r.ToId == userId);
                await BefriendAsync(db, reverse);
                return new(true, $"{name} t'avait déjà fait une demande : vous êtes maintenant amis.");
        }
        db.FriendRequests.Add(new FriendRequest { FromId = userId, ToId = targetId, CreatedAt = DateTime.UtcNow });
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException) // envoyée au même moment depuis un autre onglet
        {
            return new(false, $"Tu as déjà envoyé une demande à {name}.");
        }
        logger.LogInformation("Compte {User} : demande d'ami au compte {Target}.", userId, targetId);
        return new(true, $"Demande d'ami envoyée à {name}.");
    }

    /// <summary>Accepte une demande reçue.</summary>
    public async Task<AccountResult> AcceptAsync(string userId, int requestId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var request = await db.FriendRequests.Include(r => r.From).FirstOrDefaultAsync(r => r.Id == requestId && r.ToId == userId);
        if (request is null)
            return new(false, "Cette demande n'existe plus.");
        await BefriendAsync(db, request);
        return new(true, $"{Name(request.From?.UserName)} est maintenant ton ami.");
    }

    /// <summary>Amitié dans les deux sens ; la demande (et celle de l'autre sens, s'il y en a une) disparaît.</summary>
    private async Task BefriendAsync(ApplicationDbContext db, FriendRequest request)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.FriendRequests.Where(r => (r.FromId == request.FromId && r.ToId == request.ToId)
            || (r.FromId == request.ToId && r.ToId == request.FromId)).ExecuteDeleteAsync();
        if (!await db.Friendships.AnyAsync(f => f.UserId == request.FromId && f.FriendId == request.ToId))
        {
            var now = DateTime.UtcNow;
            db.Friendships.Add(new Friendship { UserId = request.FromId, FriendId = request.ToId, Since = now });
            db.Friendships.Add(new Friendship { UserId = request.ToId, FriendId = request.FromId, Since = now });
            await db.SaveChangesAsync();
        }
        await tx.CommitAsync();
        logger.LogInformation("Comptes {From} et {To} : amis.", request.FromId, request.ToId);
    }

    /// <summary>Refuse une demande reçue (l'autre n'est pas prévenu : elle disparaît de ses demandes envoyées).</summary>
    public async Task<AccountResult> DeclineAsync(string userId, int requestId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.FriendRequests.Where(r => r.Id == requestId && r.ToId == userId).ExecuteDeleteAsync() == 0
            ? new(false, "Cette demande n'existe plus.") : new(true, "Demande refusée.");
    }

    /// <summary>Annule une demande envoyée.</summary>
    public async Task<AccountResult> CancelAsync(string userId, int requestId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.FriendRequests.Where(r => r.Id == requestId && r.FromId == userId).ExecuteDeleteAsync() == 0
            ? new(false, "Cette demande n'existe plus.") : new(true, "Demande annulée.");
    }

    /// <summary>Retire un ami, des deux côtés.</summary>
    public async Task<AccountResult> RemoveAsync(string userId, string friendId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var name = await db.Users.Where(u => u.Id == friendId).Select(u => u.UserName).FirstOrDefaultAsync();
        var removed = await db.Friendships.Where(f => (f.UserId == userId && f.FriendId == friendId)
            || (f.UserId == friendId && f.FriendId == userId)).ExecuteDeleteAsync();
        if (removed == 0)
            return new(false, "Ce joueur n'est pas (ou plus) ton ami.");
        logger.LogInformation("Compte {User} : ami {Friend} retiré.", userId, friendId);
        return new(true, $"{Name(name)} n'est plus ton ami.");
    }

    /// <summary>Mes amis (par pseudo), les demandes reçues et envoyées (les plus récentes d'abord).</summary>
    public async Task<FriendsOverview> ListAsync(string userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var friends = await db.Friendships.AsNoTracking().Where(f => f.UserId == userId)
            .Select(f => new { f.FriendId, f.Friend!.UserName, f.Since }).ToListAsync();
        var received = await db.FriendRequests.AsNoTracking().Where(r => r.ToId == userId)
            .Select(r => new { r.Id, r.FromId, r.From!.UserName, r.CreatedAt }).ToListAsync();
        var sent = await db.FriendRequests.AsNoTracking().Where(r => r.FromId == userId)
            .Select(r => new { r.Id, r.ToId, r.To!.UserName, r.CreatedAt }).ToListAsync();
        return new(
            friends.Select(f => new FriendEntry(f.FriendId, Pseudo(f.UserName), f.Since))
                .OrderBy(f => f.Pseudo is null).ThenBy(f => BankPokemon.SortKey(f.Pseudo ?? ""), StringComparer.Ordinal).ToList(),
            received.Select(r => new FriendEntry(r.FromId, Pseudo(r.UserName), r.CreatedAt, r.Id)).OrderByDescending(r => r.At).ToList(),
            sent.Select(r => new FriendEntry(r.ToId, Pseudo(r.UserName), r.CreatedAt, r.Id)).OrderByDescending(r => r.At).ToList());
    }

    /// <summary>Demandes reçues en attente (compteur du menu).</summary>
    public async Task<int> PendingCountAsync(string userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.FriendRequests.CountAsync(r => r.ToId == userId);
    }

    private static string? Pseudo(string? userName) => AccountService.HasPseudo(userName) ? userName : null;

    private static string Name(string? userName) => Pseudo(userName) ?? "Ce joueur";
}
