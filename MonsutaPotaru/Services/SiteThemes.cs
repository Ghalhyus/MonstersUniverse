namespace MonsutaPotaru.Services;

/// <summary>Thème du site : clé (attribut <c>data-theme</c> de la page, colonne <c>Theme</c> du compte), nom et description.</summary>
public sealed record SiteTheme(string Key, string Name, string Description);

/// <summary>
/// Thèmes que chaque compte peut choisir (« Mon compte » → « Thèmes »). Le serveur pose la clé du thème du compte sur la
/// page (<c>&lt;html data-theme="pokedex"&gt;</c>, <c>App.razor</c>) ; les styles d'un thème sont tous rangés sous cet
/// attribut (<c>wwwroot/themes/</c>), le thème Classique n'en a pas.
/// </summary>
public static class SiteThemes
{
    public const string Classic = "classique";
    public const string Pokedex = "pokedex";

    public static readonly IReadOnlyList<SiteTheme> All =
    [
        new(Classic, "Classique", "Le thème d'origine du site : sobre, menu bleu nuit et violet."),
        new(Pokedex, "Pokédex", "Le Pokédex rouge : menu comme son boîtier, avec sa lentille bleue, et contenus dans des "
            + "« écrans » gris-bleu."),
    ];

    /// <summary>Clé d'un thème connu, ou celle du thème par défaut (Classique) pour une valeur absente ou inconnue.</summary>
    public static string Normalize(string? key) => All.FirstOrDefault(t => t.Key == key)?.Key ?? Classic;
}
