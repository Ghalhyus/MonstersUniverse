using Microsoft.Extensions.Logging.Abstractions;
using PKHeX.Core;
using MonsutaPotaru.Data;
using MonsutaPotaru.Services;

namespace MonsutaPotaru.Tests;

/// <summary>
/// Living dex global : compté sur le stockage interne, modes National (1 025), Complet (les cases Complet de tous les
/// jeux réunies) et Ultime (toutes les formes, femelles, Méga, Gigamax, Charmilly avec ses décorations).
/// </summary>
public sealed class GlobalLivingDexTests : IDisposable
{
    private readonly TestDatabase _db = new("sacha", "ondine");
    private readonly LivingDexService _dex;

    public GlobalLivingDexTests() => _dex = new LivingDexService(_db.Factory, NullLogger<LivingDexService>.Instance);

    public void Dispose() => _db.Dispose();

    private static IReadOnlyList<GlobalSlot> Slots(LivingDexMode mode) => GlobalLivingDex.Slots(mode);

    private static List<string> Names(LivingDexMode mode, ushort species) => Slots(mode).Where(s => s.Species == species).Select(s => s.Name).ToList();

    [Fact]
    public void Modes_AndTheirSlots()
    {
        var game = LivingDexGames.Get(LivingDexGame.Global);
        Assert.Equal([LivingDexMode.National, LivingDexMode.Complete, LivingDexMode.Ultimate], LivingDexGames.Modes(game));
        Assert.DoesNotContain(LivingDexGames.All, g => g.Game == LivingDexGame.Global); // pas parmi les jeux
        Assert.Equal(1025, Slots(LivingDexMode.National).Count);
        Assert.Equal(1283, Slots(LivingDexMode.Complete).Count);
        Assert.Equal(1578, Slots(LivingDexMode.Ultimate).Count);
        foreach (var mode in GlobalLivingDex.Modes)
            Assert.Equal(Slots(mode).Count, Slots(mode).Select(s => s.Name).Distinct().Count()); // chaque case a son nom
        Assert.Equal(("ultime", LivingDexMode.Ultimate, "Ultime"),
            (LivingDexGames.ModeSlug(LivingDexMode.Ultimate), LivingDexGames.ModeFromSlug("ultime"), LivingDexGames.ModeName(LivingDexMode.Ultimate)));
    }

    [Fact]
    public void Complete_GathersTheSlotsOfEveryGame()
    {
        var complete = Slots(LivingDexMode.Complete);
        Assert.Equal(["Prismillon"], Names(LivingDexMode.Complete, 666)); // un seul, n'importe quel motif
        Assert.True(complete.Single(s => s.Species == 666).AnyForm);
        Assert.Equal(["Shaymin", "Shaymin (forme Céleste)"], Names(LivingDexMode.Complete, 492)); // forme exacte (7e), sans Gracidée
        Assert.Equal(4, Names(LivingDexMode.Complete, 386).Count); // Deoxys
        Assert.Equal(["Dracaufeu", "Méga-Dracaufeu X", "Méga-Dracaufeu Y", "Dracaufeu Gigamax"], Names(LivingDexMode.Complete, 6));
        Assert.Equal(["Pikachu", "Pikachu Cosplayeur", "Pikachu Gigamax"], Names(LivingDexMode.Complete, 25));
        Assert.Equal(6, complete.Single(s => s.Name == "Pikachu Cosplayeur").AnyOfKeys!.Count); // n'importe quel costume
        Assert.Equal(9, Names(LivingDexMode.Complete, 869).Count(n => !n.EndsWith("Gigamax"))); // 9 crèmes
        // Sans Légendes Z-A : pas de Méga-Dracolosse (ni de Méga ajoutée par ce jeu).
        Assert.DoesNotContain(complete, s => s.Species == 149 && s.HeldItem != 0);
    }

