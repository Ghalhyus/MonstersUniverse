using PKHeX.Core;

namespace MonsutaPotaru.Services;

/// <summary>Pokémon retiré de la sauvegarde, en attente d'être enregistré dans la banque.</summary>
public sealed record PendingDeposit(PKM Pokemon, int Box, int Slot);

/// <summary>Pokémon sorti de la banque et placé dans la sauvegarde, en attente de validation.</summary>
public sealed record PendingWithdrawal(int BankId, int Box, int Slot);

/// <summary>
/// Sauvegarde chargée par l'utilisateur pendant sa session (un circuit Blazor).
/// Les dépôts/retraits restent "en attente" tant que l'utilisateur n'a pas validé
/// et téléchargé la sauvegarde modifiée : rien n'est perdu ni dupliqué s'il abandonne.
/// </summary>
public sealed class SaveSession
{
    public SaveFile? Save { get; private set; }
    public string FileName { get; private set; } = "";
    public byte[]? OriginalData { get; private set; }

    public List<PendingDeposit> Deposits { get; } = [];
    public List<PendingWithdrawal> Withdrawals { get; } = [];

    public bool IsLoaded => Save is not null;
    public bool HasChanges => Deposits.Count > 0 || Withdrawals.Count > 0;

    public bool TryLoad(byte[] data, string fileName, out string error)
    {
        // PKHeX peut modifier le tableau : on lui passe une copie pour garder l'original intact.
        if (!SaveUtil.TryGetSaveFile(data.ToArray(), out var sav, fileName) || sav is null)
        {
            error = "Fichier non reconnu comme une sauvegarde Pokémon.";
            return false;
        }
        if (!sav.HasBox)
        {
            error = "Cette sauvegarde n'a pas de boîtes PC.";
            return false;
        }

        Save = sav;
        FileName = fileName;
        OriginalData = data;
        Deposits.Clear();
        Withdrawals.Clear();
        error = "";
        return true;
    }

    /// <summary>Annule toutes les opérations en attente en rechargeant la sauvegarde d'origine.</summary>
    public void Reset()
    {
        if (OriginalData is not null)
            TryLoad(OriginalData, FileName, out _);
    }

    public void Close()
    {
        Save = null;
        OriginalData = null;
        FileName = "";
        Deposits.Clear();
        Withdrawals.Clear();
    }

    public PKM? GetSlot(int box, int slot)
    {
        var pk = Save?.GetBoxSlotAtIndex(box, slot);
        return pk is null || pk.Species == 0 ? null : pk;
    }

    public bool IsLocked(int box, int slot) =>
        Save is not null && (Save.IsBoxSlotLocked(box, slot) || Save.IsBoxSlotOverwriteProtected(box, slot));

    public bool IsPendingWithdrawal(int bankId) => Withdrawals.Any(w => w.BankId == bankId);

    public bool IsPendingSlot(int box, int slot) => Withdrawals.Any(w => w.Box == box && w.Slot == slot);

    /// <summary>Retire un Pokémon de la sauvegarde (en mémoire) pour le déposer dans la banque.</summary>
    public bool TryDeposit(int box, int slot, out string error)
    {
        error = "";
        var sav = Save!;
        var pk = GetSlot(box, slot);
        if (pk is null) { error = "Emplacement vide."; return false; }
        if (IsLocked(box, slot)) { error = "Cet emplacement est verrouillé par le jeu (Boîte Combat...)."; return false; }

        var withdrawn = Withdrawals.FirstOrDefault(w => w.Box == box && w.Slot == slot);
        if (withdrawn is not null)
            Withdrawals.Remove(withdrawn); // Il sortait tout juste de la banque : on annule simplement le retrait.
        else
            Deposits.Add(new PendingDeposit(pk.Clone(), box, slot));

        sav.SetBoxSlotAtIndex(sav.BlankPKM, box, slot);
        return true;
    }

