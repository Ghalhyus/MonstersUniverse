using Microsoft.EntityFrameworkCore;
using PKHeX.Core;
using PokeBank.Data;

namespace PokeBank.Services;

/// <summary>Mode d'un living dex : quels Pokémon il demande.</summary>
public enum LivingDexMode
{
    /// <summary>Chaque Pokémon du Pokédex régional du jeu.</summary>
    Regional,
    /// <summary>Chaque Pokémon du Pokédex national que le jeu peut contenir.</summary>
    National,
    /// <summary>
    /// Chaque forme de chaque Pokémon du national : formes régionales, Méga-Évolutions, Gigamax, formes qui changent
    /// l'aspect (Sancoki / Tritosor...) ; ni le sexe, ni les formes des espèces qui en ont plus de 10 (Zarbi, Prismillon...).
    /// Seules les formes que le jeu peut contenir comptent (aucune en 1re génération).
    /// </summary>
    Complete,
}

/// <summary>Un jeu de living dex : nom, jaquette, sprites, et Pokémon de chaque mode.</summary>
/// <param name="Cover">Jaquette officielle (outils/images.py, partie « livingdex »).</param>
/// <param name="SpriteFolder">Sprites du jeu, « images/livingdex/rouge-bleu/25.png » (Pikachu).</param>
/// <param name="Regional">Pokédex régional (numéros nationaux, dans l'ordre du Pokédex du jeu).</param>
/// <param name="NationalMax">Dernier numéro national que le jeu peut contenir.</param>
public sealed record LivingDexGameInfo(LivingDexGame Game, string Name, string Cover, string SpriteFolder, byte Generation,
    IReadOnlyList<ushort> Regional, ushort NationalMax);

/// <summary>Une case du living dex : un Pokémon (et sa forme) demandé par le mode.</summary>
public sealed record LivingDexSlot(ushort Species, byte Form);

/// <summary>Case du living dex avec le Pokémon de la sauvegarde qui la remplit, s'il y en a un.</summary>
/// <param name="BankId">Pokémon de la sauvegarde (sa fiche), null : manquant.</param>
/// <param name="Location">« Boîte 3, case 12 », « Équipe 2 ».</param>
public sealed record LivingDexEntry(ushort Species, byte Form, string Name, string Sprite, int? BankId, string? Location)
{
    public bool Owned => BankId is not null;
}

public sealed record LivingDexSummary(int Id, LivingDexGameInfo Game, string SaveLabel, int Owned, int Total);

public sealed record LivingDexView(int Id, LivingDexGameInfo Game, string SaveLabel, IReadOnlyList<LivingDexMode> Modes,
    LivingDexMode Mode, IReadOnlyList<LivingDexEntry> Entries)
{
    public int Owned => Entries.Count(e => e.Owned);
}

/// <summary>Sauvegarde stockée qui peut recevoir un living dex, et les jeux possibles (Rouge ou Bleu : au choix).</summary>
public sealed record LivingDexCandidate(int SaveId, string Label, IReadOnlyList<LivingDexGame> Games);

public sealed record LivingDexResult(bool Ok, string Message, int? Id = null);

/// <summary>Jeux qui ont un living dex : 1re génération pour l'instant.</summary>
public static class LivingDexGames
{
    private static readonly ushort[] Kanto = Enumerable.Range(1, 151).Select(n => (ushort)n).ToArray();

    // Dossiers et fichiers : à garder en accord avec outils/images.py (LIVINGDEX_SPRITES, LIVINGDEX_COVERS).
    public static readonly IReadOnlyList<LivingDexGameInfo> All =
    [
        new(LivingDexGame.Red, "Pokémon Rouge", "images/livingdex/jaquettes/rouge.png", "images/livingdex/rouge-bleu/", 1, Kanto, 151),
        new(LivingDexGame.Blue, "Pokémon Bleu", "images/livingdex/jaquettes/bleu.png", "images/livingdex/rouge-bleu/", 1, Kanto, 151),
        new(LivingDexGame.Yellow, "Pokémon Jaune", "images/livingdex/jaquettes/jaune.png", "images/livingdex/jaune/", 1, Kanto, 151),
    ];

    public static LivingDexGameInfo Get(LivingDexGame game) => All.Single(g => g.Game == game);

    /// <summary>
    /// Jeux possibles pour une sauvegarde : PKHeX reconnaît Jaune, mais pas Rouge de Bleu (même format) : au joueur de
    /// choisir. Aucun pour une sauvegarde d'une autre génération.
    /// </summary>
    public static IReadOnlyList<LivingDexGame> For(SaveFile sav) => sav.Generation != 1 ? [] : sav.Version switch
    {
        GameVersion.YW => [LivingDexGame.Yellow],
        GameVersion.RD => [LivingDexGame.Red],
        GameVersion.BU or GameVersion.GN => [LivingDexGame.Blue], // Bleu japonais ou Vert : les sprites de Rouge / Bleu
        GameVersion.RB or GameVersion.RBY or GameVersion.Any => [LivingDexGame.Red, LivingDexGame.Blue],
        _ => [LivingDexGame.Red, LivingDexGame.Blue],
    };

