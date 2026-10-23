using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using MonsutaPotaru.Services;

namespace MonsutaPotaru.Api;

/// <summary>
/// API du homebrew 3DS PokeBank Sync (JSON, messages en français prêts à afficher sur la console) :
/// <list type="bullet">
/// <item><c>POST /api/sync/liaison</c> <c>{"code", "nom"}</c> → <c>{"jeton", "compte", "message"}</c> : échange le code de
/// « Synchronisation 3DS » contre un jeton longue durée.</item>
/// <item><c>GET /api/sync/compte</c> → <c>{"compte", "console"}</c> : vérifie le jeton.</item>
/// <item><c>POST /api/sync/sauvegardes?fichier=nom.sav</c>, corps = fichier brut → <c>{"statut", "raison", "message"}</c>.</item>
/// </list>
/// Les deux derniers demandent <c>Authorization: Bearer &lt;jeton&gt;</c> (401 sinon). Un refus métier (partie d'un autre
/// compte, moins de temps de jeu...) répond 200 avec <c>"statut": "refusee"</c> : seul le message compte pour la console.
/// </summary>
public static class SyncEndpoints
{
    /// <summary>
    /// Limite d'essais de codes de liaison (contre les essais au hasard), par adresse IP : avant la liaison, la console
    /// n'est reliée à aucun compte. Une personne qui se trompe ne bloque donc pas les autres.
    /// </summary>
    public const string PairingRateLimit = "liaison-3ds";

    /// <summary>Essais de code permis par adresse IP et par minute.</summary>
    public const int PairingAttemptsPerMinute = 10;

    /// <summary>Plus grosse sauvegarde acceptée (celles des jeux lus par une 3DS font au plus 1 Mo).</summary>
    public const int MaxSaveBytes = 4 * 1024 * 1024;

    public sealed record LinkRequest(
        [property: JsonPropertyName("code")] string? Code,
        [property: JsonPropertyName("nom")] string? Name);

    public sealed record LinkResponse(
        [property: JsonPropertyName("jeton")] string? Token,
        [property: JsonPropertyName("compte")] string? Account,
        [property: JsonPropertyName("message")] string Message);

    public sealed record AccountResponse(
        [property: JsonPropertyName("compte")] string Account,
        [property: JsonPropertyName("console")] string Device);

    public sealed record UploadResponse(
        [property: JsonPropertyName("statut")] string Status,
        [property: JsonPropertyName("raison")] string? Refusal,
        [property: JsonPropertyName("message")] string Message);

    public sealed record ErrorResponse([property: JsonPropertyName("message")] string Message);

    public static IServiceCollection AddSyncRateLimits(this IServiceCollection services) =>
        services.AddRateLimiter(options =>
        {
            // Adresse de la console : en production, celle transmise par Caddy (X-Forwarded-For, lu grâce à
            // ASPNETCORE_FORWARDEDHEADERS_ENABLED dans docker-compose.yml ; Caddy remplace celle qu'enverrait le client).
            options.AddPolicy(PairingRateLimit, http => RateLimitPartition.GetFixedWindowLimiter(
                http.Connection.RemoteIpAddress?.ToString() ?? "inconnue",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = PairingAttemptsPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (context, token) => new ValueTask(context.HttpContext.Response.WriteAsJsonAsync(
                new ErrorResponse("Trop d'essais de code : attends une minute et réessaie."), token));
        });

    public static IEndpointRouteBuilder MapSyncEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/sync");

        api.MapPost("/liaison", async (LinkRequest request, SyncService sync) =>
            {
                var result = await sync.LinkAsync(request.Code, request.Name);
                return result.Ok
                    ? Results.Ok(new LinkResponse(result.Token, result.Account, result.Message))
                    : Results.BadRequest(new LinkResponse(null, null, result.Message));
            })
            .RequireRateLimiting(PairingRateLimit);

        api.MapGet("/compte", async (HttpRequest http, SyncService sync) =>
            await sync.AuthenticateAsync(BearerToken(http)) is { } caller
                ? Results.Ok(new AccountResponse(await sync.AccountNameAsync(caller.OwnerId), caller.DeviceName))
                : Unauthorized());

        api.MapPost("/sauvegardes", async (HttpRequest http, [FromQuery(Name = "fichier")] string? fileName,
                SyncService sync, ILogger<SyncService> logger) =>
            {
                if (await sync.AuthenticateAsync(BearerToken(http)) is not { } caller)
                    return Unauthorized();
                if (await ReadBodyAsync(http) is not { } data)
                    return Results.Json(new ErrorResponse($"Fichier trop gros (plus de {MaxSaveBytes / (1024 * 1024)} Mo) : ce n'est pas une sauvegarde Pokémon."),
                        statusCode: StatusCodes.Status413PayloadTooLarge);
                if (data.Length == 0)
                    return Results.BadRequest(new ErrorResponse("Fichier vide."));

                try
                {
                    var result = await sync.UploadAsync(caller, fileName, data);
                    return Results.Ok(new UploadResponse(StatusName(result.Status), result.Refusal?.ToString(), result.Message));
                }
                catch (Exception ex) // copie de sécurité impossible, base indisponible... : la console affiche le message
                {
                    logger.LogError(ex, "Envoi de « {File} » depuis la 3DS {Device} impossible.", fileName, caller.DeviceId);
                    return Results.Json(new ErrorResponse("Erreur du serveur : réessaie plus tard."), statusCode: StatusCodes.Status500InternalServerError);
                }
            });

        return app;
    }

    private static string StatusName(SyncUploadStatus status) => status switch
    {
        SyncUploadStatus.Added => "ajoutee",
        SyncUploadStatus.Updated => "mise-a-jour",
        SyncUploadStatus.Unchanged => "inchangee",
        _ => "refusee",
    };

    private static IResult Unauthorized() =>
        Results.Json(new ErrorResponse(SyncService.UnknownTokenMessage), statusCode: StatusCodes.Status401Unauthorized);

    private static string? BearerToken(HttpRequest http)
    {
        var header = http.Headers.Authorization.ToString();
        return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header["Bearer ".Length..] : null;
    }

    /// <summary>Corps de la requête (le fichier), ou null s'il dépasse <see cref="MaxSaveBytes"/>.</summary>
    private static async Task<byte[]?> ReadBodyAsync(HttpRequest http)
    {
        if (http.ContentLength > MaxSaveBytes)
            return null;
        using var ms = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await http.Body.ReadAsync(buffer, http.HttpContext.RequestAborted)) > 0)
        {
            if (ms.Length + read > MaxSaveBytes)
                return null;
            ms.Write(buffer, 0, read);
        }
        return ms.ToArray();
    }
}