    /// <summary>Remet un dépôt en attente dans la sauvegarde.</summary>
    public bool UndoDeposit(PendingDeposit deposit)
    {
        var (box, slot) = GetSlot(deposit.Box, deposit.Slot) is null ? (deposit.Box, deposit.Slot) : FirstEmptySlot();
        if (box < 0) return false;
        Save!.SetBoxSlotAtIndex(deposit.Pokemon, box, slot);
        Deposits.Remove(deposit);
        return true;
    }

    /// <summary>Place un Pokémon de la banque dans le premier emplacement libre de la sauvegarde.</summary>
    public bool TryWithdraw(int bankId, PKM pk, out string error) => TryWithdraw(bankId, pk, -1, -1, out error);

    /// <summary>
    /// Place un Pokémon de la banque dans l'emplacement indiqué (glisser-déposer),
    /// ou dans le premier emplacement libre si <paramref name="box"/> est négatif.
    /// </summary>
    public bool TryWithdraw(int bankId, PKM pk, int box, int slot, out string error)
    {
        error = "";
        var sav = Save!;
        if (IsPendingWithdrawal(bankId)) { error = "Déjà retiré."; return false; }

        if (box >= 0)
        {
            if (IsLocked(box, slot)) { error = "Cet emplacement est verrouillé par le jeu (Boîte Combat...)."; return false; }
            if (GetSlot(box, slot) is not null) { error = "Cet emplacement est déjà occupé."; return false; }
        }

        if (pk.Species > sav.MaxSpeciesID)
        {
            error = $"Ce Pokémon n'existe pas dans cette génération (espèce n°{pk.Species}).";
            return false;
        }

        var converted = pk;
        if (pk.GetType() != sav.PKMType)
        {
            converted = EntityConverter.ConvertToType(pk, sav.PKMType, out var result);
            if (converted is null || !result.IsSuccess)
            {
                error = $"Conversion {pk.Extension.ToUpperInvariant()} → {sav.BlankPKM.Extension.ToUpperInvariant()} impossible ({result}).";
                return false;
            }
        }

        var problems = sav.EvaluateCompatibility(converted);
        if (problems.Count > 0)
        {
            error = "Incompatible avec cette sauvegarde : " + string.Join(" ; ", problems);
            return false;
        }

        if (box < 0)
        {
            (box, slot) = FirstEmptySlot();
            if (box < 0) { error = "Aucun emplacement libre dans les boîtes."; return false; }
        }

        sav.SetBoxSlotAtIndex(converted, box, slot);
        Withdrawals.Add(new PendingWithdrawal(bankId, box, slot));
        return true;
    }

    public void UndoWithdrawal(PendingWithdrawal withdrawal)
    {
        Save!.SetBoxSlotAtIndex(Save.BlankPKM, withdrawal.Box, withdrawal.Slot);
        Withdrawals.Remove(withdrawal);
    }

    /// <summary>Appelé après validation en base : la sauvegarde modifiée devient la nouvelle référence.</summary>
    public void MarkCommitted(byte[] newData)
    {
        OriginalData = newData;
        Deposits.Clear();
        Withdrawals.Clear();
    }

    private (int Box, int Slot) FirstEmptySlot() => FindEmptySlot(0, 0);

    /// <summary>
    /// Premier emplacement libre à partir de (<paramref name="fromBox"/>, <paramref name="fromSlot"/>) inclus,
    /// en parcourant les boîtes dans l'ordre puis en reprenant au début ; (-1, -1) si tout est plein.
    /// </summary>
    public (int Box, int Slot) FindEmptySlot(int fromBox, int fromSlot)
    {
        var sav = Save!;
        int perBox = sav.BoxSlotCount, total = sav.BoxCount * perBox;
        int start = Math.Clamp(fromBox * perBox + fromSlot, 0, total - 1);
        for (int i = 0; i < total; i++)
        {
            int index = (start + i) % total;
            int b = index / perBox, s = index % perBox;
            if (GetSlot(b, s) is null && !IsLocked(b, s))
                return (b, s);
        }
        return (-1, -1);
    }
}
