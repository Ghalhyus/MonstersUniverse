namespace PokeBank.Data;

/// <summary>Porte-monnaie d'un utilisateur sur le site (Pokédollars retirés de ses sauvegardes stockées).</summary>
public class Wallet
{
    public string OwnerId { get; set; } = "";
    public ApplicationUser? Owner { get; set; }

    public long Money { get; set; }
}

public enum WalletOperationKind
{
    /// <summary>Pokédollars retirés d'une sauvegarde stockée et crédités sur le porte-monnaie.</summary>
    WithdrawFromSave = 1,
    /// <summary>Achat d'emplacements de collection, payé avec le porte-monnaie.</summary>
    BuyCollectionSlots = 2,
    /// <summary>Objets (Pokéballs, médicaments) retirés du sac d'une sauvegarde stockée vers l'inventaire (montant nul, liste dans les détails).</summary>
    WithdrawItemsFromSave = 3,
    /// <summary>Objets de l'inventaire déposés dans le sac d'une sauvegarde stockée (montant nul, liste dans les détails).</summary>
    DepositItemsToSave = 4,
}

/// <summary>Ligne du journal des opérations de l'inventaire : argent et objets (jamais modifiée ni supprimée).</summary>
public class WalletOperation
{
    public int Id { get; set; }

    public string OwnerId { get; set; } = "";
    public ApplicationUser? Owner { get; set; }

    public DateTime At { get; set; }
    public WalletOperationKind Kind { get; set; }
    /// <summary>Variation du solde : positive pour un crédit.</summary>
    public long Amount { get; set; }
    public long BalanceAfter { get; set; }

    /// <summary>Sauvegarde concernée ; vidé si elle est supprimée depuis (le libellé reste).</summary>
    public int? StoredSaveId { get; set; }
    public StoredSave? StoredSave { get; set; }
    public string SaveLabel { get; set; } = "";
    /// <summary>Précision affichée dans le journal (ex. « +200 emplacements (1 000 → 1 200) », « 10 × Poké Ball, 2 × Hyper Ball »).</summary>
    public string Details { get; set; } = "";
}
