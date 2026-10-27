using Microsoft.EntityFrameworkCore;
using PKHeX.Core;
using MonsutaPotaru.Data;

namespace MonsutaPotaru.Services;

/// <summary>Pokémon du parc, avec ce qu'il faut pour l'animer (sprites Donjon Mystère, famille, caractère).</summary>
/// <param name="SpriteFolder">Dossier de ses sprites Donjon Mystère (« images/donjon/0025/0000/0001/ » : chromatique).</param>
/// <param name="Family">Espèce de base de sa famille d'évolution (Pikachu et Raichu : Pichu), pour les affinités.</param>
/// <param name="Nature">Nature (index PKHeX), qui donne son caractère : plus actif, plus dormeur...</param>
public sealed record ParkPokemon(int Id, ushort Species, string SpeciesName, string Nickname, byte Level, bool IsShiny,
    string SpriteFolder, int Family, byte Nature);

/// <summary>Pokémon proposé dans la fenêtre « Choisir les Pokémon » (sans les œufs).</summary>
/// <param name="HasSprites">SpriteCollab a ses sprites Donjon Mystère : sinon, grisé (64 espèces récentes n'en ont pas encore).</param>
public sealed record ParkCandidate(int Id, ushort Species, string SpeciesName, string Nickname, byte Level, bool IsShiny,
    string SpriteUrl, int? StoredSaveId, bool InPark, bool HasSprites);

public sealed record ParkResult(bool Ok, string Message);

/// <summary>
/// « Parc » : jusqu'à <see cref="Capacity"/> Pokémon de la collection (stockage interne ou sauvegardes stockées), de toutes
/// les générations, qui s'y promènent avec les sprites Donjon Mystère de leur forme (animation dans le navigateur :
/// wwwroot/js/parc.js). Les espèces dont SpriteCollab n'a pas encore les sprites ne peuvent pas y aller.
/// </summary>
public sealed class ParkService(IDbContextFactory<ApplicationDbContext> dbFactory, ILogger<ParkService> logger)
{
    public const int Capacity = 30;

    /// <summary>Pokémon du parc, dans l'ordre de leur arrivée.</summary>
    public async Task<List<ParkPokemon>> ListAsync(string ownerId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var members = await db.ParkMembers.AsNoTracking()
            .Where(m => m.OwnerId == ownerId)
            .OrderBy(m => m.AddedAt).ThenBy(m => m.BankPokemonId)
            .Select(m => m.BankPokemon!)
            .Select(p => new { p.Id, p.Species, p.Form, p.Context, p.SpeciesName, p.Nickname, p.Level, p.IsShiny, p.Gender, p.HeldItem, p.Nature })
            .ToListAsync();
        return members.Select(p => new ParkPokemon(p.Id, p.Species, p.SpeciesName, p.Nickname, p.Level, p.IsShiny,
            PokemonSprites.DonjonFolder(p.Species, p.Form, (EntityContext)p.Context, p.IsShiny, p.Gender == 1, p.HeldItem ?? 0),
            Family(p.Species), p.Nature ?? 0)).ToList();
    }

