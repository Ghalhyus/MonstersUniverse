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

/// <param name="Expansions">Agrandissements achetés, après l'achat (0 à <see cref="ParkService.MaxExpansions"/>).</param>
public sealed record ParkExpansionResult(bool Ok, string Message, int Expansions = 0, long Balance = 0);

/// <summary>
/// « Parc » : jusqu'à <see cref="CapacityOf"/> Pokémon de la collection (stockage interne ou sauvegardes stockées), de toutes
/// les générations, qui s'y promènent avec les sprites Donjon Mystère de leur forme (animation dans le navigateur :
/// wwwroot/js/parc.js). Les espèces dont SpriteCollab n'a pas encore les sprites ne peuvent pas y aller. Le parc s'agrandit
/// en payant avec le porte-monnaie (<see cref="ExpandAsync"/>) : chaque taille a sa carte (wwwroot/parc/parc-1-{taille}.json).
/// </summary>
public sealed class ParkService(IDbContextFactory<ApplicationDbContext> dbFactory, ILogger<ParkService> logger)
{
    /// <summary>Pokémon accueillis sans agrandissement.</summary>
    public const int BaseCapacity = 25;
    /// <summary>Pokémon de plus à chaque agrandissement.</summary>
    public const int ExpansionCapacity = 25;
    /// <summary>Agrandissements possibles pour l'instant (100 Pokémon) : une carte par taille, de 1 à MaxExpansions + 1.</summary>
    public const int MaxExpansions = 3;
    public const long ExpansionPrice = 50_000;

    public static int CapacityOf(int expansions) => BaseCapacity + ExpansionCapacity * expansions;

    /// <summary>Agrandissements achetés par ce joueur (0 sans agrandissement).</summary>
    public async Task<int> GetExpansionsAsync(string ownerId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Users.Where(u => u.Id == ownerId).Select(u => u.ParkExpansions).FirstOrDefaultAsync();
    }

    /// <summary>
    /// Agrandit le parc de <see cref="ExpansionCapacity"/> Pokémon pour <see cref="ExpansionPrice"/> : taille, débit et ligne
    /// du journal de l'inventaire en une seule opération. La taille et le solde sont vérifiés par les mises à jour
    /// elles-mêmes : deux achats simultanés ne dépassent ni la plus grande taille ni le solde.
    /// </summary>
    public async Task<ParkExpansionResult> ExpandAsync(string ownerId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        var expanded = await db.Users.Where(u => u.Id == ownerId && u.ParkExpansions < MaxExpansions)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.ParkExpansions, x => x.ParkExpansions + 1));
        if (expanded == 0)
            return new(false, $"Ton parc a déjà sa plus grande taille ({CapacityOf(MaxExpansions)} Pokémon).", MaxExpansions);

        // Débit conditionnel et atomique ; sans assez d'argent, la transaction (taille comprise) est annulée
        var debited = await db.Wallets.Where(w => w.OwnerId == ownerId && w.Money >= ExpansionPrice)
            .ExecuteUpdateAsync(w => w.SetProperty(x => x.Money, x => x.Money - ExpansionPrice));
        if (debited == 0)
        {
            await tx.RollbackAsync();
            var available = await db.Wallets.Where(w => w.OwnerId == ownerId).Select(w => w.Money).FirstOrDefaultAsync();
            return new(false, $"L'agrandissement coûte {InventoryService.FormatMoney(ExpansionPrice)}, ton porte-monnaie contient " +
                              $"{InventoryService.FormatMoney(available)} : il manque {InventoryService.FormatMoney(ExpansionPrice - available)}. " +
                              "Retire de l'argent d'une sauvegarde depuis « Inventaire ».", Balance: available);
        }

        var expansions = await db.Users.Where(u => u.Id == ownerId).Select(u => u.ParkExpansions).FirstAsync();
        var balance = await db.Wallets.Where(w => w.OwnerId == ownerId).Select(w => w.Money).FirstAsync();
        db.WalletOperations.Add(new WalletOperation
        {
            OwnerId = ownerId,
            At = DateTime.UtcNow,
            Kind = WalletOperationKind.ExpandPark,
            Amount = -ExpansionPrice,
            BalanceAfter = balance,
            Details = $"Parc : {CapacityOf(expansions - 1)} → {CapacityOf(expansions)} Pokémon",
        });
        await db.SaveChangesAsync();
        await tx.CommitAsync();

        logger.LogInformation("Utilisateur {User} : parc agrandi ({Expansions} agrandissements).", ownerId, expansions);
        return new(true, $"Parc agrandi : il accueille maintenant {CapacityOf(expansions)} Pokémon. " +
                         $"Solde : {InventoryService.FormatMoney(balance)}.", expansions, balance);
    }

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
    /// restent gardent leur place, les nouveaux arrivent après. Un parc qui a plus de Pokémon que sa capacité (30 avant les
    /// agrandissements, 25 depuis) les garde : on peut en retirer, pas en ajouter tant qu'il dépasse.
    /// </summary>
    public async Task<ParkResult> SetAsync(string ownerId, IReadOnlyCollection<int> ids)
    {
        var wanted = ids.ToHashSet();
        await using var db = await dbFactory.CreateDbContextAsync();
        var capacity = CapacityOf(await db.Users.Where(u => u.Id == ownerId).Select(u => u.ParkExpansions).FirstOrDefaultAsync());
        if (wanted.Count > capacity)
        {
            var current = await db.ParkMembers.Where(m => m.OwnerId == ownerId).Select(m => m.BankPokemonId).ToListAsync();
            if (!wanted.IsSubsetOf(current))
                return new(false, $"Le parc accueille {capacity} Pokémon au plus : retires-en {wanted.Count - capacity}.");
        }

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

    /// <summary>Vide le parc (bouton « Vider le parc ») : ses Pokémon restent dans la collection.</summary>
    public async Task<ParkResult> ClearAsync(string ownerId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var count = await db.ParkMembers.Where(m => m.OwnerId == ownerId).ExecuteDeleteAsync();
        if (count == 0)
            return new(false, "Le parc est déjà vide.");
        logger.LogInformation("Utilisateur {User} : parc vidé ({Count} Pokémon).", ownerId, count);
        return new(true, count == 1
            ? "Le parc est vide : son Pokémon reste dans ta collection."
            : $"Le parc est vide : ses {count} Pokémon restent dans ta collection.");
    }

    private static IQueryable<BankPokemon> Eligible(ApplicationDbContext db, string ownerId) =>
        db.BankPokemon.AsNoTracking().Where(p => p.OwnerId == ownerId && !p.IsEgg && p.Species >= 1);

    /// <summary>Espèce de base de la famille d'évolution (celle qui sort de l'œuf) : Pikachu, Raichu → Pichu.</summary>
    public static int Family(ushort species) => PersonalTable.SV.GetFormEntry(species, 0).HatchSpecies;
}
