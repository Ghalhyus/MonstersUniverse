using System.Text.Json;
using System.Text.Json.Serialization;

namespace MonsutaPotaru.Services;

/// <summary>
/// Ligne du protocole de Showdown : « |move|p1a: Pika|Surf|p2a: Geo|[from] move: Metronome » → genre « move », arguments
/// (« p1a: Pika », « Surf », « p2a: Geo ») et mots-clés entre crochets (« from » → « move: Metronome »).
/// </summary>
public sealed record BattleLine(string Kind, IReadOnlyList<string> Args, IReadOnlyDictionary<string, string> Tags)
{
    public string Arg(int index) => index < Args.Count ? Args[index] : "";

    public string? Tag(string name) => Tags.GetValueOrDefault(name);

    public bool Has(string name) => Tags.ContainsKey(name);

    public static BattleLine Parse(string line)
    {
        var parts = line.Split('|');
        var args = new List<string>();
        var tags = new Dictionary<string, string>();
        for (var i = 2; i < parts.Length; i++)
        {
            var part = parts[i];
            var close = part.IndexOf(']');
            if (part.StartsWith('[') && close > 0)
                tags[part[1..close]] = part[(close + 1)..].Trim();
            else
                args.Add(part);
        }
        return new BattleLine(parts.Length > 1 ? parts[1] : "", args, tags);
    }
}

/// <summary>Capacité proposée à un joueur (requête de Showdown).</summary>
public sealed record BattleMoveChoice(
    [property: JsonPropertyName("move")] string Move,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("pp")] int? Pp,
    [property: JsonPropertyName("maxpp")] int? MaxPp,
    [property: JsonPropertyName("disabled")] JsonElement Disabled)
{
    /// <summary>Choix impossible (Entrave, Provoc, plus de PP...).</summary>
    public bool IsDisabled => Disabled.ValueKind is JsonValueKind.True or JsonValueKind.String;
}

/// <summary>Pokémon actif d'un joueur, dans sa requête : ses capacités, et s'il est piégé (Regard Noir, Ligotage...).</summary>
public sealed record BattleActiveRequest(
    [property: JsonPropertyName("moves")] IReadOnlyList<BattleMoveChoice> Moves,
    [property: JsonPropertyName("trapped")] bool Trapped,
    [property: JsonPropertyName("maybeTrapped")] bool MaybeTrapped);

/// <summary>Pokémon de l'équipe d'un joueur, dans sa requête (« condition » : « 51/110 par », « 0 fnt »).</summary>
public sealed record BattleTeamMember(
    [property: JsonPropertyName("ident")] string Ident,
    [property: JsonPropertyName("details")] string Details,
    [property: JsonPropertyName("condition")] string Condition,
    [property: JsonPropertyName("active")] bool Active,
    [property: JsonPropertyName("item")] string Item,
    [property: JsonPropertyName("baseAbility")] string Ability,
    [property: JsonPropertyName("moves")] IReadOnlyList<string> Moves);

public sealed record BattleSideRequest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("pokemon")] IReadOnlyList<BattleTeamMember> Pokemon);

/// <summary>
/// Ce que Showdown attend d'un joueur : une action (<see cref="Active"/>), un remplaçant après un K.O.
/// (<see cref="ForceSwitch"/>), ou rien (<see cref="Wait"/> : l'autre joueur doit choisir).
/// </summary>
public sealed record BattleRequest(
    [property: JsonPropertyName("active")] IReadOnlyList<BattleActiveRequest>? Active,
    [property: JsonPropertyName("forceSwitch")] IReadOnlyList<bool>? ForceSwitch,
    [property: JsonPropertyName("wait")] bool Wait,
    [property: JsonPropertyName("side")] BattleSideRequest Side)
{
    public bool MustSwitch => ForceSwitch?.Any(f => f) == true;

    public bool MustAct => !Wait && (MustSwitch || Active is { Count: > 0 });
}

/// <summary>Découpage des morceaux du protocole de Showdown (<see cref="ShowdownBattle.Chunk"/>).</summary>
public static class BattleProtocol
{
    /// <summary>Lignes jamais affichées : horodatage, débogage, règles du format, lignes vides.</summary>
    private static readonly HashSet<string> Ignored = ["", "t:", "debug", "gametype", "gen", "tier", "rule", "start", "upkeep", "seed", "inactive", "inactiveoff", "timestamp"];

    /// <summary>
    /// Lignes d'un morceau « update » vues par ce joueur (« p1 », « p2 ») : d'une partie secrète (<c>|split|p1</c>, suivie de
    /// la version secrète puis de la publique), le joueur concerné lit la secrète (PV exacts), l'autre la publique.
    /// </summary>
    public static IReadOnlyList<BattleLine> Lines(string chunk, string viewer)
    {
        var raw = chunk.Split('\n');
        if (raw.Length == 0 || raw[0] != "update")
            return [];
        var lines = new List<BattleLine>();
        for (var i = 1; i < raw.Length; i++)
        {
            var text = raw[i];
            if (text.StartsWith("|split|", StringComparison.Ordinal))
            {
                var side = text["|split|".Length..];
                text = side == viewer ? At(raw, i + 1) : At(raw, i + 2);
                i += 2;
            }
            if (!text.StartsWith('|'))
                continue;
            var line = BattleLine.Parse(text);
            if (!Ignored.Contains(line.Kind))
                lines.Add(line);
        }
        return lines;
    }

    private static string At(string[] lines, int index) => index < lines.Length ? lines[index] : "";

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Requête d'un morceau « sideupdate » (joueur concerné, requête), ou null (autre morceau, erreur de choix).</summary>
    public static (string Side, BattleRequest Request)? Request(string chunk)
    {
        var raw = chunk.Split('\n', 3);
        if (raw.Length < 3 || raw[0] != "sideupdate" || !raw[2].StartsWith("|request|", StringComparison.Ordinal))
            return null;
        var json = raw[2]["|request|".Length..];
        if (string.IsNullOrWhiteSpace(json) || json == "null")
            return null;
        var request = JsonSerializer.Deserialize<BattleRequest>(json, Json);
        return request is null ? null : (raw[1], request);
    }

    /// <summary>Choix refusé par Showdown (« sideupdate », « |error|[Invalid choice] ... ») : joueur et message anglais, ou null.</summary>
    public static (string Side, string Message)? Error(string chunk)
    {
        var raw = chunk.Split('\n', 3);
        return raw.Length == 3 && raw[0] == "sideupdate" && raw[2].StartsWith("|error|", StringComparison.Ordinal)
            ? (raw[1], raw[2]["|error|".Length..])
            : null;
    }

    /// <summary>Camp d'un identifiant (« p1a: Pika » → « p1 ») et nom du Pokémon (« Pika »).</summary>
    public static (string Side, string Name) Who(string ident)
    {
        var colon = ident.IndexOf(':');
        var position = colon < 0 ? ident : ident[..colon];
        return (position.Length >= 2 ? position[..2] : position, colon < 0 ? "" : ident[(colon + 1)..].Trim());
    }

    /// <summary>« 51/110 par » → (51, 110, « par ») ; « 0 fnt » → (0, 0, « fnt »).</summary>
    public static (int Hp, int MaxHp, string? Status) Condition(string condition)
    {
        var parts = condition.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return (0, 0, null);
        var status = parts.Length > 1 ? parts[1] : null;
        var hp = parts[0].Split('/');
        var current = int.TryParse(hp[0], out var c) ? c : 0;
        var max = hp.Length > 1 && int.TryParse(hp[1], out var m) ? m : 0;
        return (current, max, status);
    }
}
