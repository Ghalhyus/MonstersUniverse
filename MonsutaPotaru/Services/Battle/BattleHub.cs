namespace MonsutaPotaru.Services;

/// <summary>Étape d'un combat en direct.</summary>
public enum BattleStage { Requested, TeamBuilding, Fighting, Done, Cancelled }

/// <summary>Un joueur dans un combat : compte, pseudo, sauvegarde.</summary>
public sealed record BattleParty(string UserId, string Pseudo, int SaveId, string SaveLabel);

/// <summary>Choix d'un joueur pendant le combat : capacité (1 à 4) ou Pokémon à envoyer (place dans sa requête, 1 à 6).</summary>
public sealed record BattleChoice(bool Switch, int Index)
{
    public string Command => Switch ? $"switch {Index}" : $"move {Index}";

    public static BattleChoice Move(int index) => new(false, index);

    public static BattleChoice SwitchTo(int index) => new(true, index);
}

/// <summary>
/// Rythme du combat : temps pour choisir, et durée d'affichage des étapes (la page les joue une à une ; le temps pour
/// choisir ne commence qu'après, d'où <see cref="Playback"/>).
/// </summary>
public static class BattleTiming
{
    public static readonly TimeSpan Decision = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan TextStep = TimeSpan.FromMilliseconds(1300);
    public static readonly TimeSpan AnimationStep = TimeSpan.FromMilliseconds(500);

    public static TimeSpan Playback(IEnumerable<BattleStep> steps) =>
        steps.Aggregate(TimeSpan.Zero, (total, step) => total + (step.Text is null ? AnimationStep : TextStep));
}

/// <summary>Ce qu'une page montre d'un combat, à un instant (copie : la session change sur un autre fil).</summary>
public sealed record BattleSnapshot(
    BattleStage Stage, BattleScene Scene, BattleRequest? Request, bool Chosen, DateTime? Deadline, BattleOutcome Outcome,
    int StepCount, string? Error, string MyTrainer, string FoeTrainer);

/// <summary>
/// Un combat entre deux amis connectés (<see cref="BattleHub"/>) : <see cref="First"/> (qui a demandé) est « p1 » pour le
/// simulateur, <see cref="Second"/> « p2 ». Les deux pages s'abonnent à <see cref="Changed"/> et lisent la session par
/// ses méthodes (verrou : le simulateur la change sur son propre fil).
/// </summary>
public sealed class BattleSession(BattleParty first, BattleParty second, string format, DateTime createdAt)
{
    internal readonly object Gate = new();

    public Guid Id { get; } = Guid.NewGuid();
    public BattleParty First { get; } = first;
    public BattleParty Second { get; } = second;
    /// <summary>Format du simulateur (« gen3customgame »).</summary>
    public string Format { get; } = format;
    public DateTime CreatedAt { get; } = createdAt;
    public DateTime ExpiresAt => CreatedAt + BattleHub.RequestLifetime;

    public BattleStage Stage { get; internal set; }
    /// <summary>Dernier événement à montrer aux deux (refus, annulation, erreur).</summary>
    public string? Notice { get; internal set; }

    internal BattlePlayer? FirstTeam, SecondTeam;
    internal int FirstIllegal, SecondIllegal;
    internal ShowdownBattle? Simulator;
    internal readonly BattleView FirstView = new("p1"), SecondView = new("p2");
    internal readonly List<BattleStep> FirstSteps = [], SecondSteps = [];
    internal TimeSpan FirstPending, SecondPending; // durée des étapes pas encore jouées par la page, avant la requête
    internal DateTime? FirstDeadline, SecondDeadline;
    internal bool FirstChosen, SecondChosen;
    internal string? FirstError, SecondError;
    internal bool Ending; // abandon ou temps écoulé envoyés : on attend la fin du simulateur

    public bool IsOver => Stage is BattleStage.Done or BattleStage.Cancelled;
    public bool Involves(string userId) => First.UserId == userId || Second.UserId == userId;
    public bool IsFirst(string userId) => First.UserId == userId;
    public BattleParty Me(string userId) => IsFirst(userId) ? First : Second;
    public BattleParty Other(string userId) => IsFirst(userId) ? Second : First;
    internal string SideOf(string userId) => IsFirst(userId) ? "p1" : "p2";

