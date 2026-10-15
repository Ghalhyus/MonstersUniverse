using PKHeX.Core;

namespace PokeBank.Services;

/// <summary>Catégories d'objets du sac que le site sait transférer.</summary>
public enum ItemCategory { Items, Balls, Medicine, Berries, Machines }

/// <summary>
/// Objets du sac d'une sauvegarde (objets, Pokéballs, médicaments, baies, CT et DT), lus et modifiés en mémoire avec PKHeX (le fichier s'écrit
/// ensuite avec <see cref="SaveFile.Write"/>). Chaque objet est repéré par une clé, la même d'un jeu à l'autre :
/// <list type="bullet">
/// <item>objets, Pokéballs, médicaments et baies : leur numéro PKHeX de 4e génération et suivantes (une Super Ball retirée de Platine peut être
/// déposée dans Noir 2) ; les jeux de 3e génération, qui numérotent leurs objets autrement, sont convertis ;</item>
/// <item>CT et DT : la capacité qu'elles apprennent (<see cref="MachineKey"/>), car un même numéro de CT n'apprend pas la même
/// capacité d'une génération à l'autre (CT01 : Mitra-Poing en Platine, Affûtage en Noir 2). Une CT Séisme retirée de Platine
/// redevient la CT ou la DT qui apprend Séisme dans le jeu où elle est déposée. Les CS ne sont jamais lues ni modifiées : les
/// retirer pourrait bloquer la progression du joueur.</item>
/// </list>
/// Les objets sont cherchés dans toutes les poches du sac : les Balls ont leur poche en 4e génération mais sont rangées avec les
/// objets dans Noir 2, les médicaments sont avec les objets en 3e génération, les baies de Let's Go avec les Balls. Seul le sac
/// est concerné : les baies plantées ne sont pas touchées. Les objets rares, les Lettres et les Cristaux Z ne sont jamais lus
/// ni modifiés (voir <see cref="ItemPouches"/>).
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
    /// </summary>
    public static readonly IReadOnlySet<int> BerryItems = new HashSet<int>(
        [.. Enumerable.Range(149, 64), 686, 687, 688, .. Enumerable.Range(861, 6), 1754, .. Enumerable.Range(2651, 33)]);

    /// <summary>
    /// Baie Enigma : en 3e génération, elle vient de l'e-Reader et son nom et son effet sont enregistrés dans la sauvegarde ;
    /// elle n'y est donc ni retirée ni déposée (une baie normale à partir de la 4e génération).
    /// </summary>
    public const int EnigmaBerry = 208;

    /// <summary>
    /// Lettres (4e génération et suivantes ; celles de la 3e n'ont pas d'équivalent) : leur message est enregistré à part dans la
    /// sauvegarde, elles ne sont donc jamais transférées, même quand le jeu les range avec les objets (5e génération).
    /// </summary>
    public static readonly IReadOnlySet<int> MailItems = new HashSet<int>(Enumerable.Range(137, 12));

    /// <summary>
    /// Poches d'où viennent les objets ordinaires (catégorie <see cref="ItemCategory.Items"/>) : Objets, Objets de combat,
    /// Trésors, Ingrédients, Bonbons, Méga-Gemmes. Les autres catégories sont reconnues à leur numéro, dans toutes les poches
    /// sauf <see cref="UntouchedPouches"/>.
    /// </summary>
    public static readonly IReadOnlySet<InventoryType> ItemPouches = new HashSet<InventoryType>(
        [InventoryType.Items, InventoryType.BattleItems, InventoryType.Treasure, InventoryType.Ingredients, InventoryType.Candy, InventoryType.MegaStones]);

    /// <summary>
    /// Poches jamais lues ni modifiées : objets rares (les retirer pourrait bloquer la progression), Lettres, Cristaux Z et
    /// stockage d'objets du PC (3e génération), qui ne fait pas partie du sac.
    /// </summary>
    private static readonly IReadOnlySet<InventoryType> UntouchedPouches = new HashSet<InventoryType>(
        [InventoryType.KeyItems, InventoryType.MailItems, InventoryType.ZCrystals, InventoryType.PCItems]);

    /// <summary>Début des clés des CT et DT : <c>MachineKeyBase + numéro de la capacité</c>, au-delà de tout numéro d'objet.</summary>
    public const int MachineKeyBase = 100_000;

    /// <summary>
    /// Seuil des CT à usage unique (3e et 4e génération, Diamant Étincelant/Perle Scintillante, Écarlate/Violet ; pas les DT
    /// d'Épée/Bouclier) : une sauvegarde qui en a au moins autant d'une même CT la garde pour elle. Cette CT y est cachée, ni
    /// retirée ni déposée (<see cref="LockedMachines"/>), et un dépôt ne peut pas l'amener jusque-là (79 au plus).
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

    /// <summary>Pourquoi le sac de cette sauvegarde n'est pas pris en charge ; null s'il l'est.</summary>
    public static string? Unsupported(SaveFile sav) =>
        sav.Generation < 3 ? "Les sacs des jeux de 1re et 2e génération ne sont pas pris en charge."
        : Capacity(sav).Count == 0 ? "Le sac de ce jeu ne peut contenir aucun objet que le site sait transférer."
        : null;

    /// <summary>
    /// Objets transférables du sac (objets ordinaires, Pokéballs, médicaments, baies, CT et DT ; jamais les objets rares, les
    /// Lettres ni les CS) par clé, et leur quantité (additionnée si un objet occupe plusieurs emplacements). Les CT bloquées
    /// (<see cref="LockedMachines"/>) n'y sont pas.
    /// </summary>
    public static Dictionary<int, int> ReadItems(SaveFile sav) =>
        sav.Generation < 3 ? [] : Visible(sav, Count(sav, Bag(sav.Inventory)));

    /// <summary>
    /// CT à usage unique dont le sac a au moins <see cref="LimitedMachineThreshold"/> exemplaires, par clé : cachées pour cette
    /// sauvegarde, ni retirées ni déposées. Les autres CT du sac, et la même CT dans une autre sauvegarde, ne sont pas concernées.
    /// </summary>
    public static HashSet<int> LockedMachines(SaveFile sav) =>
        sav.Generation < 3 ? [] : Locked(sav, Count(sav, Bag(sav.Inventory)));

    /// <summary>
    /// Quantité maximale de chaque objet que le sac peut contenir, par clé : 999 en 4e et 5e génération, 99 dans la poche Objets
    /// de la 3e, 1 pour une CT réutilisable (5e à 7e génération, Épée/Bouclier, Légendes Z-A) et 79 pour une CT à usage unique
    /// (<see cref="LimitedMachineThreshold"/> ; une CT bloquée y est aussi, voir <see cref="LockedMachines"/>). Un objet que le jeu ne connaît
    /// pas est absent (les Balls Noigrume de HGSS n'existent pas dans Platine, aucune CT n'apprend Mitra-Poing dans Noir 2...),
    /// comme une capacité que le jeu n'apprend que par une CS (Surf dans Platine), la Baie Enigma en 3e génération et un objet
    /// que le jeu range parmi les objets rares.
    /// </summary>
    public static Dictionary<int, int> Capacity(SaveFile sav)
    {
        if (sav.Generation < 3)
            return [];
        var bag = Bag(sav.Inventory);
        var capacity = new Dictionary<int, int>();
        foreach (var item in BallItems.Concat(MedicineItems).Concat(BerryItems))
        {
            if (!IsEnigmaOfGen3(sav, item) && PouchFor(sav, bag, item) is { } pouch)
                capacity[item] = pouch.MaxCount;
        }
        var machines = MachinesOf(sav);
        foreach (var pouch in bag.Where(p => ItemPouches.Contains(p.Type)))
        {
            foreach (var index in pouch.GetAllItems())
            {
                if (KeyOf(sav, machines, pouch, index) is { } item && CategoryOf(item) == ItemCategory.Items)
                    capacity.TryAdd(item, pouch.MaxCount);
            }
        }
        foreach (var machine in machines.ByMove.Values)
        {
            if (PouchFor(sav, bag, machine.Item) is { } pouch)
                capacity[MachineKey(machine.Move)] = machine.Reusable ? 1
                    : machine.Limited ? Math.Min(pouch.MaxCount, LimitedMachineThreshold - 1)
                    : pouch.MaxCount;
        }
        return capacity;
    }

    /// <summary>Numéro dans le jeu de la sauvegarde de la CT ou de la DT de chaque capacité (« CT26 », « DT04 »), par clé.</summary>
    public static Dictionary<int, string> MachineLabels(SaveFile sav) =>
        sav.Generation < 3 ? [] : MachinesOf(sav).ByMove.Values.ToDictionary(m => MachineKey(m.Move), m => m.Label);

    /// <summary>Retire <paramref name="quantities"/> du sac. Message d'erreur, ou null si c'est fait (sac inchangé en cas d'erreur).</summary>
    public static string? Withdraw(SaveFile sav, IReadOnlyDictionary<int, int> quantities, Func<int, string> name)
    {
        if (Unsupported(sav) is { } why)
            return why;
        var pouches = sav.Inventory;
        var bag = Bag(pouches);
        var have = Visible(sav, Count(sav, bag)); // une CT bloquée compte pour 0 : elle n'est jamais retirée
        foreach (var (item, quantity) in quantities)
        {
            if (CategoryOf(item) is null)
                return $"{name(item)} ne peut pas être transféré.";
            if (quantity > have.GetValueOrDefault(item))
                return $"Le sac ne contient que {have.GetValueOrDefault(item)} × {name(item)}.";
        }

        var machines = MachinesOf(sav);
        var emptied = new HashSet<InventoryPouch>();
        foreach (var (item, quantity) in quantities)
        {
            var left = quantity;
            foreach (var pouch in bag)
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
        var locked = Locked(sav, have);
        var capacity = Capacity(sav);
        foreach (var (item, quantity) in quantities)
        {
            if (CategoryOf(item) is null)
                return $"{name(item)} ne peut pas être transféré.";
            if (locked.Contains(item))
                return LockedMessage(name(item));
            if (!capacity.TryGetValue(item, out var max))
                return MoveOf(item) is not null ? $"{name(item)} : aucune CT ni DT n'apprend cette capacité dans {gameName}."
                    : IsEnigmaOfGen3(sav, item) ? $"{name(item)} : en 3e génération, elle vient de l'e-Reader et ne peut pas être déposée."
                    : CategoryOf(item) == ItemCategory.Items ? $"{name(item)} n'existe pas dans {gameName}, ou y est un objet rare."
                    : $"{name(item)} n'existe pas dans {gameName}.";
            var current = have.GetValueOrDefault(item);
            if (current + quantity > max)
                return IsLimited(sav, item)
                    ? $"{name(item)} : le sac en contient déjà {current}, et une CT à usage unique est limitée à {max} par sauvegarde ({max - current} de plus)."
                    : $"{name(item)} : le sac en contient déjà {current} et en accepte {max} au plus ({max - current} de plus).";
        }

        var machines = MachinesOf(sav);
        foreach (var (item, quantity) in quantities)
        {
            var gameItem = MoveOf(item) is { } move ? machines.ByMove[move].Item : item;
            var targets = CategoryOf(item) == ItemCategory.Items ? bag.Where(p => ItemPouches.Contains(p.Type)) : bag;
            if (PouchFor(sav, targets, gameItem)!.GiveItem(sav, ToGame(sav, gameItem), quantity) < 0)
                return $"Plus de place dans le sac pour {name(item)}.";
        }
        sav.Inventory = pouches;
        return null;
    }

    /// <summary>Refus d'un dépôt d'une CT bloquée (<see cref="LockedMachines"/>), prêt à afficher.</summary>
    public static string LockedMessage(string name) =>
        $"{name} : cette sauvegarde en a déjà au moins {LimitedMachineThreshold}, elle ne peut plus en recevoir.";

    /// <summary>CT à usage unique soumise au seuil (<see cref="LimitedMachineThreshold"/>) dans le jeu de cette sauvegarde.</summary>
    private static bool IsLimited(SaveFile sav, int key) =>
        MoveOf(key) is { } move && MachinesOf(sav).ByMove.TryGetValue(move, out var machine) && machine.Limited;

    private static HashSet<int> Locked(SaveFile sav, Dictionary<int, int> counts) =>
        counts.Where(c => c.Value >= LimitedMachineThreshold && IsLimited(sav, c.Key)).Select(c => c.Key).ToHashSet();

    /// <summary>Quantités sans les CT bloquées.</summary>
    private static Dictionary<int, int> Visible(SaveFile sav, Dictionary<int, int> counts)
    {
        foreach (var key in Locked(sav, counts))
            counts.Remove(key);
        return counts;
    }

    /// <summary>Poches du sac que le site peut lire et modifier (sans <see cref="UntouchedPouches"/>).</summary>
    private static List<InventoryPouch> Bag(IReadOnlyList<InventoryPouch> pouches) =>
        pouches.Where(p => !UntouchedPouches.Contains(p.Type)).ToList();

    /// <summary>Poche où le jeu range cet objet (« Balls » en 4e génération, « Objets » en 5e...), null s'il ne le connaît pas.</summary>
    private static InventoryPouch? PouchFor(SaveFile sav, IEnumerable<InventoryPouch> bag, int item)
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

    /// <summary>
    /// Clé d'un objet lu dans une poche du sac (voir <see cref="SaveBag"/>), null si le site ne le transfère pas : CS (rangées
    /// avec les CT), objet ordinaire hors de <see cref="ItemPouches"/>, Lettre, Baie Enigma de la 3e génération, objet de la
    /// 3e génération sans équivalent ensuite (ses Lettres).
    /// </summary>
    private static int? KeyOf(SaveFile sav, Machines machines, InventoryPouch pouch, int index)
    {
        var item = ToNational(sav, index);
        if (item == 0 || ToGame(sav, item) != index)
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
