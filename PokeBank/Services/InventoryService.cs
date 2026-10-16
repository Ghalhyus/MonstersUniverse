using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PokeBank.Data;

namespace PokeBank.Services;

/// <summary>Sauvegarde stockée telle que proposée pour un retrait de Pokédollars.</summary>
/// <param name="Money">Argent dans le fichier stocké ; null si PKHeX ne sait pas le lire pour ce jeu.</param>
public sealed record MoneySource(int SaveId, string Label, string TrainerId, string FileName, long? Money);

public sealed record WalletOperationView(DateTime At, WalletOperationKind Kind, long Amount, long BalanceAfter, string SaveLabel, string Details)
{
    /// <summary>« Jeu — Dresseur » de <see cref="SaveLabel"/>.</summary>
    public string SaveName => SaveLabel.Split(WalletOperation.FileSeparator)[0];

    /// <summary>Nom du fichier de la sauvegarde au moment de l'opération ; null s'il n'est pas connu.</summary>
    public string? SaveFileName => SaveLabel.Split(WalletOperation.FileSeparator) is [_, var file, ..] && file != "" ? file : null;
}

public sealed record WithdrawResult(bool Ok, string Message, long Balance = 0);

public sealed record PurchaseResult(bool Ok, string Message, int Limit = 0, long Balance = 0);

/// <summary>Objet de l'inventaire ou du sac d'une sauvegarde (objet ordinaire, Pokéball, médicament, baie, CT ou DT), prêt à afficher.</summary>
/// <param name="ItemId">Clé de l'objet (voir <see cref="SaveBag"/>) : pour une CT ou une DT, la capacité qu'elle apprend.</param>
/// <param name="Name">Nom sous le titre de sa catégorie (pour une CT ou une DT, celui de la capacité).</param>
public sealed record ItemStack(int ItemId, ItemCategory Category, string Name, string SpriteUrl, int Count);

/// <summary>Sauvegarde stockée telle que proposée pour un transfert d'objets.</summary>
/// <param name="Unsupported">Raison pour laquelle son sac n'est pas pris en charge ; null s'il l'est.</param>
/// <param name="Items">
/// Objets, Pokéballs, médicaments, baies, CT et DT de son sac et de son PC additionnés (jamais les objets rares ni les CS) : ce qu'on
/// consulte. Pour un transfert, voir <see cref="BagItems"/> et <see cref="PcItems"/>.
/// </param>
/// <param name="BagItems">Ceux du sac seulement.</param>
/// <param name="Capacity">Quantité maximale de chaque objet que son sac accepte ; absent si son jeu ne le connaît pas (dépôt).</param>
/// <param name="MachineLabels">Numéro dans son jeu de la CT ou de la DT de chaque capacité (« CT26 »), par clé.</param>
/// <param name="LockedMachines">CT cachées de cette sauvegarde, ni retirées ni déposées (<see cref="SaveBag.LockedMachines"/>).</param>
/// <param name="PcItems">Ceux de son PC d'objets (1re à 3e génération) ; null si son jeu n'en a pas.</param>
/// <param name="PcCapacity">Comme <paramref name="Capacity"/>, pour son PC ; null si son jeu n'en a pas.</param>
public sealed record ItemSource(
    int SaveId, string Label, string TrainerId, string FileName, string GameName, string? Unsupported,
    IReadOnlyList<ItemStack> Items, IReadOnlyList<ItemStack> BagItems, IReadOnlyDictionary<int, int> Capacity,
    IReadOnlyDictionary<int, string> MachineLabels, IReadOnlySet<int> LockedMachines,
    IReadOnlyList<ItemStack>? PcItems = null, IReadOnlyDictionary<int, int>? PcCapacity = null)
{
    /// <summary>La sauvegarde a un PC d'objets, à côté du sac (1re à 3e génération).</summary>
    public bool HasPc => PcItems is not null;

    /// <summary>« Noire — Ghal · noir.sav », pour une liste déroulante (une seule ligne, sans mise en forme).</summary>
    public string LabelAndFile => $"{Label} · {FileName}";

    public int Total(ItemCategory category) => Items.Where(i => i.Category == category).Sum(i => i.Count);

    /// <summary>Quantité d'un objet, sac et PC additionnés.</summary>
    public int CountOf(int itemId) => Items.FirstOrDefault(i => i.ItemId == itemId)?.Count ?? 0;

    public IReadOnlyList<ItemStack> ItemsIn(BagPlace place) => place == BagPlace.Pc ? PcItems ?? [] : BagItems;

    public IReadOnlyDictionary<int, int> CapacityIn(BagPlace place) =>
        place == BagPlace.Pc ? PcCapacity ?? new Dictionary<int, int>() : Capacity;

    /// <summary>Quantité d'un objet dans le sac ou dans le PC.</summary>
    public int CountIn(int itemId, BagPlace place) => ItemsIn(place).FirstOrDefault(i => i.ItemId == itemId)?.Count ?? 0;
}

