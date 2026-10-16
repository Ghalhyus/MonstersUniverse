using PKHeX.Core;
using PokeBank.Data;
using PokeBank.Services;

namespace PokeBank.Tests;

/// <summary>Filtres de « Ma collection » (pastilles) : caractéristiques enregistrées, requête, adresse.</summary>
public sealed class CollectionFiltersTests : IDisposable
{
    private readonly TestDatabase _db = new("sacha");

    public void Dispose() => _db.Dispose();

    private const byte Fire = 9, Water = 10, Electric = 12, Psychic = 13, Fairy = 17;
    private const byte Adamant = 3, Modest = 15;

    // ---------- Caractéristiques d'un Pokémon ----------

    [Fact]
    public void Types_AreThoseOfRecentGames_ForTheForm()
    {
        Assert.Equal((Fairy, Fairy), PokemonTraits.Types(35, 0)); // Mélofée : Normal en 1re génération, Fée aujourd'hui
        Assert.Equal((Electric, Psychic), PokemonTraits.Types(26, 1)); // Raichu d'Alola
        Assert.Equal(((byte)16, (byte)0), PokemonTraits.Types(19, 1)); // Rattata d'Alola (absent d'Écarlate/Violet)
    }

    [Fact]
    public void FirstGeneration_IvsAreScaled_NatureComesFromExperience_NoAbility()
    {
        var pk = new PK1 { Species = 25, EXP = 1234, DV16 = 0xFFFF };
        Assert.Equal([31, 31, 31, 31, 31, 31], PokemonTraits.Ivs(pk)); // 15 → 31
        Assert.Equal((byte)(1234 % 25), PokemonTraits.Nature(pk));

        var entry = new BankPokemon();
        PokemonTraits.Fill(entry, pk);
        Assert.Equal(0, entry.Ability);
    }

    [Fact]
    public void FormKinds_AndMegaStones()
    {
        Assert.Equal(FormKind.Standard, PokemonTraits.KindOf(19, 0, EntityContext.Gen7));
        Assert.Equal(FormKind.Alola, PokemonTraits.KindOf(19, 1, EntityContext.Gen7));
        Assert.Equal(FormKind.Galar, PokemonTraits.KindOf(555, 2, EntityContext.Gen8)); // Darumacho de Galar
        Assert.Equal(FormKind.Hisui, PokemonTraits.KindOf(59, 1, EntityContext.Gen9)); // Arcanin de Hisui
        Assert.Equal(FormKind.Paldea, PokemonTraits.KindOf(128, 1, EntityContext.Gen9)); // Tauros de Paldea
        Assert.Equal(FormKind.Other, PokemonTraits.KindOf(201, 1, EntityContext.Gen4)); // Zarbi B

        var pk = SaveSessionTests.CreatePk5(25, 20);
        var entry = BankService.CreateEntry(new PokemonInfo(), pk, "sacha", DateTime.UtcNow);
        Assert.False(entry.MegaReady);
        Assert.NotNull(entry.Nature); // rempli dès l'enregistrement
    }

    // ---------- Requête ----------

    private int _nextId = 1;

    private BankPokemon Add(byte type1, byte type2 = 255, byte gender = 0, byte nature = 0, int ability = 0, byte[]? ivs = null,
        FormKind form = FormKind.Standard, bool mega = false, bool shiny = false, bool egg = false)
    {
        ivs ??= new byte[6];
        var entry = new BankPokemon
        {
            Id = _nextId++, OwnerId = "sacha", SpeciesName = "Test", Nickname = "Test", DepositedAt = DateTime.UtcNow, Data = [1],
            Type1 = type1, Type2 = type2 == 255 ? type1 : type2, Gender = gender, Nature = nature, Ability = ability,
            IvHp = ivs[0], IvAtk = ivs[1], IvDef = ivs[2], IvSpA = ivs[3], IvSpD = ivs[4], IvSpe = ivs[5],
            FormKind = form, MegaReady = mega, IsShiny = shiny, IsEgg = egg,
        };
        using var db = _db.NewContext();
        db.BankPokemon.Add(entry);
        db.SaveChanges();
        return entry;
    }

    private async Task<int[]> Find(PokemonCriteria criteria)
    {
        var items = await _db.Bank().GetRangeAsync("sacha", null, 0, 100, CollectionFilter.All with { Criteria = criteria });
        return items.Select(i => i.Id).Order().ToArray();
    }

    [Fact]
    public async Task Filters_AddUpWithinACriterion_AndCombineAcrossCriteria()
    {
        var charmander = Add(Fire, gender: 0, nature: Adamant, ability: 66).Id;
        var squirtle = Add(Water, gender: 1, nature: Modest, ability: 67).Id;
        var raichu = Add(Electric, Psychic, gender: 1, form: FormKind.Alola, shiny: true).Id;

        Assert.Equal([charmander, squirtle], await Find(new() { Types = new HashSet<byte> { Fire, Water } })); // Feu ou Eau
        Assert.Equal([raichu], await Find(new() { Types = new HashSet<byte> { Psychic } })); // second type
        Assert.Equal([squirtle], await Find(new() { Types = new HashSet<byte> { Fire, Water }, Genders = new HashSet<byte> { 1 } })); // et femelle
        Assert.Equal([charmander], await Find(new() { Natures = new HashSet<byte> { Adamant } }));
        Assert.Equal([squirtle], await Find(new() { Abilities = new HashSet<int> { 67 } }));
        Assert.Equal([raichu], await Find(new() { Forms = new HashSet<FormKind> { FormKind.Alola } }));
        Assert.Equal([raichu], await Find(new() { Shiny = true }));
        Assert.Equal([charmander, squirtle], await Find(new() { Shiny = false }));
        Assert.Equal([charmander, squirtle, raichu], await Find(new() { Types = new HashSet<byte>() })); // filtre sans valeur
    }