    /// <summary>Pokémon demandés par un mode.</summary>
    public static IReadOnlyList<LivingDexSlot> Slots(LivingDexGameInfo game, LivingDexMode mode)
    {
        var national = Enumerable.Range(1, game.NationalMax).Select(n => new LivingDexSlot((ushort)n, 0));
        return mode switch
        {
            LivingDexMode.Regional => game.Regional.Select(n => new LivingDexSlot(n, 0)).ToList(),
            LivingDexMode.National => national.ToList(),
            _ => national.Concat(Forms(game)).OrderBy(s => s.Species).ThenBy(s => s.Form).ToList(),
        };
    }

    /// <summary>
    /// Formes en plus des Pokémon de base pour le mode Complet (voir <see cref="LivingDexMode.Complete"/>), réduites à
    /// celles que le jeu peut contenir. La 1re génération n'en a aucune : ni forme régionale, ni Méga-Évolution, ni
    /// Gigamax, ni forme d'aspect (Pikachu n'a pas de casquette avant la 7e génération, et en aurait plus de 10).
    /// </summary>
    private static IEnumerable<LivingDexSlot> Forms(LivingDexGameInfo game) => game.Generation switch
    {
        1 => [],
        _ => [],
    };

    /// <summary>
    /// Modes proposés, dans l'ordre Régional, National, Complet : un mode qui demande exactement les mêmes Pokémon
    /// qu'un autre est caché (en 1re génération, une seule région et aucune forme : il ne reste que le National).
    /// </summary>
    public static IReadOnlyList<LivingDexMode> Modes(LivingDexGameInfo game)
    {
        var national = Slots(game, LivingDexMode.National);
        var modes = new List<LivingDexMode>();
        if (!Slots(game, LivingDexMode.Regional).SequenceEqual(national))
            modes.Add(LivingDexMode.Regional);
        modes.Add(LivingDexMode.National);
        if (!Slots(game, LivingDexMode.Complete).SequenceEqual(national))
            modes.Add(LivingDexMode.Complete);
        return modes;
    }

    public static string ModeName(LivingDexMode mode) => mode switch
    {
        LivingDexMode.Regional => "Régional",
        LivingDexMode.National => "National",
        _ => "Complet",
    };
}

/// <summary>
/// Living dex : un exemplaire de chaque Pokémon du Pokédex d'un jeu, compté dans une sauvegarde stockée (équipe et
/// boîtes, telle qu'envoyée sur le site). Au plus un par jeu et par compte ; supprimé avec sa sauvegarde.
/// </summary>
public sealed class LivingDexService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    ILogger<LivingDexService> logger)
{
    private static readonly string[] FrenchSpecies = GameInfo.GetStrings("fr").specieslist;

    public async Task<List<LivingDexSummary>> ListAsync(string ownerId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var dexes = await db.LivingDexes.AsNoTracking().Where(d => d.OwnerId == ownerId)
            .Select(d => new { d.Id, d.Game, d.StoredSaveId, d.StoredSave!.TrainerName, d.StoredSave.FileName })
            .ToListAsync();
        var summaries = new List<LivingDexSummary>();
        foreach (var d in dexes.OrderBy(d => d.Game))
        {
            var game = LivingDexGames.Get(d.Game);
            var slots = LivingDexGames.Slots(game, LivingDexMode.National);
            var owned = await OwnedAsync(db, d.StoredSaveId);
            summaries.Add(new(d.Id, game, SaveLabel(d.TrainerName, d.FileName),
                slots.Count(s => Find(owned, s, LivingDexMode.National) is not null), slots.Count));
        }
        return summaries;
    }

    /// <summary>Sauvegardes stockées qui peuvent recevoir un living dex : de 1re génération, pas encore liées, avec un jeu libre.</summary>
    public async Task<List<LivingDexCandidate>> ListCandidatesAsync(string ownerId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var taken = await db.LivingDexes.Where(d => d.OwnerId == ownerId).Select(d => d.Game).ToListAsync();
        var linked = db.LivingDexes.Select(d => d.StoredSaveId);
        var saves = await db.StoredSaves.AsNoTracking()
            .Where(s => s.OwnerId == ownerId && s.Generation == 1 && !linked.Contains(s.Id))
            .OrderBy(s => s.UploadedAt)
            .Select(s => new { s.Id, s.GameName, s.TrainerName, s.FileName, s.Data })
            .ToListAsync();
        var candidates = new List<LivingDexCandidate>();
        foreach (var s in saves)
        {
            if (!StoredSaveService.TryRead(s.Data, s.FileName, out var sav))
                continue;
            var games = LivingDexGames.For(sav).Except(taken).ToList();
            if (games.Count > 0)
                candidates.Add(new(s.Id, $"{s.GameName} — {SaveLabel(s.TrainerName, s.FileName)}", games));
        }
        return candidates;
    }

