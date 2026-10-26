using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using MonsutaPotaru.Data;

namespace Microsoft.AspNetCore.Routing;

internal static class IdentityComponentsEndpointRouteBuilderExtensions
{
    // Points d'accès des pages du compte (Components/Account/Pages) : déconnexion, téléchargement des données personnelles.
    public static IEndpointConventionBuilder MapAdditionalIdentityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var accountGroup = endpoints.MapGroup("/Account");

        accountGroup.MapPost("/Logout", async (
            ClaimsPrincipal user,
            [FromServices] SignInManager<ApplicationUser> signInManager,
            [FromForm] string returnUrl) =>
        {
            await signInManager.SignOutAsync();
            return TypedResults.LocalRedirect($"~/{returnUrl}");
        });

        var manageGroup = accountGroup.MapGroup("/Manage").RequireAuthorization();

        var loggerFactory = endpoints.ServiceProvider.GetRequiredService<ILoggerFactory>();
        var downloadLogger = loggerFactory.CreateLogger("DownloadPersonalData");

        manageGroup.MapPost("/DownloadPersonalData", async (
            HttpContext context,
            [FromServices] UserManager<ApplicationUser> userManager,
            [FromServices] AuthenticationStateProvider authenticationStateProvider) =>
        {
            var user = await userManager.GetUserAsync(context.User);
            if (user is null)
            {
                return Results.NotFound("Ce compte n'existe plus.");
            }

            var userId = await userManager.GetUserIdAsync(user);
            downloadLogger.LogInformation("Compte {UserId} : données personnelles téléchargées.", userId);

            // Ce que le compte garde sur la personne (ni téléphone ni double authentification : retirés du site).
            var personalData = new Dictionary<string, string>
            {
                ["Identifiant"] = user.Id,
                ["Pseudo"] = user.UserName ?? "",
                ["Adresse mail"] = user.Email ?? "",
            };
            var fileBytes = JsonSerializer.SerializeToUtf8Bytes(personalData, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // accents lisibles
            });

            return TypedResults.File(fileBytes, contentType: "application/json", fileDownloadName: "donnees-personnelles.json");
        });

        return accountGroup;
    }
}
