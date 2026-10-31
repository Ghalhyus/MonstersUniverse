using System.Text.Json;
using MonsutaPotaru.Services;

namespace MonsutaPotaru.Tests;

/// <summary>
/// Cartes du « Parc », une par taille (wwwroot/parc/parc-1-{taille}.json, écrites par outils/generer-parc.py puis
/// carte-parc.py) : ce que parc.js lit, une zone « Parc » où chaque case libre est accessible depuis les autres (pas de
/// Pokémon enfermé), et chaque agrandissement qui garde les cases libres de la taille d'avant.
/// </summary>
public sealed class ParkMapTests
{
    private const int Tile = 16;
    private static readonly string[] Layers = ["Sol", "Décors", "Devant", "Collisions"];

    /// <summary>Tailles du parc : une carte pour chacune (sans agrandissement, puis un à trois).</summary>
    public static TheoryData<int> Sizes() => [.. Enumerable.Range(1, ParkService.MaxExpansions + 1)];

    private static JsonElement Map(int size)
    {
        var path = Path.Combine(RealSavesTests.WebRoot(), "parc", $"parc-1-{size}.json");
        return JsonDocument.Parse(File.ReadAllText(path)).RootElement;
    }

    private static JsonElement Layer(JsonElement map, string name) =>
        map.GetProperty("layers").EnumerateArray().Single(l => l.GetProperty("name").GetString() == name);

    private static (int X, int Y, int W, int H) Zone(JsonElement map, string name)
    {
        var zone = Layer(map, "Zones").GetProperty("objects").EnumerateArray().Single(o => o.GetProperty("name").GetString() == name);
        int Tiles(string property) => (int)Math.Round(zone.GetProperty(property).GetDouble() / Tile);
        return (Tiles("x"), Tiles("y"), Tiles("width"), Tiles("height"));
    }

    /// <summary>Cases où les Pokémon marchent : dans la zone « Parc », sans tuile du calque « Collisions » (comme parc.js).</summary>
    private static HashSet<(int x, int y)> FreeTiles(JsonElement map)
    {
        int width = map.GetProperty("width").GetInt32();
        var (x0, y0, w, h) = Zone(map, "Parc");
        var collisions = Layer(map, "Collisions").GetProperty("data").EnumerateArray().Select(t => t.GetInt64()).ToArray();
        return Enumerable.Range(y0, h).SelectMany(y => Enumerable.Range(x0, w).Select(x => (x, y)))
            .Where(c => collisions[c.y * width + c.x] == 0).ToHashSet();
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void Map_HasWhatTheParkReads(int size)
    {
        var map = Map(size);
        Assert.Equal("orthogonal", map.GetProperty("orientation").GetString());
        Assert.False(map.GetProperty("infinite").GetBoolean());
        Assert.Equal(Tile, map.GetProperty("tilewidth").GetInt32());
        Assert.Equal(Tile, map.GetProperty("tileheight").GetInt32());
        var tiles = map.GetProperty("width").GetInt32() * map.GetProperty("height").GetInt32();
        foreach (var name in Layers)
        {
            var layer = Layer(map, name);
            Assert.Equal("tilelayer", layer.GetProperty("type").GetString());
            Assert.Equal(tiles, layer.GetProperty("data").GetArrayLength()); // CSV, pas de compression
        }
        // Jeux de tuiles incorporés, planche cherchée dans images/parc/tuiles/ sous son seul nom de fichier
        foreach (var tileset in map.GetProperty("tilesets").EnumerateArray())
        {
            Assert.False(tileset.TryGetProperty("source", out _));
            var image = tileset.GetProperty("image").GetString()!;
            Assert.Equal(Path.GetFileName(image), image);
            Assert.EndsWith(".png", image);
        }
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void ParkZone_InsideTheMap_AndEveryFreeTileReachable(int size)
    {
        var map = Map(size);
        int width = map.GetProperty("width").GetInt32(), height = map.GetProperty("height").GetInt32();
        var (sx, sy, sw, sh) = Zone(map, "Départ");
        var (x0, y0, w, h) = Zone(map, "Parc");
        Assert.True(x0 >= 0 && y0 >= 0 && x0 + w <= width && y0 + h <= height, "zone « Parc » hors de la carte");
        Assert.True(x0 <= sx && y0 <= sy && sx + sw <= x0 + w && sy + sh <= y0 + h, "zone « Départ » hors de la zone « Parc »");

        var freeTiles = FreeTiles(map);
        bool Free(int x, int y) => freeTiles.Contains((x, y));
        var free = freeTiles.Order().ToList();
        // Place pour ses Pokémon : 25 par taille, une quinzaine de cases chacun au moins
        var capacity = ParkService.CapacityOf(size - 1);
        Assert.True(free.Count >= capacity * 14, $"zone « Parc » trop encombrée : {free.Count} cases libres pour {capacity} Pokémon");

        // Comme parc.js : en diagonale seulement si les deux côtés sont libres
        var reached = new HashSet<(int x, int y)> { free[0] };
        var queue = new Queue<(int x, int y)>(reached);
        while (queue.TryDequeue(out var c))
        {
            for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    if ((dx == 0 && dy == 0) || !Free(c.x + dx, c.y + dy)) continue;
                    if (dx != 0 && dy != 0 && (!Free(c.x + dx, c.y) || !Free(c.x, c.y + dy))) continue;
                    if (reached.Add((c.x + dx, c.y + dy))) queue.Enqueue((c.x + dx, c.y + dy));
                }
        }
        var enclosed = free.Where(c => !reached.Contains(c)).ToList();
        Assert.True(enclosed.Count == 0, $"cases libres inaccessibles (Pokémon enfermé) : {string.Join(", ", enclosed.Take(10))}");
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void Expansion_KeepsEveryFreeTileOfTheSmallerPark(int size)
    {
        if (size == 1) return;
        var before = FreeTiles(Map(size - 1));
        var after = FreeTiles(Map(size));
        var lost = before.Where(c => !after.Contains(c)).ToList();
        Assert.True(lost.Count == 0, $"cases libres perdues en passant à la taille {size} : {string.Join(", ", lost.Take(10))}");
        Assert.True(after.Count > before.Count);
    }
}
