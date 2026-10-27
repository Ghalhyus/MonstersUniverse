using System.Threading.RateLimiting;

namespace MonsutaPotaru.Services;

/// <summary>
/// Limite d'envois du formulaire de connexion par adresse IP : contre les essais de mots de passe sur beaucoup de comptes à
/// la fois, que le blocage d'un compte (<see cref="AccountService.FindForLoginAsync"/>) ne voit pas. En production,
/// l'adresse est celle transmise par Caddy, comme pour la liaison 3DS (<see cref="Api.SyncEndpoints.PairingRateLimit"/>).
/// Singleton : les compteurs sont partagés par toutes les requêtes.
/// </summary>
public sealed class LoginLimiter : IDisposable
{
    /// <summary>Envois du formulaire permis par adresse IP et par minute.</summary>
    public const int AttemptsPerMinute = 10;

    public const string TooManyAttemptsMessage = "Trop d'essais de connexion : attends une minute et réessaie.";

    private readonly PartitionedRateLimiter<string> limiter = PartitionedRateLimiter.Create<string, string>(ip =>
        RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = AttemptsPerMinute,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));

    /// <summary>Un envoi de plus depuis cette adresse : faux au-delà de la limite (rien n'est alors vérifié).</summary>
    public bool TryAttempt(string? ip)
    {
        using var lease = limiter.AttemptAcquire(ip ?? "inconnue");
        return lease.IsAcquired;
    }

    public void Dispose() => limiter.Dispose();
}
