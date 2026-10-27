using PKHeX.Core;

namespace MonsutaPotaru.Services;

/// <summary>
/// Sprites d'un combat vus par un joueur : ceux de SON jeu (le dossier des sprites de son living dex,
/// <see cref="LivingDexGameInfo.SpriteFolder"/>), de face pour l'adversaire, de dos pour les siens, chromatiques compris :
/// « images/livingdex/emeraude/dos/chromatique/6.png ». Noms à garder en accord avec outils/images.py (BATTLE_FORMS).
/// </summary>
/// <param name="Folder">Dossier des sprites du jeu, « images/livingdex/rubis-saphir/ » ; null : images de la collection.</param>
/// <param name="DeoxysSuffix">
/// Deoxys a, en 3e génération, la forme du jeu qui l'affiche (« -attack » en Rouge Feu) : chaque jeu n'a que la sienne.
/// </param>
public sealed record BattleSprites(string? Folder, string DeoxysSuffix = "")
{
    /// <summary>Sans sprites du jeu : les images de la collection (de face, même pour ses propres Pokémon).</summary>
    public static readonly BattleSprites None = new((string?)null);

    /// <summary>Sprites du jeu de cette sauvegarde (Rouge Feu et Vert Feuille partagent un dossier ; Deoxys de Rouge Feu).</summary>
    public static BattleSprites For(SaveFile sav)
    {
        if (LivingDexGames.For(sav) is not [var game, ..] || LivingDexGames.Get(game).SpriteFolder is not { } folder)
            return None;
        var deoxys = sav.Generation == 3 ? BattleTeam.DeoxysForme(sav.Version).ToLowerInvariant() : "";
        return new(folder, deoxys.Length > 0 ? $"-{deoxys}" : "");
    }

    /// <summary>Sprite d'un Pokémon en combat : de dos (<paramref name="back"/>) pour ceux du joueur.</summary>
    public string Url(BattleFighter f, bool back) => Folder is null
        ? Fallback(f)
        : $"{Folder}{(back ? "dos/" : "")}{(f.Shiny ? "chromatique/" : "")}{f.Species}{Suffix(f)}.png";

    /// <summary>Image de la collection, en remplacement d'un sprite du jeu absent.</summary>
    public static string Fallback(BattleFighter f) =>
        PokemonSprites.Url(f.Species, BattleNames.FormIndex(f.Species, f.Forme), EntityContext.Gen3, f.Gender == "F", 0, f.Shiny);

    /// <summary>Fin du nom de fichier d'une forme (noms de PokéAPI) : Zarbi B à ?, Deoxys du jeu, Morphéo selon le temps.</summary>
    private string Suffix(BattleFighter f) => (f.Species, BattleNames.FormIndex(f.Species, f.Forme)) switch
    {
        ((ushort)Species.Deoxys, _) => DeoxysSuffix,
        ((ushort)Species.Unown, 0) => "",
        ((ushort)Species.Unown, 26) => "-exclamation",
        ((ushort)Species.Unown, 27) => "-question",
        ((ushort)Species.Unown, var letter) => $"-{(char)('a' + letter)}",
        ((ushort)Species.Castform, 1) => "-sunny",
        ((ushort)Species.Castform, 2) => "-rainy",
        ((ushort)Species.Castform, 3) => "-snowy",
        _ => "",
    };
}
