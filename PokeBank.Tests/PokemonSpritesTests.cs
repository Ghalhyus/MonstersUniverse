using PKHeX.Core;
using PokeBank.Services;

namespace PokeBank.Tests;

/// <summary>Choix de l'image d'un Pokémon (forme, Méga-Évolution, femelle) parmi celles du site.</summary>
public sealed class PokemonSpritesTests
{
    public PokemonSpritesTests() => PokemonSprites.Load(RealSavesTests.WebRootWithImages());

    private static string Url(ushort species, byte form = 0, EntityContext context = EntityContext.Gen9, bool female = false,
        int heldItem = 0, bool shiny = false, bool egg = false) =>
        PokemonSprites.Url(species, form, context, female, heldItem, shiny, egg).Replace("images/pokemon/", "");

    private static int StoneOf(Species species, byte form) => ItemStorage9ZA.GetExpectedMegaStoneOrPrimalOrb((ushort)species, form);

    [Fact]
    public void Forms_HaveTheirOwnImage()
    {
        Assert.Equal("19-alola.png", Url(19, 1, EntityContext.Gen7)); // Rattata d'Alola
        Assert.Equal("chromatique/19-alola.png", Url(19, 1, EntityContext.Gen7, shiny: true));
        Assert.Equal("201-b.png", Url(201, 1, EntityContext.Gen4)); // Zarbi B
        Assert.Equal("201-exclamation.png", Url(201, 26, EntityContext.Gen4)); // Zarbi !
        Assert.Equal("128-paldea-combat-breed.png", Url(128, 1)); // Tauros de Paldea, race Combative
        Assert.Equal("25-rock-star.png", Url(25, 1, EntityContext.Gen6)); // Pikachu Rockeur (Rubis Oméga)
        Assert.Equal("25-original-cap.png", Url(25, 1, EntityContext.Gen7)); // Pikachu casquette originale
        Assert.Equal("25-starter.png", Url(25, 8, EntityContext.Gen7b)); // Pikachu partenaire (Let's Go)
        Assert.Equal("666-icy-snow.png", Url(666)); // Prismillon : forme 0 de PKHeX (Blizzard), pas celle de PokéAPI
        Assert.Equal("666.png", Url(666, 6)); // Bocage : l'image de l'espèce chez PokéAPI
        Assert.Equal("19.png", Url(19, 1, EntityContext.Gen7, egg: true)); // un œuf garde l'image de l'espèce
    }

    [Fact]
    public void Females_DifferentFromMales_HaveTheirOwnImage()
    {
        Assert.Equal("femelle/25.png", Url(25, female: true));
        Assert.Equal("chromatique/femelle/25.png", Url(25, female: true, shiny: true));
        Assert.Equal("1.png", Url(1, female: true)); // Bulbizarre : même image pour les deux
        Assert.Equal("678-female.png", Url(678, 1, female: true)); // Mistigrix femelle : une forme à part
    }

    [Fact]
    public void MegaStones_AndPrimalOrbs_ShowTheEvolvedForm()
    {
        Assert.Equal("6-mega-x.png", Url(6, heldItem: StoneOf(Species.Charizard, 1))); // Dracaufite X
        Assert.Equal("6-mega-y.png", Url(6, heldItem: StoneOf(Species.Charizard, 2))); // Dracaufite Y
        Assert.Equal("chromatique/6-mega-y.png", Url(6, heldItem: StoneOf(Species.Charizard, 2), shiny: true));
        Assert.Equal("6.png", Url(6, heldItem: StoneOf(Species.Venusaur, 1))); // la gemme d'une autre espèce
        Assert.Equal("382-primal.png", Url(382, heldItem: StoneOf(Species.Kyogre, 1))); // Orbe Bleu
        Assert.Equal("384.png", Url(384, heldItem: StoneOf(Species.Kyogre, 1))); // Méga-Rayquaza : pas de gemme
        Assert.Equal("678-mega-female.png", Url(678, 1, female: true, heldItem: StoneOf(Species.Meowstic, 2)));
        Assert.Equal("678-mega-male.png", Url(678, 0, heldItem: StoneOf(Species.Meowstic, 2)));
        Assert.Equal("26-mega-x.png", Url(26, heldItem: StoneOf(Species.Raichu, 2))); // Méga-Raichu X (Légendes Z-A)
    }

    /// <summary>
    /// Formes de PKHeX sans image à elles, qui prennent celle de l'espèce : identiques à la forme de base (Cheniselle de
    /// Lépidonille, Pérégrain...), ou absentes de PokéAPI. Une forme qui sort de cette liste a perdu son image (ou en a
    /// gagné une : la retirer d'ici).
    /// </summary>
    private static readonly HashSet<string> WithoutImage =
    [
        "414 Sandy", "414 Trash", // Papilord : le même quelle que soit la cape de Cheniti
        "493 Legend", // Arceus avec la Plaque Légende (Légendes Arceus)
        .. new[] { 664, 665 }.SelectMany(s => new[] { "Archipelago", "Continental", "Elegant", "Fancy", "Garden", "High Plains",
            "Jungle", "Marine", "Meadow", "Modern", "Monsoon", "Ocean", "Poké Ball", "Polar", "River", "Sandstorm", "Savanna",
            "Sun", "Tundra" }.Select(f => $"{s} {f}")), // Lépidonille et Pérégrain : les motifs ne se voient qu'en Prismillon
        "666 Meadow", // Prismillon Bocage : c'est l'image de l'espèce chez PokéAPI
        "718 Mega", // Méga-Zygarde (Légendes Z-A) : pas encore chez PokéAPI
        "900 Lord", // Hachécateur Baron : pas chez PokéAPI
        "925 Family of Four", // Famignol famille de quatre : pas chez PokéAPI
        "1007 Gliding", "1007 Limited", "1007 Sprinting", "1007 Swimming", // Koraidon : pas chez PokéAPI
        "1008 Aquatic", "1008 Drive", "1008 Glide", "1008 Low-Power", // Miraidon : pas chez PokéAPI
        "1017 *Teal", // Ogerpon téracristallisé sans masque : l'image de l'espèce
    ];

    [Fact]
    public void EveryFormOfPkhex_HasAnImage_OrIsKnownNotToHaveOne()
    {
        var english = GameInfo.GetStrings("en");
        var missing = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var context in Enum.GetValues<EntityContext>().Where(c => c.IsValid()))
            for (ushort species = 1; species < (ushort)Species.MAX_COUNT; species++)
            {
                var names = FormConverter.GetFormList(species, english.types, english.forms, GameInfo.GenderSymbolASCII, context);
                for (byte form = 1; form < names.Length; form++)
                    if (Url(species, form, context) == $"{species}.png")
                        missing.Add($"{species} {names[form]}");
            }
        Assert.True(missing.SetEquals(WithoutImage), $"Sans image : {string.Join(" ; ", missing)}");
    }
}