    [Fact]
    public void Ultimate_HasEveryForm()
    {
        var ultimate = Slots(LivingDexMode.Ultimate);
        var pikachu = Names(LivingDexMode.Ultimate, 25);
        Assert.Contains("Pikachu ♀", pikachu); // femelle différente
        Assert.Contains("Pikachu Rockeur", pikachu); // costumes de Rubis Oméga / Saphir Alpha
        Assert.Contains("Pikachu (casquette Originale)", pikachu); // même numéro (1), autre forme en 7e
        Assert.Contains("Pikachu (casquette Monde)", pikachu);
        Assert.Contains("Pikachu Partenaire", pikachu); // Let's Go
        Assert.Equal(20, Names(LivingDexMode.Ultimate, 666).Count); // chaque motif de Prismillon
        Assert.Equal(64, Names(LivingDexMode.Ultimate, 869).Count); // 9 crèmes × 7 décorations, et le Gigamax
        Assert.Contains("Charmilly (Lait Ruby, Étoile)", Names(LivingDexMode.Ultimate, 869));
        Assert.Equal(["Mistigrix ♂", "Mistigrix ♀"], Names(LivingDexMode.Ultimate, 678));
        Assert.Equal(["Rattatac ♂", "Rattatac ♀", "Rattatac d'Alola", "Rattatac Dominant"], Names(LivingDexMode.Ultimate, 20));
        Assert.Equal(4, Names(LivingDexMode.Ultimate, 718).Count); // Zygarde 50 % et 10 %, avec ou sans Rassemblement ; pas Parfait
        Assert.Equal(19, Names(LivingDexMode.Ultimate, 493).Count); // Arceus : chaque Plaque, Légende ; pas « ??? »

        // Une seule case : Spinda (motifs infinis), Couafarel (coupes en équipe seulement), Koraidon (formes de monture).
        Assert.Equal((1, 1, 1), (Names(LivingDexMode.Ultimate, 327).Count, Names(LivingDexMode.Ultimate, 676).Count, Names(LivingDexMode.Ultimate, 1007).Count));
        Assert.Equal(5, Names(LivingDexMode.Ultimate, 670).Count); // Floette : pas l'Éternelle, jamais distribuée
        Assert.Equal(["Arcanin", "Arcanin de Hisui"], Names(LivingDexMode.Ultimate, 59)); // pas le Roi
        Assert.Equal(["Darumacho", "Darumacho de Galar"], Names(LivingDexMode.Ultimate, 555)); // pas la Transe

        // Formes de combat dues à un objet tenu : un exemplaire de plus qui le tient.
        Assert.Equal((ushort)1103, ultimate.Single(s => s.Name == "Zacian Suprême").HeldItem);
        Assert.Equal((ushort)923, ultimate.Single(s => s.Name == "Ultra-Necrozma").HeldItem);
        Assert.Equal((ushort)620, ultimate.Single(s => s.Name == "Méga-Rayquaza").Move);
        Assert.Equal(2, ultimate.Count(s => s.Species == 849 && s.Gigamax)); // Salarsen : un Gigamax par forme
    }

    private static GlobalLivingDex.Copy Copy(int id, ushort species, byte form = 0, EntityContext context = EntityContext.Gen9,
        byte gender = 0, int held = 0, byte? sweet = null, bool gigamax = false) =>
        new(id, species, form, context, gender, held, sweet, gigamax, false);

    private static string? Owner(LivingDexMode mode, IReadOnlyList<GlobalLivingDex.Copy> copies, string name)
    {
        var slots = Slots(mode);
        var assigned = GlobalLivingDex.Assign(slots, copies, mode);
        var index = slots.ToList().FindIndex(s => s.Name == name);
        return assigned[index] is { } c ? $"{c.Id}" : null;
    }

    [Fact]
    public void Assign_EachCopyFillsOneSlot_ByTheNameOfItsForm()
    {
        // Méga : l'exemplaire qui tient la gemme ; la case Dracaufeu prend l'autre.
        var charizards = new[] { Copy(1, 6, held: 660), Copy(2, 6) };
        Assert.Equal(("1", "2"), (Owner(LivingDexMode.Complete, charizards, "Méga-Dracaufeu X"), Owner(LivingDexMode.Complete, charizards, "Dracaufeu")));
        Assert.Null(Owner(LivingDexMode.Complete, [Copy(1, 6, held: 660)], "Dracaufeu")); // un seul : il va à la Méga

        // Pikachu 1 : Rockeur en 6e génération, casquette Originale ensuite.
        var rockStar = new[] { Copy(3, 25, 1, EntityContext.Gen6, gender: 1) };
        Assert.Equal("3", Owner(LivingDexMode.Ultimate, rockStar, "Pikachu Rockeur"));
        Assert.Null(Owner(LivingDexMode.Ultimate, rockStar, "Pikachu (casquette Originale)"));
        Assert.Equal("3", Owner(LivingDexMode.Complete, rockStar, "Pikachu Cosplayeur")); // en Complet, n'importe quel costume
        Assert.Equal("4", Owner(LivingDexMode.Ultimate, [Copy(4, 25, 1, EntityContext.Gen7)], "Pikachu (casquette Originale)"));

        // Femelle : sa propre case en Ultime ; en Complet, n'importe quel sexe.
        var female = new[] { Copy(5, 25, gender: 1) };
        Assert.Equal(("5", (string?)null), (Owner(LivingDexMode.Ultimate, female, "Pikachu ♀"), Owner(LivingDexMode.Ultimate, female, "Pikachu ♂")));
        Assert.Equal("5", Owner(LivingDexMode.Complete, female, "Pikachu"));

        // Dominant : sa case en Ultime, celle de sa forme ordinaire en Complet.
        var totem = new[] { Copy(6, 20, 2, EntityContext.Gen7) };
        Assert.Equal(("6", "6"), (Owner(LivingDexMode.Ultimate, totem, "Rattatac Dominant"), Owner(LivingDexMode.Complete, totem, "Rattatac d'Alola")));

        // Charmilly : crème et décoration.
        Assert.Equal("7", Owner(LivingDexMode.Ultimate, [Copy(7, 869, 1, EntityContext.Gen8, gender: 1, sweet: 3)], "Charmilly (Lait Ruby, Étoile)"));
        // Gigamax : un exemplaire de plus avec le facteur.
        var gmax = new[] { Copy(8, 6, gigamax: true) };
        Assert.Equal(("8", (string?)null), (Owner(LivingDexMode.Complete, gmax, "Dracaufeu Gigamax"), Owner(LivingDexMode.Complete, gmax, "Dracaufeu")));
    }

