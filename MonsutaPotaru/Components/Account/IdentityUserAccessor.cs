using Microsoft.AspNetCore.Identity;
using MonsutaPotaru.Data;

namespace MonsutaPotaru.Components.Account;

internal sealed class IdentityUserAccessor(UserManager<ApplicationUser> userManager, IdentityRedirectManager redirectManager)
{
    public async Task<ApplicationUser> GetRequiredUserAsync(HttpContext context)
    {
        var user = await userManager.GetUserAsync(context.User);

        if (user is null)
        {
            redirectManager.RedirectToWithStatus("Account/InvalidUser", "Erreur : ce compte n'existe plus. Déconnecte-toi, puis reconnecte-toi.", context);
        }

        return user;
    }
}
