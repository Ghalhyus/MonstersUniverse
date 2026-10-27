using PKHeX.Core;

namespace MonsutaPotaru.Services;

/// <summary>Catégories d'objets du sac que le site sait transférer.</summary>
public enum ItemCategory { Items, Balls, Medicine, Berries, Machines }

/// <summary>
/// Où sont rangés les objets d'une sauvegarde : le sac, ou le PC d'objets des jeux de la 1re à la 3e génération (voir
/// <see cref="SaveBag.HasPc"/>).
/// </summary>
public enum BagPlace { Bag, Pc }

/// <summary>
/// Objets du sac et du PC d'une sauvegarde (objets, Pokéballs, médicaments, baies, CT et DT), lus et modifiés en mémoire avec PKHeX (le fichier s'écrit
/// ensuite avec <see cref="SaveFile.Write"/>). Chaque objet est repéré par une clé, la même d'un jeu à l'autre :
/// <list type="bullet">
/// <item>objets, Pokéballs, médicaments et baies : leur numéro PKHeX de 4e génération et suivantes (une Super Ball retirée de Platine peut être
/// déposée dans Noir 2) ; les jeux de la 1re à la 3e génération, qui numérotent leurs objets autrement, sont convertis
/// (<see cref="ToNational"/>) ;</item>
/// <item>CT et DT : la capacité qu'elles apprennent (<see cref="MachineKey"/>), car un même numéro de CT n'apprend pas la même
/// capacité d'une génération à l'autre (CT01 : Mitra-Poing en Platine, Affûtage en Noir 2). Une CT Séisme retirée de Platine
/// redevient la CT ou la DT qui apprend Séisme dans le jeu où elle est déposée. Les CS ne sont jamais lues ni modifiées : les
/// retirer pourrait bloquer la progression du joueur.</item>
/// </list>
/// Les objets sont cherchés dans toutes les poches du sac : les Balls ont leur poche en 4e génération mais sont rangées avec les
/// objets dans Noir 2, les médicaments sont avec les objets en 3e génération, les baies de Let's Go avec les Balls, et Rouge/Bleu
/// n'ont qu'une poche. Seuls le sac et le PC d'objets sont concernés : les baies plantées ne sont pas touchées. Les objets rares,
/// les Lettres et les Cristaux Z ne sont jamais lus ni modifiés (voir <see cref="ItemPouches"/> et <see cref="IsExcluded"/>).
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

    /// <summary>
    /// Baies : tout ce que les jeux de la 3e à la 9e génération rangent dans leur poche « Baies » (Baie Ceriz à Baie Rangma,
    /// Extra Baies de Légendes Z-A), plus les baies de capture de Let's Go (Framby, Nanab et Nanana argentées et dorées) et la
    /// Baie Hopo de Légendes Arceus. Pas le Jus de Baie (médicament), ni la Baie Pêcha Mythique (objet rare d'Écarlate/Violet).
    /// Les baies d'Or/Argent/Cristal deviennent leur équivalent (Baie → Baie Oran, Baie Mystère → Baie Mepo...).
    /// </summary>
    public static readonly IReadOnlySet<int> BerryItems = new HashSet<int>(
        [.. Enumerable.Range(149, 64), 686, 687, 688, .. Enumerable.Range(861, 6), 1754, .. Enumerable.Range(2651, 33)]);

    /// <summary>
    /// Baie Enigma : en 3e génération, elle vient de l'e-Reader et son nom et son effet sont enregistrés dans la sauvegarde ;
    /// elle n'y est donc ni retirée ni déposée (une baie normale à partir de la 4e génération).
    /// </summary>
    public const int EnigmaBerry = 208;

    /// <summary>
    /// Lettres (4e génération et suivantes ; celles de la 2e et de la 3e n'ont pas d'équivalent) : leur message est enregistré à
    /// part dans la sauvegarde, elles ne sont donc jamais transférées, même quand le jeu les range avec les objets (5e génération).
    /// </summary>
    public static readonly IReadOnlySet<int> MailItems = new HashSet<int>(Enumerable.Range(137, 12));

    /// <summary>
    /// Poches d'où viennent les objets ordinaires (catégorie <see cref="ItemCategory.Items"/>) : Objets, Objets de combat,
    /// Trésors, Ingrédients, Bonbons, Méga-Gemmes, et le PC d'objets (1re à 3e génération). Les autres catégories sont
    /// reconnues à leur numéro, dans toutes les poches sauf <see cref="UntouchedPouches"/>.
    /// </summary>
    public static readonly IReadOnlySet<InventoryType> ItemPouches = new HashSet<InventoryType>(
        [InventoryType.Items, InventoryType.BattleItems, InventoryType.Treasure, InventoryType.Ingredients, InventoryType.Candy,
         InventoryType.MegaStones, InventoryType.PCItems]);

    /// <summary>
    /// Poches du sac jamais lues ni modifiées : objets rares (les retirer pourrait bloquer la progression), Lettres et Cristaux Z.
    /// Le PC d'objets n'est pas une poche du sac : il n'est lu et modifié que comme <see cref="BagPlace.Pc"/>.
    /// </summary>
    private static readonly IReadOnlySet<InventoryType> UntouchedPouches = new HashSet<InventoryType>(
        [InventoryType.KeyItems, InventoryType.MailItems, InventoryType.ZCrystals, InventoryType.PCItems]);

    /// <summary>Début des clés des CT et DT : <c>MachineKeyBase + numéro de la capacité</c>, au-delà de tout numéro d'objet.</summary>
    public const int MachineKeyBase = 100_000;

    /// <summary>
    /// Seuil des CT à usage unique (1re à 4e génération, Diamant Étincelant/Perle Scintillante, Écarlate/Violet ; pas les DT
    /// d'Épée/Bouclier) : une sauvegarde qui en a au moins autant d'une même CT (sac et PC additionnés) la garde pour elle. Cette
    /// CT y est cachée, ni retirée ni déposée (<see cref="LockedMachines"/>), et un dépôt ne peut pas l'amener jusque-là (79 au plus).
    /// </summary>
    public const int LimitedMachineThreshold = 80;

    /// <summary>Clé d'une CT ou d'une DT : la capacité qu'elle apprend.</summary>
    public static int MachineKey(ushort move) => MachineKeyBase + move;

    /// <summary>Capacité apprise par une clé de CT ou de DT ; null pour une autre clé.</summary>
    public static ushort? MoveOf(int key) =>
        key > MachineKeyBase && key - MachineKeyBase <= ushort.MaxValue ? (ushort)(key - MachineKeyBase) : null;

    /// <summary>
    /// Catégorie d'une clé d'objet (voir <see cref="SaveBag"/>), null si le site ne la transfère jamais (Lettres). Ce qui n'est
    /// ni une Pokéball, ni un médicament, ni une baie, ni une CT est un objet ordinaire ; c'est la poche où chaque jeu le range
    /// qui dit s'il y est transférable (<see cref="ItemPouches"/>).
    /// </summary>
    public static ItemCategory? CategoryOf(int key) =>
        BallItems.Contains(key) ? ItemCategory.Balls
        : MedicineItems.Contains(key) ? ItemCategory.Medicine
        : BerryItems.Contains(key) ? ItemCategory.Berries
        : MoveOf(key) is not null ? ItemCategory.Machines
        : key is > 0 and < MachineKeyBase && !MailItems.Contains(key) ? ItemCategory.Items
        : null;

    /// <summary>La sauvegarde a un PC d'objets, à côté du sac (1re à 3e génération).</summary>
    public static bool HasPc(SaveFile sav) => sav.Generation <= 3 && sav.Inventory.Pouches.Any(p => p.Type == InventoryType.PCItems);

    /// <summary>« le sac », « le PC ».</summary>
    public static string PlaceName(BagPlace place) => place == BagPlace.Pc ? "le PC" : "le sac";

    /// <summary>Pourquoi le sac de cette sauvegarde n'est pas pris en charge ; null s'il l'est.</summary>
    public static string? Unsupported(SaveFile sav) =>
        Capacity(sav).Count == 0 ? "Le sac de ce jeu ne peut contenir aucun objet que le site sait transférer." : null;

    /// <summary>
    /// Objets transférables de la sauvegarde, sac et PC additionnés (objets ordinaires, Pokéballs, médicaments, baies, CT et DT ;
    /// jamais les objets rares, les Lettres ni les CS) par clé, et leur quantité (additionnée si un objet occupe plusieurs
    /// emplacements). Les CT bloquées (<see cref="LockedMachines"/>) n'y sont pas.
    /// </summary>
    public static Dictionary<int, int> ReadItems(SaveFile sav)
    {
        var total = Total(Counts(sav, sav.Inventory.Pouches));
        return Visible(total, Locked(sav, total));
    }

    /// <summary>Comme <see cref="ReadItems(SaveFile)"/>, pour le sac ou pour le PC seulement.</summary>
    public static Dictionary<int, int> ReadItems(SaveFile sav, BagPlace place)
    {
        var counts = Counts(sav, sav.Inventory.Pouches);
        return Visible(place == BagPlace.Pc ? counts.Pc : counts.Bag, Locked(sav, Total(counts)));
    }

    /// <summary>
    /// CT à usage unique dont la sauvegarde a au moins <see cref="LimitedMachineThreshold"/> exemplaires (sac et PC additionnés),
    /// par clé : cachées pour cette sauvegarde, ni retirées ni déposées. Les autres CT, et la même CT dans une autre sauvegarde,
    /// ne sont pas concernées.
    /// </summary>
    public static HashSet<int> LockedMachines(SaveFile sav) => Locked(sav, Total(Counts(sav, sav.Inventory.Pouches)));

    /// <summary>
    /// Quantité maximale de chaque objet que le sac (ou le PC) peut contenir, par clé : 999 en 4e et 5e génération, 99 dans la
    /// poche Objets de la 3e et dans les jeux de 1re et 2e génération, 1 pour une CT réutilisable (5e à 7e génération,
    /// Épée/Bouclier, Légendes Z-A) et 79 pour une CT à usage unique, moins ce que l'autre endroit en a déjà
    /// (<see cref="LimitedMachineThreshold"/> ; une CT bloquée y est aussi, voir <see cref="LockedMachines"/>). Un objet que le
    /// jeu ne connaît pas est absent (les Balls Noigrume de HGSS n'existent pas dans Platine, aucune CT n'apprend Mitra-Poing
    /// dans Noir 2...), comme une capacité que le jeu n'apprend que par une CS (Surf dans Platine), la Baie Enigma en
    /// 3e génération, un objet que le jeu range parmi les objets rares, et tout pour le PC d'un jeu qui n'en a pas.
    /// </summary>
    public static Dictionary<int, int> Capacity(SaveFile sav, BagPlace place = BagPlace.Bag)
    {
        var all = sav.Inventory.Pouches;
        var target = Pouches(all, place);
        var counts = Counts(sav, all);
        var other = place == BagPlace.Pc ? counts.Bag : counts.Pc;
        var capacity = new Dictionary<int, int>();
        foreach (var item in BallItems.Concat(MedicineItems).Concat(BerryItems))
        {
            if (!IsEnigmaOfGen3(sav, item) && !IsExcluded(sav, item) && PouchFor(sav, target, item) is { } pouch)
                capacity[item] = pouch.MaxCount;
        }
        var machines = MachinesOf(sav);
        foreach (var pouch in target.Where(p => ItemPouches.Contains(p.Type)))
        {
            foreach (var index in pouch.GetAllItems())
            {
                if (KeyOf(sav, machines, pouch, index) is { } item && CategoryOf(item) == ItemCategory.Items)
                    capacity.TryAdd(item, pouch.MaxCount);
            }
        }
        foreach (var machine in machines.ByMove.Values)
        {
            var key = MachineKey(machine.Move);
            if (PouchFor(sav, target, machine.Item) is { } pouch)
                capacity[key] = machine.Reusable ? 1
                    : machine.Limited ? Math.Max(0, Math.Min(pouch.MaxCount, LimitedMachineThreshold - 1 - other.GetValueOrDefault(key)))
                    : pouch.MaxCount;
        }
        return capacity;
    }

    /// <summary>Numéro dans le jeu de la sauvegarde de la CT ou de la DT de chaque capacité (« CT26 », « DT04 »), par clé.</summary>
    public static Dictionary<int, string> MachineLabels(SaveFile sav) =>
        MachinesOf(sav).ByMove.Values.ToDictionary(m => MachineKey(m.Move), m => m.Label);

    /// <summary>
    /// Retire <paramref name="quantities"/> du sac (ou du PC). Message d'erreur, ou null si c'est fait (sac inchangé en cas d'erreur).
    /// </summary>
    public static string? Withdraw(SaveFile sav, IReadOnlyDictionary<int, int> quantities, Func<int, string> name,
        BagPlace place = BagPlace.Bag)
    {
        if (Unsupported(sav) is { } why)
            return why;
        if (place == BagPlace.Pc && !HasPc(sav))
            return "Cette sauvegarde n'a pas de PC d'objets.";
        var bag = sav.Inventory; // copie du sac : réécrite dans la sauvegarde à la fin (CopyTo)
        var target = Pouches(bag.Pouches, place);
        var counts = Counts(sav, bag.Pouches);
        // Une CT bloquée compte pour 0 : elle n'est jamais retirée.
        var have = Visible(place == BagPlace.Pc ? counts.Pc : counts.Bag, Locked(sav, Total(counts)));
        foreach (var (item, quantity) in quantities)
        {
            if (CategoryOf(item) is null)
                return $"{name(item)} ne peut pas être transféré.";
            if (quantity > have.GetValueOrDefault(item))
                return $"{Capitalized(PlaceName(place))} ne contient que {have.GetValueOrDefault(item)} × {name(item)}.";
        }

        var machines = MachinesOf(sav);
        var emptied = new HashSet<InventoryPouch>();
        foreach (var (item, quantity) in quantities)
        {
            var left = quantity;
            foreach (var pouch in target)
            {
                foreach (var slot in pouch.Items.Where(s => s.Count > 0 && KeyOf(sav, machines, pouch, s.Index) == item))
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
            pouch.ClearCount0(); // le jeu attend des emplacements sans trou : seules les poches vidées sont tassées (les CS restent)
        bag.CopyTo(sav);
        ClearEmptiedMachines(sav, bag, emptied);
        return null;
    }

    /// <summary>Position de la poche dans la sauvegarde : champ interne de PKHeX (voir <see cref="ClearEmptiedMachines"/>).</summary>
    private static readonly System.Reflection.FieldInfo PouchOffset =
        typeof(InventoryPouch).GetField("Offset", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
        ?? throw new InvalidOperationException("PKHeX : position des poches introuvable (InventoryPouch.Offset).");

    /// <summary>
    /// Or/Argent/Cristal rangent leurs CT/CS dans un octet par numéro, et PKHeX (26.10.18) n'y écrit pas les quantités tombées à
    /// zéro : une CT entièrement retirée y garderait son ancienne quantité. Les octets des CT qui n'ont plus d'exemplaire sont
    /// donc remis à zéro ici, dans l'ordre des CT/CS de la poche.
    /// </summary>
    private static void ClearEmptiedMachines(SaveFile sav, PlayerBag bag, IEnumerable<InventoryPouch> emptied)
    {
        foreach (var pouch in emptied.Where(p => sav.Generation == 2 && p.Type == InventoryType.TMHMs))
        {
            var offset = (int)PouchOffset.GetValue(pouch)!;
            var slots = bag.Info.GetItems(InventoryType.TMHMs).ToArray();
            for (var i = 0; i < slots.Length; i++)
            {
                if (!pouch.Items.Any(s => s.Index == slots[i] && s.Count > 0))
                    sav.Data[offset + i] = 0;
            }
        }
    }

    /// <summary>
    /// Ajoute <paramref name="quantities"/> au sac (ou au PC). Message d'erreur, ou null si c'est fait (sac inchangé en cas d'erreur).
    /// </summary>
    public static string? Deposit(SaveFile sav, IReadOnlyDictionary<int, int> quantities, Func<int, string> name, string gameName,
        BagPlace place = BagPlace.Bag)
    {
        if (Unsupported(sav) is { } why)
            return why;
        if (place == BagPlace.Pc && !HasPc(sav))
            return "Cette sauvegarde n'a pas de PC d'objets.";
        var bag = sav.Inventory; // copie du sac : réécrite dans la sauvegarde à la fin (CopyTo)
        var target = Pouches(bag.Pouches, place);
        var counts = Counts(sav, bag.Pouches);
        var have = place == BagPlace.Pc ? counts.Pc : counts.Bag;
        var total = Total(counts);
        var locked = Locked(sav, total);
        var capacity = Capacity(sav, place);
        foreach (var (item, quantity) in quantities)
        {
            if (CategoryOf(item) is null)
                return $"{name(item)} ne peut pas être transféré.";
            if (locked.Contains(item))
                return LockedMessage(name(item));
            if (IsLimited(sav, item) && total.GetValueOrDefault(item) + quantity > LimitedMachineThreshold - 1)
            {
                var already = total.GetValueOrDefault(item);
                return $"{name(item)} : la sauvegarde en contient déjà {already}, et une CT à usage unique est limitée à " +
                       $"{LimitedMachineThreshold - 1} par sauvegarde ({LimitedMachineThreshold - 1 - already} de plus).";
            }
            if (!capacity.TryGetValue(item, out var max))
                return place == BagPlace.Pc && Capacity(sav).ContainsKey(item) ? $"{name(item)} ne peut pas être rangé dans le PC de {gameName}."
                    : MoveOf(item) is not null ? $"{name(item)} : aucune CT ni DT n'apprend cette capacité dans {gameName}."
                    : IsEnigmaOfGen3(sav, item) ? $"{name(item)} : en 3e génération, elle vient de l'e-Reader et ne peut pas être déposée."
                    : CategoryOf(item) == ItemCategory.Items ? $"{name(item)} n'existe pas dans {gameName}, ou y est un objet rare."
                    : $"{name(item)} n'existe pas dans {gameName}.";
            var current = have.GetValueOrDefault(item);
            if (current + quantity > max)
                return $"{name(item)} : {PlaceName(place)} en contient déjà {current} et en accepte {max} au plus ({max - current} de plus).";
        }

        var machines = MachinesOf(sav);
        foreach (var (item, quantity) in quantities)
        {
            var gameItem = MoveOf(item) is { } move ? machines.ByMove[move].Item : item;
            var targets = CategoryOf(item) == ItemCategory.Items ? target.Where(p => ItemPouches.Contains(p.Type)) : target;
            if (PouchFor(sav, targets, gameItem)!.GiveItem(bag, ToGame(sav, gameItem), quantity) < 0)
                return $"Plus de place dans {PlaceName(place)} pour {name(item)}.";
        }
        bag.CopyTo(sav);
        return null;
    }

    /// <summary>Refus d'un dépôt d'une CT bloquée (<see cref="LockedMachines"/>), prêt à afficher.</summary>
    public static string LockedMessage(string name) =>
        $"{name} : cette sauvegarde en a déjà au moins {LimitedMachineThreshold}, elle ne peut plus en recevoir.";

    private static string Capitalized(string text) => char.ToUpperInvariant(text[0]) + text[1..];

    /// <summary>CT à usage unique soumise au seuil (<see cref="LimitedMachineThreshold"/>) dans le jeu de cette sauvegarde.</summary>
    private static bool IsLimited(SaveFile sav, int key) =>
        MoveOf(key) is { } move && MachinesOf(sav).ByMove.TryGetValue(move, out var machine) && machine.Limited;

    private static HashSet<int> Locked(SaveFile sav, Dictionary<int, int> counts) =>
        counts.Where(c => c.Value >= LimitedMachineThreshold && IsLimited(sav, c.Key)).Select(c => c.Key).ToHashSet();

    /// <summary>Quantités sans les CT bloquées.</summary>
    private static Dictionary<int, int> Visible(Dictionary<int, int> counts, HashSet<int> locked) =>
        counts.Where(c => !locked.Contains(c.Key)).ToDictionary(c => c.Key, c => c.Value);

    /// <summary>Poches lues et modifiées : celles du sac (sans <see cref="UntouchedPouches"/>), ou le PC d'objets.</summary>
    private static List<InventoryPouch> Pouches(IReadOnlyList<InventoryPouch> pouches, BagPlace place) =>
        place == BagPlace.Pc
            ? pouches.Where(p => p.Type == InventoryType.PCItems).ToList()
            : pouches.Where(p => !UntouchedPouches.Contains(p.Type)).ToList();

    /// <summary>Quantités du sac et du PC (toutes les CT, bloquées comprises).</summary>
    private static (Dictionary<int, int> Bag, Dictionary<int, int> Pc) Counts(SaveFile sav, IReadOnlyList<InventoryPouch> pouches) =>
        (Count(sav, Pouches(pouches, BagPlace.Bag)), Count(sav, Pouches(pouches, BagPlace.Pc)));

    private static Dictionary<int, int> Total((Dictionary<int, int> Bag, Dictionary<int, int> Pc) counts)
    {
        var total = new Dictionary<int, int>(counts.Bag);
        foreach (var (item, count) in counts.Pc)
            total[item] = total.GetValueOrDefault(item) + count;
        return total;
    }

    /// <summary>Poche où le jeu range cet objet (« Balls » en 4e génération, « Objets » en 5e...), null s'il ne le connaît pas.</summary>
    private static InventoryPouch? PouchFor(SaveFile sav, IEnumerable<InventoryPouch> bag, int item)
    {
        var index = ToGame(sav, item);
        return index == 0 ? null : bag.FirstOrDefault(p => p.CanContain(index));
    }

    // ---------- Numéros d'objets des jeux de 1re à 3e génération ----------

    /// <summary>
    /// Rouge/Bleu/Jaune → numéro de 4e génération et suivantes : PKHeX ne fait pas cette conversion. Table établie d'après les
    /// noms anglais (via Or/Argent/Cristal), plus les fossiles, l'Élixir et le Max Élixir, absents de la 2e génération ; CT01 à
    /// CT50 (201 à 250) ajoutées à part. N'y sont pas, donc jamais touchés : les objets rares (Bicyclette, Cannes, Scope
    /// Sylphe...), les CS et le Multi Exp (sans équivalent exact).
    /// </summary>
    private static readonly Dictionary<int, int> Gen1Items = new Dictionary<int, int>
    {
        [1] = 1, [2] = 2, [3] = 3, [4] = 4, [10] = 81, [11] = 18, [12] = 19, [13] = 20, [14] = 21, [15] = 22, [16] = 23,
        [17] = 24, [18] = 25, [19] = 26, [20] = 17, [29] = 78, [30] = 79, [31] = 103, [32] = 82, [33] = 83, [34] = 84,
        [35] = 45, [36] = 46, [37] = 47, [38] = 48, [39] = 49, [40] = 50, [41] = 102, [42] = 101, [46] = 60, [47] = 85,
        [49] = 92, [51] = 63, [52] = 27, [53] = 28, [54] = 29, [55] = 55, [56] = 76, [57] = 77, [58] = 56, [60] = 30,
        [61] = 31, [62] = 32, [65] = 57, [66] = 58, [67] = 59, [68] = 61, [79] = 51, [80] = 38, [81] = 39, [82] = 40,
        [83] = 41,
    }.Concat(Enumerable.Range(1, 50).Select(n => KeyValuePair.Create(200 + n, 327 + n))).ToDictionary();

    private static readonly Dictionary<int, int> Gen1Back = Gen1Items.ToDictionary(i => i.Value, i => i.Key);

    /// <summary>Appât Ball d'Or/Argent/Cristal, que PKHeX ne convertit pas (494 ensuite).</summary>
    private const int LureBall2 = 160, LureBall = 494;

    /// <summary>
    /// Numéro de 4e génération et suivantes d'un objet lu dans le sac (0 s'il n'en a pas) : table <see cref="Gen1Items"/> pour
    /// Rouge/Bleu/Jaune, conversions de PKHeX pour la 2e génération (128 : sans équivalent) et la 3e.
    /// </summary>
    private static int ToNational(SaveFile sav, int index) => sav.Generation switch
    {
        1 => Gen1Items.GetValueOrDefault(index),
        2 => index == LureBall2 ? LureBall
            : index is > 0 and <= byte.MaxValue && ItemConverter.GetItemFuture2((byte)index) is var item and not 128 ? item : 0,
        3 => ItemConverter.GetItemFuture3((ushort)index),
        _ => index,
    };

    /// <summary>Numéro de l'objet dans le jeu de la sauvegarde (0 si l'objet n'y existe pas).</summary>
    private static ushort ToGame(SaveFile sav, int item) => sav.Generation switch
    {
        1 => (ushort)Gen1Back.GetValueOrDefault(item),
        2 => item == LureBall ? (ushort)LureBall2
            : item is > 0 and <= ushort.MaxValue && ItemConverter.GetItemOld2((ushort)item) is var index && ToNational(sav, index) == item
                ? index : (ushort)0,
        3 => ItemConverter.GetItemOld3((ushort)item),
        _ => (ushort)item,
    };

    /// <summary>
    /// Objets jamais touchés des jeux de 2e et 3e génération, quel que soit l'endroit (le PC d'objets accepte presque tout) :
    /// les CS et les objets rares du jeu. Rouge/Bleu/Jaune n'ont pas de poche « Objets rares » : leur table
    /// (<see cref="Gen1Items"/>) ne contient que ce qui est transférable.
    /// </summary>
    private static bool IsExcluded(SaveFile sav, int item) =>
        sav.Generation is 2 or 3 && (item is >= 420 and <= 427 || KeyItemsOf(sav).Contains(item));

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, HashSet<int>> KeyItemsCache = new();

    /// <summary>Objets rares du jeu, en numéros de 4e génération et suivantes.</summary>
    private static HashSet<int> KeyItemsOf(SaveFile sav) => KeyItemsCache.GetOrAdd(sav.GetType(), _ =>
    {
        var keys = sav.Generation == 2
            ? ItemStorage2.KeyCrystal.ToArray() // Cristal en a plus qu'Or/Argent
            : sav.Inventory.Pouches.FirstOrDefault(p => p.Type == InventoryType.KeyItems)?.GetAllItems().ToArray() ?? [];
        return keys.Select(k => ToNational(sav, k)).Where(k => k != 0).ToHashSet();
    });

    /// <summary>
    /// Clé d'un objet lu dans une poche du sac ou du PC (voir <see cref="SaveBag"/>), null si le site ne le transfère pas : CS
    /// (rangées avec les CT), objet ordinaire hors de <see cref="ItemPouches"/>, Lettre, objet rare (<see cref="IsExcluded"/>),
    /// Baie Enigma de la 3e génération, objet ancien sans équivalent ensuite.
    /// </summary>
    private static int? KeyOf(SaveFile sav, Machines machines, InventoryPouch pouch, int index)
    {
        var item = ToNational(sav, index);
        if (item == 0 || ToGame(sav, item) != index || IsExcluded(sav, item))
            return null;
        if (machines.ByItem.TryGetValue(item, out var machine))
            return MachineKey(machine.Move);
        return CategoryOf(item) switch
        {
            ItemCategory.Items when ItemPouches.Contains(pouch.Type) => item,
            ItemCategory.Balls or ItemCategory.Medicine or ItemCategory.Berries when !IsEnigmaOfGen3(sav, item) => item,
            _ => null,
        };
    }

    private static bool IsEnigmaOfGen3(SaveFile sav, int item) => sav.Generation == 3 && item == EnigmaBerry;

    private static Dictionary<int, int> Count(SaveFile sav, IEnumerable<InventoryPouch> bag)
    {
        var machines = MachinesOf(sav);
        var counts = new Dictionary<int, int>();
        foreach (var pouch in bag)
        {
            foreach (var slot in pouch.Items)
            {
                if (slot.Index == 0 || slot.Count <= 0)
                    continue;
                if (KeyOf(sav, machines, pouch, slot.Index) is { } key)
                    counts[key] = counts.GetValueOrDefault(key) + slot.Count;
            }
        }
        return counts;
    }

    // ---------- CT et DT de chaque jeu ----------

    /// <summary>CT ou DT d'un jeu : son objet (numéro de 4e génération et suivantes), la capacité apprise et son numéro (« CT26 »).</summary>
    private sealed record Machine(int Item, ushort Move, string Label, bool Reusable)
    {
        /// <summary>CT à usage unique, soumise au seuil <see cref="LimitedMachineThreshold"/> ; jamais les DT d'Épée/Bouclier.</summary>
        public bool Limited => !Reusable && Label.StartsWith("CT", StringComparison.Ordinal);
    }

    /// <summary>CT et DT d'un jeu (jamais ses CS), par objet et par capacité.</summary>
    private sealed class Machines(IReadOnlyList<Machine> machines)
    {
        public readonly Dictionary<int, Machine> ByItem = machines.ToDictionary(m => m.Item);
        // Une seule CT par capacité dans chaque jeu ; par prudence, la première l'emporte.
        public readonly Dictionary<ushort, Machine> ByMove = machines.GroupBy(m => m.Move).ToDictionary(g => g.Key, g => g.First());
    }

    private static readonly Machines None = new([]);
    // Rouge/Bleu/Jaune et Or/Argent/Cristal : CT01 à CT50, à usage unique, en numéros de 4e génération (CT01 = 328) comme
    // le donne la conversion de leurs objets (ToNational) ; leurs CS (les 5 et 7 dernières capacités) ne sont pas des CT.
    private static readonly Lazy<Machines> Gen1 = new(() => new(Numbered(
        [.. Enumerable.Range(328, 50).Select(i => (ushort)i)], [.. PersonalInfo1.MachineMoves[..50].ToArray().Select(m => (ushort)m)], "CT", 2, reusable: false)));
    private static readonly Lazy<Machines> Gen2 = new(() => new(Numbered(
        [.. Enumerable.Range(328, 50).Select(i => (ushort)i)], [.. PersonalInfo2.MachineMoves[..50].ToArray().Select(m => (ushort)m)], "CT", 2, reusable: false)));
    private static readonly Lazy<Machines> Gen3 = new(() => new(Numbered(
        [.. ItemStorage3RS.MachineOnlyTM.ToArray().Select(i => (ushort)ItemConverter.GetItemFuture3(i))],
        PersonalInfo3.MachineMovesTechnical, "CT", 2, reusable: false)));
    private static readonly Lazy<Machines> Gen4 = new(() => new(Numbered(
        ItemStorage4.Machine[..PersonalInfo4.MachineMovesTechnical.Length], PersonalInfo4.MachineMovesTechnical, "CT", 2, reusable: false)));
    private static readonly Lazy<Machines> Gen5 = new(() => new(Numbered(
        ItemStorage5.Machine[..95], PersonalInfo5BW.MachineMoves[..95], "CT", 2, reusable: true)));
    private static readonly Lazy<Machines> XY = new(() => new(Numbered(
        ItemStorage6XY.Machine[..100], LearnSource6XY.MachineMoves[..100], "CT", 2, reusable: true)));
    private static readonly Lazy<Machines> ORAS = new(() => new(Numbered(
        ItemStorage6AO.Machine[..100], LearnSource6AO.MachineMoves[..100], "CT", 2, reusable: true)));
    private static readonly Lazy<Machines> SM = new(() => new(Numbered(
        ItemStorage7SM.Machine, PersonalInfo7.MachineMoves, "CT", 2, reusable: true)));
    private static readonly Lazy<Machines> LGPE = new(() => new(Numbered(
        ItemStorage7GG.Machine, LearnSource7GG.MachineMoves, "CT", 2, reusable: true)));
    // Diamant Étincelant / Perle Scintillante : les objets des CS de Platine y sont les CT93 à CT100 (Coupe, Vol, Surf...).
    private static readonly Lazy<Machines> BDSP = new(() => new(Numbered(
        ItemStorage8BDSP.Machine, PersonalInfo8BDSP.MachineMoves, "CT", 2, reusable: false)));
    private static readonly Lazy<Machines> ZA = new(() => new(Numbered(
        ItemStorage9ZA.TM, PersonalInfo9ZA.MachineMoves, "CT", 3, reusable: true)));
    private static readonly Lazy<Machines> SWSH = new(() => new(SwordShield()));
    private static readonly Lazy<Machines> SV = new(() => new(ScarletViolet()));

    /// <summary>CT et DT du jeu de la sauvegarde.</summary>
    private static Machines MachinesOf(SaveFile sav) => sav.Context switch
    {
        EntityContext.Gen1 => Gen1.Value,
        EntityContext.Gen2 => Gen2.Value,
        EntityContext.Gen3 => Gen3.Value,
        EntityContext.Gen4 => Gen4.Value,
        EntityContext.Gen5 => Gen5.Value,
        EntityContext.Gen6 => sav is SAV6AO or SAV6AODemo ? ORAS.Value : XY.Value,
        EntityContext.Gen7 => SM.Value,
        EntityContext.Gen7b => LGPE.Value,
        EntityContext.Gen8 => SWSH.Value,
        EntityContext.Gen8b => BDSP.Value,
        EntityContext.Gen9 => SV.Value,
        EntityContext.Gen9a => ZA.Value,
        _ => None, // Légendes Arceus n'a pas de CT
    };

    /// <summary>CT dans l'ordre de PKHeX : la n-ième (à partir de 1) apprend la n-ième capacité.</summary>
    private static List<Machine> Numbered(ReadOnlySpan<ushort> items, ReadOnlySpan<ushort> moves, string prefix, int digits, bool reusable)
    {
        if (items.Length != moves.Length)
            throw new InvalidOperationException($"PKHeX : {items.Length} CT pour {moves.Length} capacités.");
        var machines = new List<Machine>(items.Length);
        for (var i = 0; i < items.Length; i++)
            machines.Add(new(items[i], moves[i], prefix + (i + 1).ToString().PadLeft(digits, '0'), reusable));
        return machines;
    }

    /// <summary>
    /// Épée/Bouclier : CT00 à CT99, réutilisables, et DT00 à DT99, à usage unique. PKHeX range les objets ainsi : CT01 à CT99,
    /// DT00 à DT99, puis CT00 ; les capacités sont dans l'ordre des numéros (CT00 d'abord).
    /// </summary>
    private static List<Machine> SwordShield()
    {
        var items = ItemStorage8SWSH.MachineRecord;
        var tm = PersonalInfo8SWSH.MachineMovesTechnical;
        var tr = PersonalInfo8SWSH.MachineMovesRecord;
        var machines = new List<Machine> { new(items[^1], tm[0], "CT00", true) };
        for (var n = 1; n < tm.Length; n++)
            machines.Add(new(items[n - 1], tm[n], $"CT{n:00}", true));
        for (var n = 0; n < tr.Length; n++)
            machines.Add(new(items[tm.Length - 1 + n], tr[n], $"DT{n:00}", false));
        return machines;
    }

    /// <summary>
    /// Écarlate/Violet : CT001 à CT229, à usage unique. Les capacités sont dans l'ordre des numéros, à partir d'une CT000 qui
    /// n'existe pas dans le jeu ; PKHeX range les objets ainsi : CT001 à CT099, cet objet CT000 (ignoré), puis CT100 à CT229.
    /// </summary>
    private static List<Machine> ScarletViolet()
    {
        var items = ItemStorage9SV.Machine;
        var moves = PersonalInfo9SV.MachineMoves;
        var machines = new List<Machine>();
        for (var n = 1; n < moves.Length; n++)
            machines.Add(new(items[n < 100 ? n - 1 : n], moves[n], $"CT{n:000}", false));
        return machines;
    }
}
