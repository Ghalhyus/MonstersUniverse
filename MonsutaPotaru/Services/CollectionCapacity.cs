using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MonsutaPotaru.Data;

namespace MonsutaPotaru.Services;

/// <summary>Occupation de la collection : Pokémon de la banque + Pokémon des sauvegardes stockées (œufs compris).</summary>
public readonly record struct CollectionUsage(int Used, int Limit)
{
    public int Free => Math.Max(0, Limit - Used);
    public bool IsFull => Used >= Limit;
}

/// <summary>
/// Taille maximale de la collection. Une opération qui la fait grossir (dépôt en banque, ajout ou mise à jour d'une
/// sauvegarde stockée) est refusée en entier si elle dépasse la limite ; ce qui la réduit n'est jamais bloqué, même
/// pour un compte déjà au-dessus. La limite s'agrandit par lots achetés avec le porte-monnaie.
/// </summary>
public static class CollectionCapacity
{
    public const int DefaultLimit = 1000;
    public const int SlotsPerLot = 50;
    public const long LotPrice = 10_000;

    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    public static string FormatCount(int count) => count.ToString("N0", French);

    public static string FormatUsage(CollectionUsage usage) => $"{FormatCount(usage.Used)} / {FormatCount(usage.Limit)}";

    /// <summary>
    /// Occupation actuelle, lue dans <paramref name="db"/>. Appelée dans la transaction de l'opération qui ajoute
    /// des Pokémon : SQLite n'autorise qu'une écriture à la fois, donc deux ajouts simultanés ne peuvent pas dépasser.
    /// </summary>
    public static async Task<CollectionUsage> GetUsageAsync(ApplicationDbContext db, string ownerId)
    {
        var used = await db.BankPokemon.CountAsync(p => p.OwnerId == ownerId);
        var limit = await db.Users.Where(u => u.Id == ownerId).Select(u => u.CollectionLimit).FirstOrDefaultAsync();
        return new CollectionUsage(used, limit);
    }

    /// <summary>L'opération fait passer la collection de <c>usage.Used</c> à <paramref name="after"/> Pokémon : refusée si elle grossit au-delà de la limite.</summary>
    public static bool Exceeds(CollectionUsage usage, int after) => after > usage.Limit && after > usage.Used;

    private const string Advice = "Agrandis-la depuis « Ma collection ».";

    private static string FreeText(CollectionUsage usage) => usage.Free switch
    {
        0 => "aucun emplacement libre",
        1 => "1 emplacement libre",
        var free => $"{FormatCount(free)} emplacements libres",
    };

    /// <summary>Dépôt en banque : <paramref name="adding"/> Pokémon de plus que l'état actuel (dépôts en attente compris).</summary>
    public static string BankFullMessage(CollectionUsage usage, int adding) =>
        $"Pas assez de place dans ta collection : {FormatCount(adding)} Pokémon à déposer, {FreeText(usage)} ({FormatUsage(usage)}). {Advice}";

    public static string SaveFullMessage(CollectionUsage usage, int saveCount, int previousCount = 0)
    {
        var missing = usage.Used - previousCount + saveCount - usage.Limit;
        var what = previousCount == 0
            ? $"Cette sauvegarde contient {FormatCount(saveCount)} Pokémon"
            : $"Cette version de la sauvegarde contient {FormatCount(saveCount)} Pokémon ({FormatCount(saveCount - previousCount)} de plus que la version stockée)";
        var room = usage.Free switch
        {
            0 => "n'a plus aucun emplacement libre",
            1 => "n'a plus qu'un emplacement libre",
            var free => $"n'a plus que {FormatCount(free)} emplacements libres",
        };
        return $"{what}, mais ta collection {room} ({FormatUsage(usage)}) : il manque {FormatCount(missing)} emplacement{(missing > 1 ? "s" : "")}. " +
               "Agrandis ta collection depuis « Ma collection ».";
    }
}