    /// <summary>
    /// Fiche d'un Pokémon du parc de ce compte (clic sur le Pokémon), ou null s'il n'est pas dans ce parc. Le numéro vient
    /// du navigateur : un visiteur ne doit pas pouvoir lire ainsi le reste de la collection du joueur.
    /// </summary>
    public async Task<BankPokemon?> GetMemberAsync(string ownerId, int bankPokemonId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.ParkMembers.AsNoTracking()
            .Where(m => m.OwnerId == ownerId && m.BankPokemonId == bankPokemonId)
            .Select(m => m.BankPokemon!)
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// Pokémon qui peuvent aller au parc (sans les œufs), filtrés comme dans « Ma collection » (pastilles), par numéro de
    /// Pokédex.
    /// </summary>
    public async Task<List<ParkCandidate>> ListCandidatesAsync(string ownerId, PokemonCriteria? criteria = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var inPark = db.ParkMembers.Where(m => m.OwnerId == ownerId).Select(m => m.BankPokemonId);
        var query = Eligible(db, ownerId);
        if (criteria is not null)
            query = criteria.Apply(query);
        var candidates = await query
            .OrderBy(p => p.Species).ThenByDescending(p => p.Level).ThenBy(p => p.Id)
            .Select(p => new { p.Id, p.Species, p.Form, p.Context, p.SpeciesName, p.Nickname, p.Level, p.IsShiny, p.Gender, p.HeldItem,
                p.StoredSaveId, InPark = inPark.Contains(p.Id) })
            .ToListAsync();
        return candidates.Select(p => new ParkCandidate(p.Id, p.Species, p.SpeciesName, p.Nickname, p.Level, p.IsShiny,
            PokemonSprites.Url(p.Species, p.Form, (EntityContext)p.Context, p.Gender == 1, p.HeldItem ?? 0, p.IsShiny),
            p.StoredSaveId, p.InPark, PokemonSprites.HasDonjonSprites(p.Species))).ToList();
    }

    /// <summary>
    /// Remplace les Pokémon du parc par <paramref name="ids"/> (choix de la fenêtre « Ajouter des Pokémon ») : ceux qui y
    /// restent gardent leur place, les nouveaux arrivent après.
    /// </summary>
    public async Task<ParkResult> SetAsync(string ownerId, IReadOnlyCollection<int> ids)
    {
        var wanted = ids.ToHashSet();
        if (wanted.Count > Capacity)
            return new(false, $"Le parc accueille {Capacity} Pokémon au plus : retires-en {wanted.Count - Capacity}.");

        await using var db = await dbFactory.CreateDbContextAsync();
        var eligible = await Eligible(db, ownerId).Where(p => wanted.Contains(p.Id)).Select(p => new { p.Id, p.Species, p.SpeciesName }).ToListAsync();
        if (eligible.Count != wanted.Count)
            return new(false, "Seuls tes Pokémon (pas les œufs) peuvent aller au parc.");
        if (eligible.FirstOrDefault(p => !PokemonSprites.HasDonjonSprites(p.Species)) is { } withoutSprites)
            return new(false, $"{withoutSprites.SpeciesName} n'a pas encore de sprites Donjon Mystère : il ne peut pas aller au parc.");

        await using var tx = await db.Database.BeginTransactionAsync();
        await db.ParkMembers.Where(m => m.OwnerId == ownerId && !wanted.Contains(m.BankPokemonId)).ExecuteDeleteAsync();
        var present = await db.ParkMembers.Where(m => m.OwnerId == ownerId).Select(m => m.BankPokemonId).ToListAsync();
        var now = DateTime.UtcNow;
        db.ParkMembers.AddRange(wanted.Except(present).Select(id => new ParkMember { BankPokemonId = id, OwnerId = ownerId, AddedAt = now }));
        await db.SaveChangesAsync();
        await tx.CommitAsync();

        logger.LogInformation("Utilisateur {User} : parc de {Count} Pokémon.", ownerId, wanted.Count);
        return new(true, wanted.Count == 0 ? "Le parc est vide." : $"Parc mis à jour : {wanted.Count} Pokémon.");
    }

    /// <summary>Retire un Pokémon du parc (il reste dans la collection).</summary>
    public async Task<bool> RemoveAsync(string ownerId, int bankPokemonId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.ParkMembers.Where(m => m.OwnerId == ownerId && m.BankPokemonId == bankPokemonId).ExecuteDeleteAsync() > 0;
    }

    private static IQueryable<BankPokemon> Eligible(ApplicationDbContext db, string ownerId) =>
        db.BankPokemon.AsNoTracking().Where(p => p.OwnerId == ownerId && !p.IsEgg && p.Species >= 1);

    /// <summary>Espèce de base de la famille d'évolution (celle qui sort de l'œuf) : Pikachu, Raichu → Pichu.</summary>
    public static int Family(ushort species) => PersonalTable.SV.GetFormEntry(species, 0).HatchSpecies;
}
