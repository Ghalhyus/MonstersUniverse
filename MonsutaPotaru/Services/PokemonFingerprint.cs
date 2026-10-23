using Microsoft.EntityFrameworkCore;
using PKHeX.Core;
using MonsutaPotaru.Data;

namespace MonsutaPotaru.Services;

/// <summary>Autre exemplaire d'un Pokémon, déjà dans le compte : où il est, pour le nommer dans un avertissement.</summary>
/// <param name="StoredSaveLabel">« Rubis Oméga — Ghal », ou null pour le stockage interne.</param>
public sealed record DuplicateCopy(string Fingerprint, string SpeciesName, byte Level, bool IsEgg, string? StoredSaveLabel)
{
    /// <summary>« Pikachu N.20 (stockage interne) », « Œuf (Noire — Ghal) ».</summary>
    public string Describe() => $"{(IsEgg ? "Œuf" : $"{SpeciesName} N.{Level}")} ({StoredSaveLabel ?? "stockage interne"})";
}

/// <summary>
/// Empreinte d'un Pokémon, pour reconnaître le même Pokémon à deux endroits du compte : celui déposé dans le stockage
/// interne qui réapparaît dans une vieille copie de sa sauvegarde, stockée ensuite ou rechargée dans « Transfert ».
/// Sans l'espèce, qui change à l'évolution (la vieille copie peut avoir un Pichu, le stockage le Pikachu qu'il est devenu).
/// <list type="bullet">
/// <item>3e génération et suivantes : PID et ID dresseur complet (ID + ID secret), qui ne changent jamais, même d'un jeu
/// à l'autre.</item>
/// <item>1re et 2e génération (sans PID) : IV, ID dresseur, nom du dresseur et famille d'évolution (environ 1 risque sur
/// 65 000 de confondre deux Pokémon du même dresseur et de la même famille).</item>
/// </list>
/// Une sauvegarde dont un Pokémon est déjà ailleurs dans le compte est refusée (« Mes sauvegardes », 3DS, chargement dans
/// « Transfert ») : il faut envoyer la sauvegarde actuelle du jeu. Revers accepté : deux Pokémon d'événement à PID fixe
/// (même distribution) ou d'échange en jeu, et un Pokémon échangé entre deux jeux dont l'un est stocké dans une version
/// plus ancienne, sont pris pour des copies.
/// </summary>
public static class PokemonFingerprint
{
    /// <summary>Plus grand nombre de Pokémon nommés dans un refus (« ... et 3 autres »).</summary>
    public const int MaxNamed = 5;

    public static string Of(PKM pk) => pk is GBPKM gb
        ? $"g12:{gb.DV16:X4}:{gb.TID16}:{gb.OriginalTrainerName}:{ParkService.Family(gb.Species)}"
        : $"pid:{pk.PID:X8}:{pk.ID32:X8}";

    /// <summary>
    /// Pokémon du compte qui ont l'une de ces empreintes : stockage interne et sauvegardes stockées, sauf
    /// <paramref name="exceptSaveId"/> (la sauvegarde qu'on met à jour), et seulement le stockage interne si
    /// <paramref name="internalOnly"/>. Jamais ceux des autres comptes.
    /// </summary>
    public static async Task<List<DuplicateCopy>> FindAsync(ApplicationDbContext db, string ownerId, IEnumerable<string> fingerprints,
        int? exceptSaveId = null, bool internalOnly = false)
    {
        var wanted = fingerprints.Distinct().ToList();
        if (wanted.Count == 0)
            return [];
        var query = db.BankPokemon.AsNoTracking().Where(p => p.OwnerId == ownerId && wanted.Contains(p.Fingerprint!));
        if (internalOnly)
            query = query.Where(p => p.StoredSaveId == null);
        else if (exceptSaveId is { } id)
            query = query.Where(p => p.StoredSaveId == null || p.StoredSaveId != id);
        var rows = await query
            .OrderBy(p => p.StoredSaveId != null).ThenBy(p => p.Id) // le stockage interne d'abord
            .Select(p => new
            {
                p.Fingerprint, p.SpeciesName, p.Level, p.IsEgg,
                Label = p.StoredSave == null ? null : p.StoredSave.GameName + " — " + p.StoredSave.TrainerName,
            })
            .ToListAsync();
        // Un exemplaire par empreinte : un Pokémon de la sauvegarde = un doublon, même s'il est à plusieurs endroits.
        return rows.DistinctBy(r => r.Fingerprint)
            .Select(r => new DuplicateCopy(r.Fingerprint!, r.SpeciesName, r.Level, r.IsEgg, r.Label))
            .ToList();
    }

    /// <summary>
    /// « Sauvegarde refusée : 2 Pokémon de cette sauvegarde sont déjà ailleurs dans ton compte : Pikachu N.20 (stockage
    /// interne), Évoli N.5 (Noire — Ghal). C'est sans doute une vieille copie : <paramref name="advice"/> » ; null s'il n'y
    /// en a pas.
    /// </summary>
    public static string? RefusalMessage(IReadOnlyList<DuplicateCopy> copies, string advice)
    {
        if (copies.Count == 0)
            return null;
        var names = string.Join(", ", copies.Take(MaxNamed).Select(c => c.Describe()));
        var more = copies.Count > MaxNamed ? $" et {copies.Count - MaxNamed} autre{(copies.Count - MaxNamed > 1 ? "s" : "")}" : "";
        var count = copies.Count == 1
            ? "1 Pokémon de cette sauvegarde est déjà ailleurs dans ton compte"
            : $"{copies.Count} Pokémon de cette sauvegarde sont déjà ailleurs dans ton compte";
        return $"Sauvegarde refusée : {count} : {names}{more}. C'est sans doute une vieille copie : {advice}";
    }

    /// <summary>Conseil d'un refus dans « Mes sauvegardes » et pour la 3DS.</summary>
    public const string StoreAdvice = "envoie la sauvegarde actuelle de ton jeu.";

    /// <summary>Conseil d'un refus au chargement dans « Transfert ».</summary>
    public const string TransferAdvice = "charge la sauvegarde actuelle de ton jeu.";

    /// <summary>Remplit l'empreinte des Pokémon enregistrés avant son ajout (au démarrage, repère : Fingerprint null).</summary>
    public static async Task FillAsync(ApplicationDbContext db)
    {
        while (true)
        {
            var batch = await db.BankPokemon.Where(p => p.Fingerprint == null).OrderBy(p => p.Id).Take(500).ToListAsync();
            if (batch.Count == 0)
                return;
            foreach (var entry in batch)
                entry.Fingerprint = BankService.ToPkm(entry) is { } pk ? Of(pk) : ""; // octets illisibles : pas d'empreinte, ne pas les relire
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
        }
    }
}
