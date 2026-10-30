namespace MonsutaPotaru.Components.Layout;

/// <summary>Étape du fil d'Ariane : son nom et son adresse (null : pas de lien, page affichée ou simple rubrique du menu).</summary>
public sealed record Crumb(string Label, string? Href);

/// <summary>
/// Fil d'Ariane de la barre du haut du thème Ardoise (<c>ArdoiseLayout</c>) : le chemin de la page dans le menu
/// (« Multijoueurs › Échanges », « Multijoueurs › Rechercher un joueur › Sacha › Parc »), déduit de son adresse.
/// </summary>
public static class Breadcrumbs
{
    /// <summary>Étapes pour une adresse relative à la racine du site (« joueurs/Sacha/parc?x=1 ») ; vide pour une page inconnue.</summary>
    public static IReadOnlyList<Crumb> For(string? relativeUrl)
    {
        var path = (relativeUrl ?? "").Split('?', '#')[0];
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return [new("Accueil", null)];

        return Lower(parts[0]) switch
        {
            "transfert" or "banque" => [new("Transfert", null)],
            "collection" => [new("Ma collection", null)],
            "parc" => [new("Parc", null)],
            "inventaire" => [new("Inventaire", null)],
            "livingdex" => LivingDex(parts.Skip(1).ToArray(), "livingdex"),
            "sauvegardes" when parts.Length > 1 && Lower(parts[1]) == "3ds" => [new("Sauvegardes", null), new("Synchronisation 3DS", null)],
            "sauvegardes" => [new("Sauvegardes", null), new("Mes sauvegardes", null)],
            "joueurs" => Player(parts),
            "amis" => [new("Multijoueurs", null), new("Amis", null)],
            "echanges" => [new("Multijoueurs", null), new("Échanges", null)],
            "combats" => [new("Multijoueurs", null), new("Combats", null)],
            "administration" => [new("Administration", null)],
            "statistiques" => [new("Mon compte", "Account/Manage"), new("Statistiques", null)],
            "account" => Account(parts.Skip(1).ToArray()),
            _ => [],
        };
    }

    private static string Lower(string part) => part.ToLowerInvariant();

    /// <summary>« Living dex », ou « Living dex › Détail » pour l'une d'elles ; <paramref name="href"/> : la liste.</summary>
    private static List<Crumb> LivingDex(string[] rest, string href) =>
        rest.Length == 0 ? [new("Living dex", null)] : [new("Living dex", href), new("Détail", null)];

    /// <summary>Recherche d'un joueur, sa page, puis l'une de ses pages publiques (collection, inventaire, parc, living dex).</summary>
    private static List<Crumb> Player(string[] parts)
    {
        List<Crumb> crumbs = [new("Multijoueurs", null)];
        if (parts.Length == 1)
        {
            crumbs.Add(new("Rechercher un joueur", null));
            return crumbs;
        }
        crumbs.Add(new("Rechercher un joueur", "joueurs"));
        var pseudo = Uri.UnescapeDataString(parts[1]);
        var playerHref = "joueurs/" + Uri.EscapeDataString(pseudo);
        if (parts.Length == 2)
        {
            crumbs.Add(new(pseudo, null));
            return crumbs;
        }
        crumbs.Add(new(pseudo, playerHref));
        crumbs.AddRange(Lower(parts[2]) switch
        {
            "collection" => [new Crumb("Collection", null)],
            "inventaire" => [new Crumb("Inventaire", null)],
            "parc" => [new Crumb("Parc", null)],
            "livingdex" => LivingDex(parts.Skip(3).ToArray(), playerHref + "/livingdex"),
            _ => [],
        });
        return crumbs;
    }

    /// <summary>Pages du compte : connexion, inscription, et « Mon compte » avec ses rubriques.</summary>
    private static List<Crumb> Account(string[] rest)
    {
        if (rest.Length == 0)
            return [];
        if (Lower(rest[0]) != "manage")
        {
            return Lower(rest[0]) switch
            {
                "login" => [new("Connexion", null)],
                "register" => [new("Inscription", null)],
                "pseudo" => [new("Choix du pseudo", null)],
                "accessdenied" => [new("Accès refusé", null)],
                _ => [],
            };
        }

        List<Crumb> crumbs = [new("Mon compte", "Account/Manage")];
        var section = rest.Length > 1 ? Lower(rest[1]) : "";
        crumbs.AddRange(section switch
        {
            "" => [new Crumb("Profil", null)],
            "email" => [new Crumb("Adresse mail", null)],
            "changepassword" => [new Crumb("Mot de passe", null)],
            "themes" => [new Crumb("Thèmes", null)],
            "visibility" => [new Crumb("Visibilité", null)],
            "personaldata" => [new Crumb("Données personnelles", null)],
            "deletepersonaldata" => [new Crumb("Données personnelles", "Account/Manage/PersonalData"), new Crumb("Suppression du compte", null)],
            _ => [],
        });
        return crumbs;
    }
}
