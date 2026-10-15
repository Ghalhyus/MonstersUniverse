using PKHeX.Core;

namespace PokeBank.Services;

/// <summary>
/// Pokéballs du sac d'une sauvegarde, lues et modifiées en mémoire avec PKHeX (le fichier s'écrit ensuite avec
/// <see cref="SaveFile.Write"/>). Les Balls sont repérées par leur numéro d'objet, le même d'un jeu à l'autre à partir de
/// la 3e génération (une Super Ball retirée de Platine peut être déposée dans Noir 2), et cherchées dans toutes les poches :
/// la 4e génération a une poche « Balls », mais Noir 2 / Blanc 2 les range avec les objets.
/// </summary>
public static class SaveBag
{
    /// <summary>
    /// Numéros d'objets des Pokéballs : Master Ball à Mémoire Ball, Balls Noigrume de HGSS (Speed Ball à Parc Ball),
    /// Rêve Ball, Ultra Ball (Ultra-Chimères), Balls de Légendes Arceus et Étrange Ball. Les objets qui n'ont que « Ball »
    /// dans leur nom anglais (Balle Fer, Boule Fumée, Poké Ball donnée...) n'en font pas partie.
    /// </summary>
    public static readonly IReadOnlySet<int> BallItems = new HashSet<int>(
        [.. Enumerable.Range(1, 16), .. Enumerable.Range(492, 9), 576, 851, 1713, 1746, 1747, 1749, 1750, 1771, 1785]);

    /// <summary>Pourquoi le sac de cette sauvegarde n'est pas pris en charge ; null s'il l'est.</summary>
    public static string? Unsupported(SaveFile sav) =>
        sav.Generation < 3 ? "Les sacs des jeux de 1re et 2e génération ne sont pas pris en charge."
        : Capacity(sav).Count == 0 ? "Le sac de ce jeu ne peut pas contenir de Pokéballs."
        : null;

    /// <summary>Balls du sac et leur quantité (additionnée si une Ball occupe plusieurs emplacements).</summary>
    public static Dictionary<int, int> ReadBalls(SaveFile sav) =>
        sav.Generation < 3 ? [] : Count(Bag(sav.Inventory));

    /// <summary>
    /// Quantité maximale de chaque Ball que le sac peut contenir (999 en 4e et 5e génération) ; une Ball que le jeu ne connaît
    /// pas est absente (les Balls Noigrume de HGSS n'existent pas dans Noir 2...).
    /// </summary>
    public static Dictionary<int, int> Capacity(SaveFile sav)
    {
        if (sav.Generation < 3)
            return [];
        var bag = Bag(sav.Inventory);
        var capacity = new Dictionary<int, int>();
        foreach (var ball in BallItems)
        {
            if (PouchFor(bag, ball) is { } pouch)
                capacity[ball] = pouch.MaxCount;
        }
        return capacity;
    }

    /// <summary>Retire <paramref name="quantities"/> du sac. Message d'erreur, ou null si c'est fait (sac inchangé en cas d'erreur).</summary>
    public static string? Withdraw(SaveFile sav, IReadOnlyDictionary<int, int> quantities, Func<int, string> name)
    {
        if (Unsupported(sav) is { } why)
            return why;
        var pouches = sav.Inventory;
        var bag = Bag(pouches);
        var have = Count(bag);
        foreach (var (item, quantity) in quantities)
        {
            if (!BallItems.Contains(item))
                return $"{name(item)} n'est pas une Pokéball.";
            if (quantity > have.GetValueOrDefault(item))
                return $"Le sac ne contient que {have.GetValueOrDefault(item)} × {name(item)}.";
        }

        var emptied = new HashSet<InventoryPouch>();
        foreach (var (item, quantity) in quantities)
        {
            var left = quantity;
            foreach (var pouch in bag)
            {
                foreach (var slot in pouch.Items.Where(s => s.Index == item && s.Count > 0))
                {
                    var taken = Math.Min(left, slot.Count);
                    slot.Count -= taken;
                    left -= taken;
                    if (slot.Count != 0)
                        continue;
                    slot.Index = 0;
                    emptied.Add(pouch);
                }
            }
        }
        foreach (var pouch in emptied)
            pouch.ClearCount0(); // le jeu attend des emplacements sans trou : seules les poches vidées sont tassées
        sav.Inventory = pouches;
        return null;
    }

    /// <summary>Ajoute <paramref name="quantities"/> au sac. Message d'erreur, ou null si c'est fait (sac inchangé en cas d'erreur).</summary>
    public static string? Deposit(SaveFile sav, IReadOnlyDictionary<int, int> quantities, Func<int, string> name, string gameName)
    {
        if (Unsupported(sav) is { } why)
            return why;
        var pouches = sav.Inventory;
        var bag = Bag(pouches);
        var have = Count(bag);
        foreach (var (item, quantity) in quantities)
        {
            if (!BallItems.Contains(item))
                return $"{name(item)} n'est pas une Pokéball.";
            if (PouchFor(bag, item) is not { } pouch)
                return $"{name(item)} n'existe pas dans {gameName}.";
            var current = have.GetValueOrDefault(item);
            if (current + quantity > pouch.MaxCount)
                return $"{name(item)} : le sac en contient déjà {current} et en accepte {pouch.MaxCount} au plus " +
                       $"({pouch.MaxCount - current} de plus).";
        }

        foreach (var (item, quantity) in quantities)
        {
            if (PouchFor(bag, item)!.GiveItem(sav, (ushort)item, quantity) < 0)
                return $"Plus de place dans le sac pour {name(item)}.";
        }
        sav.Inventory = pouches;
        return null;
    }

    /// <summary>Poches du sac, sans le stockage d'objets du PC (3e génération) qui n'en fait pas partie.</summary>
    private static List<InventoryPouch> Bag(IReadOnlyList<InventoryPouch> pouches) =>
        pouches.Where(p => p.Type != InventoryType.PCItems).ToList();

    /// <summary>Poche où le jeu range cette Ball (« Balls » en 4e génération, « Objets » en 5e...), null s'il ne la connaît pas.</summary>
    private static InventoryPouch? PouchFor(List<InventoryPouch> bag, int item) =>
        bag.FirstOrDefault(p => p.CanContain((ushort)item));

    private static Dictionary<int, int> Count(IEnumerable<InventoryPouch> bag)
    {
        var counts = new Dictionary<int, int>();
        foreach (var slot in bag.SelectMany(p => p.Items))
        {
            if (BallItems.Contains(slot.Index) && slot.Count > 0)
                counts[slot.Index] = counts.GetValueOrDefault(slot.Index) + slot.Count;
        }
        return counts;
    }
}
