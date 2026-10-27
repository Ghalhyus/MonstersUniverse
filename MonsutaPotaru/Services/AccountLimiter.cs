using System.Threading.RateLimiting;

namespace MonsutaPotaru.Services;

/// <summary>
/// Limites d'envois des formulaires de compte par adresse IP. Connexion : contre les essais de mots de passe sur beaucoup
/// de comptes à la fois, que le blocage d'un compte (<see cref="AccountService.FindForLoginAsync"/>) ne voit pas.
/// Inscription : contre les essais d'adresses (une adresse déjà inscrite est refusée) et de codes d'invitation. En
/// production, l'adresse IP est celle transmise par Caddy, comme pour la liaison 3DS
/// (<see cref="Api.SyncEndpoints.PairingRateLimit"/>). Singleton : les compteurs sont partagés par toutes les requêtes.
/// </summary>
public sealed class AccountLimiter : IDisposable
{
    /// <summary>Envois du formulaire de connexion permis par adresse IP et par minute.</summary>
    public const int LoginsPerMinute = 10;

    /// <summary>Envois du formulaire d'inscription permis par adresse IP et par heure.</summary>
    public const int RegistrationsPerHour = 10;

    public const string TooManyLoginsMessage = "Trop d'essais de connexion : attends une minute et réessaie.";
    public const string TooManyRegistrationsMessage = "Trop d'essais d'inscription : réessaie dans une heure.";

    private readonly PartitionedRateLimiter<string> logins = PerAddress(LoginsPerMinute, TimeSpan.FromMinutes(1));
    private readonly PartitionedRateLimiter<string> registrations = PerAddress(RegistrationsPerHour, TimeSpan.FromHours(1));

    private static PartitionedRateLimiter<string> PerAddress(int permits, TimeSpan window) =>
        PartitionedRateLimiter.Create<string, string>(ip => RateLimitPartition.GetFixedWindowLimiter(ip, _ =>
            new FixedWindowRateLimiterOptions { PermitLimit = permits, Window = window, QueueLimit = 0 }));

    /// <summary>Un envoi de plus du formulaire de connexion : faux au-delà de la limite (rien n'est alors vérifié).</summary>
    public bool TryLogin(string? ip) => TryAcquire(logins, ip);

    /// <summary>Un envoi de plus du formulaire d'inscription : faux au-delà de la limite (rien n'est alors vérifié).</summary>
    public bool TryRegister(string? ip) => TryAcquire(registrations, ip);

    private static bool TryAcquire(PartitionedRateLimiter<string> limiter, string? ip)
    {
        using var lease = limiter.AttemptAcquire(ip ?? "inconnue");
        return lease.IsAcquired;
    }

    public void Dispose()
    {
        logins.Dispose();
        registrations.Dispose();
    }
}
