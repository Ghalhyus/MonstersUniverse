namespace MonsutaPotaru.Components.Shared;

/// <summary>
/// Nombre de colonnes des grilles de Pokémon (« Ma collection », living dex) : curseur de <see cref="Min"/> à
/// <see cref="Max"/>, gardé dans l'adresse (?colonnes=12) ; sans choix, il suit la largeur de la fenêtre
/// (fonction JavaScript watchScreenSize d'App.razor, tranches <see cref="NarrowWidth"/> et <see cref="WindowWidths"/>).
/// </summary>
public static class GridColumns
{
    public const int Min = 3, Max = 20;

    /// <summary>Paramètre d'adresse du nombre choisi au curseur.</summary>
    public const string QueryName = "colonnes";

    /// <summary>
    /// Colonnes par défaut selon la largeur de la fenêtre : étroite (téléphone, moins de <see cref="NarrowWidth"/> px affichés),
    /// puis en pixels physiques : jusqu'à 1920 (plein écran 1080p), jusqu'à 2560 (plein écran 1440p), au-delà (3440 × 1440, 4K...).
    /// Les pixels physiques ne dépendent pas de la mise à l'échelle de Windows : plein écran sur un 3440 × 1440 à 150 % = 3440.
    /// </summary>
    public const int NarrowWidth = 641;
    public static readonly int[] WindowWidths = [1920, 2560];
    private static readonly int[] ByScreen = [5, 10, 15, 20];

    /// <summary>Colonnes tant que la largeur de la fenêtre est inconnue (page pas encore interactive).</summary>
    public static int Default => ByScreen[1];

    /// <summary>Colonnes par défaut pour une tranche de largeur de fenêtre (0 : étroite... 3 : très large).</summary>
    public static int ForScreen(int size) => ByScreen[Math.Clamp(size, 0, ByScreen.Length - 1)];

    public static int Clamp(int columns) => Math.Clamp(columns, Min, Max);
}
