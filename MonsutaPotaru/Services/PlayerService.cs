using Microsoft.EntityFrameworkCore;
using MonsutaPotaru.Data;

namespace MonsutaPotaru.Services;

/// <summary>Joueur consultable par les autres : pseudo et pages publiques.</summary>
public sealed record PlayerProfile(string Id, string Pseudo, PublicPages Pages);

/// <summary>Visibilité du profil d'un compte (« Mon compte » → « Visibilité »).</summary>
public sealed record ProfileVisibility(bool Private, PublicPages Pages);

/// <summary>Une page consultable d'un joueur : titre, morceau d'adresse (<c>joueurs/{pseudo}/{Slug}</c>).</summary>
public sealed record PlayerPage(PublicPages Page, string Title, string Slug);

/// <summary>
/// Multijoueurs : recherche d'un joueur par son pseudo et consultation de ses pages publiques (Collection, Parc,
/// Inventaire, Living dex), en lecture seule. Un profil privé, une page non publique ou un compte sans pseudo répondent
/// comme un joueur inexistant (<see cref="HiddenMessage"/>), pour tout le monde, administrateurs compris.
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
    /// Pseudos des profils publics qui contiennent la recherche (sans tenir compte des majuscules ni des accents), par
    /// ordre alphabétique ; rien sous <see cref="MinSearchLength"/> caractères (pas d'annuaire de tous les joueurs).
    /// </summary>
    public async Task<List<string>> SearchAsync(string? query)
    {
        var key = BankPokemon.SortKey(AccountService.NormalizePseudo(query));
        if (key.Length < MinSearchLength)
            return [];
        await using var db = await dbFactory.CreateDbContextAsync();
        var names = await db.Users.AsNoTracking().Where(u => !u.ProfilePrivate).Select(u => u.UserName).ToListAsync();
        return names
            .Where(n => AccountService.HasPseudo(n) && BankPokemon.SortKey(n!).Contains(key, StringComparison.Ordinal))
            .Select(n => n!)
            .OrderBy(n => BankPokemon.SortKey(n), StringComparer.Ordinal)
            .Take(MaxSearchResults)
            .ToList();
    }

    /// <summary>
    /// Joueur de ce pseudo, si son profil est public (et la page demandée publique, si <paramref name="page"/> n'est pas
    /// <see cref="PublicPages.None"/>) ; null sinon, sans dire pourquoi.
    /// </summary>
    public async Task<PlayerProfile?> FindAsync(string? pseudo, PublicPages page = PublicPages.None)
    {
        var normalized = AccountService.NormalizePseudo(pseudo).ToUpperInvariant(); // comme le NormalizedUserName d'Identity
        if (normalized.Length == 0)
            return null;
        await using var db = await dbFactory.CreateDbContextAsync();
        var player = await db.Users.AsNoTracking()
            .Where(u => u.NormalizedUserName == normalized && !u.ProfilePrivate)
            .Select(u => new { u.Id, u.UserName, u.PublicPages })
            .FirstOrDefaultAsync();
        if (player is null || !AccountService.HasPseudo(player.UserName) || (page != PublicPages.None && !player.PublicPages.HasFlag(page)))
            return null;
        return new(player.Id, player.UserName!, player.PublicPages);
    }

    public async Task<ProfileVisibility> VisibilityAsync(string userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var v = await db.Users.AsNoTracking().Where(u => u.Id == userId)
            .Select(u => new ProfileVisibility(u.ProfilePrivate, u.PublicPages)).FirstOrDefaultAsync();
        return v ?? new(true, PublicPages.None);
    }

    public async Task<AccountResult> SetVisibilityAsync(string userId, bool isPrivate, PublicPages pages)
    {
        pages &= PublicPages.All;
        await using var db = await dbFactory.CreateDbContextAsync();
        var changed = await db.Users.Where(u => u.Id == userId)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.ProfilePrivate, isPrivate).SetProperty(x => x.PublicPages, pages));
        if (changed == 0)
            return new(false, "Ce compte n'existe plus.");
        return new(true, isPrivate ? "Ton profil est privé : personne ne peut le trouver ni le consulter."
            : pages == PublicPages.None ? "Ton profil est public, mais aucune page n'est visible."
            : $"Ton profil est public : {string.Join(", ", Pages.Where(p => pages.HasFlag(p.Page)).Select(p => p.Title))} visibles des autres joueurs.");
    }
}