    public async Task<LivingDexResult> CreateAsync(string ownerId, int saveId, LivingDexGame game)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var save = await db.StoredSaves.AsNoTracking().Where(s => s.Id == saveId && s.OwnerId == ownerId)
            .Select(s => new { s.FileName, s.Data }).FirstOrDefaultAsync();
        if (save is null)
            return new(false, "Cette sauvegarde n'existe plus.");
        var name = LivingDexGames.Get(game).Name;
        if (!StoredSaveService.TryRead(save.Data, save.FileName, out var sav) || !LivingDexGames.For(sav).Contains(game))
            return new(false, $"Cette sauvegarde n'est pas une partie de {name}.");
        if (await db.LivingDexes.AnyAsync(d => d.OwnerId == ownerId && d.Game == game))
            return new(false, $"Tu as déjà un living dex {name} : supprime-le pour en créer un autre.");
        if (await db.LivingDexes.AnyAsync(d => d.StoredSaveId == saveId))
            return new(false, "Cette sauvegarde a déjà un living dex.");

        var dex = new LivingDex { OwnerId = ownerId, Game = game, StoredSaveId = saveId, CreatedAt = DateTime.UtcNow };
        db.LivingDexes.Add(dex);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException) // créé au même moment depuis un autre onglet (index uniques)
        {
            return new(false, $"Tu as déjà un living dex {name}, ou cette sauvegarde en a déjà un.");
        }
        logger.LogInformation("Utilisateur {User} : living dex {Game} créé sur la sauvegarde {Save}.", ownerId, game, saveId);
        return new(true, $"Living dex {name} créé.", dex.Id);
    }

    /// <summary>Living dex dans un mode (le National si celui demandé n'est pas proposé pour ce jeu), null s'il n'existe pas.</summary>
    public async Task<LivingDexView?> GetAsync(string ownerId, int id, LivingDexMode mode = LivingDexMode.National)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var dex = await db.LivingDexes.AsNoTracking().Where(d => d.Id == id && d.OwnerId == ownerId)
            .Select(d => new { d.Id, d.Game, d.StoredSaveId, d.StoredSave!.TrainerName, d.StoredSave.FileName })
            .FirstOrDefaultAsync();
        if (dex is null)
            return null;
        var game = LivingDexGames.Get(dex.Game);
        var modes = LivingDexGames.Modes(game);
        if (!modes.Contains(mode))
            mode = LivingDexMode.National;
        var owned = await OwnedAsync(db, dex.StoredSaveId);
        var entries = LivingDexGames.Slots(game, mode).Select(slot =>
        {
            var copy = Find(owned, slot, mode);
            return new LivingDexEntry(slot.Species, slot.Form, FrenchSpecies[slot.Species], $"{game.SpriteFolder}{slot.Species}.png",
                copy?.Id, copy?.Location);
        }).ToList();
        return new(dex.Id, game, SaveLabel(dex.TrainerName, dex.FileName), modes, mode, entries);
    }

    public async Task<bool> DeleteAsync(string ownerId, int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var deleted = await db.LivingDexes.Where(d => d.Id == id && d.OwnerId == ownerId).ExecuteDeleteAsync();
        if (deleted > 0)
            logger.LogInformation("Utilisateur {User} : living dex {Id} supprimé.", ownerId, id);
        return deleted > 0;
    }

    /// <summary>Living dex de chaque sauvegarde (nom du jeu), pour prévenir qu'il partira avec elle (« Supprimer »).</summary>
    public async Task<Dictionary<int, string>> NamesBySaveAsync(string ownerId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return (await db.LivingDexes.Where(d => d.OwnerId == ownerId).Select(d => new { d.StoredSaveId, d.Game }).ToListAsync())
            .ToDictionary(d => d.StoredSaveId, d => LivingDexGames.Get(d.Game).Name);
    }

    /// <summary>Pokémon de la sauvegarde qui remplit une case.</summary>
    private sealed record OwnedCopy(int Id, ushort Species, byte Form, string? Location);

    /// <summary>Pokémon de la sauvegarde (les œufs ne comptent pas), dans son ordre : équipe, puis boîtes.</summary>
    private static async Task<List<OwnedCopy>> OwnedAsync(ApplicationDbContext db, int saveId) =>
        await db.BankPokemon.AsNoTracking()
            .Where(p => p.StoredSaveId == saveId && !p.IsEgg)
            .OrderBy(p => p.Position)
            .Select(p => new OwnedCopy(p.Id, p.Species, p.Form, p.Location))
            .ToListAsync();

    /// <summary>
    /// Premier Pokémon de la sauvegarde qui remplit la case : la même espèce, sous n'importe quelle forme en Régional et
    /// National (un Raichu d'Alola est un Raichu), la forme exacte en Complet.
    /// </summary>
    private static OwnedCopy? Find(List<OwnedCopy> owned, LivingDexSlot slot, LivingDexMode mode) =>
        owned.FirstOrDefault(p => p.Species == slot.Species && (mode != LivingDexMode.Complete || p.Form == slot.Form));

    private static string SaveLabel(string trainer, string fileName) => $"{trainer} · {fileName}";
}
