using Microsoft.EntityFrameworkCore;
using MonsutaPotaru.Data;
using PKHeX.Core;

namespace MonsutaPotaru.Services;

/// <summary>Place d'un Pokémon dans une sauvegarde : équipe (<see cref="Box"/> = -1) ou boîte.</summary>
public readonly record struct TradeSlot(int Box, int Slot)
{
    public bool InParty => Box < 0;
    public static TradeSlot Party(int index) => new(-1, index);
}

/// <summary>
/// Pokémon qu'une sauvegarde peut recevoir de l'autre joueur : de quoi l'afficher, sa place, et son empreinte
/// (<see cref="PokemonFingerprint"/>) pour vérifier au moment de l'échange qu'il n'a pas changé entre-temps.
/// </summary>
public sealed record TradeCandidate(TradeSlot Slot, string Fingerprint, ushort Species, string Name, string SpeciesName, int Level,
    bool IsEgg, bool IsShiny, string SpriteUrl, string Location);

/// <summary>Deux sauvegardes qui peuvent échanger (même génération) : la mienne et celle de l'ami.</summary>
public sealed record TradeSavePair(StoredSaveInfo Mine, StoredSaveInfo Theirs);

/// <summary>Un côté d'un échange : le joueur, sa sauvegarde, et le Pokémon qu'il donne (choisi par l'autre).</summary>
public sealed record TradeSide(string UserId, int SaveId, TradeCandidate Gives);

/// <summary>Résultat d'un échange ; <see cref="FirstMessage"/> et <see cref="SecondMessage"/> : ce que chacun a reçu.</summary>
/// <param name="Retry">Échec qui permet de choisir de nouveau (un Pokémon a changé, sauvegarde modifiée entre-temps).</param>
public sealed record TradeOutcome(bool Ok, string FirstMessage, string SecondMessage, bool Retry = false)
{
    public static TradeOutcome Failed(string message, bool retry = true) => new(false, message, message, retry);
}

/// <summary>Statistiques d'échange d'un joueur (pour les futurs succès).</summary>
public sealed record TradeStats(int Total, int Partners);

