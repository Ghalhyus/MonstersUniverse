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
/// <item><c>POST /api/sync/sauvegardes?fichier=nom.sav&amp;cle=...</c>, corps = fichier brut → <c>{"statut", "raison", "message"}</c>
/// (<c>cle</c> : emplacement du fichier sur la console, noté pour la récupération).</item>
/// <item><c>GET /api/sync/sauvegardes</c> → <c>{"sauvegardes": [...]}</c> : sauvegardes stockées du compte, pour « Récupérer ».</item>
/// <item><c>POST /api/sync/sauvegardes/{id}/comparer</c>, corps = sauvegarde de la console → <c>{"verdict", "modifieeSurSite",
/// "message"}</c> : peut-elle être remplacée par celle du site ?</item>
/// <item><c>GET /api/sync/sauvegardes/{id}?cle=...</c> → fichier brut de la version du site.</item>
/// </list>
/// Toutes sauf la liaison demandent <c>Authorization: Bearer &lt;jeton&gt;</c> (401 sinon). Un refus métier (partie d'un autre
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

    public sealed record SaveEntryResponse(
        [property: JsonPropertyName("id")] int Id,
        [property: JsonPropertyName("jeu")] string Game,
        [property: JsonPropertyName("dresseur")] string Trainer,
        [property: JsonPropertyName("idDresseur")] string TrainerId,
        [property: JsonPropertyName("fichier")] string FileName,
        [property: JsonPropertyName("empreinte")] string Hash,
        [property: JsonPropertyName("tempsDeJeu")] int PlayTimeSeconds,
        [property: JsonPropertyName("modifieeSurSite")] bool ModifiedOnServer,
        [property: JsonPropertyName("console")] string Console,
        [property: JsonPropertyName("famille")] string Family,
        [property: JsonPropertyName("cles")] IReadOnlyList<string> Keys);

    public sealed record SaveListResponse([property: JsonPropertyName("sauvegardes")] IReadOnlyList<SaveEntryResponse> Saves);

    public sealed record CompareResponse(
        [property: JsonPropertyName("verdict")] string Verdict,
        [property: JsonPropertyName("modifieeSurSite")] bool ModifiedOnServer,
        [property: JsonPropertyName("message")] string Message);

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
                [FromQuery(Name = "cle")] string? key, SyncService sync, ILogger<SyncService> logger) =>
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
                    var result = await sync.UploadAsync(caller, fileName, data, key);
                    return Results.Ok(new UploadResponse(StatusName(result.Status), result.Refusal?.ToString(), result.Message));
                }
                catch (Exception ex) // copie de sécurité impossible, base indisponible... : la console affiche le message
                {
                    logger.LogError(ex, "Envoi de « {File} » depuis la 3DS {Device} impossible.", fileName, caller.DeviceId);
                    return Results.Json(new ErrorResponse("Erreur du serveur : réessaie plus tard."), statusCode: StatusCodes.Status500InternalServerError);
                }
            });

        api.MapGet("/sauvegardes", async (HttpRequest http, SyncService sync) =>
        {
            if (await sync.AuthenticateAsync(BearerToken(http)) is not { } caller)
                return Unauthorized();
            var saves = await sync.ListForDeviceAsync(caller);
            return Results.Ok(new SaveListResponse(saves.Select(s => new SaveEntryResponse(s.Id, s.Game, s.Trainer, s.TrainerId,
                s.FileName, s.Hash, s.PlayTimeSeconds, s.ModifiedOnServer, s.Console, s.Family, s.Keys)).ToList()));
        });

        api.MapPost("/sauvegardes/{id:int}/comparer", async (HttpRequest http, int id, SyncService sync) =>
        {
            if (await sync.AuthenticateAsync(BearerToken(http)) is not { } caller)
                return Unauthorized();
            if (await ReadBodyAsync(http) is not { } data)
                return Results.Json(new ErrorResponse($"Fichier trop gros (plus de {MaxSaveBytes / (1024 * 1024)} Mo) : ce n'est pas une sauvegarde Pokémon."),
                    statusCode: StatusCodes.Status413PayloadTooLarge);
            var result = await sync.CompareAsync(caller, id, data);
            var verdict = result.Verdict switch
            {
                SyncCompareVerdict.Retrievable => "recuperable",
                SyncCompareVerdict.UpToDate => "a-jour",
                _ => "refusee",
            };
            return Results.Ok(new CompareResponse(verdict, result.ModifiedOnServer, result.Message));
        });

        api.MapGet("/sauvegardes/{id:int}", async (HttpRequest http, int id, [FromQuery(Name = "cle")] string? key,
            SyncService sync, ILogger<SyncService> logger) =>
        {
            if (await sync.AuthenticateAsync(BearerToken(http)) is not { } caller)
                return Unauthorized();
            try
            {
                return await sync.DownloadAsync(caller, id, key) is { } file
                    ? Results.File(file.Data, "application/octet-stream")
                    : Results.Json(new ErrorResponse("Cette sauvegarde n'existe plus sur le site."), statusCode: StatusCodes.Status404NotFound);
            }
            catch (Exception ex) // modifiée au même moment (retrait depuis le site), base indisponible...
            {
                logger.LogError(ex, "Récupération de la sauvegarde {Id} par la 3DS {Device} impossible.", id, caller.DeviceId);
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
