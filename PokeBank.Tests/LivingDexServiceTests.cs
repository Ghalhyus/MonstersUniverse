using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PKHeX.Core;
using PokeBank.Data;
using PokeBank.Services;

namespace PokeBank.Tests;

/// <summary>
/// Living dex : un exemplaire de chaque Pokémon du Pokédex d'un jeu, compté dans une sauvegarde stockée (1re génération :
/// Rouge, Bleu, Jaune ; 2e : Or, Argent, Cristal ; 3e : Rubis, Saphir, Émeraude, Rouge Feu, Vert Feuille ; 4e : Diamant,
/// Perle, Platine, HeartGold, SoulSilver). Vraies sauvegardes de Rouge, Bleu, Argent et SoulSilver (PokeBank.Tests/Sauvegardes).
/// </summary>
public sealed class LivingDexServiceTests : IDisposable
{
    private readonly TestDatabase _db = new("sacha", "ondine", "pierre");
    private readonly StoredSaveService _saves;
    private readonly LivingDexService _dex;

    public LivingDexServiceTests()
    {
        _saves = _db.StoredSaves();
        _dex = new LivingDexService(_db.Factory, NullLogger<LivingDexService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    private static byte[] RealSave(string file) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Sauvegardes", file));

    private async Task<int> Store(string owner, string file)
    {
        var result = await _saves.AddAsync(owner, file, RealSave(file));
        Assert.True(result.Ok, result.Message);
        return (await _saves.ListAsync(owner)).Single(s => s.FileName == file).Id;
    }

    [Fact]
    public void FirstGeneration_OnlyHasTheNationalMode_Of151()
    {
        foreach (var game in LivingDexGames.All.Where(g => g.Generation == 1))
        {
            Assert.Equal([LivingDexMode.National], LivingDexGames.Modes(game)); // une région, aucune forme : Régional et Complet cachés
            Assert.Equal(151, LivingDexGames.Slots(game, LivingDexMode.National).Count);
            Assert.Equal(LivingDexGames.Slots(game, LivingDexMode.National), LivingDexGames.Slots(game, LivingDexMode.Complete));
        }
        Assert.Equal("images/livingdex/rouge-bleu/", LivingDexGames.Get(LivingDexGame.Blue).SpriteFolder);
        Assert.Equal("images/livingdex/jaune/", LivingDexGames.Get(LivingDexGame.Yellow).SpriteFolder);
        Assert.Equal("images/livingdex/jaquettes/rouge.png", LivingDexGames.Get(LivingDexGame.Red).Cover);
    }

    [Fact]
    public void SecondGeneration_HasJohto_National_AndTheLettersOfUnown()
    {
        foreach (var game in LivingDexGames.All.Where(g => g.Generation == 2))
        {
            // Johto : les mêmes 251 Pokémon que le national, dans un autre ordre (Germignon d'abord) ; Complet : les 26
            // lettres de Zarbi (A à Z), enregistrées dans chaque Zarbi.
            Assert.Equal([LivingDexMode.Regional, LivingDexMode.National, LivingDexMode.Complete], LivingDexGames.Modes(game));
            var johto = LivingDexGames.Slots(game, LivingDexMode.Regional);
            Assert.Equal(251, johto.Count);
            Assert.Equal(Enumerable.Range(1, 251), johto.Select(s => (int)s.Species).Order());
            Assert.Equal([152, 153, 154], johto.Take(3).Select(s => (int)s.Species)); // Germignon, Macronium, Méganium
            Assert.Equal([150, 151, 251], johto.TakeLast(3).Select(s => (int)s.Species)); // Mewtwo, Mew, Celebi
            var complete = LivingDexGames.Slots(game, LivingDexMode.Complete);
            Assert.Equal(276, complete.Count); // 251 + 25
            Assert.Equal(Enumerable.Range(0, 26), complete.Where(s => s.Species == 201).Select(s => (int)s.Form));
            Assert.Equal("Johto", game.Region);
        }
        Assert.Equal("images/livingdex/argent/", LivingDexGames.Get(LivingDexGame.Silver).SpriteFolder);
        Assert.Equal("images/livingdex/jaquettes/cristal.png", LivingDexGames.Get(LivingDexGame.Crystal).Cover);
    }

    [Fact]
    public void Games_OfASave_RedAndBlueCannotBeTold()
    {
        foreach (var file in new[] { "rouge.sav", "bleu.sav" })
        {
            Assert.True(SaveUtil.TryGetSaveFile(RealSave(file), out var sav, file));
            Assert.Equal([LivingDexGame.Red, LivingDexGame.Blue], LivingDexGames.For(sav!)); // même format : au joueur de choisir
        }
        Assert.Equal([LivingDexGame.Yellow], LivingDexGames.For(new SAV1(LanguageID.French, GameVersion.YW)));

        // Or et Argent non plus ; Cristal oui.
        Assert.True(SaveUtil.TryGetSaveFile(RealSave("argent.sav"), out var silver, "argent.sav"));
        Assert.Equal([LivingDexGame.Gold, LivingDexGame.Silver], LivingDexGames.For(silver!));
        Assert.Equal([LivingDexGame.Crystal], LivingDexGames.For(new SAV2(LanguageID.French, GameVersion.C)));

        // 3e génération : Émeraude reconnu, pas Rubis de Saphir ni Rouge Feu de Vert Feuille ; ni Colosseum / XD.
        Assert.Equal([LivingDexGame.Emerald], LivingDexGames.For(new SAV3E()));
        Assert.Equal([LivingDexGame.Ruby, LivingDexGame.Sapphire], LivingDexGames.For(new SAV3RS()));
        Assert.Equal([LivingDexGame.FireRed, LivingDexGame.LeafGreen], LivingDexGames.For(new SAV3FRLG()));
        Assert.Empty(LivingDexGames.For(new SAV3XD()));

        // 4e génération : Platine reconnu, pas Diamant de Perle ni HeartGold de SoulSilver.
        Assert.Equal([LivingDexGame.Platinum], LivingDexGames.For(new SAV4Pt()));
        Assert.Equal([LivingDexGame.Diamond, LivingDexGame.Pearl], LivingDexGames.For(new SAV4DP()));
        Assert.True(SaveUtil.TryGetSaveFile(RealSave("soulsilver.sav"), out var soulSilver, "soulsilver.sav"));
        Assert.Equal([LivingDexGame.HeartGold, LivingDexGame.SoulSilver], LivingDexGames.For(soulSilver!));
        Assert.Empty(LivingDexGames.For(new SAV5B2W2()));
    }

    [Theory]
    [InlineData(LivingDexGame.Diamond, 151, 490, 529)] // Manaphy à la fin ; 493 + 27 lettres de Zarbi + 3 Deoxys + 2 × 2 capes + 2 mers
    [InlineData(LivingDexGame.Pearl, 151, 490, 529)]
    [InlineData(LivingDexGame.Platinum, 210, 487, 536)] // Giratina à la fin ; + 5 Motisma, Giratina Originel, Shaymin Céleste
    [InlineData(LivingDexGame.HeartGold, 256, 251, 537)] // Celebi à la fin ; + Pichu Troizépi
    [InlineData(LivingDexGame.SoulSilver, 256, 251, 537)]
    public void FourthGeneration_HasItsRegion_National493_AndTheFormsOfTheGame(LivingDexGame value, int regional, int last, int complete)
    {
        var game = LivingDexGames.Get(value);
        Assert.Equal([LivingDexMode.Regional, LivingDexMode.National, LivingDexMode.Complete], LivingDexGames.Modes(game));
        var dex = LivingDexGames.Slots(game, LivingDexMode.Regional);
        Assert.Equal(regional, dex.Count);
        Assert.Equal(last, dex[^1].Species);
        Assert.Equal(value is LivingDexGame.HeartGold or LivingDexGame.SoulSilver ? 152 : 387, dex[0].Species); // Germignon, Tortipouss
        Assert.Equal(493, LivingDexGames.Slots(game, LivingDexMode.National).Count);

        var slots = LivingDexGames.Slots(game, LivingDexMode.Complete);
        Assert.Equal(complete, slots.Count);
        int Forms(ushort species) => slots.Count(s => s.Species == species);
        Assert.Equal((28, 4, 3, 3, 2, 2), (Forms(201), Forms(386), Forms(412), Forms(413), Forms(422), Forms(423)));
        Assert.Equal((1, 1), (Forms(351), Forms(421))); // Morphéo et Ceriflor : formes de combat
        Assert.True(Assert.Single(slots, s => s.Species == 493).AnyForm); // Arceus : une case, quelle que soit sa Plaque
        var platinumForms = value is LivingDexGame.Diamond or LivingDexGame.Pearl ? 1 : 2; // Motisma, Giratina, Shaymin : à partir de Platine
        Assert.Equal((platinumForms == 1 ? 1 : 6, platinumForms, platinumForms), (Forms(479), Forms(487), Forms(492)));
        Assert.Equal(value is LivingDexGame.HeartGold or LivingDexGame.SoulSilver ? 2 : 1, Forms(172)); // Pichu Troizépi
    }

    [Fact]
    public void FourthGeneration_FormNames_AndSprites()
    {
        var game = LivingDexGames.Get(LivingDexGame.HeartGold);
        var slots = LivingDexGames.Slots(game, LivingDexMode.Complete);
        string Name(ushort species, byte form) => LivingDexGames.Name(game, slots.Single(s => s.Species == species && s.Form == form), LivingDexMode.Complete);
        string Sprite(ushort species, byte form) => LivingDexGames.Sprite(game, slots.Single(s => s.Species == species && s.Form == form));

        Assert.Equal(("Sancoki (mer Occident)", "Sancoki (mer Orient)"), (Name(422, 0), Name(422, 1)));
        Assert.Equal(("Cheniselle (cape Déchet)", "images/livingdex/heartgold-soulsilver/413-trash.png"), (Name(413, 2), Sprite(413, 2)));
        Assert.Equal(("Motisma", "Motisma (Chaleur)", "Motisma (Tonte)"), (Name(479, 0), Name(479, 1), Name(479, 5)));
        Assert.Equal(("images/livingdex/heartgold-soulsilver/479-heat.png", "images/livingdex/heartgold-soulsilver/479-mow.png"),
            (Sprite(479, 1), Sprite(479, 5)));
        Assert.Equal(("Giratina (forme Originelle)", "images/livingdex/heartgold-soulsilver/487-origin.png"), (Name(487, 1), Sprite(487, 1)));
        Assert.Equal(("Shaymin (forme Céleste)", "images/livingdex/heartgold-soulsilver/492-sky.png"), (Name(492, 1), Sprite(492, 1)));
        Assert.Equal(("Pichu (Troizépi)", "images/livingdex/heartgold-soulsilver/172-spiky-eared.png"), (Name(172, 1), Sprite(172, 1)));
        Assert.Equal(("Deoxys", "Deoxys (forme Attaque)"), (Name(386, 0), Name(386, 1)));
        Assert.Equal("Sancoki", LivingDexGames.Name(game, new(422, 0), LivingDexMode.National)); // la mer seulement en Complet

        // Shaymin Céleste : un deuxième Shaymin, de n'importe quelle forme, avec la Gracidée (case suivante).
        var sky = slots.Single(s => s.Species == 492 && s.Form == 1);
        Assert.Equal((true, (ushort)466), (sky.AnyForm, sky.KeyItem));
        Assert.True(slots.Single(s => s.Species == 492 && s.Form == 0).AnyForm);
        Assert.Equal("images/livingdex/diamant-perle/", LivingDexGames.Get(LivingDexGame.Pearl).SpriteFolder);
        Assert.Equal("images/livingdex/jaquettes/soulsilver.png", LivingDexGames.Get(LivingDexGame.SoulSilver).Cover);
    }

    [Fact]
    public void ThirdGeneration_HasItsRegion_National386_AndComplete413()
    {
        foreach (var game in LivingDexGames.All.Where(g => g.Generation == 3))
        {
            Assert.Equal([LivingDexMode.Regional, LivingDexMode.National, LivingDexMode.Complete], LivingDexGames.Modes(game));
            Assert.Equal(386, LivingDexGames.Slots(game, LivingDexMode.National).Count);
            var complete = LivingDexGames.Slots(game, LivingDexMode.Complete);
            Assert.Equal(413, complete.Count); // 386 + 27 lettres de Zarbi (A à Z, « ! », « ? »)
            Assert.Equal(Enumerable.Range(0, 28), complete.Where(s => s.Species == 201).Select(s => (int)s.Form));
            Assert.Single(complete, s => s.Species == 386); // Deoxys : la forme de son jeu seulement
            Assert.Single(complete, s => s.Species == 351); // Morphéo : ses formes n'existent qu'en combat
        }

        // Hoenn : 202 Pokémon dans l'ordre du jeu (Arcko d'abord, Deoxys à la fin).
        var hoenn = LivingDexGames.Slots(LivingDexGames.Get(LivingDexGame.Emerald), LivingDexMode.Regional);
        Assert.Equal(202, hoenn.Count);
        Assert.Equal([252, 253, 254], hoenn.Take(3).Select(s => (int)s.Species)); // Arcko, Massko, Jungko
        Assert.Equal([384, 385, 386], hoenn.TakeLast(3).Select(s => (int)s.Species)); // Rayquaza, Jirachi, Deoxys
        Assert.Equal("Hoenn", LivingDexGames.Get(LivingDexGame.Ruby).Region);

        // Rouge Feu / Vert Feuille : Kanto, les 151 premiers.
        var kanto = LivingDexGames.Slots(LivingDexGames.Get(LivingDexGame.LeafGreen), LivingDexMode.Regional);
        Assert.Equal(Enumerable.Range(1, 151), kanto.Select(s => (int)s.Species));
        Assert.Equal("Kanto", LivingDexGames.Get(LivingDexGame.FireRed).Region);
    }

    [Theory]
    [InlineData(LivingDexGame.Ruby, 0, "images/livingdex/rubis-saphir/386.png", "Deoxys")]
    [InlineData(LivingDexGame.Sapphire, 0, "images/livingdex/rubis-saphir/386.png", "Deoxys")]
    [InlineData(LivingDexGame.Emerald, 3, "images/livingdex/emeraude/386-speed.png", "Deoxys (forme Vitesse)")]
    [InlineData(LivingDexGame.FireRed, 1, "images/livingdex/rouge-feu-vert-feuille/386-attack.png", "Deoxys (forme Attaque)")]
    [InlineData(LivingDexGame.LeafGreen, 2, "images/livingdex/rouge-feu-vert-feuille/386-defense.png", "Deoxys (forme Défense)")]
    public void Deoxys_HasTheFormOfItsGame_AndAnyDeoxysFillsIt(LivingDexGame value, byte form, string sprite, string name)
    {
        var game = LivingDexGames.Get(value);
        // Tous les modes où il est (pas le Kanto de Rouge Feu / Vert Feuille).
        foreach (var mode in LivingDexGames.Modes(game).Where(m => LivingDexGames.Slots(game, m).Any(s => s.Species == 386)))
        {
            var slot = Assert.Single(LivingDexGames.Slots(game, mode), s => s.Species == 386);
            Assert.Equal(form, slot.Form);
            Assert.Equal(sprite, LivingDexGames.Sprite(game, slot));
            Assert.Equal(name, LivingDexGames.Name(game, slot, mode));
            Assert.True(LivingDexGames.Fills(slot, 386, 0, mode)); // un Deoxys de 3e génération n'enregistre pas sa forme
        }
    }

    [Fact]
    public void Complete_EachFormHasItsOwnNameAndSprite()
    {
        foreach (var game in LivingDexGames.All)
        {
            var slots = LivingDexGames.Slots(game, LivingDexMode.Complete);
            // Deux cases ne montrent jamais la même image : chaque forme a la sienne.
            Assert.Equal(slots.Count, slots.Select(s => LivingDexGames.Sprite(game, s)).Distinct().Count());
            Assert.Equal(slots.Count, slots.Select(s => LivingDexGames.Name(game, s, LivingDexMode.Complete)).Distinct().Count());
        }

        var emerald = LivingDexGames.Get(LivingDexGame.Emerald);
        var unown = LivingDexGames.Slots(emerald, LivingDexMode.Complete).Where(s => s.Species == 201).ToList();
        Assert.Equal(["Zarbi A", "Zarbi B"], unown.Take(2).Select(s => LivingDexGames.Name(emerald, s, LivingDexMode.Complete)));
        Assert.Equal(["images/livingdex/emeraude/201.png", "images/livingdex/emeraude/201-b.png"],
            unown.Take(2).Select(s => LivingDexGames.Sprite(emerald, s)));
        Assert.Equal(["images/livingdex/emeraude/201-exclamation.png", "images/livingdex/emeraude/201-question.png"],
            unown.TakeLast(2).Select(s => LivingDexGames.Sprite(emerald, s)));
        Assert.Equal("images/livingdex/cristal/201-z.png", LivingDexGames.Sprite(LivingDexGames.Get(LivingDexGame.Crystal), new(201, 25)));
        Assert.Equal("Zarbi", LivingDexGames.Name(emerald, unown[0], LivingDexMode.National)); // la lettre seulement en Complet

        // En Complet, la lettre compte ; en National, n'importe quel Zarbi remplit la case.
        Assert.False(LivingDexGames.Fills(unown[1], 201, 0, LivingDexMode.Complete));
        Assert.True(LivingDexGames.Fills(unown[1], 201, 1, LivingDexMode.Complete));
        Assert.True(LivingDexGames.Fills(new(201, 0), 201, 5, LivingDexMode.National));
    }

    [Fact]
    public void ThirdGeneration_Sprites_AndCovers()
    {
        Assert.Equal("images/livingdex/rubis-saphir/", LivingDexGames.Get(LivingDexGame.Sapphire).SpriteFolder);
        Assert.Equal("images/livingdex/rouge-feu-vert-feuille/", LivingDexGames.Get(LivingDexGame.LeafGreen).SpriteFolder);
        Assert.Equal("images/livingdex/jaquettes/emeraude.png", LivingDexGames.Get(LivingDexGame.Emerald).Cover);
        Assert.Equal("images/livingdex/jaquettes/vert-feuille.png", LivingDexGames.Get(LivingDexGame.LeafGreen).Cover);
        Assert.Equal(["Pokémon Rubis", "Pokémon Saphir", "Pokémon Émeraude", "Pokémon Rouge Feu", "Pokémon Vert Feuille"],
            LivingDexGames.All.Where(g => g.Generation == 3).Select(g => g.Name));
    }

    [Fact]
    public async Task Create_OnePerGame_OneSaveForOne()
    {
        var red = await Store("sacha", "rouge.sav");
        await Store("sacha", "rubis-omega.sav"); // 6e génération : pas de living dex

        var candidate = Assert.Single(await _dex.ListCandidatesAsync("sacha"));
        Assert.Equal(red, candidate.SaveId);
        Assert.Equal([LivingDexGame.Red, LivingDexGame.Blue], candidate.Games);

        Assert.Equal("Cette sauvegarde n'est pas une partie de Pokémon Jaune.", (await _dex.CreateAsync("sacha", red, LivingDexGame.Yellow)).Message);
        Assert.False((await _dex.CreateAsync("ondine", red, LivingDexGame.Red)).Ok); // pas la sienne
        var created = await _dex.CreateAsync("sacha", red, LivingDexGame.Red);
        Assert.True(created.Ok, created.Message);
        Assert.Equal("Cette sauvegarde a déjà un living dex.", (await _dex.CreateAsync("sacha", red, LivingDexGame.Blue)).Message);
        Assert.Empty(await _dex.ListCandidatesAsync("sacha")); // sauvegarde déjà liée

        // Une seconde partie de Rouge / Bleu : seul Bleu reste possible.
        var blue = await Store("sacha", "bleu.sav");
        Assert.Equal([LivingDexGame.Blue], Assert.Single(await _dex.ListCandidatesAsync("sacha")).Games);
        Assert.Equal("Tu as déjà un living dex Pokémon Rouge : supprime-le pour en créer un autre.",
            (await _dex.CreateAsync("sacha", blue, LivingDexGame.Red)).Message);
        Assert.True((await _dex.CreateAsync("sacha", blue, LivingDexGame.Blue)).Ok);

        Assert.Equal(["Pokémon Rouge", "Pokémon Bleu"], (await _dex.ListAsync("sacha")).Select(d => d.Game.Name));
        Assert.Empty(await _dex.ListAsync("ondine"));
        Assert.False(await _dex.DeleteAsync("ondine", created.Id!.Value));
        Assert.True(await _dex.DeleteAsync("sacha", created.Id!.Value));
        Assert.Equal([LivingDexGame.Red], Assert.Single(await _dex.ListCandidatesAsync("sacha")).Games); // Bleu reste pris
    }

    [Fact]
    public async Task Counts_ThePokemonOfTheSave_WithTheirPlace()
    {
        var red = await Store("sacha", "rouge.sav");
        var id = (await _dex.CreateAsync("sacha", red, LivingDexGame.Red)).Id!.Value;

        var view = await _dex.GetAsync("sacha", id, LivingDexMode.Complete); // pas proposé en 1re génération : National
        Assert.NotNull(view);
        Assert.Equal(LivingDexMode.National, view.Mode);
        Assert.Equal(151, view.Entries.Count);
        Assert.Equal(Enumerable.Range(1, 151), view.Entries.Select(e => (int)e.Species));

        List<(ushort Species, int Id, string? Location)> pokemon;
        using (var db = _db.NewContext())
            pokemon = db.BankPokemon.Where(p => p.StoredSaveId == red).OrderBy(p => p.Position)
                .Select(p => new { p.Species, p.Id, p.Location }).AsEnumerable().Select(p => (p.Species, p.Id, p.Location)).ToList();
        var species = pokemon.Select(p => p.Species).Distinct().ToList();
        Assert.Equal(species.Count, view.Owned);
        Assert.Equal(species.Count, Assert.Single(await _dex.ListAsync("sacha")).Owned);

        // Chaque espèce possédée : son premier exemplaire (équipe, puis boîtes), en couleur ; les autres manquent.
        var first = pokemon.First();
        var entry = view.Entries.Single(e => e.Species == first.Species);
        Assert.True(entry.Owned);
        Assert.Equal((first.Id, first.Location), (entry.BankId!.Value, entry.Location));
        Assert.Equal($"images/livingdex/rouge-bleu/{first.Species}.png", entry.Sprite);
        Assert.All(view.Entries.Where(e => !species.Contains(e.Species)), e => Assert.Null(e.BankId));
        Assert.Equal("Pikachu", view.Entries[24].Name);

        Assert.Null(await _dex.GetAsync("ondine", id));
    }

    [Fact]
    public async Task Johto_ShowsTheRegionalNumbers()
    {
        var silver = await Store("sacha", "argent.sav");
        var candidate = Assert.Single(await _dex.ListCandidatesAsync("sacha"));
        Assert.Equal([LivingDexGame.Gold, LivingDexGame.Silver], candidate.Games);
        var id = (await _dex.CreateAsync("sacha", silver, LivingDexGame.Silver)).Id!.Value;

        var johto = (await _dex.GetAsync("sacha", id, LivingDexMode.Regional))!;
        var national = (await _dex.GetAsync("sacha", id))!;
        Assert.Equal([LivingDexMode.Regional, LivingDexMode.National, LivingDexMode.Complete], johto.Modes);
        Assert.Equal(LivingDexMode.Regional, johto.Mode);

        // Complet : les 26 lettres de Zarbi en plus ; un Zarbi de la sauvegarde ne remplit que la case de sa lettre.
        var complete = (await _dex.GetAsync("sacha", id, LivingDexMode.Complete))!;
        Assert.Equal(276, complete.Entries.Count);
        Assert.Equal(26, complete.Entries.Count(e => e.Species == 201));
        Assert.Equal(national.Owned - (national.Entries.Single(e => e.Species == 201).Owned ? 1 : 0),
            complete.Entries.Count(e => e.Species != 201 && e.Owned));
        Assert.Equal((1, (ushort)152, "Germignon"), (johto.Entries[0].Number, johto.Entries[0].Species, johto.Entries[0].Name));
        Assert.Equal((152, (ushort)152), (national.Entries[151].Number, national.Entries[151].Species));
        Assert.Equal("images/livingdex/argent/152.png", johto.Entries[0].Sprite);
        Assert.Equal(national.Owned, johto.Owned); // mêmes Pokémon, autre ordre
        Assert.True(johto.Owned > 0);
    }

    /// <summary>SoulSilver de l'utilisateur, avec des Shaymin en boîte (Terrestres, PID propre au dresseur) et la Gracidée ou non.</summary>
    private static byte[] SoulSilverWith(string owner, int shaymins, bool gracidea)
    {
        Assert.True(SaveUtil.TryGetSaveFile(RealSave("soulsilver.sav"), out var sav, "soulsilver.sav"));
        var free = Enumerable.Range(0, sav!.SlotCount).Where(i => sav.GetBoxSlotAtIndex(i).Species == 0).ToList();
        for (var n = 0; n < shaymins; n++)
        {
            var pk = new PK4
            {
                Species = 492, Version = GameVersion.Pt, Language = (int)LanguageID.French, OriginalTrainerName = "Ghal",
                TID16 = 12345, PID = (uint)(0x49200000 + owner.Length * 16 + n), MetLevel = 30, Move1 = 33,
            };
            pk.CurrentLevel = 30;
            pk.ClearNickname();
            pk.RefreshChecksum();
            sav.SetBoxSlotAtIndex(pk, free[n]);
        }
        if (gracidea)
            SaveSessionTests.GiveItems(sav, (466, 1));
        return sav.Write().ToArray();
    }

    [Theory]
    [InlineData(1, true, false, "il faut un deuxième Shaymin")]
    [InlineData(2, false, false, "il faut la Gracidée dans le sac")]
    [InlineData(0, false, false, "il faut un deuxième Shaymin et la Gracidée dans le sac")]
    [InlineData(2, true, true, "Gracidée dans le sac")]
    public async Task ShayminSky_NeedsASecondShaymin_AndTheGracidea(int shaymins, bool gracidea, bool filled, string note)
    {
        var result = await _saves.AddAsync("sacha", "soulsilver.sav", SoulSilverWith("sacha", shaymins, gracidea));
        Assert.True(result.Ok, result.Message);
        var save = Assert.Single(await _saves.ListAsync("sacha")).Id;
        var id = (await _dex.CreateAsync("sacha", save, LivingDexGame.SoulSilver)).Id!.Value;

        var view = (await _dex.GetAsync("sacha", id, LivingDexMode.Complete))!;
        var land = view.Entries.Single(e => e.Species == 492 && e.Form == 0);
        var sky = view.Entries.Single(e => e.Species == 492 && e.Form == 1);
        Assert.Equal(shaymins > 0, land.Owned);
        Assert.Null(land.Note);
        Assert.Equal((filled, note), (sky.Owned, sky.Note));
        if (filled)
            Assert.NotEqual(land.BankId, sky.BankId); // deux Shaymin différents

        // En National, un seul Shaymin suffit, sans condition.
        var national = (await _dex.GetAsync("sacha", id))!.Entries.Single(e => e.Species == 492);
        Assert.Equal((shaymins > 0, (string?)null), (national.Owned, national.Note));
    }

    [Fact]
    public async Task SoulSilver_CountsItsPokemon()
    {
        var save = await Store("sacha", "soulsilver.sav");
        var candidate = Assert.Single(await _dex.ListCandidatesAsync("sacha"));
        Assert.Equal([LivingDexGame.HeartGold, LivingDexGame.SoulSilver], candidate.Games);
        Assert.Equal("Or HeartGold / Argent SoulSilver — Ghal · soulsilver.sav", candidate.Label); // pas « HGSS » (PokemonInfo.GroupNames)
        var id = (await _dex.CreateAsync("sacha", save, LivingDexGame.SoulSilver)).Id!.Value;

        var johto = (await _dex.GetAsync("sacha", id, LivingDexMode.Regional))!;
        Assert.Equal(256, johto.Entries.Count);
        Assert.Equal((1, (ushort)152, "images/livingdex/heartgold-soulsilver/152.png"),
            (johto.Entries[0].Number, johto.Entries[0].Species, johto.Entries[0].Sprite));
        var national = (await _dex.GetAsync("sacha", id))!;
        Assert.Equal(28, national.Owned); // 28 espèces dans la sauvegarde, toutes du Pokédex de Johto
        Assert.Equal(28, johto.Owned);

        // Complet : le Zarbi de la sauvegarde (D) ne remplit que sa lettre.
        var complete = (await _dex.GetAsync("sacha", id, LivingDexMode.Complete))!;
        Assert.Equal(["Zarbi D"], complete.Entries.Where(e => e.Species == 201 && e.Owned).Select(e => e.Name));
    }

    [Fact]
    public async Task LivingDex_LeavesWithItsSave()
    {
        var red = await Store("sacha", "rouge.sav");
        await _dex.CreateAsync("sacha", red, LivingDexGame.Red);
        Assert.Equal("Pokémon Rouge", (await _dex.NamesBySaveAsync("sacha"))[red]);

        Assert.True((await _saves.DeleteWithoutDownloadAsync("sacha", red)).Ok);
        Assert.Empty(await _dex.ListAsync("sacha"));
        using var db = _db.NewContext();
        Assert.Empty(db.LivingDexes);
    }
}