/// <summary>
/// Échanges de Pokémon entre les sauvegardes stockées de deux amis (Multijoueurs → Échanges ; le déroulé en direct est
/// dans <see cref="TradeHub"/>). Deux sauvegardes échangent si elles sont du même contexte de jeu de PKHeX (même
/// génération : Noire ↔ Noire 2, Platine ↔ HeartGold...). Chaque Pokémon reçu prend la place de celui qui part, évolue si
/// l'échange le fait évoluer (<see cref="EvolveByTrade"/>), entre au Pokédex ; les deux sauvegardes sont écrites ensemble
/// et marquées « modifiées sur le serveur » (à télécharger, comme après un retrait).
/// </summary>
public sealed class TradeService(IDbContextFactory<ApplicationDbContext> dbFactory, StoredSaveService storedSaves, PokemonInfo info,
    ILogger<TradeService> logger)
{
    /// <summary>Paires de sauvegardes (la mienne, celle de l'ami) de même génération, les plus récentes d'abord.</summary>
    public async Task<List<TradeSavePair>> SavePairsAsync(string userId, string friendId)
    {
        var mine = await storedSaves.ListAsync(userId);
        var theirs = await storedSaves.ListAsync(friendId);
        var contexts = await ContextsAsync(mine.Select(s => s.Id).Concat(theirs.Select(s => s.Id)));
        return [.. from m in mine
                   from t in theirs
                   where contexts.TryGetValue(m.Id, out var cm) && contexts.TryGetValue(t.Id, out var ct) && cm == ct
                   orderby m.UpdatedAt descending, t.UpdatedAt descending
                   select new TradeSavePair(m, t)];
    }

    private async Task<Dictionary<int, EntityContext>> ContextsAsync(IEnumerable<int> saveIds)
    {
        var ids = saveIds.ToList();
        await using var db = await dbFactory.CreateDbContextAsync();
        var files = await db.StoredSaves.AsNoTracking().Where(s => ids.Contains(s.Id)).Select(s => new { s.Id, s.Data, s.FileName }).ToListAsync();
        var contexts = new Dictionary<int, EntityContext>();
        foreach (var f in files)
        {
            if (StoredSaveService.TryRead(f.Data, f.FileName, out var sav))
                contexts[f.Id] = sav.Context;
        }
        return contexts;
    }

    /// <summary>
    /// Pokémon de la sauvegarde de <paramref name="ownerId"/> que celle de <paramref name="receiverId"/> peut recevoir
    /// (équipe puis boîtes ; ni cases verrouillées, ni Pokémon que l'autre jeu ne connaît pas : espèce, forme, objet,
    /// capacité). Vide si l'une des deux n'existe plus.
    /// </summary>
    public async Task<List<TradeCandidate>> CandidatesAsync(string ownerId, int ownerSaveId, string receiverId, int receiverSaveId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var owner = await db.StoredSaves.AsNoTracking().FirstOrDefaultAsync(s => s.Id == ownerSaveId && s.OwnerId == ownerId);
        var receiver = await db.StoredSaves.AsNoTracking().FirstOrDefaultAsync(s => s.Id == receiverSaveId && s.OwnerId == receiverId);
        if (owner is null || receiver is null
            || !StoredSaveService.TryRead(owner.Data, owner.FileName, out var from)
            || !StoredSaveService.TryRead(receiver.Data, receiver.FileName, out var to)
            || from.Context != to.Context)
            return [];

        var candidates = new List<TradeCandidate>();
        foreach (var (slot, pk) in Pokemon(from))
        {
            if (ConvertFor(pk, to) is null)
                continue;
            var view = info.Describe(pk);
            var location = slot.InParty ? $"Équipe {slot.Slot + 1}" : $"Boîte {slot.Box + 1}, case {slot.Slot + 1}";
            candidates.Add(new TradeCandidate(slot, PokemonFingerprint.Of(pk), pk.Species, pk.IsEgg ? "Œuf" : view.Nickname,
                pk.IsEgg ? "" : view.SpeciesName, pk.IsEgg ? 0 : pk.CurrentLevel, pk.IsEgg, pk.IsShiny && !pk.IsEgg,
                PokemonSprites.Url(pk), location));
        }
        return candidates;
    }

    /// <summary>Pokémon de l'équipe puis des boîtes (cases verrouillées exclues : Boîte Combat, équipe de combat...).</summary>
    private static IEnumerable<(TradeSlot Slot, PKM Pokemon)> Pokemon(SaveFile sav)
    {
        for (int i = 0; i < sav.PartyCount; i++)
        {
            if (sav.GetPartySlotAtIndex(i) is { Species: > 0 } pk)
                yield return (TradeSlot.Party(i), pk);
        }
        for (int box = 0; box < sav.BoxCount; box++)
        {
            for (int slot = 0; slot < sav.BoxSlotCount; slot++)
            {
                if (!sav.IsBoxSlotLocked(box, slot) && sav.GetBoxSlotAtIndex(box, slot) is { Species: > 0 } pk)
                    yield return (new TradeSlot(box, slot), pk);
            }
        }
    }

    private static PKM? Get(SaveFile sav, TradeSlot slot) =>
        slot.InParty ? (slot.Slot < sav.PartyCount ? sav.GetPartySlotAtIndex(slot.Slot) : null)
        : slot.Box < sav.BoxCount && slot.Slot < sav.BoxSlotCount && !sav.IsBoxSlotLocked(slot.Box, slot.Slot) ? sav.GetBoxSlotAtIndex(slot.Box, slot.Slot)
        : null;

    /// <summary>Le Pokémon au format de l'autre sauvegarde, s'il peut y aller ; null sinon.</summary>
    private static PKM? ConvertFor(PKM pk, SaveFile to)
    {
        var converted = pk.GetType() == to.PKMType ? pk.Clone() : EntityConverter.ConvertToType(pk, to.PKMType, out _);
        return converted is not null && to.IsCompatiblePKM(converted) ? converted : null;
    }

    /// <summary>
    /// Une équipe garde au moins un Pokémon qui n'est pas un œuf : on ne donne pas son dernier contre un œuf.
    /// </summary>
    private static bool LeavesAPokemon(SaveFile sav, TradeSlot gives, PKM received)
    {
        if (!gives.InParty || !received.IsEgg)
            return true;
        for (int i = 0; i < sav.PartyCount; i++)
        {
            if (i != gives.Slot && sav.GetPartySlotAtIndex(i) is { Species: > 0, IsEgg: false })
                return true;
        }
        return false;
    }

    /// <summary>Pierre Stase, selon la numérotation des objets de chaque génération (pas d'objet tenu en 1re génération).</summary>
    private static int Everstone(PKM pk) => pk.Format switch { 1 => -1, 2 => 112, 3 => 195, _ => 229 };

    private const ushort Karrablast = 588, Shelmet = 616;

    /// <summary>
    /// Fait évoluer un Pokémon qui vient d'être échangé, selon les évolutions de son jeu (PKHeX) : par échange (Kadabra),
    /// avec un objet tenu, consommé (Onix et Peau Métal), ou contre l'autre de la paire Carabing / Escargaume. Ni un œuf, ni
    /// un Pokémon qui tient une Pierre Stase. Renvoie la nouvelle espèce, ou null s'il n'évolue pas.
    /// </summary>
    public static ushort? EvolveByTrade(PKM pk, ushort partnerSpecies)
    {
        if (pk.IsEgg || pk.HeldItem == Everstone(pk))
            return null;
        var tree = EvolutionTree.GetEvolutionTree(pk.Context);
        foreach (var evo in tree.Forward.GetForward(pk.Species, pk.Form).Span)
        {
            var possible = evo.Method switch
            {
                EvolutionType.Trade => true,
                EvolutionType.TradeHeldItem => pk.HeldItem == evo.Argument,
                EvolutionType.TradeShelmetKarrablast => partnerSpecies == (pk.Species == Karrablast ? Shelmet : Karrablast),
                _ => false,
            };
            if (!possible)
                continue;
            if (evo.Method == EvolutionType.TradeHeldItem)
                pk.HeldItem = 0; // l'objet est consommé
            var wasNicknamed = pk.IsNicknamed;
            pk.Species = evo.Species;
            pk.Form = evo.GetDestinationForm(pk.Form);
            if (!wasNicknamed)
                pk.ClearNickname(); // le nom de la nouvelle espèce
            if (pk.Format >= 3)
                pk.RefreshAbility(pk.AbilityNumber >> 1);
            pk.ResetPartyStats();
            return evo.Species;
        }
        return null;
    }

    /// <summary>
    /// Fait l'échange : chacun reçoit le Pokémon qu'il a choisi chez l'autre, à la place de celui qu'il donne. Tout ou rien :
    /// les deux sauvegardes sont écrites dans une même transaction (copies de sécurité avant et après).
    /// </summary>
    public async Task<TradeOutcome> ExecuteAsync(TradeSide first, TradeSide second)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var saveA = await db.StoredSaves.FirstOrDefaultAsync(s => s.Id == first.SaveId && s.OwnerId == first.UserId);
        var saveB = await db.StoredSaves.FirstOrDefaultAsync(s => s.Id == second.SaveId && s.OwnerId == second.UserId);
        if (saveA is null || saveB is null)
            return TradeOutcome.Failed("Une des deux sauvegardes n'existe plus : l'échange est annulé.", retry: false);
        if (!StoredSaveService.TryRead(saveA.Data, saveA.FileName, out var savA) || !StoredSaveService.TryRead(saveB.Data, saveB.FileName, out var savB)
            || savA.Context != savB.Context)
            return TradeOutcome.Failed("Une des deux sauvegardes ne peut plus être lue : l'échange est annulé.", retry: false);

        var pkA = Get(savA, first.Gives.Slot);
        var pkB = Get(savB, second.Gives.Slot);
        if (pkA is not { Species: > 0 } || PokemonFingerprint.Of(pkA) != first.Gives.Fingerprint
            || pkB is not { Species: > 0 } || PokemonFingerprint.Of(pkB) != second.Gives.Fingerprint)
            return TradeOutcome.Failed("Une des sauvegardes a changé depuis vos choix (mise à jour, autre échange) : choisissez de nouveau.");
        if (!LeavesAPokemon(savA, first.Gives.Slot, pkB) || !LeavesAPokemon(savB, second.Gives.Slot, pkA))
            return TradeOutcome.Failed("Une équipe garderait seulement des œufs : choisissez un autre Pokémon.");

        var toB = ConvertFor(pkA, savB);
        var toA = ConvertFor(pkB, savA);
        if (toA is null || toB is null)
            return TradeOutcome.Failed("Un des deux Pokémon ne peut plus aller dans l'autre jeu : choisissez-en un autre.");
        var receivedByA = ReceivedName(toA); // avant l'évolution : « tu as reçu Kadabra, qui a évolué en Alakazam »
        var receivedByB = ReceivedName(toB);
        var evolvedToB = EvolveByTrade(toB, pkB.Species);
        var evolvedToA = EvolveByTrade(toA, pkA.Species);
        Set(savA, first.Gives.Slot, toA);
        Set(savB, second.Gives.Slot, toB);
        var dataA = savA.Write().ToArray();
        var dataB = savB.Write().ToArray();

        await storedSaves.WriteBackupAsync(first.UserId, saveA, "echange-avant");
        await storedSaves.WriteBackupAsync(second.UserId, saveB, "echange-avant");
        var now = DateTime.UtcNow;
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await storedSaves.ApplyServerVersionAsync(db, saveA, savA, dataA, m => m.PokemonTraded = true, now);
            await storedSaves.ApplyServerVersionAsync(db, saveB, savB, dataB, m => m.PokemonTraded = true, now);
            db.TradeRecords.Add(new TradeRecord
            {
                At = now,
                FirstId = first.UserId,
                SecondId = second.UserId,
                FirstGave = pkA.Species,
                FirstGaveEvolvedTo = evolvedToB,
                SecondGave = pkB.Species,
                SecondGaveEvolvedTo = evolvedToA,
            });
            try
            {
                await db.SaveChangesAsync(); // jeton de concurrence : échoue si une sauvegarde a changé depuis sa lecture
            }
            catch (DbUpdateConcurrencyException)
            {
                return TradeOutcome.Failed("Une des sauvegardes vient d'être modifiée ailleurs : choisissez de nouveau.");
            }
            await tx.CommitAsync();
        }
        await storedSaves.WriteBackupAsync(first.UserId, saveA, "echange-apres");
        await storedSaves.WriteBackupAsync(second.UserId, saveB, "echange-apres");
        logger.LogInformation("Échange entre les comptes {First} (sauvegarde {SaveA}) et {Second} (sauvegarde {SaveB}).",
            first.UserId, saveA.Id, second.UserId, saveB.Id);

        return new(true, Received(receivedByA, toA, evolvedToA, saveA), Received(receivedByB, toB, evolvedToB, saveB));
    }

    private static void Set(SaveFile sav, TradeSlot slot, PKM pk)
    {
        if (slot.InParty)
            sav.SetPartySlotAtIndex(pk, slot.Slot, EntityImportSettings.All); // dresseur actuel, Pokédex
        else
            sav.SetBoxSlotAtIndex(pk, slot.Box, slot.Slot, EntityImportSettings.All);
    }

    /// <summary>« Kadabra », « Bob (Kadabra) » pour un Pokémon surnommé, « un œuf ».</summary>
    private string ReceivedName(PKM pk)
    {
        if (pk.IsEgg)
            return "un œuf";
        var view = info.Describe(pk);
        return view.Nickname == view.SpeciesName ? view.SpeciesName : $"{view.Nickname} ({view.SpeciesName})";
    }

    private string Received(string name, PKM pk, ushort? evolvedTo, StoredSave save)
    {
        var evolved = evolvedTo is null ? "" : $", qui a évolué en {info.Describe(pk).SpeciesName}";
        return $"Échange réussi : tu as reçu {name}{evolved} dans « {save.GameName} — {save.TrainerName} ». "
            + "Télécharge cette sauvegarde (« Mes sauvegardes », ou « Récupérer » depuis ta 3DS) et remets-la dans ton jeu.";
    }

    /// <summary>Échanges réussis du joueur : en tout, et avec combien de partenaires différents.</summary>
    public async Task<TradeStats> StatsAsync(string userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var partners = await db.TradeRecords.AsNoTracking()
            .Where(t => t.FirstId == userId || t.SecondId == userId)
            .Select(t => t.FirstId == userId ? t.SecondId : t.FirstId)
            .ToListAsync();
        return new(partners.Count, partners.Distinct().Count());
    }
}