    /// <summary>L'équipe de ce joueur est choisie.</summary>
    public bool TeamReady(string userId)
    {
        lock (Gate)
            return (IsFirst(userId) ? FirstTeam : SecondTeam) is not null;
    }

    /// <summary>Pokémon signalés illégaux dans l'équipe de l'autre joueur (montré aux deux au début du combat).</summary>
    public int OtherIllegal(string userId)
    {
        lock (Gate)
            return IsFirst(userId) ? SecondIllegal : FirstIllegal;
    }

    /// <summary>Étapes du combat vues par ce joueur, à partir de la n° <paramref name="from"/>.</summary>
    public IReadOnlyList<BattleStep> Steps(string userId, int from)
    {
        lock (Gate)
        {
            var steps = IsFirst(userId) ? FirstSteps : SecondSteps;
            return from >= steps.Count ? [] : steps.GetRange(from, steps.Count - from);
        }
    }

    public BattleSnapshot Snapshot(string userId)
    {
        lock (Gate)
        {
            var first = IsFirst(userId);
            var view = first ? FirstView : SecondView;
            return new BattleSnapshot(Stage, view.Scene, view.Request, first ? FirstChosen : SecondChosen,
                first ? FirstDeadline : SecondDeadline, view.Outcome, (first ? FirstSteps : SecondSteps).Count,
                first ? FirstError : SecondError, Me(userId).Pseudo, Other(userId).Pseudo);
        }
    }

    /// <summary>Quelque chose a changé : les pages se redessinent.</summary>
    public event Action? Changed;
    internal void Notify() => Changed?.Invoke();
}

/// <summary>
/// Combats en direct (Multijoueurs → Combats), en mémoire : un redémarrage du serveur les arrête. Un joueur ne fait qu'un
/// combat à la fois. Une demande vaut <see cref="RequestLifetime"/>, tant que son auteur reste sur la page ; puis chacun
/// choisit son équipe (<see cref="SubmitTeam"/>), et le combat se joue dans le simulateur de Showdown
/// (<see cref="ShowdownBattle"/>, un processus par combat). Chaque choix est vérifié ici avant d'être transmis ; un joueur
/// qui ne choisit pas dans les <see cref="BattleTiming.Decision"/> perd (<see cref="Tick"/>), comme celui qui abandonne ou
/// quitte la page.
/// </summary>
public sealed class BattleHub : IDisposable
{
    public static readonly TimeSpan RequestLifetime = TimeSpan.FromMinutes(10);

    private readonly object gate = new();
    private readonly List<BattleSession> sessions = [];
    private readonly Timer? timer;
    private readonly TimeProvider time;
    private readonly ShowdownOptions options;
    private readonly ILogger logger;

    public BattleHub(ShowdownOptions options, ILogger<BattleHub> logger) : this(TimeProvider.System, options, logger) { }