public sealed record ItemTransferResult(bool Ok, string Message);

/// <summary>Libellés d'une catégorie d'objets, dans l'ordre d'affichage (<see cref="All"/>).</summary>
public sealed record ItemCategories(ItemCategory Category, string Title, string Empty)
{
    public static readonly IReadOnlyList<ItemCategories> All =
    [
        new(ItemCategory.Items, "Objets", "Aucun objet pour l'instant : retires-en du sac d'une de tes sauvegardes stockées."),
        new(ItemCategory.Balls, "Pokéballs", "Aucune Pokéball pour l'instant : retires-en du sac d'une de tes sauvegardes stockées."),
        new(ItemCategory.Medicine, "Médicaments", "Aucun médicament pour l'instant : retires-en du sac d'une de tes sauvegardes stockées."),
        new(ItemCategory.Berries, "Baies", "Aucune baie pour l'instant : retires-en du sac d'une de tes sauvegardes stockées."),
        new(ItemCategory.Machines, "CT / DT", "Aucune CT ni DT pour l'instant : retires-en du sac d'une de tes sauvegardes stockées."),
    ];
}

/// <summary>
/// Inventaire du site : un porte-monnaie de Pokédollars, alimenté en retirant de l'argent des sauvegardes stockées, et des
/// objets (objets ordinaires, Pokéballs, médicaments, baies, CT et DT rangées par capacité), retirés du sac des sauvegardes stockées ou déposés dedans. Seule la copie du serveur est
/// modifiée ; la partie est alors marquée « modifiée sur le serveur » (voir <see cref="SaveModification"/>). Chaque mouvement
/// est inscrit dans un journal.
/// </summary>
public sealed class InventoryService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    StoredSaveService storedSaves,
    PokemonInfo info,
    ILogger<InventoryService> logger)
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    public static string FormatMoney(long amount) => $"{amount.ToString("N0", French)} ₽";

    public async Task<long> GetBalanceAsync(string ownerId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Wallets.Where(w => w.OwnerId == ownerId).Select(w => w.Money).FirstOrDefaultAsync();
    }

    /// <summary>Journal des opérations, du plus récent au plus ancien.</summary>
    public async Task<List<WalletOperationView>> ListOperationsAsync(string ownerId, int take = 200)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.WalletOperations.AsNoTracking()
            .Where(o => o.OwnerId == ownerId)
            .OrderByDescending(o => o.At).ThenByDescending(o => o.Id)
            .Take(take)
            .Select(o => new WalletOperationView(o.At, o.Kind, o.Amount, o.BalanceAfter, o.SaveLabel, o.Details))
            .ToListAsync();
    }

    /// <summary>
    /// Achète <paramref name="lots"/> lots d'emplacements de collection (<see cref="CollectionCapacity.SlotsPerLot"/> chacun,
    /// <see cref="CollectionCapacity.LotPrice"/> l'un) : débit, nouvelle limite et ligne de journal en une seule opération.
    /// </summary>
    public async Task<PurchaseResult> BuyCollectionSlotsAsync(string ownerId, int lots)
    {
        if (lots <= 0)
            return new(false, "Choisis au moins un lot.");
        if (lots > 100_000)
            return new(false, "Trop de lots en une seule fois.");

        long cost = lots * CollectionCapacity.LotPrice;
        int slots = lots * CollectionCapacity.SlotsPerLot;

        await using var db = await dbFactory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        // Débit conditionnel et atomique : deux achats simultanés ne peuvent pas rendre le solde négatif.
        var debited = await db.Wallets.Where(w => w.OwnerId == ownerId && w.Money >= cost)
            .ExecuteUpdateAsync(w => w.SetProperty(x => x.Money, x => x.Money - cost));
        if (debited == 0)
        {
            var available = await db.Wallets.Where(w => w.OwnerId == ownerId).Select(w => w.Money).FirstOrDefaultAsync();
            return new(false, $"{lots} lot{(lots > 1 ? "s" : "")} coûte{(lots > 1 ? "nt" : "")} {FormatMoney(cost)}, ton porte-monnaie contient {FormatMoney(available)} : " +
                              $"il manque {FormatMoney(cost - available)}. Retire de l'argent d'une sauvegarde depuis « Inventaire ».");
        }

        var limitBefore = await db.Users.Where(u => u.Id == ownerId).Select(u => u.CollectionLimit).FirstAsync();
        await db.Users.Where(u => u.Id == ownerId)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.CollectionLimit, x => x.CollectionLimit + slots));
        var limit = limitBefore + slots;
        var balance = await db.Wallets.Where(w => w.OwnerId == ownerId).Select(w => w.Money).FirstAsync();

        db.WalletOperations.Add(new WalletOperation
        {
            OwnerId = ownerId,
            At = DateTime.UtcNow,
            Kind = WalletOperationKind.BuyCollectionSlots,
            Amount = -cost,
            BalanceAfter = balance,
            Details = $"+{CollectionCapacity.FormatCount(slots)} emplacements ({CollectionCapacity.FormatCount(limitBefore)} → {CollectionCapacity.FormatCount(limit)})",
        });
        await db.SaveChangesAsync();
        await tx.CommitAsync();

        logger.LogInformation("Utilisateur {User} : {Slots} emplacements achetés pour {Cost} ₽ (limite {Limit}).", ownerId, slots, cost, limit);
        return new(true, $"Collection agrandie : {CollectionCapacity.FormatCount(limit)} emplacements. Solde : {FormatMoney(balance)}.", limit, balance);
    }

    /// <summary>Sauvegardes stockées de l'utilisateur, avec l'argent que contient chacune (lu dans le fichier).</summary>
    public async Task<List<MoneySource>> ListMoneySourcesAsync(string ownerId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var saves = await db.StoredSaves.AsNoTracking()
            .Where(s => s.OwnerId == ownerId)
            .OrderBy(s => s.UploadedAt)
            .Select(s => new { s.Id, s.GameName, s.TrainerName, s.TrainerId, s.FileName, s.Data })
            .ToListAsync();
        return saves.Select(s => new MoneySource(s.Id, $"{s.GameName} — {s.TrainerName}", s.TrainerId, s.FileName,
                StoredSaveService.TryRead(s.Data, s.FileName, out var sav) ? sav.Money : null))
            .ToList();
    }

    /// <summary>
    /// Retire <paramref name="amount"/> Pokédollars de la sauvegarde stockée et les crédite sur le porte-monnaie,
    /// en une seule opération. La partie est marquée « modifiée sur le serveur » (voir <see cref="SaveModification"/>).
    /// </summary>
    public async Task<WithdrawResult> WithdrawFromSaveAsync(string ownerId, int saveId, long amount)
    {
        if (amount <= 0)
            return new(false, "Le montant doit être supérieur à 0.");

        await using var db = await dbFactory.CreateDbContextAsync();
        var save = await db.StoredSaves.FirstOrDefaultAsync(s => s.Id == saveId && s.OwnerId == ownerId);
        if (save is null)
            return new(false, "Cette sauvegarde n'existe plus.");
        if (!StoredSaveService.TryRead(save.Data, save.FileName, out var sav))
            return new(false, "Impossible de lire cette sauvegarde.");

        var label = $"{save.GameName} — {save.TrainerName}";
        long before = sav.Money;
        if (amount > before)
            return new(false, $"« {label} » ne contient que {FormatMoney(before)}.");

        // Modifie le fichier, puis vérifie en le relisant que le jeu a bien pris la nouvelle valeur.
        sav.Money = (uint)(before - amount);
        var newData = sav.Write().ToArray();
        if (!StoredSaveService.TryRead(newData, save.FileName, out var check) || check.Money != before - amount)
            return new(false, $"La modification de l'argent n'est pas prise en charge pour ce jeu ({save.GameName}).");

        long balance = 0;
        var error = await SaveServerChangeAsync(db, ownerId, save, newData, check.Money, "retrait",
            m => m.Withdrawn += amount,
            async now =>
            {
                // Crédit atomique (deux retraits simultanés depuis deux sauvegardes ne s'écrasent pas).
                var credited = await db.Wallets.Where(w => w.OwnerId == ownerId)
                    .ExecuteUpdateAsync(w => w.SetProperty(x => x.Money, x => x.Money + amount));
                if (credited == 0)
                {
                    db.Wallets.Add(new Wallet { OwnerId = ownerId, Money = amount });
                    await db.SaveChangesAsync();
                }
                balance = await db.Wallets.Where(w => w.OwnerId == ownerId).Select(w => w.Money).FirstAsync();
                AddOperation(db, ownerId, now, WalletOperationKind.WithdrawFromSave, amount, balance, save);
                return null;
            });
        if (error is not null)
            return new(false, error);

        logger.LogInformation("Utilisateur {User} : {Amount} ₽ retirés de la sauvegarde {Id}.", ownerId, amount, save.Id);
        return new(true, $"{FormatMoney(amount)} retirés de « {label} ». Solde : {FormatMoney(balance)}.", balance);
    }

    // ---------- Objets (objets ordinaires, Pokéballs, médicaments, baies, CT et DT) ----------

    /// <summary>Objets de l'inventaire : les objets ordinaires, les Pokéballs, les médicaments, les baies puis les CT et DT (voir <see cref="Ordered"/>).</summary>
    public async Task<List<ItemStack>> GetItemsAsync(string ownerId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var items = await db.OwnedItems.AsNoTracking()
            .Where(i => i.OwnerId == ownerId && i.Count > 0)
            .Select(i => new { i.ItemId, i.Count })
            .ToListAsync();
        return Ordered(items.ToDictionary(i => i.ItemId, i => i.Count)).Select(i => Stack(i.Key, i.Value)).ToList();
    }

    /// <summary>
    /// Sauvegardes stockées de l'utilisateur, avec les objets, Pokéballs, médicaments, baies, CT et DT de leur sac et de leur PC
    /// (lus dans le fichier).
    /// </summary>
    public async Task<List<ItemSource>> ListItemSourcesAsync(string ownerId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var saves = await db.StoredSaves.AsNoTracking()
            .Where(s => s.OwnerId == ownerId)
            .OrderBy(s => s.UploadedAt)
            .Select(s => new { s.Id, s.GameName, s.TrainerName, s.TrainerId, s.FileName, s.Data })
            .ToListAsync();
        return saves.Select(s =>
        {
            var label = $"{s.GameName} — {s.TrainerName}";
            if (!StoredSaveService.TryRead(s.Data, s.FileName, out var sav))
                return new ItemSource(s.Id, label, s.TrainerId, s.FileName, s.GameName, "Sauvegarde illisible.", [], [], new Dictionary<int, int>(),
                    new Dictionary<int, string>(), new HashSet<int>());
            var hasPc = SaveBag.HasPc(sav);
            return new ItemSource(s.Id, label, s.TrainerId, s.FileName, s.GameName, SaveBag.Unsupported(sav),
                Stacks(SaveBag.ReadItems(sav)), Stacks(SaveBag.ReadItems(sav, BagPlace.Bag)), SaveBag.Capacity(sav),
                SaveBag.MachineLabels(sav), SaveBag.LockedMachines(sav),
                hasPc ? Stacks(SaveBag.ReadItems(sav, BagPlace.Pc)) : null, hasPc ? SaveBag.Capacity(sav, BagPlace.Pc) : null);
        }).ToList();
    }

    /// <summary>
    /// Retire des objets (objets, Pokéballs, médicaments, baies, CT et DT, mélangés si besoin) du sac de la sauvegarde stockée,
    /// et de son PC d'objets (<paramref name="fromPc"/>, 1re à 3e génération), et les ajoute à l'inventaire, en une seule opération.
    /// La partie est marquée « modifiée sur le serveur » : le jeu du joueur a toujours ces objets.
    /// </summary>
    public Task<ItemTransferResult> WithdrawItemsAsync(string ownerId, int saveId, IReadOnlyDictionary<int, int> quantities,
        IReadOnlyDictionary<int, int>? fromPc = null) =>
        TransferItemsAsync(ownerId, saveId, quantities, fromPc, deposit: false);

    /// <summary>
    /// Dépose des objets de l'inventaire dans le sac de la sauvegarde stockée, et dans son PC d'objets (<paramref name="toPc"/>),
    /// en une seule opération. La partie est marquée « modifiée sur le serveur » : le joueur doit la télécharger pour avoir ces
    /// objets dans son jeu.
    /// </summary>
    public Task<ItemTransferResult> DepositItemsAsync(string ownerId, int saveId, IReadOnlyDictionary<int, int> quantities,
        IReadOnlyDictionary<int, int>? toPc = null) =>
        TransferItemsAsync(ownerId, saveId, quantities, toPc, deposit: true);

    private async Task<ItemTransferResult> TransferItemsAsync(string ownerId, int saveId, IReadOnlyDictionary<int, int> bagQuantities,
        IReadOnlyDictionary<int, int>? pcQuantities, bool deposit)
    {
        pcQuantities ??= new Dictionary<int, int>();
        if (bagQuantities.Values.Concat(pcQuantities.Values).Any(q => q < 0))
            return new(false, "Les quantités ne peuvent pas être négatives.");
        // Ce qui bouge à chaque endroit, puis le total par objet (ce qui entre dans l'inventaire ou en sort).
        var moves = new Dictionary<BagPlace, Dictionary<int, int>>
        {
            [BagPlace.Bag] = Ordered(bagQuantities.Where(q => q.Value > 0).ToDictionary()).ToDictionary(),
            [BagPlace.Pc] = Ordered(pcQuantities.Where(q => q.Value > 0).ToDictionary()).ToDictionary(),
        };
        var wanted = Ordered(moves.Values.SelectMany(m => m).GroupBy(m => m.Key).ToDictionary(g => g.Key, g => g.Sum(m => m.Value)))
            .ToDictionary();
        if (wanted.Count == 0)
            return new(false, "Choisis au moins un objet.");

        await using var db = await dbFactory.CreateDbContextAsync();
        var save = await db.StoredSaves.FirstOrDefaultAsync(s => s.Id == saveId && s.OwnerId == ownerId);
        if (save is null)
            return new(false, "Cette sauvegarde n'existe plus.");
        if (!StoredSaveService.TryRead(save.Data, save.FileName, out var sav))
            return new(false, "Impossible de lire cette sauvegarde.");
        var label = $"{save.GameName} — {save.TrainerName}";
        var hasPc = SaveBag.HasPc(sav);
        if (moves[BagPlace.Pc].Count > 0 && !hasPc)
            return new(false, "Cette sauvegarde n'a pas de PC d'objets.");

        if (deposit)
        {
            var owned = await db.OwnedItems.Where(i => i.OwnerId == ownerId).ToDictionaryAsync(i => i.ItemId, i => i.Count);
            foreach (var (item, quantity) in wanted)
            {
                if (quantity > owned.GetValueOrDefault(item))
                    return new(false, $"Ton inventaire ne contient que {owned.GetValueOrDefault(item)} × {info.ItemName(item)}.");
            }
        }

        // Modifie le sac et le PC, puis vérifie en relisant le fichier que le jeu a bien pris les nouvelles quantités.
        foreach (var (place, quantities) in moves.Where(m => m.Value.Count > 0))
        {
            var refused = deposit
                ? SaveBag.Deposit(sav, quantities, info.ItemName, save.GameName, place)
                : SaveBag.Withdraw(sav, quantities, info.ItemName, place);
            if (refused is not null)
                return new(false, refused);
        }
        var expectedBag = SaveBag.ReadItems(sav, BagPlace.Bag);
        var expectedPc = SaveBag.ReadItems(sav, BagPlace.Pc);
        var newData = sav.Write().ToArray();
        if (!StoredSaveService.TryRead(newData, save.FileName, out var check)
            || !SameCounts(SaveBag.ReadItems(check, BagPlace.Bag), expectedBag) || !SameCounts(SaveBag.ReadItems(check, BagPlace.Pc), expectedPc))
            return new(false, $"La modification du sac n'est pas prise en charge pour ce jeu ({save.GameName}).");

        var details = hasPc ? Describe(moves) : Describe(wanted);
        var error = await SaveServerChangeAsync(db, ownerId, save, newData, check.Money, deposit ? "depot-objets" : "retrait-objets",
            m => m.ItemsChanged = true,
            async now =>
            {
                foreach (var (item, quantity) in wanted)
                {
                    if (deposit)
                    {
                        // Débit conditionnel et atomique : un autre onglet a pu utiliser ces objets entre-temps.
                        var debited = await db.OwnedItems.Where(i => i.OwnerId == ownerId && i.ItemId == item && i.Count >= quantity)
                            .ExecuteUpdateAsync(i => i.SetProperty(x => x.Count, x => x.Count - quantity));
                        if (debited == 0)
                            return $"Ton inventaire ne contient plus assez de {info.ItemName(item)} (utilisé depuis un autre onglet ?).";
                    }
                    else
                    {
                        var credited = await db.OwnedItems.Where(i => i.OwnerId == ownerId && i.ItemId == item)
                            .ExecuteUpdateAsync(i => i.SetProperty(x => x.Count, x => x.Count + quantity));
                        if (credited == 0)
                            db.OwnedItems.Add(new OwnedItem { OwnerId = ownerId, ItemId = item, Count = quantity });
                    }
                }
                if (deposit)
                    await db.OwnedItems.Where(i => i.OwnerId == ownerId && i.Count == 0).ExecuteDeleteAsync();

                var balance = await db.Wallets.Where(w => w.OwnerId == ownerId).Select(w => w.Money).FirstOrDefaultAsync();
                var kind = deposit ? WalletOperationKind.DepositItemsToSave : WalletOperationKind.WithdrawItemsFromSave;
                AddOperation(db, ownerId, now, kind, 0, balance, save, details);
                return null;
            });
        if (error is not null)
            return new(false, error);

        logger.LogInformation("Utilisateur {User} : objets {Direction} la sauvegarde {Id} ({Details}).",
            ownerId, deposit ? "déposés dans" : "retirés de", save.Id, details);
        return deposit
            ? new(true, $"Déposé dans « {label} » : {details}. Télécharge la sauvegarde depuis « Mes sauvegardes » pour les avoir dans ton jeu.")
            : new(true, $"Retiré de « {label} » : {details}.");
    }

    private List<ItemStack> Stacks(IReadOnlyDictionary<int, int> items) => Ordered(items).Select(i => Stack(i.Key, i.Value)).ToList();

    private ItemStack Stack(int item, int count) =>
        new(item, SaveBag.CategoryOf(item) ?? ItemCategory.Balls, info.ShortItemName(item), PokemonInfo.ItemSpriteUrl(item), count);

    /// <summary>
    /// Objets ordinaires d'abord, puis Pokéballs, médicaments et baies, chacun dans l'ordre des numéros d'objets, puis CT et DT
    /// dans l'ordre alphabétique des capacités (leur numéro change d'un jeu à l'autre).
    /// </summary>
    private IEnumerable<KeyValuePair<int, int>> Ordered(IReadOnlyDictionary<int, int> items) =>
        items.OrderBy(i => SaveBag.CategoryOf(i.Key))
            .ThenBy(i => SaveBag.MoveOf(i.Key) is { } move ? info.MoveName(move) : "", StringComparer.Create(French, ignoreCase: true))
            .ThenBy(i => i.Key);

    /// <summary>« 10 × Poké Ball, 5 × Potion, 1 × CT/DT Séisme » (dans l'ordre de <see cref="Ordered"/>).</summary>
    private string Describe(IReadOnlyDictionary<int, int> quantities) =>
        string.Join(", ", Ordered(quantities).Select(q => $"{q.Value} × {info.ItemName(q.Key)}"));

    /// <summary>Pour une sauvegarde qui a un PC : « Sac : 3 × Potion ; PC : 1 × Pierre Lune ».</summary>
    private string Describe(Dictionary<BagPlace, Dictionary<int, int>> moves) =>
        string.Join(" ; ", moves.Where(m => m.Value.Count > 0).Select(m => $"{(m.Key == BagPlace.Pc ? "PC" : "Sac")} : {Describe(m.Value)}"));


    private static bool SameCounts(Dictionary<int, int> actual, Dictionary<int, int> expected) =>
        actual.Count == expected.Count && expected.All(e => actual.GetValueOrDefault(e.Key) == e.Value);

    // ---------- Modification d'une sauvegarde stockée ----------

    /// <summary>
    /// Enregistre <paramref name="newData"/> (déjà vérifié) comme nouvelle version de la sauvegarde stockée, pose ou complète la
    /// trace « modifiée sur le serveur » (<paramref name="describe"/> y ajoute la nature du changement), puis applique le changement
    /// au compte (<paramref name="applyToAccount"/> : porte-monnaie, objets, journal), le tout dans une seule transaction.
    /// Copie de sécurité avant et après. Renvoie un message d'erreur (rien n'est enregistré), ou null si c'est fait.
    /// </summary>
    private async Task<string?> SaveServerChangeAsync(
        ApplicationDbContext db, string ownerId, StoredSave save, byte[] newData, long moneyAfter, string backup,
        Action<SaveModification> describe, Func<DateTime, Task<string?>> applyToAccount)
    {
        await storedSaves.WriteBackupAsync(ownerId, save, $"{backup}-avant");

        var now = DateTime.UtcNow;
        await using var tx = await db.Database.BeginTransactionAsync();

        save.Data = newData;
        save.DownloadedSinceModification = false; // la version que le joueur a pu télécharger n'est plus la bonne
        save.Version++;

        // Trace rattachée à la partie (et non à la sauvegarde stockée) : elle survit à une suppression.
        var modification = await db.SaveModifications.FindAsync(save.IdentityKey);
        if (modification is null)
            db.SaveModifications.Add(modification = new SaveModification { IdentityKey = save.IdentityKey });
        modification.PlayTimeSeconds = save.PlayTimeSeconds; // une modification du serveur ne change pas le temps de jeu
        modification.MoneyAfter = moneyAfter;
        modification.DataHash = SaveModification.Hash(newData);
        modification.ModifiedAt = now;
        describe(modification);
        try
        {
            await db.SaveChangesAsync(); // jeton de concurrence : échoue si le fichier a changé depuis sa lecture
        }
        catch (DbUpdateConcurrencyException)
        {
            return StoredSaveService.ConflictResult().Message;
        }

        if (await applyToAccount(now) is { } error)
            return error; // transaction annulée : la sauvegarde reste telle quelle
        await db.SaveChangesAsync();
        await tx.CommitAsync();

        await storedSaves.WriteBackupAsync(ownerId, save, $"{backup}-apres");
        return null;
    }

    private static void AddOperation(ApplicationDbContext db, string ownerId, DateTime at, WalletOperationKind kind, long amount,
        long balanceAfter, StoredSave save, string details = "") =>
        db.WalletOperations.Add(new WalletOperation
        {
            OwnerId = ownerId,
            At = at,
            Kind = kind,
            Amount = amount,
            BalanceAfter = balanceAfter,
            StoredSaveId = save.Id,
            SaveLabel = WalletOperation.SaveLabelOf(save),
            Details = details,
        });
}