    /// <summary>Pokémon déposé dans le stockage interne (PID propre à chacun : pas de doublon).</summary>
    private async Task Deposit(string owner, ushort species, byte gender = 0, int index = 0, bool shiny = false)
    {
        var pk = SaveSessionTests.CreatePk5(species, 10, owner, index);
        pk.Gender = gender;
        if (shiny)
            pk.SetShiny();
        pk.RefreshChecksum();
        using var db = _db.NewContext();
        db.BankPokemon.Add(BankService.CreateEntry(new PokemonInfo(), pk, owner, DateTime.UtcNow));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Global_CountsTheInternalStorage_FirstOfTheList()
    {
        var created = await _dex.CreateGlobalAsync("sacha");
        Assert.True(created.Ok, created.Message);
        Assert.Equal("Tu as déjà un living dex global.", (await _dex.CreateGlobalAsync("sacha")).Message);
        Assert.Contains(LivingDexGame.Global, await _dex.TakenGamesAsync("sacha"));

        await Deposit("sacha", 25, gender: 1);
        await Deposit("sacha", 25, gender: 0, index: 1);
        await Deposit("sacha", 133);
        await Deposit("sacha", 1, shiny: true); // chromatique : ignoré
        await Deposit("ondine", 1); // pas le sien

        var national = (await _dex.GetAsync("sacha", created.Id!.Value))!;
        Assert.Equal(("Stockage interne", 1025, 2), (national.SaveLabel, national.Entries.Count, national.Owned));
        Assert.False(national.Entries.Single(e => e.Species == 1).Owned); // le Bulbizarre chromatique ne compte pas
        Assert.Equal("Stockage interne", national.Entries.Single(e => e.Species == 25).Location);
        var ultimate = (await _dex.GetAsync("sacha", created.Id!.Value, LivingDexMode.Ultimate))!;
        Assert.Equal(1578, ultimate.Entries.Count);
        Assert.Equal([true, true], ultimate.Entries.Where(e => e.Name is "Pikachu ♂" or "Pikachu ♀").Select(e => e.Owned));
        Assert.Equal("il faut un Dracaufeu de plus avec Dracaufite X", ultimate.Entries.Single(e => e.Name == "Méga-Dracaufeu X").Note);
        // Régional : pas pour le global, il compte en National.
        Assert.Equal(LivingDexMode.National, (await _dex.GetAsync("sacha", created.Id!.Value, LivingDexMode.Regional))!.Mode);

        // Vue générale : en tête ; en Ultime, seul lui.
        Assert.True((await _db.StoredSaves().AddAsync("sacha", "rouge.sav", File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Sauvegardes", "rouge.sav")))).Ok);
        var red = (await _dex.ListCandidatesAsync("sacha")).Single().SaveId;
        Assert.True((await _dex.CreateAsync("sacha", red, LivingDexGame.Red)).Ok);
        var cards = LivingDexGames.Sort(await _dex.ListAsync("sacha", LivingDexMode.Regional), LivingDexSort.Generation, descending: true);
        Assert.Equal([LivingDexGame.Global, LivingDexGame.Red], cards.Select(c => c.Game.Game));
        Assert.Equal((LivingDexMode.National, 2), (cards[0].Mode, cards[0].Owned)); // le Rouge ne compte pas pour le global
        Assert.Equal([LivingDexGame.Global], (await _dex.ListAsync("sacha", LivingDexMode.Ultimate)).Select(c => c.Game.Game));
        Assert.Empty(await _dex.NamesBySaveAsync("ondine"));
        Assert.Equal(["Pokémon Rouge"], (await _dex.NamesBySaveAsync("sacha")).Values);
    }
}
