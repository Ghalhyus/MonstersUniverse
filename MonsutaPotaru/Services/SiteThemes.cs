namespace MonsutaPotaru.Services;

/// <summary>Thème du site : clé (attribut <c>data-theme</c> de la page, colonne <c>Theme</c> du compte), nom et description.</summary>
public sealed record SiteTheme(string Key, string Name, string Description);

/// <summary>
/// Thèmes que chaque compte peut choisir (« Mon compte » → « Thèmes »). Le serveur pose la clé du thème du compte sur la
/// page (<c>&lt;html data-theme="pokedex"&gt;</c>, <c>App.razor</c>) ; les styles d'un thème sont tous rangés sous cet
/// attribut (<c>wwwroot/themes/</c>), le thème Classique n'en a pas. Le thème Ardoise a en plus son propre cadre (menu et
/// barre du haut, <c>ArdoiseLayout</c>), choisi par <c>MainLayout</c>.
/// </summary>
public static class SiteThemes
{
    public const string Classic = "classique";
    public const string Pokedex = "pokedex";
    public const string Ardoise = "ardoise";

    /// <summary>Thème sans compte, ou d'un compte qui n'en a pas choisi (colonne <c>Theme</c> à null).</summary>
    public const string Default = Ardoise;

    public static readonly IReadOnlyList<SiteTheme> All =
    [
        new(Classic, "Classique", "Le thème d'origine du site : sobre, menu bleu nuit et violet."),
        new(Pokedex, "Pokédex", "Le Pokédex rouge : menu comme son boîtier, avec sa lentille bleue, et contenus dans des "
            + "« écrans » gris-bleu."),
        new(Ardoise, "Ardoise", "Sobre, comme un outil de gestion : menu ardoise repliable, barre du haut avec le chemin de "
            + "la page et ton compte, contenus en blocs blancs, touches d'orange."),
    ];

    /// <summary>Clé d'un thème connu, ou celle du thème par défaut (Ardoise) pour une valeur absente ou inconnue.</summary>
    public static string Normalize(string? key) => All.FirstOrDefault(t => t.Key == key)?.Key ?? Default;
}
