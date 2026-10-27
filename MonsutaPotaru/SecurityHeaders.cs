namespace MonsutaPotaru;

/// <summary>
/// En-têtes de sécurité de toutes les réponses (<c>Program.cs</c>, README « Sécurité ») : pas de devinette du type des
/// fichiers, adresse de la page jamais envoyée à un autre site, et politique de contenu (CSP) qui n'accepte que le site.
/// Aucun script écrit dans la page n'est exécuté (ni bloc <c>&lt;script&gt;</c>, ni <c>onerror</c>, ni <c>onclick</c>) :
/// les scripts du site sont dans <c>wwwroot/js</c> (<c>app.js</c>, chargé par <c>App.razor</c>).
/// </summary>
public static class SecurityHeaders
{
    /// <summary>
    /// Politique de contenu. Les styles écrits dans la page restent permis (Blazor et sa fenêtre de reconnexion en
    /// écrivent). En développement, les scripts de rechargement de Visual Studio, servis par un autre port de la
    /// machine, sont permis aussi.
    /// </summary>
    public static string ContentSecurityPolicy(bool development)
    {
        var devScripts = development ? " http://localhost:* https://localhost:*" : "";
        var devConnections = development ? " ws://localhost:* wss://localhost:* http://localhost:* https://localhost:*" : "";
        return string.Join("; ",
            "default-src 'self'",
            $"script-src 'self'{devScripts}",
            "style-src 'self' 'unsafe-inline'",
            "img-src 'self' data: blob:", // icônes du menu en data: dans le CSS
            $"connect-src 'self'{devConnections}", // 'self' couvre aussi le WebSocket de Blazor
            "object-src 'none'",
            "base-uri 'self'",
            "form-action 'self'",
            "frame-ancestors 'self'");
    }

    /// <summary>Ajoute les en-têtes à chaque réponse (avant tout le reste du pipeline : fichiers statiques compris).</summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app, bool development)
    {
        var policy = ContentSecurityPolicy(development);
        return app.Use((context, next) =>
        {
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers["Referrer-Policy"] = "same-origin";
            headers.ContentSecurityPolicy = policy;
            return next(context);
        });
    }
}
