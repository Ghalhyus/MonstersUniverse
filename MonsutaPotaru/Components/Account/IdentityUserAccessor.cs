using Microsoft.AspNetCore.Identity;
using MonsutaPotaru.Data;

namespace MonsutaPotaru.Components.Account;

internal sealed class IdentityUserAccessor(UserManager<ApplicationUser> userManager, IdentityRedirectManager redirectManager)
{
    /// <summary>
    /// Compte connecté ; null s'il n'existe plus : la page est alors redirigée vers « Account/InvalidUser », et l'appelant
    /// doit sortir tout de suite (la redirection ne lance plus d'exception, voir <see cref="IdentityRedirectManager"/>).
    /// </summary>
    public async Task<ApplicationUser?> GetRequiredUserAsync(HttpContext context)
    {
        var user = await userManager.GetUserAsync(context.User);

        if (user is null)
        {
            redirectManager.RedirectToWithStatus("Account/InvalidUser", "Erreur : ce compte n'existe plus. Déconnecte-toi, puis reconnecte-toi.", context);
        }

        return user;
    }
}
