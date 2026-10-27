using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MonsutaPotaru.Services;

/// <summary>Joueur d'un combat, pour le simulateur : pseudo et équipe.</summary>
public sealed record BattlePlayer(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("team")] IReadOnlyList<BattleSet> Team);

/// <summary>Début d'un combat (1re ligne envoyée à <c>showdown/combat.js</c>).</summary>
public sealed record BattleSetup(
    [property: JsonPropertyName("format")] string Format,
    [property: JsonPropertyName("seed")] IReadOnlyList<int> Seed,
    [property: JsonPropertyName("p1")] BattlePlayer P1,
    [property: JsonPropertyName("p2")] BattlePlayer P2)
{
    /// <summary>Graine du hasard du combat, tirée par le serveur (jamais par un joueur).</summary>
    public static IReadOnlyList<int> NewSeed() =>
        Enumerable.Range(0, 4).Select(_ => System.Security.Cryptography.RandomNumberGenerator.GetInt32(0, 0x10000)).ToList();
}

/// <summary>Où trouver Node.js et le pont du simulateur (<c>Battle:Node</c>, <c>Battle:Showdown</c>).</summary>
public sealed record ShowdownOptions(string Node, string Folder)
{
    /// <summary>
    /// Réglages du site : par défaut <c>node</c> et le dossier <c>showdown</c> à côté de l'application (image Docker :
    /// <c>/app/showdown</c>) ou du projet (développement : à la racine du dépôt).
    /// </summary>
    public static ShowdownOptions From(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var folder = configuration["Battle:Showdown"];
        if (string.IsNullOrEmpty(folder))
        {
            var beside = Path.Combine(environment.ContentRootPath, "showdown");
            folder = Directory.Exists(beside) ? beside : Path.GetFullPath(Path.Combine(environment.ContentRootPath, "..", "showdown"));
        }
        return new ShowdownOptions(configuration["Battle:Node"] ?? "node", folder);
    }
}

/// <summary>
/// Un combat en cours dans le simulateur de Pokémon Showdown : un processus Node.js (<c>showdown/combat.js</c>) qui reçoit
/// les choix des joueurs et renvoie le protocole de Showdown, morceau par morceau (<see cref="Chunk"/>, dans l'ordre, sur
/// un fil à part). Arrêté par <see cref="DisposeAsync"/>.
/// </summary>
public sealed class ShowdownBattle : IAsyncDisposable
{
    private readonly Process process;
    private readonly ILogger logger;
    private readonly object writeGate = new();

    /// <summary>Morceau du protocole : « update » (pour tous), « sideupdate » (requête d'un joueur), « end ».</summary>
    public event Action<string>? Chunk;

    /// <summary>Le processus s'est arrêté (fin normale ou erreur).</summary>
    public event Action? Exited;

    private ShowdownBattle(Process process, ILogger logger)
    {
        this.process = process;
        this.logger = logger;
    }

    /// <summary>Lance le simulateur et le combat (les deux équipes entrent tout de suite sur le terrain).</summary>
    public static ShowdownBattle Start(ShowdownOptions options, BattleSetup setup, ILogger logger)
    {
        var script = Path.Combine(options.Folder, "combat.js");
        if (!File.Exists(script))
            throw new InvalidOperationException($"Simulateur de combat introuvable ({script}) : lancer « npm ci » dans le dossier showdown.");
        var info = new ProcessStartInfo(options.Node, [script])
        {
            WorkingDirectory = options.Folder,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardInputEncoding = new System.Text.UTF8Encoding(false),
        };
        var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        var battle = new ShowdownBattle(process, logger);
        process.OutputDataReceived += (_, e) => battle.OnOutput(e.Data);
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                logger.LogWarning("Simulateur de combat : {Message}", e.Data);
        };
        process.Exited += (_, _) => battle.Exited?.Invoke();
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        battle.Write(JsonSerializer.Serialize(setup));
        return battle;
    }

    private void OnOutput(string? line)
    {
        if (string.IsNullOrEmpty(line))
            return;
        string? chunk;
        try
        {
            chunk = JsonSerializer.Deserialize<string>(line);
        }
        catch (JsonException)
        {
            logger.LogWarning("Simulateur de combat : ligne illisible.");
            return;
        }
        if (chunk is not null)
            Chunk?.Invoke(chunk);
    }

    /// <summary>Choix d'un joueur (« p1 move 2 », « p2 switch 3 ») ou fin forcée (« forcelose p1 », « forcetie »).</summary>
    public void Send(string command) => Write(command);

    private void Write(string line)
    {
        lock (writeGate)
        {
            if (process.HasExited)
                return;
            process.StandardInput.WriteLine(line);
            process.StandardInput.Flush();
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!process.HasExited)
            {
                lock (writeGate)
                    process.StandardInput.Close(); // le pont s'arrête de lui-même
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try
                {
                    await process.WaitForExitAsync(timeout.Token);
                }
                catch (OperationCanceledException)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
        }
        catch (InvalidOperationException) // jamais démarré, ou déjà libéré
        {
        }
        process.Dispose();
    }
}
