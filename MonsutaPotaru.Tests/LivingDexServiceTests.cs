using System.Buffers.Binary;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PKHeX.Core;
using MonsutaPotaru.Data;
using MonsutaPotaru.Services;

namespace MonsutaPotaru.Tests;

/// <summary>
/// Living dex : un exemplaire de chaque Pokémon du Pokédex d'un jeu, compté dans une sauvegarde stockée (1re génération :
/// Rouge, Bleu, Jaune ; 2e : Or, Argent, Cristal ; 3e : Rubis, Saphir, Émeraude, Rouge Feu, Vert Feuille ; 4e : Diamant,
/// Perle, Platine, HeartGold, SoulSilver ; 5e : Noire, Blanche, Noire 2, Blanche 2 ; 6e : X, Y, Rubis Oméga, Saphir
/// Alpha ; 7e : Soleil, Lune, Ultra-Soleil, Ultra-Lune, Let's Go Pikachu, Let's Go Évoli). Vraies sauvegardes de Rouge,
/// Bleu, Argent, SoulSilver, Noire, Noire 2, Rubis Oméga et Ultra-Soleil (MonsutaPotaru.Tests/Sauvegardes) ; Let's Go
/// fabriquée en mémoire.
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

        // 5e génération : chaque sauvegarde dit son jeu ; une sauvegarde vide de PKHeX (« Any ») propose les deux de son format.
        Assert.True(SaveUtil.TryGetSaveFile(RealSave("noir.sav"), out var black, "noir.sav"));
        Assert.Equal([LivingDexGame.Black], LivingDexGames.For(black!));
        Assert.True(SaveUtil.TryGetSaveFile(RealSave("noire-2.sav"), out var black2, "noire-2.sav"));
        Assert.Equal([LivingDexGame.Black2], LivingDexGames.For(black2!));
        Assert.Equal([LivingDexGame.Black, LivingDexGame.White], LivingDexGames.For(new SAV5BW()));
        Assert.Equal([LivingDexGame.Black2, LivingDexGame.White2], LivingDexGames.For(new SAV5B2W2()));

        // 6e génération : chaque sauvegarde dit son jeu.
        Assert.True(SaveUtil.TryGetSaveFile(RealSave("rubis-omega.sav"), out var omegaRuby, "rubis-omega.sav"));
        Assert.Equal([LivingDexGame.OmegaRuby], LivingDexGames.For(omegaRuby!));
        Assert.Equal([LivingDexGame.X, LivingDexGame.Y], LivingDexGames.For(new SAV6XY()));
        Assert.Equal([LivingDexGame.OmegaRuby, LivingDexGame.AlphaSapphire], LivingDexGames.For(new SAV6AO()));

        // 7e génération : chaque sauvegarde dit son jeu, Let's Go compris ; rien pour la 8e.
        Assert.True(SaveUtil.TryGetSaveFile(RealSave("ultra-soleil.sav"), out var ultraSun, "ultra-soleil.sav"));
        Assert.Equal([LivingDexGame.UltraSun], LivingDexGames.For(ultraSun!));
        Assert.Equal([LivingDexGame.Sun, LivingDexGame.Moon], LivingDexGames.For(new SAV7SM()));
        Assert.Equal([LivingDexGame.UltraSun, LivingDexGame.UltraMoon], LivingDexGames.For(new SAV7USUM()));
        Assert.Equal([LivingDexGame.LetsGoPikachu, LivingDexGame.LetsGoEevee], LivingDexGames.For(new SAV7b()));
        Assert.Equal([LivingDexGame.LetsGoEevee], LivingDexGames.For(new SAV7b { Version = GameVersion.GE }));
        Assert.True(SaveUtil.TryGetSaveFile(RealSave("epee.sav"), out var sword, "epee.sav"));
        Assert.Empty(LivingDexGames.For(sword!));
    }

    [Theory]
    [InlineData(LivingDexGame.X, 457, 825, 30)] // Kalos (3 sous-Pokédex) ; 30 Méga
    [InlineData(LivingDexGame.Y, 457, 825, 30)]
    [InlineData(LivingDexGame.OmegaRuby, 211, 847, 50)] // + 18 Méga, 2 Primo, Pikachu Cosplayeur, Hoopa Déchaîné
    [InlineData(LivingDexGame.AlphaSapphire, 211, 847, 50)]
    public void SixthGeneration_HasItsRegion_National721_FormsAndMegas(LivingDexGame value, int regional, int complete, int megas)
    {
        var game = LivingDexGames.Get(value);
        Assert.Equal([LivingDexMode.Regional, LivingDexMode.National, LivingDexMode.Complete], LivingDexGames.Modes(game));
        Assert.Equal(regional, LivingDexGames.Slots(game, LivingDexMode.Regional).Count);
        Assert.Equal(721, LivingDexGames.Slots(game, LivingDexMode.National).Count);
        var slots = LivingDexGames.Slots(game, LivingDexMode.Complete);
        Assert.Equal(complete, slots.Count);
        Assert.Equal(megas, slots.Count(s => s.MegaItem != 0 || s.MegaMove != 0));
        int Forms(ushort species) => slots.Count(s => s.Species == species);
        Assert.Equal((5, 5, 5, 4, 4), (Forms(669), Forms(670), Forms(671), Forms(710), Forms(711))); // fleurs (sans l'Éternelle), tailles
        Assert.Equal((1, 1, 1, 1, 1, 1), (Forms(666), Forms(676), Forms(678), Forms(681), Forms(716), Forms(649))); // une case chacun
        Assert.All(slots.Where(s => s.Species is 666 or 676 or 678), s => Assert.True(s.AnyForm)); // tout motif, coupe, sexe
        Assert.Equal((4, 4), (Forms(585), Forms(586))); // saisons, sans conseil : elles ne changent plus en 6e
        Assert.All(slots, s => Assert.Null(s.Hint));
        Assert.Null(game.SpriteFolder); // images de la collection
    }

    [Fact]
    public void SixthGeneration_Megas_Kalos_AndOmegaRubyForms()
    {
        var x = LivingDexGames.Get(LivingDexGame.X);
        // Kalos : Centre, Côtes, Montagnes, chacun numéroté à partir de 1.
        Assert.Equal([("Kalos Centre", 1), ("Kalos Centre", 153), ("Kalos Côtes", 1), ("Kalos Montagnes", 151)],
            new[] { 0, 152, 153, 456 }.Select(i => LivingDexGames.RegionalNumber(x, i)));
        Assert.Equal(((string?)null, 1), LivingDexGames.RegionalNumber(LivingDexGames.Get(LivingDexGame.OmegaRuby), 0));

        var oras = LivingDexGames.Get(LivingDexGame.OmegaRuby);
        var slots = LivingDexGames.Slots(oras, LivingDexMode.Complete);
        LivingDexSlot Slot(ushort species, byte form) => slots.Single(s => s.Species == species && s.Form == form);
        string Name(LivingDexSlot s) => LivingDexGames.Name(oras, s, LivingDexMode.Complete);

        var megaX = Slot(6, 1);
        // Images de la collection : la Méga par sa gemme, comme un Dracaufeu qui tient le Dracaufite X.
        Assert.Equal(("Méga-Dracaufeu X", PokemonSprites.Url(6, 0, EntityContext.Gen6, false, 660, false), (ushort)660),
            (Name(megaX), LivingDexGames.Sprite(oras, megaX), megaX.MegaItem));
        Assert.Equal("Méga-Mewtwo Y", Name(Slot(150, 2)));
        Assert.Equal(("Primo-Kyogre", PokemonSprites.Url(382, 0, EntityContext.Gen6, false, 535, false)),
            (Name(Slot(382, 1)), LivingDexGames.Sprite(oras, Slot(382, 1))));
        Assert.Equal(("Méga-Rayquaza", (ushort)0, (ushort)620), (Name(Slot(384, 1)), Slot(384, 1).MegaItem, Slot(384, 1).MegaMove));
        Assert.True(Slot(6, 0).AnyForm);

        var cosplay = Slot(25, 6);
        Assert.Equal(("Pikachu Cosplayeur", PokemonSprites.Url(25, 6, EntityContext.Gen6, false, 0, false)), (Name(cosplay), LivingDexGames.Sprite(oras, cosplay)));
        Assert.True(LivingDexGames.Fills(cosplay, 25, 1, LivingDexMode.Complete)); // Pikachu Rockeur
        Assert.False(LivingDexGames.Fills(Slot(25, 0), 25, 1, LivingDexMode.Complete)); // le Pikachu ordinaire, à part

        var unbound = Slot(720, 1);
        Assert.Equal(("Hoopa Déchaîné", (ushort)765), (Name(unbound), unbound.KeyItem));
        Assert.Equal(("Flabébé (fleur Bleue)", PokemonSprites.Url(669, 3, EntityContext.Gen6, false, 0, false)),
            (Name(Slot(669, 3)), LivingDexGames.Sprite(oras, Slot(669, 3))));
        Assert.Equal("Banshitrouye (taille Ultra)", Name(Slot(711, 3)));

        // X / Y : ni Hoopa Déchaîné, ni Pikachu Cosplayeur, ni Méga ajoutées par Rubis Oméga / Saphir Alpha (Méga-Jungko).
        var xSlots = LivingDexGames.Slots(x, LivingDexMode.Complete);
        Assert.Single(xSlots, s => s.Species == 720);
        Assert.Single(xSlots, s => s.Species == 25);
        Assert.Single(xSlots, s => s.Species == 254);
    }

    [Theory]
    [InlineData(LivingDexGame.Sun, 302, 802, 956, 50)] // + 18 formes d'Alola, 7 noyaux de Météno, styles, Zygarde 10 %...
    [InlineData(LivingDexGame.Moon, 302, 802, 956, 50)]
    [InlineData(LivingDexGame.UltraSun, 403, 807, 964, 50)] // + Lougaroc Crépusculaire, les deux Necrozma fusionnés
    [InlineData(LivingDexGame.UltraMoon, 403, 807, 964, 50)]
    public void SeventhGeneration_HasAlola_National_FormsAndMegas(LivingDexGame value, int regional, int national, int complete, int megas)
    {
        var game = LivingDexGames.Get(value);
        Assert.Equal([LivingDexMode.Regional, LivingDexMode.National, LivingDexMode.Complete], LivingDexGames.Modes(game));
        var alola = LivingDexGames.Slots(game, LivingDexMode.Regional);
        Assert.Equal(regional, alola.Count);
        Assert.Equal([722, 723, 724], alola.Take(3).Select(s => (int)s.Species)); // Brindibou, Efflèche, Archéduc
        Assert.Equal(national, LivingDexGames.Slots(game, LivingDexMode.National).Count);
        var slots = LivingDexGames.Slots(game, LivingDexMode.Complete);
        Assert.Equal(complete, slots.Count);
        Assert.Equal(megas, slots.Count(s => s.MegaItem != 0 || s.MegaMove != 0));
        Assert.All(slots, s => Assert.False(s.MegaInBag)); // les gemmes se tiennent
        int Forms(ushort species) => slots.Count(s => s.Species == species);
        var ultra = value is LivingDexGame.UltraSun or LivingDexGame.UltraMoon;
        Assert.Equal((2, 2, 2, 2), (Forms(19), Forms(26), Forms(105), Forms(20))); // forme d'Alola ; pas de Dominant
        Assert.Equal((7, 4, 2), (Forms(774), Forms(741), Forms(718))); // noyaux de Météno, styles, Zygarde 10 % et 50 %
        Assert.Equal((ultra ? 3 : 2, ultra ? 3 : 1), (Forms(745), Forms(800))); // Lougaroc, Necrozma
        Assert.Equal((1, 1, 1, 1, 1), (Forms(773), Forms(25), Forms(658), Forms(778), Forms(746))); // Silvallié, Pikachu, Amphinobi...
        Assert.Equal(new byte[] { 7, 8, 9, 10, 11, 12, 13 }, slots.Where(s => s.Species == 774).Select(s => s.Form)); // pas Météore
        Assert.All(slots.Where(s => s.Species is 492 or 720), s => Assert.Equal(0, s.KeyItem)); // en boîte en 7e
        Assert.Null(game.SpriteFolder);
    }

    [Fact]
    public void SeventhGeneration_FormNames_TotemsAndZygarde()
    {
        var ultraSun = LivingDexGames.Get(LivingDexGame.UltraSun);
        var slots = LivingDexGames.Slots(ultraSun, LivingDexMode.Complete);
        LivingDexSlot Slot(ushort species, byte form) => slots.Single(s => s.Species == species && s.Form == form);
        string Name(LivingDexSlot s) => LivingDexGames.Name(ultraSun, s, LivingDexMode.Complete);

        Assert.Equal(["Rattata", "Rattata d'Alola"], slots.Where(s => s.Species == 19).Select(Name));
        Assert.Equal(["Plumeline (style Flamenco)", "Plumeline (style Pom-Pom)", "Plumeline (style Hula)", "Plumeline (style Buyō)"],
            slots.Where(s => s.Species == 741).Select(Name));
        Assert.Equal(["Lougaroc (forme Diurne)", "Lougaroc (forme Nocturne)", "Lougaroc (forme Crépusculaire)"],
            slots.Where(s => s.Species == 745).Select(Name));
        Assert.Equal(["Zygarde (forme 50 %)", "Zygarde (forme 10 %)"], slots.Where(s => s.Species == 718).Select(Name));
        Assert.Equal(("Météno (noyau Rouge)", "Météno (noyau Violet)"), (Name(Slot(774, 7)), Name(Slot(774, 13))));
        Assert.Equal(("Necrozma Crinière du Couchant", "Shaymin (forme Céleste)", "Hoopa Déchaîné"),
            (Name(Slot(800, 1)), Name(Slot(492, 1)), Name(Slot(720, 1))));
        Assert.Equal("Méga-Dracaufeu Y", Name(Slot(6, 2)));
        Assert.Equal(PokemonSprites.Url(19, 1, EntityContext.Gen7, false, 0, false), LivingDexGames.Sprite(ultraSun, Slot(19, 1)));

        // Un Dominant remplit la case de sa forme ordinaire ; Zygarde avec Rassemblement, celle de son pourcentage.
        Assert.True(LivingDexGames.Fills(Slot(20, 1), 20, 2, LivingDexMode.Complete)); // Rattatac Dominant : d'Alola
        Assert.False(LivingDexGames.Fills(Slot(20, 0), 20, 2, LivingDexMode.Complete));
        Assert.True(LivingDexGames.Fills(Slot(718, 1), 718, 2, LivingDexMode.Complete)); // 10 %-C
        Assert.True(LivingDexGames.Fills(Slot(718, 0), 718, 3, LivingDexMode.Complete)); // 50 %-C
        Assert.True(LivingDexGames.Fills(Slot(25, 0), 25, 3, LivingDexMode.Complete)); // un Pikachu à casquette reste un Pikachu
        Assert.False(LivingDexGames.Fills(Slot(774, 7), 774, 8, LivingDexMode.Complete)); // chaque noyau compte
        Assert.Equal("images/livingdex/jaquettes/ultra-soleil.png", ultraSun.Cover);
    }

    [Fact]
    public void LetsGo_OnlyKanto_MeltanAndMelmetal_AlolaFormsAndMegasInTheBag()
    {
        foreach (var game in LivingDexGames.All.Where(LivingDexGames.IsLetsGo))
        {
            // Son Pokédex est tout ce qu'il peut contenir : National et Complet.
            Assert.Equal([LivingDexMode.National, LivingDexMode.Complete], LivingDexGames.Modes(game));
            var national = LivingDexGames.Slots(game, LivingDexMode.National);
            Assert.Equal([.. Enumerable.Range(1, 151), 808, 809], national.Select(s => (int)s.Species));
            var slots = LivingDexGames.Slots(game, LivingDexMode.Complete);
            Assert.Equal(153 + 18 + 15, slots.Count); // 18 formes d'Alola, 15 Méga
            var megas = slots.Where(s => s.MegaItem != 0).ToList();
            Assert.Equal(15, megas.Count);
            Assert.All(megas, s => Assert.True(s.MegaInBag));
            Assert.Single(slots, s => s.Species == 25); // ni casquette ni Partenaire
            Assert.Single(slots, s => s.Species == 133);
            Assert.Equal("Méga-Mewtwo X", LivingDexGames.Name(game, slots.Single(s => s.Species == 150 && s.Form == 1), LivingDexMode.Complete));
        }
        Assert.Equal("images/livingdex/jaquettes/lets-go-evoli.png", LivingDexGames.Get(LivingDexGame.LetsGoEevee).Cover);
    }

    [Theory]
    [InlineData(LivingDexGame.Black, 156, 699)] // 649 + 27 lettres de Zarbi, 3 Deoxys, 2 × 2 capes, 2 mers, 5 Motisma,
    [InlineData(LivingDexGame.White, 156, 699)] // Giratina, Shaymin, Bargantua Bleu, 2 × 3 saisons
    [InlineData(LivingDexGame.Black2, 301, 705)] // + 3 formes Totémiques, Kyurem Blanc et Noir, Keldeo Décidé
    [InlineData(LivingDexGame.White2, 301, 705)]
    public void FifthGeneration_HasUnova_National649_AndTheFormsOfTheGame(LivingDexGame value, int regional, int complete)
    {
        var game = LivingDexGames.Get(value);
        Assert.Equal([LivingDexMode.Regional, LivingDexMode.National, LivingDexMode.Complete], LivingDexGames.Modes(game));
        var unova = LivingDexGames.Slots(game, LivingDexMode.Regional);
        Assert.Equal(regional, unova.Count);
        Assert.Equal((494, 649), (unova[0].Species, unova[^1].Species)); // Victini (n° 0), Genesect
        Assert.Equal((0, "Unys"), (game.RegionalStart, game.Region));
        Assert.Equal(649, LivingDexGames.Slots(game, LivingDexMode.National).Count);

        var slots = LivingDexGames.Slots(game, LivingDexMode.Complete);
        Assert.Equal(complete, slots.Count);
        int Forms(ushort species) => slots.Count(s => s.Species == species);
        Assert.Equal((28, 4, 6, 2, 2, 4, 4), (Forms(201), Forms(386), Forms(479), Forms(492), Forms(550), Forms(585), Forms(586)));
        Assert.Equal((1, 1, 1, 1, 1), (Forms(172), Forms(351), Forms(421), Forms(555), Forms(648))); // Pichu ; formes de combat
        Assert.True(Assert.Single(slots, s => s.Species == 649).AnyForm); // Genesect : une case, quel que soit son Module
        Assert.True(Assert.Single(slots, s => s.Species == 493).AnyForm);
        var b2w2 = value is LivingDexGame.Black2 or LivingDexGame.White2;
        Assert.Equal(b2w2 ? (2, 2, 2, 3, 2) : (1, 1, 1, 1, 1), (Forms(641), Forms(642), Forms(645), Forms(646), Forms(647)));
        Assert.Equal("images/livingdex/noire-blanche/", game.SpriteFolder);
    }

    [Fact]
    public void FifthGeneration_FormNames_Sprites_AndSeasons()
    {
        var game = LivingDexGames.Get(LivingDexGame.White2);
        var slots = LivingDexGames.Slots(game, LivingDexMode.Complete);
        LivingDexSlot Slot(ushort species, byte form) => slots.Single(s => s.Species == species && s.Form == form);
        string Name(ushort species, byte form) => LivingDexGames.Name(game, Slot(species, form), LivingDexMode.Complete);
        string Sprite(ushort species, byte form) => LivingDexGames.Sprite(game, Slot(species, form));

        Assert.Equal(("Bargantua (motif Rouge)", "Bargantua (motif Bleu)", "images/livingdex/noire-blanche/550-blue-striped.png"),
            (Name(550, 0), Name(550, 1), Sprite(550, 1)));
        Assert.Equal(("Vivaldaim (forme Printemps)", "Haydaim (forme Hiver)", "images/livingdex/noire-blanche/586-winter.png"),
            (Name(585, 0), Name(586, 3), Sprite(586, 3)));
        Assert.Equal(("Boréas", "Boréas (forme Totémique)", "images/livingdex/noire-blanche/641-therian.png"),
            (Name(641, 0), Name(641, 1), Sprite(641, 1)));
        Assert.Equal(("Kyurem Blanc", "Kyurem Noir", "images/livingdex/noire-blanche/646-white.png", "images/livingdex/noire-blanche/646-black.png"),
            (Name(646, 1), Name(646, 2), Sprite(646, 1), Sprite(646, 2)));
        Assert.Equal(("Keldeo (forme Décidée)", "images/livingdex/noire-blanche/647-resolute.png"), (Name(647, 1), Sprite(647, 1)));
        Assert.Equal("Vivaldaim", LivingDexGames.Name(game, new(585, 0), LivingDexMode.National)); // la saison seulement en Complet

        // Saisons : chacune a sa case (en boîte, elles se gardent), avec un conseil tant qu'elle manque.
        Assert.All(slots.Where(s => s.Species is 585 or 586), s => Assert.Equal("à garder en boîte (dans l'équipe, il prend la saison du jeu)", s.Hint));
        Assert.Null(Slot(550, 1).Hint);
        Assert.Equal("images/livingdex/jaquettes/blanche-2.png", game.Cover);
        Assert.Equal(["Pokémon Noire", "Pokémon Blanche", "Pokémon Noire 2", "Pokémon Blanche 2"],
            LivingDexGames.All.Where(g => g.Generation == 5).Select(g => g.Name));
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
        // Sprites du jeu (jusqu'à la 5e génération) ; ensuite, ceux de la collection, qui dépendent des images présentes.
        foreach (var game in LivingDexGames.All.Where(g => g.SpriteFolder is not null))
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
        await Store("sacha", "epee.sav"); // 8e génération : pas de living dex

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
    public async Task Unova_StartsAtZero_AndSeasonsGiveAHint()
    {
        var save = await Store("sacha", "noir.sav");
        var candidate = Assert.Single(await _dex.ListCandidatesAsync("sacha"));
        Assert.Equal([LivingDexGame.Black], candidate.Games);
        var id = (await _dex.CreateAsync("sacha", save, LivingDexGame.Black)).Id!.Value;

        var unova = (await _dex.GetAsync("sacha", id, LivingDexMode.Regional))!;
        Assert.Equal(156, unova.Entries.Count);
        Assert.Equal((0, (ushort)494, "Victini"), (unova.Entries[0].Number, unova.Entries[0].Species, unova.Entries[0].Name));
        Assert.Equal((155, (ushort)649), (unova.Entries[^1].Number, unova.Entries[^1].Species));
        Assert.Equal("images/livingdex/noire-blanche/494.png", unova.Entries[0].Sprite);
        var national = (await _dex.GetAsync("sacha", id))!;
        Assert.True(national.Owned > 0);

        // La sauvegarde a un Haydaim d'Automne en boîte : il remplit sa saison, les trois autres manquent avec le conseil.
        var complete = (await _dex.GetAsync("sacha", id, LivingDexMode.Complete))!;
        var sawsbuck = complete.Entries.Where(e => e.Species == 586).ToList();
        Assert.Equal([false, false, true, false], sawsbuck.Select(e => e.Owned));
        Assert.Equal((string?)null, sawsbuck[2].Note);
        Assert.All(sawsbuck.Where(e => !e.Owned), e => Assert.Equal("à garder en boîte (dans l'équipe, il prend la saison du jeu)", e.Note));
    }

    [Fact]
    public async Task Black2_CountsItsPokemon()
    {
        var save = await Store("sacha", "noire-2.sav");
        Assert.Equal([LivingDexGame.Black2], Assert.Single(await _dex.ListCandidatesAsync("sacha")).Games);
        var id = (await _dex.CreateAsync("sacha", save, LivingDexGame.Black2)).Id!.Value;

        var national = (await _dex.GetAsync("sacha", id))!;
        Assert.Equal(59, national.Owned); // 60 Pokémon, 59 espèces
        var unova = (await _dex.GetAsync("sacha", id, LivingDexMode.Regional))!;
        Assert.Equal(301, unova.Entries.Count);
        Assert.Equal((300, (ushort)649), (unova.Entries[^1].Number, unova.Entries[^1].Species));
    }

    [Theory]
    [InlineData(0, 151, "0,0 %")]
    [InlineData(28, 493, "5,6 %")] // 5,679... : arrondi vers le bas
    [InlineData(999, 1000, "99,9 %")] // 100 % seulement quand tout y est
    [InlineData(151, 151, "100,0 %")]
    [InlineData(0, 0, "0,0 %")]
    public void Percent_HasOneDecimal_RoundedDown(int owned, int total, string expected) =>
        Assert.Equal(expected, LivingDexGames.Percent(owned, total));

    [Theory]
    [InlineData(null, LivingDexMode.National)]
    [InlineData("regional", LivingDexMode.Regional)]
    [InlineData("complet", LivingDexMode.Complete)]
    [InlineData("n-importe-quoi", LivingDexMode.National)]
    public void Mode_InTheAddress(string? slug, LivingDexMode mode)
    {
        Assert.Equal(mode, LivingDexGames.ModeFromSlug(slug));
        Assert.Equal(slug is "regional" or "complet" ? slug : null, LivingDexGames.ModeSlug(mode));
    }

    [Fact]
    public async Task List_CountsInTheChosenMode_NationalWhenTheGameHasNotIt()
    {
        var red = await Store("sacha", "rouge.sav");
        var silver = await Store("sacha", "argent.sav");
        await _dex.CreateAsync("sacha", red, LivingDexGame.Red);
        var silverId = (await _dex.CreateAsync("sacha", silver, LivingDexGame.Silver)).Id!.Value;

        foreach (var mode in new[] { LivingDexMode.Regional, LivingDexMode.National, LivingDexMode.Complete })
        {
            var cards = await _dex.ListAsync("sacha", mode);
            // Rouge n'a que le National ; Argent compte comme sa page dans ce mode.
            Assert.Equal((LivingDexMode.National, 151), (cards[0].Mode, cards[0].Total));
            var page = (await _dex.GetAsync("sacha", silverId, mode))!;
            Assert.Equal((mode, page.Owned, page.Entries.Count), (cards[1].Mode, cards[1].Owned, cards[1].Total));
        }
        Assert.Equal(276, (await _dex.ListAsync("sacha", LivingDexMode.Complete))[1].Total); // 251 + 25 lettres de Zarbi
    }

    [Fact]
    public async Task OmegaRuby_MegaNeedsOneMoreCopyWithItsStone()
    {
        var save = await Store("sacha", "rubis-omega.sav");
        Assert.Equal([LivingDexGame.OmegaRuby], Assert.Single(await _dex.ListCandidatesAsync("sacha")).Games);
        var id = (await _dex.CreateAsync("sacha", save, LivingDexGame.OmegaRuby)).Id!.Value;

        var hoenn = (await _dex.GetAsync("sacha", id, LivingDexMode.Regional))!;
        Assert.Equal((211, 1, (ushort)252), (hoenn.Entries.Count, hoenn.Entries[0].Number, hoenn.Entries[0].Species));
        Assert.All(hoenn.Entries, e => Assert.Null(e.Section));

        // Un seul Mysdibule, qui tient la Mysdibulite : il remplit la case Méga, la case Mysdibule attend un autre exemplaire.
        var complete = (await _dex.GetAsync("sacha", id, LivingDexMode.Complete))!;
        Assert.Equal(847, complete.Entries.Count);
        var mawile = complete.Entries.Where(e => e.Species == 303).ToList();
        Assert.Equal(["Mysdibule", "Méga-Mysdibule"], mawile.Select(e => e.Name));
        Assert.Equal((false, true), (mawile[0].Owned, mawile[1].Owned));
        Assert.Equal("avec Mysdibulite", mawile[1].Note);
        var national = (await _dex.GetAsync("sacha", id))!;
        Assert.True(national.Entries.Single(e => e.Species == 303).Owned); // en National, un Mysdibule suffit

        // Méga manquante : ce qu'il faut ; Pikachu Rockeur remplit la case Pikachu Cosplayeur.
        Assert.Equal("il faut un Dracaufeu de plus avec Dracaufite X", complete.Entries.Single(e => e.Name == "Méga-Dracaufeu X").Note);
        // Le Rayquaza de la sauvegarde connaît Draco-Ascension : il remplit Méga-Rayquaza.
        var megaRayquaza = complete.Entries.Single(e => e.Name == "Méga-Rayquaza");
        Assert.Equal((true, "qui connaît Draco-Ascension"), (megaRayquaza.Owned, megaRayquaza.Note));
        Assert.True(complete.Entries.Single(e => e.Name == "Pikachu Cosplayeur").Owned);
        Assert.Equal("il faut un deuxième Hoopa et le Vase Scellé dans le sac", complete.Entries.Single(e => e.Name == "Hoopa Déchaîné").Note);
    }

    [Fact]
    public async Task UltraSun_CountsItsPokemon()
    {
        var save = await Store("sacha", "ultra-soleil.sav");
        var candidate = Assert.Single(await _dex.ListCandidatesAsync("sacha"));
        Assert.Equal([LivingDexGame.UltraSun], candidate.Games);
        var id = (await _dex.CreateAsync("sacha", save, LivingDexGame.UltraSun)).Id!.Value;

        var alola = (await _dex.GetAsync("sacha", id, LivingDexMode.Regional))!;
        Assert.Equal((403, 1, (ushort)722, "Brindibou"), (alola.Entries.Count, alola.Entries[0].Number, alola.Entries[0].Species, alola.Entries[0].Name));
        Assert.Equal((403, (ushort)807), (alola.Entries[^1].Number, alola.Entries[^1].Species)); // Zeraora
        var national = (await _dex.GetAsync("sacha", id))!;
        Assert.Equal(807, national.Entries.Count);
        Assert.True(national.Owned > 0);
        var complete = (await _dex.GetAsync("sacha", id, LivingDexMode.Complete))!;
        Assert.Equal(964, complete.Entries.Count); // 960 places en boîte + 6 dans l'équipe : ça tient
        Assert.True(complete.Owned > 0);
    }

    /// <summary>
    /// Sauvegarde Let's Go Pikachu fabriquée en mémoire, avec des Dracaufeu en boîte (PID propre au dresseur) et des
    /// gemmes dans le sac. Une sauvegarde vide de PKHeX n'a pas les repères que SaveUtil cherche (pied « BEEF », numéro
    /// de bloc 0x13) : on les écrit.
    /// </summary>
    private static byte[] LetsGoWith(string owner, int charizards, params ushort[] stones)
    {
        var data = new SAV7b { Version = GameVersion.GP, OT = "Chen", Language = (int)LanguageID.French }.Write().ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0xB8800 - 0x1F0), 0x42454546);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0xB8800 - 0x200 + 0xB0), 0x13);
        Assert.True(SaveUtil.TryGetSaveFile(data, out var sav, "lets-go.sav"));
        for (var n = 0; n < charizards; n++)
        {
            var pk = new PB7
            {
                Species = 6, Version = GameVersion.GP, Language = (int)LanguageID.French, OriginalTrainerName = "Chen",
                TID16 = 4242, PID = (uint)(0x60000000 + owner.Length * 16 + n), MetLevel = 36, Move1 = 33,
            };
            pk.CurrentLevel = 36;
            pk.ClearNickname();
            pk.RefreshChecksum();
            sav!.SetBoxSlotAtIndex(pk, n);
        }
        SaveSessionTests.GiveItems(sav!, stones.Select(s => ((int)s, 1)).ToArray());
        return sav!.Write().ToArray();
    }

    [Theory]
    [InlineData(1, new ushort[] { 660, 678 }, false, "il faut un Dracaufeu de plus", false, "il faut un Dracaufeu de plus")]
    [InlineData(2, new ushort[] { 660 }, true, "Dracaufite X dans le sac", false, "il faut un Dracaufeu de plus et la Dracaufite Y dans le sac")]
    [InlineData(2, new ushort[] { 678 }, false, "il faut la Dracaufite X dans le sac", true, "Dracaufite Y dans le sac")]
    [InlineData(3, new ushort[] { 660, 678 }, true, "Dracaufite X dans le sac", true, "Dracaufite Y dans le sac")]
    public async Task LetsGo_MegaNeedsOneMoreCopy_AndItsStoneInTheBag(int charizards, ushort[] stones, bool x, string xNote, bool y, string yNote)
    {
        var result = await _saves.AddAsync("sacha", "lets-go.sav", LetsGoWith("sacha", charizards, stones));
        Assert.True(result.Ok, result.Message);
        var candidate = Assert.Single(await _dex.ListCandidatesAsync("sacha"));
        Assert.Equal([LivingDexGame.LetsGoPikachu], candidate.Games);
        var id = (await _dex.CreateAsync("sacha", candidate.SaveId, LivingDexGame.LetsGoPikachu)).Id!.Value;

        var complete = (await _dex.GetAsync("sacha", id, LivingDexMode.Complete))!;
        Assert.Equal(186, complete.Entries.Count);
        var charizard = complete.Entries.Where(e => e.Species == 6).ToList();
        Assert.Equal(["Dracaufeu", "Méga-Dracaufeu X", "Méga-Dracaufeu Y"], charizard.Select(e => e.Name));
        Assert.True(charizard[0].Owned); // le premier Dracaufeu reste à sa case
        Assert.Equal((x, xNote, y, yNote), (charizard[1].Owned, charizard[1].Note, charizard[2].Owned, charizard[2].Note));
        Assert.Equal(1 + (x ? 1 : 0) + (y ? 1 : 0), charizard.Select(e => e.BankId).Where(b => b is not null).Distinct().Count());
        Assert.Equal("il faut un Alakazam de plus et l'Alakazamite dans le sac", complete.Entries.Single(e => e.Name == "Méga-Alakazam").Note);

        // En National, un Dracaufeu suffit.
        var national = (await _dex.GetAsync("sacha", id))!;
        Assert.Equal((153, 1), (national.Entries.Count, national.Owned));
        Assert.Equal((809, (ushort)809), (national.Entries[^1].Number, national.Entries[^1].Species)); // Melmetal
    }

    [Fact]
    public async Task Kalos_ShowsItsThreeSections()
    {
        // Pas de vraie sauvegarde X / Y : celle de Rubis Oméga ne peut pas en être une.
        var save = await Store("sacha", "rubis-omega.sav");
        Assert.Equal("Cette sauvegarde n'est pas une partie de Pokémon X.", (await _dex.CreateAsync("sacha", save, LivingDexGame.X)).Message);
        var x = LivingDexGames.Get(LivingDexGame.X);
        Assert.Equal(["Kalos Centre", "Kalos Côtes", "Kalos Montagnes"], x.Sections!.Select(s => s.Name));
        Assert.Equal(457, x.Sections!.Sum(s => s.Count));
        Assert.Equal(457, x.Regional.Distinct().Count()); // aucun Pokémon dans deux sous-Pokédex
        Assert.Equal("images/livingdex/jaquettes/rubis-omega.png", LivingDexGames.Get(LivingDexGame.OmegaRuby).Cover);
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