    /// <param name="ticking">Faux pour les tests : <see cref="Tick"/> est alors appelé à la main.</param>
    public BattleHub(TimeProvider time, ShowdownOptions options, ILogger logger, bool ticking = true)
    {
        this.time = time;
        this.options = options;
        this.logger = logger;
        if (ticking)
            timer = new Timer(_ => Tick(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    /// <summary>Demandes reçues ou annulées pour ce compte (compteur du menu, liste de la page « Combats »).</summary>
    public event Action<string>? RequestsChanged;

    /// <summary>Combat terminé (victoire, défaite ou nul) : pour les statistiques.</summary>
    public event Action<BattleSession>? Finished;

    private DateTime Now => time.GetUtcNow().UtcDateTime;

    /// <summary>Combat en cours de ce joueur (demande comprise), s'il en a un.</summary>
    public BattleSession? Active(string userId)
    {
        lock (gate)
            return sessions.FirstOrDefault(s => !s.IsOver && s.Involves(userId) && (s.Stage != BattleStage.Requested || s.IsFirst(userId)));
    }

    /// <summary>Demandes de combat reçues, en attente de réponse.</summary>
    public List<BattleSession> Incoming(string userId)
    {
        lock (gate)
            return sessions.Where(s => s.Stage == BattleStage.Requested && s.Second.UserId == userId).ToList();
    }

    private bool Busy(string userId) =>
        sessions.Any(s => !s.IsOver && s.Involves(userId) && (s.Stage != BattleStage.Requested || s.IsFirst(userId)));

    /// <summary>Envoie une demande de combat (l'appelant a vérifié l'amitié et les sauvegardes, <see cref="BattleService"/>).</summary>
    public (BattleSession? Session, string Message) Request(BattleParty first, BattleParty second, string format)
    {
        BattleSession session;
        lock (gate)
        {
            if (Busy(first.UserId))
                return (null, "Tu as déjà un combat en cours.");
            if (sessions.Any(s => !s.IsOver && s.Stage != BattleStage.Requested && s.Involves(second.UserId)))
                return (null, $"{second.Pseudo} est déjà en train de combattre : réessaie plus tard.");
            session = new BattleSession(first, second, format, Now);
            sessions.Add(session);
        }
        RequestsChanged?.Invoke(second.UserId);
        return (session, $"Demande envoyée à {second.Pseudo} : en attente de sa réponse.");
    }

    /// <summary>Accepte une demande reçue : les deux joueurs passent au choix de leur équipe.</summary>
    public string? Accept(BattleSession session, string userId)
    {
        lock (gate)
        {
            if (session.Stage != BattleStage.Requested || session.Second.UserId != userId)
                return "Cette demande n'est plus valable.";
            if (Busy(userId))
                return "Tu as déjà un combat en cours.";
            session.Stage = BattleStage.TeamBuilding;
            session.Notice = null;
        }
        session.Notify();
        RequestsChanged?.Invoke(userId);
        return null;
    }

    /// <summary>
    /// Équipe choisie par ce joueur (construite et vérifiée par <see cref="BattleService.TeamAsync"/>) ; quand les deux sont
    /// prêtes, le combat commence dans le simulateur.
    /// </summary>
    public string? SubmitTeam(BattleSession session, string userId, BattlePlayer team, int illegal)
    {
        lock (gate)
        {
            lock (session.Gate)
            {
                if (session.Stage != BattleStage.TeamBuilding || !session.Involves(userId))
                    return "Ce combat n'attend plus d'équipe.";
                if (session.IsFirst(userId))
                    (session.FirstTeam, session.FirstIllegal) = (team, illegal);
                else
                    (session.SecondTeam, session.SecondIllegal) = (team, illegal);
                if (session.FirstTeam is not null && session.SecondTeam is not null)
                    Start(session);
            }
        }
        session.Notify();
        return null;
    }

    private void Start(BattleSession session)
    {
        try
        {
            var setup = new BattleSetup(session.Format, BattleSetup.NewSeed(), session.FirstTeam!, session.SecondTeam!);
            var simulator = ShowdownBattle.Start(options, setup, logger);
            session.Simulator = simulator;
            session.Stage = BattleStage.Fighting;
            simulator.Chunk += chunk => OnChunk(session, chunk);
            simulator.Exited += () => OnExited(session);
            logger.LogInformation("Combat {Id} : {First} contre {Second}.", session.Id, session.First.UserId, session.Second.UserId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Combat {Id} : simulateur impossible à lancer.", session.Id);
            session.Stage = BattleStage.Cancelled;
            session.Notice = "Le combat n'a pas pu commencer (erreur du serveur).";
            sessions.Remove(session);
        }
    }

    /// <summary>Morceau du protocole du simulateur (sur le fil du processus) : étapes, requêtes, refus, fin.</summary>
    private void OnChunk(BattleSession session, string chunk)
    {
        var finished = false;
        lock (session.Gate)
        {
            if (session.IsOver)
                return;
            if (BattleProtocol.Request(chunk) is var (side, request))
            {
                var first = side == "p1";
                (first ? session.FirstView : session.SecondView).Request = request;
                var deadline = request.MustAct && !session.Ending ? Now + (first ? session.FirstPending : session.SecondPending) + BattleTiming.Decision : (DateTime?)null;
                if (first)
                    (session.FirstChosen, session.FirstDeadline, session.FirstPending) = (false, deadline, TimeSpan.Zero);
                else
                    (session.SecondChosen, session.SecondDeadline, session.SecondPending) = (false, deadline, TimeSpan.Zero);
            }
            else if (BattleProtocol.Error(chunk) is var (errorSide, english))
            {
                logger.LogInformation("Combat {Id} : choix refusé par le simulateur ({Side}) : {Message}", session.Id, errorSide, english);
                const string refused = "Ce choix n'est pas possible pour l'instant : choisis autre chose.";
                if (errorSide == "p1")
                    (session.FirstChosen, session.FirstError) = (false, refused);
                else
                    (session.SecondChosen, session.SecondError) = (false, refused);
            }
            else
            {
                var firstSteps = session.FirstView.Apply(BattleProtocol.Lines(chunk, "p1"));
                var secondSteps = session.SecondView.Apply(BattleProtocol.Lines(chunk, "p2"));
                session.FirstSteps.AddRange(firstSteps);
                session.SecondSteps.AddRange(secondSteps);
                session.FirstPending += BattleTiming.Playback(firstSteps);
                session.SecondPending += BattleTiming.Playback(secondSteps);
                if (session.FirstView.Outcome != BattleOutcome.None)
                {
                    session.Stage = BattleStage.Done;
                    (session.FirstDeadline, session.SecondDeadline) = (null, null);
                    finished = true;
                }
            }
        }
        if (finished)
            Finish(session);
        session.Notify();
    }

    private void Finish(BattleSession session)
    {
        lock (gate)
            sessions.Remove(session); // les pages gardent leur référence pour la fin du combat
        if (session.Simulator is { } simulator)
            _ = simulator.DisposeAsync().AsTask();
        logger.LogInformation("Combat {Id} terminé : {Outcome} pour {First}.", session.Id, session.FirstView.Outcome, session.First.UserId);
        try
        {
            Finished?.Invoke(session);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Combat {Id} : enregistrement du résultat impossible.", session.Id);
        }
    }

    /// <summary>Le simulateur s'est arrêté avant la fin du combat (erreur) : combat annulé, sans vainqueur.</summary>
    private void OnExited(BattleSession session)
    {
        lock (session.Gate)
        {
            if (session.IsOver)
                return;
            session.Stage = BattleStage.Cancelled;
            session.Notice = "Le combat s'est arrêté à cause d'une erreur du serveur.";
        }
        lock (gate)
            sessions.Remove(session);
        logger.LogError("Combat {Id} : le simulateur s'est arrêté avant la fin.", session.Id);
        session.Notify();
    }

    /// <summary>Choix de ce joueur (capacité ou Pokémon), vérifié sur sa dernière requête. Message d'erreur, ou null si envoyé.</summary>
    public string? Choose(BattleSession session, string userId, BattleChoice choice)
    {
        lock (session.Gate)
        {
            if (session.Stage != BattleStage.Fighting || !session.Involves(userId) || session.Ending)
                return "Le combat n'est pas en cours.";
            var first = session.IsFirst(userId);
            var request = (first ? session.FirstView : session.SecondView).Request;
            if (request is null || !request.MustAct || (first ? session.FirstChosen : session.SecondChosen))
                return "Ce n'est pas le moment de choisir.";
            if (Refusal(request, choice) is { } refusal)
                return refusal;
            session.Simulator!.Send($"{session.SideOf(userId)} {choice.Command}");
            if (first)
                (session.FirstChosen, session.FirstError) = (true, null);
            else
                (session.SecondChosen, session.SecondError) = (true, null);
        }
        session.Notify();
        return null;
    }

    /// <summary>Pourquoi ce choix n'est pas permis par la requête (null : permis).</summary>
    internal static string? Refusal(BattleRequest request, BattleChoice choice)
    {
        if (choice.Switch)
        {
            if (choice.Index < 1 || choice.Index > request.Side.Pokemon.Count)
                return "Ce Pokémon n'existe pas.";
            var target = request.Side.Pokemon[choice.Index - 1];
            if (target.Active)
                return "Ce Pokémon est déjà au combat.";
            if (target.Condition.EndsWith("fnt", StringComparison.Ordinal))
                return "Ce Pokémon est K.O.";
            if (!request.MustSwitch && request.Active is [{ Trapped: true }, ..])
                return "Ton Pokémon ne peut pas être rappelé !";
            return null;
        }
        if (request.MustSwitch)
            return "Choisis le Pokémon à envoyer.";
        var moves = request.Active?.FirstOrDefault()?.Moves ?? [];
        if (choice.Index < 1 || choice.Index > moves.Count)
            return "Cette capacité n'existe pas.";
        return moves[choice.Index - 1].IsDisabled ? "Cette capacité ne peut pas être utilisée !" : null;
    }

    /// <summary>« Fuite » : le joueur abandonne, l'autre gagne.</summary>
    public void Forfeit(BattleSession session, string userId) =>
        ForceLoss(session, userId, $"{session.Me(userId).Pseudo} abandonne le combat.");

    private void ForceLoss(BattleSession session, string userId, string notice)
    {
        lock (session.Gate)
        {
            if (session.Stage != BattleStage.Fighting || session.Ending || !session.Involves(userId))
                return;
            session.Ending = true;
            (session.FirstDeadline, session.SecondDeadline) = (null, null);
            foreach (var (view, steps) in new[] { (session.FirstView, session.FirstSteps), (session.SecondView, session.SecondSteps) })
                steps.Add(new BattleStep(notice, BattleCue.None, false, view.Scene));
            session.Simulator!.Send($"forcelose {session.SideOf(userId)}");
        }
        session.Notify();
    }

    /// <summary>
    /// Annule (bouton « Annuler », demande refusée) ou, pendant le combat, abandonne (page quittée, onglet fermé) ;
    /// sans effet sur un combat fini.
    /// </summary>
    public void Leave(BattleSession session, string userId, string? reason = null)
    {
        if (session.Stage == BattleStage.Fighting)
        {
            ForceLoss(session, userId, reason ?? $"{session.Me(userId).Pseudo} a quitté le combat.");
            return;
        }
        lock (gate)
        {
            if (session.IsOver || !session.Involves(userId))
                return;
            var refused = session.Stage == BattleStage.Requested && !session.IsFirst(userId);
            session.Stage = BattleStage.Cancelled;
            session.Notice = reason ?? (refused ? $"{session.Me(userId).Pseudo} a refusé le combat." : $"{session.Me(userId).Pseudo} a annulé le combat.");
            sessions.Remove(session);
        }
        session.Notify();
        RequestsChanged?.Invoke(session.Second.UserId);
    }

    /// <summary>Chaque seconde : demandes expirées, et joueurs qui n'ont pas choisi à temps (défaite).</summary>
    public void Tick()
    {
        var now = Now;
        List<BattleSession> expired;
        List<(BattleSession Session, string UserId)> late = [];
        lock (gate)
        {
            expired = sessions.Where(s => s.Stage == BattleStage.Requested && s.ExpiresAt <= now).ToList();
            foreach (var s in expired)
            {
                s.Stage = BattleStage.Cancelled;
                s.Notice = $"{s.Second.Pseudo} n'a pas répondu à temps : la demande a expiré.";
                sessions.Remove(s);
            }
            foreach (var s in sessions.Where(s => s.Stage == BattleStage.Fighting))
            {
                lock (s.Gate)
                {
                    if (s.Ending)
                        continue;
                    if (s.FirstDeadline <= now && !s.FirstChosen)
                        late.Add((s, s.First.UserId));
                    else if (s.SecondDeadline <= now && !s.SecondChosen)
                        late.Add((s, s.Second.UserId));
                }
            }
        }
        foreach (var s in expired)
        {
            s.Notify();
            RequestsChanged?.Invoke(s.Second.UserId);
        }
        foreach (var (s, userId) in late)
            ForceLoss(s, userId, $"Temps écoulé ! {s.Me(userId).Pseudo} n'a pas choisi à temps et perd le combat.");
    }

    public void Dispose()
    {
        timer?.Dispose();
        List<BattleSession> running;
        lock (gate)
            running = sessions.Where(s => s.Simulator is not null).ToList();
        foreach (var s in running)
            s.Simulator!.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(3));
    }
}