    [Fact]
    public async Task Ivs_AreMinimums_PerStatistic()
    {
        var perfect = Add(Fire, ivs: [31, 31, 31, 31, 31, 31]).Id;
        var trickRoom = Add(Fire, ivs: [31, 31, 31, 31, 31, 0]).Id;
        Add(Fire, ivs: [20, 31, 31, 31, 31, 31]);

        Assert.Equal([perfect, trickRoom], await Find(new() { MinIvs = [31, 0, 0, 0, 0, 0] }));
        Assert.Equal([perfect], await Find(new() { MinIvs = [31, 0, 0, 0, 0, 25] }));
    }

    [Fact]
    public async Task Eggs_AreHiddenByOtherFilters_AndHaveTheirOwn()
    {
        var pokemon = Add(Fire).Id;
        var egg = Add(Fire, egg: true).Id; // un œuf de Salamèche ne doit pas sortir avec « Feu »
        var megaReady = Add(Water, mega: true).Id;

        Assert.Equal([pokemon], await Find(new() { Types = new HashSet<byte> { Fire } }));
        Assert.Equal([egg], await Find(new() { Egg = true }));
        Assert.Equal([pokemon, megaReady], await Find(new() { Egg = false }));
        Assert.Empty(await Find(new() { Egg = true, Types = new HashSet<byte> { Fire } })); // les autres filtres excluent les œufs
        Assert.Equal([megaReady], await Find(new() { Mega = true }));
        Assert.Equal(1, await _db.Bank().CountAsync("sacha", null, CollectionFilter.All with { Criteria = new() { Mega = true } }));
    }

    [Fact]
    public async Task Abilities_OfTheCollection_AreListed_WithoutEggs()
    {
        Add(Fire, ability: 66);
        Add(Water, ability: 67, egg: true);
        Add(Fire, ability: 0); // 1re ou 2e génération
        Assert.Equal([66], await _db.Bank().ListAbilitiesAsync("sacha"));
    }

    // ---------- Adresse ----------

    [Fact]
    public void Criteria_GoThroughTheAddress()
    {
        var criteria = new PokemonCriteria
        {
            Types = new HashSet<byte> { Water, Fire },
            Genders = new HashSet<byte> { 1, 2 },
            Natures = new HashSet<byte> { Modest },
            Abilities = new HashSet<int> { 66 },
            MinIvs = [31, 0, 0, 25, 0, 0],
            Forms = new HashSet<FormKind> { FormKind.Standard, FormKind.Hisui },
            Mega = true,
            Shiny = false,
            Egg = false,
        };
        var query = criteria.ToQuery();
        Assert.Equal("feu,eau", query["type"]); // ordre des types, noms sans accents
        Assert.Equal("f,aucun", query["sexe"]);
        Assert.Equal("modeste", query["nature"]);
        Assert.Equal("pv31,atqspe25", query["iv"]);
        Assert.Equal("standard,hisui", query["forme"]);
        Assert.Equal(("oui", "non", "non"), (query["mega"], query["chromatique"], query["oeuf"]));

        var read = PokemonCriteria.FromQuery(name => query.TryGetValue(name, out var value) ? value as string : null);
        Assert.Equal(criteria.Key, read.Key);
        Assert.True(PokemonCriteria.FromQuery(_ => null).IsEmpty);
        Assert.Equal([0, 0, 0, 0, 0, 31], PokemonCriteria.FromQuery(n => n == "iv" ? "vit99,inconnu5" : null).MinIvs); // 31 au plus
    }

    // ---------- Remplissage au démarrage ----------

    [Fact]
    public async Task FillFilterDetails_ReadsTheStoredBytes_OfOlderPokemon()
    {
        var pk = SaveSessionTests.CreatePk5(4, 20); // Salamèche
        pk.Nature = Nature.Modest;
        pk.IV_SPE = 31;
        using var db = _db.NewContext();
        var entry = BankService.CreateEntry(new PokemonInfo(), pk, "sacha", DateTime.UtcNow);
        (entry.Nature, entry.Type1, entry.IvSpe) = (null, 0, 0); // comme avant la migration CollectionFilters
        db.BankPokemon.Add(entry);
        db.BankPokemon.Add(new BankPokemon { OwnerId = "sacha", Data = [1, 2, 3], DepositedAt = DateTime.UtcNow }); // illisible
        await db.SaveChangesAsync();

        await BankService.FillFilterDetailsAsync(db);
        var filled = db.BankPokemon.Single(p => p.Id == entry.Id);
        Assert.Equal(((byte?)Modest, Fire, (byte)31), (filled.Nature, filled.Type1, filled.IvSpe));
        Assert.DoesNotContain(db.BankPokemon, p => p.Nature == null);
    }
}
