using PKHeX.Core;

namespace PokeBank.Services;

/// <summary>Catégories d'objets du sac que le site sait transférer.</summary>
public enum ItemCategory { Balls, Medicine }

/// <summary>
/// Objets du sac d'une sauvegarde (Pokéballs et médicaments), lus et modifiés en mémoire avec PKHeX (le fichier s'écrit
/// ensuite avec <see cref="SaveFile.Write"/>). Les objets sont repérés par leur numéro PKHeX de 4e génération et suivantes,
/// le même d'un jeu à l'autre (une Super Ball retirée de Platine peut être déposée dans Noir 2) ; les jeux de 3e génération,
/// qui numérotent leurs objets autrement, sont convertis. Ils sont cherchés dans toutes les poches du sac : les Balls ont leur
/// poche en 4e génération mais sont rangées avec les objets dans Noir 2, les médicaments sont avec les objets en 3e génération.
/// </summary>
public static class SaveBag
{
    /// <summary>
    /// Pokéballs : Master Ball à Mémoire Ball, Balls Noigrume de HGSS (Speed Ball à Parc Ball), Rêve Ball, Ultra Ball
    /// (Ultra-Chimères), Balls de Légendes Arceus et Étrange Ball. Les objets qui n'ont que « Ball » dans leur nom anglais
    /// (Balle Fer, Boule Fumée, Poké Ball donnée...) n'en font pas partie.
    /// </summary>
    public static readonly IReadOnlySet<int> BallItems = new HashSet<int>(
        [.. Enumerable.Range(1, 16), .. Enumerable.Range(492, 9), 576, 851, 1713, 1746, 1747, 1749, 1750, 1771, 1785]);

    /// <summary>
    /// Médicaments : tout ce que les jeux de la 4e à la 9e génération rangent dans leur poche « Médicaments » (listes PKHeX) :
    /// Potion à Vieux Gâteau (soins, statuts, Rappel, Huile/Élixir, boissons, Poudres/Herbes, vitamines, PP Plus/Max, Super Bonbon),
    /// Flûtes Bleue/Jaune/Rouge, Chococœur, Bonbon Rage, Plumes, Jolie Plume, Glace Volute, Pilule Talent, Galette Illumis,
    /// Sablé Yantreizh, Malasada Maxi, Crok'Argenta et Maxi Miel.
    /// </summary>
    public static readonly IReadOnlySet<int> MedicineItems = new HashSet<int>(
        [.. Enumerable.Range(17, 38), 65, 66, 67, 134, 504, .. Enumerable.Range(565, 7), 591, 645, 708, 709, 852, 903, 1579]);

    /// <summary>Catégorie d'un objet (numéro de 4e génération et suivantes), null si le site ne le transfère pas.</summary>
    public static ItemCategory? CategoryOf(int item) =>
        BallItems.Contains(item) ? ItemCategory.Balls
        : MedicineItems.Contains(item) ? ItemCategory.Medicine
        : null;

    /// <summary>Pourquoi le sac de cette sauvegarde n'est pas pris en charge ; null s'il l'est.</summary>
    public static string? Unsupported(SaveFile sav) =>
        sav.Generation < 3 ? "Les sacs des jeux de 1re et 2e génération ne sont pas pris en charge."
        : Capacity(sav).Count == 0 ? "Le sac de ce jeu ne peut contenir aucune Pokéball ni aucun médicament."
        : null;

    /// <summary>Pokéballs et médicaments du sac, et leur quantité (additionnée si un objet occupe plusieurs emplacements).</summary>
    public static Dictionary<int, int> ReadItems(SaveFile sav) =>
        sav.Generation < 3 ? [] : Count(sav, Bag(sav.Inventory));

