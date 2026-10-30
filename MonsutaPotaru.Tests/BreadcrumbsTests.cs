using MonsutaPotaru.Components.Layout;

namespace MonsutaPotaru.Tests;

/// <summary>Fil d'Ariane du thème Ardoise : chemin de la page dans le menu, déduit de son adresse.</summary>
public class BreadcrumbsTests
{
    private static string Labels(string url) => string.Join(" › ", Breadcrumbs.For(url).Select(c => c.Label));

    [Theory]
    [InlineData("", "Accueil")]
    [InlineData("transfert", "Transfert")]
    [InlineData("banque", "Transfert")]
    [InlineData("collection?tri=numero", "Ma collection")]
    [InlineData("livingdex", "Living dex")]
    [InlineData("livingdex/12", "Living dex › Détail")]
    [InlineData("sauvegardes", "Sauvegardes › Mes sauvegardes")]
    [InlineData("sauvegardes/3ds", "Sauvegardes › Synchronisation 3DS")]
    [InlineData("echanges", "Multijoueurs › Échanges")]
    [InlineData("joueurs", "Multijoueurs › Rechercher un joueur")]
    [InlineData("joueurs/Sacha", "Multijoueurs › Rechercher un joueur › Sacha")]
    [InlineData("joueurs/Sacha/parc", "Multijoueurs › Rechercher un joueur › Sacha › Parc")]
    [InlineData("joueurs/Sacha/livingdex/3", "Multijoueurs › Rechercher un joueur › Sacha › Living dex › Détail")]
    [InlineData("statistiques", "Mon compte › Statistiques")]
    [InlineData("Account/Manage", "Mon compte › Profil")]
    [InlineData("Account/Manage/Themes", "Mon compte › Thèmes")]
    [InlineData("Account/Manage/DeletePersonalData", "Mon compte › Données personnelles › Suppression du compte")]
    [InlineData("Account/Login?ReturnUrl=%2Fparc", "Connexion")]
    [InlineData("page-inconnue", "")]
    public void For_FollowsTheMenu(string url, string expected) => Assert.Equal(expected, Labels(url));

    [Fact]
    public void For_LinksEveryStepButTheLast()
    {
        var crumbs = Breadcrumbs.For("joueurs/Jean%20Luc/livingdex/3");

        Assert.Equal([null, "joueurs", "joueurs/Jean%20Luc", "joueurs/Jean%20Luc/livingdex", null], crumbs.Select(c => c.Href));
        Assert.Equal("Jean Luc", crumbs[2].Label); // pseudo décodé pour l'affichage, encodé dans le lien
    }
}
