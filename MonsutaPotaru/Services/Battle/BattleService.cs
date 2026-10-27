using Microsoft.EntityFrameworkCore;
using MonsutaPotaru.Data;
using PKHeX.Core;

namespace MonsutaPotaru.Services;

/// <summary>Deux sauvegardes qui peuvent combattre (même génération, prise en charge) : la mienne et celle de l'ami.</summary>
public sealed record BattleSavePair(StoredSaveInfo Mine, StoredSaveInfo Theirs, string Format);

/// <summary>
/// Pokémon d'une sauvegarde proposé pour l'équipe : de quoi l'afficher (fiche, légalité), sa place, et son empreinte
/// (<see cref="PokemonFingerprint"/>) pour vérifier au début du combat qu'il n'a pas changé entre-temps.
/// </summary>
public sealed record BattleCandidate(TradeSlot Slot, string Fingerprint, ushort Species, string Name, string SpeciesName, int Level,
    string Gender, bool IsShiny, bool IsLegal, string SpriteUrl, string Location, string Item, string Ability, IReadOnlyList<string> Moves);

/// <summary>Combats terminés du joueur : victoires, défaites, matchs nuls, et contre combien d'adversaires différents.</summary>
public sealed record BattleRecordStats(int Won, int Lost, int Draws, int Opponents)
{
    public int Total => Won + Lost + Draws;
}

/// <summary>
/// Équipe prête pour le simulateur, le nombre de ses Pokémon signalés illégaux et les sprites du jeu de la sauvegarde
/// (ce que le joueur verra) ; ou le refus.
/// </summary>
public sealed record BattleTeamResult(BattlePlayer? Team, int Illegal, string? Error, BattleSprites? Sprites = null);

/// <summary>
/// Combats (Multijoueurs → Combats ; le déroulé en direct est dans <see cref="BattleHub"/>) : sauvegardes des deux amis
/// qui peuvent combattre, Pokémon proposés pour l'équipe, équipe vérifiée et convertie pour le simulateur. Les
/// sauvegardes ne sont jamais modifiées : combat amical, sans expérience ni objet consommé.
/// </summary>
public sealed class BattleService(IDbContextFactory<ApplicationDbContext> dbFactory, StoredSaveService storedSaves, PokemonInfo info)
{
    /// <summary>
    /// Paires de sauvegardes (la mienne, celle de l'ami) de même génération, dans une génération qui a ses combats
    /// (<see cref="BattleTeam.Format"/>), les plus récentes d'abord. Vide si ce n'est pas (ou plus) un ami.
    /// </summary>
    public async Task<List<BattleSavePair>> SavePairsAsync(string userId, string friendId)
    {
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            if (await FriendService.RelationAsync(db, userId, friendId) != PlayerRelation.Friend)
                return [];
        }
        var mine = await storedSaves.ListAsync(userId);
        var theirs = await storedSaves.ListAsync(friendId);
        return [.. from m in mine
                   from t in theirs
                   where m.Generation == t.Generation && BattleTeam.Format(m.Generation) is not null
                   orderby m.UpdatedAt descending, t.UpdatedAt descending
                   select new BattleSavePair(m, t, BattleTeam.Format(m.Generation)!)];
    }

    private async Task<SaveFile?> ReadAsync(string ownerId, int saveId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var save = await db.StoredSaves.AsNoTracking().FirstOrDefaultAsync(s => s.Id == saveId && s.OwnerId == ownerId);
        return save is not null && StoredSaveService.TryRead(save.Data, save.FileName, out var sav) ? sav : null;
    }

    /// <summary>Pokémon de cette sauvegarde qui peuvent combattre (équipe puis boîtes, sans les œufs). Vide si elle n'est pas à lui.</summary>
    public async Task<List<BattleCandidate>> CandidatesAsync(string ownerId, int saveId)
    {
        if (await ReadAsync(ownerId, saveId) is not { } sav)
            return [];
        return BattleTeam.Candidates(sav).Select(c =>
        {
            var view = info.Describe(c.Pokemon);
            var slot = new TradeSlot(c.Box, c.Slot);
            var location = slot.InParty ? $"Équipe {slot.Slot + 1}" : $"Boîte {slot.Box + 1}, case {slot.Slot + 1}";
            return new BattleCandidate(slot, PokemonFingerprint.Of(c.Pokemon), c.Pokemon.Species, view.Nickname, view.SpeciesName,
                c.Pokemon.CurrentLevel, view.Gender, view.IsShiny, view.IsLegal, view.SpriteUrl, location, view.HeldItem, view.Ability, view.Moves);
        }).ToList();
    }

    /// <summary>Combats terminés du joueur (<see cref="BattleRecorder"/>).</summary>
    public async Task<BattleRecordStats> StatsAsync(string userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var battles = await db.BattleRecords.AsNoTracking()
            .Where(b => b.FirstId == userId || b.SecondId == userId)
            .Select(b => new { Opponent = b.FirstId == userId ? b.SecondId : b.FirstId, b.WinnerId })
            .ToListAsync();
        return new(battles.Count(b => b.WinnerId == userId), battles.Count(b => b.WinnerId != null && b.WinnerId != userId),
            battles.Count(b => b.WinnerId == null), battles.Select(b => b.Opponent).Distinct().Count());
    }

    /// <summary>Taille d'équipe demandée : 6, ou tous ses Pokémon si la sauvegarde en a moins.</summary>
    public static int TeamSize(int available) => Math.Min(BattleTeam.MaxSize, available);

    /// <summary>
    /// Équipe choisie (dans l'ordre : le premier entre en combat), relue dans la sauvegarde : chaque Pokémon doit être
    /// encore à sa place (empreinte), pas un œuf, sans doublon ; 6 Pokémon, ou tous si la sauvegarde en a moins.
    /// </summary>
    public async Task<BattleTeamResult> TeamAsync(string ownerId, int saveId, string pseudo, IReadOnlyList<BattleCandidate> chosen)
    {
        if (await ReadAsync(ownerId, saveId) is not { } sav)
            return new(null, 0, "Cette sauvegarde n'existe plus.");
        var available = BattleTeam.Candidates(sav).ToList();
        var size = TeamSize(available.Count);
        if (chosen.Count != size || chosen.Select(c => c.Slot).Distinct().Count() != chosen.Count)
            return new(null, 0, $"Choisis {size} Pokémon différents.");

        var sets = new List<BattleSet>();
        var illegal = 0;
        foreach (var candidate in chosen)
        {
            var match = available.FirstOrDefault(a => a.Box == candidate.Slot.Box && a.Slot == candidate.Slot.Slot);
            if (match.Pokemon is null || PokemonFingerprint.Of(match.Pokemon) != candidate.Fingerprint)
                return new(null, 0, "Ta sauvegarde a changé depuis le choix de l'équipe : choisis-la de nouveau.");
            sets.Add(BattleTeam.ToSet(match.Pokemon, sav));
            if (!PokemonInfo.IsLegal(match.Pokemon))
                illegal++;
        }
        return new(new BattlePlayer(pseudo, sets), illegal, null, BattleSprites.For(sav));
    }
}
