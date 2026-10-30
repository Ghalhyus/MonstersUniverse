using System.Text.Json;

namespace MonsutaPotaru.Tests;

/// <summary>
/// Carte du « Parc » (wwwroot/parc/parc-1.json, écrite par outils/carte-parc.py depuis Tiled) : ce que parc.js lit, et une
/// zone « Départ » où chaque case libre est accessible depuis les autres (pas de Pokémon enfermé).
/// </summary>
public sealed class ParkMapTests
{
    private const int Tile = 16;
    private static readonly string[] Layers = ["Sol", "Décors", "Devant", "Collisions"];

    private static JsonElement Map()
    {
        var path = Path.Combine(RealSavesTests.WebRoot(), "parc", "parc-1.json");
        return JsonDocument.Parse(File.ReadAllText(path)).RootElement;
    }

    private static JsonElement Layer(JsonElement map, string name) =>
        map.GetProperty("layers").EnumerateArray().Single(l => l.GetProperty("name").GetString() == name);

    [Fact]
    public void Map_HasWhatTheParkReads()
    {
        var map = Map();
        Assert.Equal("orthogonal", map.GetProperty("orientation").GetString());
        Assert.False(map.GetProperty("infinite").GetBoolean());
        Assert.Equal(Tile, map.GetProperty("tilewidth").GetInt32());
        Assert.Equal(Tile, map.GetProperty("tileheight").GetInt32());
        var size = map.GetProperty("width").GetInt32() * map.GetProperty("height").GetInt32();
        foreach (var name in Layers)
        {
            var layer = Layer(map, name);
            Assert.Equal("tilelayer", layer.GetProperty("type").GetString());
            Assert.Equal(size, layer.GetProperty("data").GetArrayLength()); // CSV, pas de compression
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

    [Fact]
    public void StartZone_InsideTheMap_AndEveryFreeTileReachable()
    {
        var map = Map();
        int width = map.GetProperty("width").GetInt32(), height = map.GetProperty("height").GetInt32();
        var start = Layer(map, "Zones").GetProperty("objects").EnumerateArray().Single(o => o.GetProperty("name").GetString() == "Départ");
        int x0 = (int)Math.Round(start.GetProperty("x").GetDouble() / Tile), y0 = (int)Math.Round(start.GetProperty("y").GetDouble() / Tile);
        int w = (int)Math.Round(start.GetProperty("width").GetDouble() / Tile), h = (int)Math.Round(start.GetProperty("height").GetDouble() / Tile);
        Assert.True(x0 >= 0 && y0 >= 0 && x0 + w <= width && y0 + h <= height, "zone « Départ » hors de la carte");

        var collisions = Layer(map, "Collisions").GetProperty("data").EnumerateArray().Select(t => t.GetInt64()).ToArray();
        bool Free(int x, int y) => x >= x0 && y >= y0 && x < x0 + w && y < y0 + h && collisions[y * width + x] == 0;
        var free = Enumerable.Range(y0, h).SelectMany(y => Enumerable.Range(x0, w).Select(x => (x, y))).Where(c => Free(c.x, c.y)).ToList();
        Assert.True(free.Count >= w * h / 2, $"zone « Départ » trop encombrée : {free.Count} cases libres sur {w * h}");

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
}
