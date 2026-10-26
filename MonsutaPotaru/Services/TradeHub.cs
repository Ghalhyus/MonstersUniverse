namespace MonsutaPotaru.Services;

/// <summary>Étape d'un échange en direct.</summary>
public enum TradeStage { Requested, Choosing, Confirming, Trading, Done, Cancelled }

/// <summary>Un joueur dans un échange : compte, pseudo, sauvegarde.</summary>
public sealed record TradeParty(string UserId, string Pseudo, int SaveId, string SaveLabel);

/// <summary>
/// Un échange en cours entre deux joueurs connectés (<see cref="TradeHub"/>). Les deux pages s'abonnent à
/// <see cref="Changed"/> ; tout changement passe par le hub (verrou), jamais directement.
/// </summary>
public sealed class TradeSession(TradeParty first, TradeParty second, DateTime createdAt)
{
    public Guid Id { get; } = Guid.NewGuid();
    /// <summary>Celui qui a demandé l'échange.</summary>
    public TradeParty First { get; } = first;
    public TradeParty Second { get; } = second;
    public DateTime CreatedAt { get; } = createdAt;
    public DateTime ExpiresAt => CreatedAt + TradeHub.RequestLifetime;

    public TradeStage Stage { get; internal set; }
    /// <summary>Pokémon de <see cref="Second"/> que <see cref="First"/> veut recevoir (et l'inverse).</summary>
    public TradeCandidate? FirstWants { get; internal set; }
    public TradeCandidate? SecondWants { get; internal set; }
    public bool FirstConfirmed { get; internal set; }
    public bool SecondConfirmed { get; internal set; }
    /// <summary>Dernier événement à montrer aux deux (refus, annulation, échec) ; pour la fin, voir <see cref="Outcome"/>.</summary>
    public string? Notice { get; internal set; }
    public TradeOutcome? Outcome { get; internal set; }

    public bool IsOver => Stage is TradeStage.Done or TradeStage.Cancelled;
    public bool Involves(string userId) => First.UserId == userId || Second.UserId == userId;
    public bool IsFirst(string userId) => First.UserId == userId;
    public TradeParty Me(string userId) => IsFirst(userId) ? First : Second;
    public TradeParty Other(string userId) => IsFirst(userId) ? Second : First;
    public TradeCandidate? Wants(string userId) => IsFirst(userId) ? FirstWants : SecondWants;
    public TradeCandidate? WantedFromMe(string userId) => IsFirst(userId) ? SecondWants : FirstWants;
    public bool Confirmed(string userId) => IsFirst(userId) ? FirstConfirmed : SecondConfirmed;
    public bool OtherConfirmed(string userId) => IsFirst(userId) ? SecondConfirmed : FirstConfirmed;

    /// <summary>Quelque chose a changé : les pages se redessinent.</summary>
    public event Action? Changed;
    internal void Notify() => Changed?.Invoke();
}

/// <summary>
/// Échanges en direct (Multijoueurs → Échanges), en mémoire : un redémarrage du serveur les annule. Un joueur ne fait
/// qu'un échange à la fois. Une demande vaut <see cref="RequestLifetime"/>, tant que son auteur reste sur la page ; puis
/// chacun choisit le Pokémon de l'autre qu'il veut, les deux valident (un refus ramène au choix), et l'échange est fait
/// par la page du dernier à valider (<see cref="TradeService.ExecuteAsync"/>, résultat rendu par <see cref="Complete"/>).
/// </summary>
public sealed class TradeHub : IDisposable
{
    public static readonly TimeSpan RequestLifetime = TimeSpan.FromMinutes(10);

    private readonly object gate = new();
    private readonly List<TradeSession> sessions = [];
    private readonly Timer expiry;
    private readonly TimeProvider time;

    public TradeHub() : this(TimeProvider.System) { }