    /// <summary>
    /// Quantité maximale de chaque objet que le sac peut contenir (999 en 4e et 5e génération, 99 dans la poche Objets de la 3e) ;
    /// un objet que le jeu ne connaît pas est absent (les Balls Noigrume de HGSS n'existent pas dans Platine...).
    /// </summary>
    public static Dictionary<int, int> Capacity(SaveFile sav)
    {
        if (sav.Generation < 3)
            return [];
        var bag = Bag(sav.Inventory);
        var capacity = new Dictionary<int, int>();
        foreach (var item in BallItems.Concat(MedicineItems))
        {
            if (PouchFor(sav, bag, item) is { } pouch)
                capacity[item] = pouch.MaxCount;
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
        var have = Count(sav, bag);
        foreach (var (item, quantity) in quantities)
        {
            if (CategoryOf(item) is null)
                return $"{name(item)} n'est ni une Pokéball ni un médicament.";
            if (quantity > have.GetValueOrDefault(item))
                return $"Le sac ne contient que {have.GetValueOrDefault(item)} × {name(item)}.";
        }

        var emptied = new HashSet<InventoryPouch>();
        foreach (var (item, quantity) in quantities)
        {
            var left = quantity;
            foreach (var pouch in bag)
            {
                foreach (var slot in pouch.Items.Where(s => s.Count > 0 && ToNational(sav, s.Index) == item))
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
        var have = Count(sav, bag);
        foreach (var (item, quantity) in quantities)
        {
            if (CategoryOf(item) is null)
                return $"{name(item)} n'est ni une Pokéball ni un médicament.";
            if (PouchFor(sav, bag, item) is not { } pouch)
                return $"{name(item)} n'existe pas dans {gameName}.";
            var current = have.GetValueOrDefault(item);
            if (current + quantity > pouch.MaxCount)
                return $"{name(item)} : le sac en contient déjà {current} et en accepte {pouch.MaxCount} au plus " +
                       $"({pouch.MaxCount - current} de plus).";
        }

        foreach (var (item, quantity) in quantities)
        {
            if (PouchFor(sav, bag, item)!.GiveItem(sav, ToGame(sav, item), quantity) < 0)
                return $"Plus de place dans le sac pour {name(item)}.";
        }
        sav.Inventory = pouches;
        return null;
    }

    /// <summary>Poches du sac, sans le stockage d'objets du PC (3e génération) qui n'en fait pas partie.</summary>
    private static List<InventoryPouch> Bag(IReadOnlyList<InventoryPouch> pouches) =>
        pouches.Where(p => p.Type != InventoryType.PCItems).ToList();

    /// <summary>Poche où le jeu range cet objet (« Balls » en 4e génération, « Objets » en 5e...), null s'il ne le connaît pas.</summary>
    private static InventoryPouch? PouchFor(SaveFile sav, List<InventoryPouch> bag, int item)
    {
        var index = ToGame(sav, item);
        return index == 0 ? null : bag.FirstOrDefault(p => p.CanContain(index));
    }

    /// <summary>Numéro de l'objet dans le jeu : celui de la 4e génération, sauf en 3e génération (0 si l'objet n'y existe pas).</summary>
    private static ushort ToGame(SaveFile sav, int item) =>
        sav.Generation == 3 ? ItemConverter.GetItemOld3((ushort)item) : (ushort)item;

    /// <summary>Numéro de 4e génération et suivantes d'un objet lu dans le sac.</summary>
    private static int ToNational(SaveFile sav, int index) =>
        sav.Generation == 3 ? ItemConverter.GetItemFuture3((ushort)index) : index;

    private static Dictionary<int, int> Count(SaveFile sav, IEnumerable<InventoryPouch> bag)
    {
        var counts = new Dictionary<int, int>();
        foreach (var slot in bag.SelectMany(p => p.Items))
        {
            if (slot.Index == 0 || slot.Count <= 0)
                continue;
            var item = ToNational(sav, slot.Index);
            if (CategoryOf(item) is not null)
                counts[item] = counts.GetValueOrDefault(item) + slot.Count;
        }
        return counts;
    }
}
