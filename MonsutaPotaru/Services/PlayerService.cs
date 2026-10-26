using Microsoft.EntityFrameworkCore;
using MonsutaPotaru.Data;

namespace MonsutaPotaru.Services;

/// <summary>Lien entre le joueur connecté et un autre joueur.</summary>
public enum PlayerRelation { None, Self, Friend, RequestSent, RequestReceived }

/// <summary>
/// Joueur consultable par le joueur connecté : pseudo, pages qu'il peut voir (<see cref="PublicPages.None"/> pour un profil
/// « Amis seulement » vu par un non-ami), et leur lien.
/// </summary>
/// <param name="FriendsOnly">Profil « Amis seulement » : ses pages sont cachées aux non-amis.</param>
public sealed record PlayerProfile(string Id, string Pseudo, PublicPages Pages, PlayerRelation Relation, bool FriendsOnly = false);

/// <summary>Visibilité du profil d'un compte (« Mon compte » → « Visibilité »).</summary>
public sealed record ProfileVisibility(ProfileAudience Audience, PublicPages Pages);

/// <summary>Une page consultable d'un joueur : titre, morceau d'adresse (<c>joueurs/{pseudo}/{Slug}</c>).</summary>
public sealed record PlayerPage(PublicPages Page, string Title, string Slug);

/// <summary>
/// Multijoueurs : recherche d'un joueur par son pseudo et consultation de ses pages, en lecture seule, selon sa
/// visibilité (<see cref="ProfileAudience"/>) : tout le monde ; ses amis seulement (trouvable par tous, pour recevoir des
/// demandes) ; personne (introuvable, même par ses amis). Un profil caché, une page non visible ou un compte sans pseudo
/// répondent comme un joueur inexistant (<see cref="HiddenMessage"/>), administrateurs compris.
/// </summary>
public sealed class PlayerService(IDbContextFactory<ApplicationDbContext> dbFactory)
{
    public const int MinSearchLength = 2;
    public const int MaxSearchResults = 50;
    public const string HiddenMessage = "Ce joueur n'existe pas ou son profil est privé.";

    public static readonly IReadOnlyList<PlayerPage> Pages =
    [
        new(PublicPages.Collection, "Collection", "collection"),
        new(PublicPages.Park, "Parc", "parc"),
        new(PublicPages.Inventory, "Inventaire", "inventaire"),
        new(PublicPages.LivingDex, "Living dex", "livingdex"),
    ];

    /// <summary>Adresse d'une page d'un joueur (le pseudo peut contenir des espaces et des accents).</summary>
    public static string Link(string pseudo, string? slug = null) =>
        $"joueurs/{Uri.EscapeDataString(pseudo)}" + (slug is null ? "" : $"/{slug}");

    /// <summary>
    /// Pseudos des profils trouvables (« Tout le monde » et « Amis seulement ») qui contiennent la recherche (sans tenir
    /// compte des majuscules ni des accents), par ordre alphabétique ; rien sous <see cref="MinSearchLength"/> caractères
    /// (pas d'annuaire de tous les joueurs).
    /// </summary>
    public async Task<List<string>> SearchAsync(string? query)
    {
        var key = BankPokemon.SortKey(AccountService.NormalizePseudo(query));
        if (key.Length < MinSearchLength)
            return [];
        await using var db = await dbFactory.CreateDbContextAsync();
        var names = await db.Users.AsNoTracking().Where(u => u.Audience != ProfileAudience.Nobody).Select(u => u.UserName).ToListAsync();
        return names
            .Where(n => AccountService.HasPseudo(n) && BankPokemon.SortKey(n!).Contains(key, StringComparison.Ordinal))
            .Select(n => n!)
            .OrderBy(n => BankPokemon.SortKey(n), StringComparer.Ordinal)
            .Take(MaxSearchResults)
            .ToList();
    }

    /// <summary>
    /// Joueur de ce pseudo, vu par <paramref name="viewerId"/> : null s'il est introuvable (« Personne », compte sans
    /// pseudo) ou si <paramref name="page"/> (sauf <see cref="PublicPages.None"/>) ne lui est pas visible, sans dire
    /// pourquoi. Le joueur lui-même voit ses pages comme un ami.
    /// </summary>
    public async Task<PlayerProfile?> FindAsync(string? viewerId, string? pseudo, PublicPages page = PublicPages.None)
    {
        var normalized = AccountService.NormalizePseudo(pseudo).ToUpperInvariant(); // comme le NormalizedUserName d'Identity
        if (normalized.Length == 0)
            return null;
        await using var db = await dbFactory.CreateDbContextAsync();
        var player = await db.Users.AsNoTracking()
            .Where(u => u.NormalizedUserName == normalized && u.Audience != ProfileAudience.Nobody)
            .Select(u => new { u.Id, u.UserName, u.PublicPages, u.Audience })
            .FirstOrDefaultAsync();
        if (player is null || !AccountService.HasPseudo(player.UserName))
            return null;

        var relation = await FriendService.RelationAsync(db, viewerId, player.Id);
        var seesPages = player.Audience == ProfileAudience.Everyone || relation is PlayerRelation.Friend or PlayerRelation.Self;
        var pages = seesPages ? player.PublicPages : PublicPages.None;
        if (page != PublicPages.None && !pages.HasFlag(page))
            return null;
        return new(player.Id, player.UserName!, pages, relation, player.Audience == ProfileAudience.Friends);
    }

    public async Task<ProfileVisibility> VisibilityAsync(string userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var v = await db.Users.AsNoTracking().Where(u => u.Id == userId)
            .Select(u => new ProfileVisibility(u.Audience, u.PublicPages)).FirstOrDefaultAsync();
        return v ?? new(ProfileAudience.Nobody, PublicPages.None);
    }

    public async Task<AccountResult> SetVisibilityAsync(string userId, ProfileAudience audience, PublicPages pages)
    {
        if (!Enum.IsDefined(audience))
            return new(false, "Visibilité inconnue.");
        pages &= PublicPages.All;
        await using var db = await dbFactory.CreateDbContextAsync();
        var changed = await db.Users.Where(u => u.Id == userId)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.Audience, audience).SetProperty(x => x.PublicPages, pages));
        if (changed == 0)
            return new(false, "Ce compte n'existe plus.");
        var shown = string.Join(", ", Pages.Where(p => pages.HasFlag(p.Page)).Select(p => p.Title));
        return new(true, audience switch
        {
            ProfileAudience.Nobody => "Personne ne peut voir tes pages ni te trouver : on ne peut te demander en ami qu'avec ton code.",
            _ when pages == PublicPages.None => "Aucune page n'est visible.",
            ProfileAudience.Friends => $"Tes amis peuvent voir : {shown}.",
            _ => $"Tout le monde peut voir : {shown}.",
        });
    }
}