    public TradeHub(TimeProvider time)
    {
        this.time = time;
        expiry = new Timer(_ => ExpireRequests(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    /// <summary>Demandes reçues ou annulées pour ce compte (compteur du menu, liste de la page « Échanges »).</summary>
    public event Action<string>? RequestsChanged;

    /// <summary>Échange en cours de ce joueur (demande comprise), s'il en a un.</summary>
    public TradeSession? Active(string userId)
    {
        lock (gate)
            return sessions.FirstOrDefault(s => !s.IsOver && s.Involves(userId) && (s.Stage != TradeStage.Requested || s.IsFirst(userId)));
    }

    /// <summary>Demandes d'échange reçues, en attente de réponse.</summary>
    public List<TradeSession> Incoming(string userId)
    {
        lock (gate)
            return sessions.Where(s => s.Stage == TradeStage.Requested && s.Second.UserId == userId).ToList();
    }

    /// <summary>Envoie une demande d'échange (l'appelant a vérifié l'amitié et les sauvegardes).</summary>
    public (TradeSession? Session, string Message) Request(TradeParty first, TradeParty second)
    {
        TradeSession session;
        lock (gate)
        {
            if (Busy(first.UserId))
                return (null, "Tu as déjà un échange en cours.");
            if (sessions.Any(s => !s.IsOver && s.Stage != TradeStage.Requested && s.Involves(second.UserId)))
                return (null, $"{second.Pseudo} est déjà en train d'échanger : réessaie plus tard.");
            session = new TradeSession(first, second, time.GetUtcNow().UtcDateTime);
            sessions.Add(session);
        }
        RequestsChanged?.Invoke(second.UserId);
        return (session, $"Demande envoyée à {second.Pseudo} : en attente de sa réponse.");
    }

    private bool Busy(string userId) => sessions.Any(s => !s.IsOver && s.Involves(userId) && (s.Stage != TradeStage.Requested || s.IsFirst(userId)));

    /// <summary>Accepte une demande reçue : les autres demandes reçues restent, mais on ne peut plus en accepter.</summary>
    public string? Accept(TradeSession session, string userId)
    {
        lock (gate)
        {
            if (session.Stage != TradeStage.Requested || session.Second.UserId != userId)
                return "Cette demande n'est plus valable.";
            if (Busy(userId))
                return "Tu as déjà un échange en cours.";
            session.Stage = TradeStage.Choosing;
            session.Notice = null;
        }
        session.Notify();
        RequestsChanged?.Invoke(userId);
        return null;
    }

    /// <summary>Choix du Pokémon de l'autre qu'on veut recevoir ; quand les deux ont choisi, place à la validation.</summary>
    public void Choose(TradeSession session, string userId, TradeCandidate wanted)
    {
        lock (gate)
        {
            if (session.Stage is not (TradeStage.Choosing or TradeStage.Confirming) || !session.Involves(userId))
                return;
            if (session.IsFirst(userId))
                session.FirstWants = wanted;
            else
                session.SecondWants = wanted;
            session.FirstConfirmed = session.SecondConfirmed = false;
            session.Stage = session.FirstWants is not null && session.SecondWants is not null ? TradeStage.Confirming : TradeStage.Choosing;
            session.Notice = null;
        }
        session.Notify();
    }

    /// <summary>
    /// Validation (ou refus, qui ramène les deux au choix). Vrai quand l'appelant est le second à valider : c'est à lui de
    /// faire l'échange, puis de rendre le résultat par <see cref="Complete"/>.
    /// </summary>
    public bool Confirm(TradeSession session, string userId, bool accept)
    {
        bool execute = false;
        lock (gate)
        {
            if (session.Stage != TradeStage.Confirming || !session.Involves(userId))
                return false;
            if (!accept)
            {
                session.Stage = TradeStage.Choosing;
                session.FirstWants = session.SecondWants = null;
                session.FirstConfirmed = session.SecondConfirmed = false;
                session.Notice = $"{session.Me(userId).Pseudo} a refusé l'échange : choisissez de nouveau.";
            }
            else
            {
                if (session.IsFirst(userId))
                    session.FirstConfirmed = true;
                else
                    session.SecondConfirmed = true;
                if (session.FirstConfirmed && session.SecondConfirmed)
                {
                    session.Stage = TradeStage.Trading;
                    execute = true;
                }
            }
        }
        session.Notify();
        return execute;
    }

    /// <summary>Résultat de l'échange : terminé, ou retour au choix (un Pokémon a changé...), ou annulé.</summary>
    public void Complete(TradeSession session, TradeOutcome outcome)
    {
        lock (gate)
        {
            if (session.Stage != TradeStage.Trading)
                return;
            if (outcome.Ok)
            {
                session.Stage = TradeStage.Done;
                session.Outcome = outcome;
            }
            else if (outcome.Retry)
            {
                session.Stage = TradeStage.Choosing;
                session.FirstWants = session.SecondWants = null;
                session.FirstConfirmed = session.SecondConfirmed = false;
                session.Notice = outcome.FirstMessage;
            }
            else
            {
                session.Stage = TradeStage.Cancelled;
                session.Notice = outcome.FirstMessage;
            }
            Forget(session);
        }
        session.Notify();
    }

    /// <summary>Annule (bouton « Annuler », page quittée, demande refusée) ; sans effet sur un échange fini ou en cours d'écriture.</summary>
    public void Cancel(TradeSession session, string userId, string? reason = null)
    {
        lock (gate)
        {
            if (session.IsOver || session.Stage == TradeStage.Trading || !session.Involves(userId))
                return;
            var refused = session.Stage == TradeStage.Requested && !session.IsFirst(userId);
            session.Stage = TradeStage.Cancelled;
            session.Notice = reason ?? (refused ? $"{session.Me(userId).Pseudo} a refusé la demande d'échange."
                : $"{session.Me(userId).Pseudo} a annulé l'échange.");
            Forget(session);
        }
        session.Notify();
        RequestsChanged?.Invoke(session.Second.UserId);
    }

    /// <summary>Demandes restées sans réponse trop longtemps : annulées.</summary>
    public void ExpireRequests()
    {
        List<TradeSession> expired;
        var now = time.GetUtcNow().UtcDateTime;
        lock (gate)
        {
            expired = sessions.Where(s => s.Stage == TradeStage.Requested && s.ExpiresAt <= now).ToList();
            foreach (var s in expired)
            {
                s.Stage = TradeStage.Cancelled;
                s.Notice = $"{s.Second.Pseudo} n'a pas répondu à temps : la demande a expiré.";
                Forget(s);
            }
        }
        foreach (var s in expired)
        {
            s.Notify();
            RequestsChanged?.Invoke(s.Second.UserId);
        }
    }

    private void Forget(TradeSession session) => sessions.Remove(session); // les pages gardent leur référence pour le message de fin

    public void Dispose() => expiry.Dispose();
}
