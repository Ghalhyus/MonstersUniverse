using Microsoft.EntityFrameworkCore;
using PKHeX.Core;
using MonsutaPotaru.Data;

namespace MonsutaPotaru.Services;

/// <summary>Mode d'un living dex : quels Pokémon il demande.</summary>
public enum LivingDexMode
{
    /// <summary>Chaque Pokémon du Pokédex régional du jeu.</summary>
    Regional,
    /// <summary>Chaque Pokémon du Pokédex national que le jeu peut contenir.</summary>
    National,
    /// <summary>
    /// Chaque forme de chaque Pokémon du national : formes régionales, Méga-Évolutions, Gigamax, formes qui changent
    /// l'aspect (Sancoki / Tritosor, Zarbi...) ; pas le sexe. Seules les formes qu'une sauvegarde du jeu peut contenir
    /// comptent : aucune en 1re génération, les lettres de Zarbi en 2e et 3e (les formes de Morphéo n'existent qu'en
    /// combat ; Deoxys a la forme de son jeu, voir <see cref="LivingDexGameInfo.DeoxysForm"/>), celles de
    /// <see cref="LivingDexGames"/> (« Forms ») à partir de la 4e.
    /// </summary>
    Complete,
}

/// <summary>Tri des cartes de la vue générale.</summary>
public enum LivingDexSort
{
    /// <summary>Dans l'ordre des jeux (génération, puis jeu : Rouge, Bleu, Jaune...).</summary>
    Generation,
    /// <summary>Par pourcentage dans le mode affiché ; à égalité, dans l'ordre des jeux.</summary>
    Completion,
}

/// <summary>Un jeu de living dex : nom, jaquette, sprites, et Pokémon de chaque mode.</summary>
/// <param name="Cover">Jaquette officielle (outils/images.py, partie « livingdex »).</param>
/// <param name="SpriteFolder">
/// Sprites du jeu, « images/livingdex/rouge-bleu/25.png » (Pikachu) ; null à partir de la 6e génération : les images de la
/// collection (<see cref="PokemonSprites"/>), les sprites 3D de ces jeux n'ayant pas plu.
/// </param>
/// <param name="Region">Région du Pokédex régional (« Kanto », « Johto », « Hoenn »).</param>
/// <param name="Regional">Pokédex régional (numéros nationaux, dans l'ordre du Pokédex du jeu).</param>
/// <param name="NationalMax">Dernier numéro national que le jeu peut contenir.</param>
/// <param name="National">
/// Pokémon du national que le jeu peut contenir, quand ce ne sont pas tous ceux jusqu'à <paramref name="NationalMax"/> :
/// Let's Go n'a que ceux de Kanto, Meltan et Melmetal ; Épée / Bouclier et Écarlate / Violet, ceux que PKHeX dit
/// présents dans le jeu.
/// </param>
/// <param name="RegionalNumbers">
/// Numéros du jeu des Pokémon du régional, quand ils ne se suivent pas : Galar, puis ce que l'Isolarmure et la Couronneige
/// ajoutent (chacun avec ses numéros, sans les Pokémon déjà vus) ; de même Paldea, Septentria et la Myrtille.
/// </param>
/// <param name="DeoxysForm">
/// Forme de Deoxys dans ce jeu (3e génération) : un Pokémon de 3e génération n'enregistre pas sa forme, c'est le jeu qui
/// la donne (Normale dans Rubis / Saphir, Attaque dans Rouge Feu, Défense dans Vert Feuille, Vitesse dans Émeraude). Une
/// sauvegarde ne peut donc avoir que celle-là : une seule case, remplie par n'importe quel Deoxys.
/// </param>
/// <param name="RegionalStart">Premier numéro du Pokédex régional : 1, sauf Unys qui commence à 0 (Victini).</param>
/// <param name="Sections">
/// Sous-Pokédex qui forment le régional, dans l'ordre (Kalos : Centre, Côtes, Montagnes), avec leur nombre de Pokémon :
/// chacun a sa numérotation et son titre dans la grille. Null : un seul Pokédex, celui de <paramref name="Region"/>.
/// </param>
public sealed record LivingDexGameInfo(LivingDexGame Game, string Name, string Cover, string? SpriteFolder, byte Generation,
    string Region, IReadOnlyList<ushort> Regional, ushort NationalMax, byte DeoxysForm = 0, int RegionalStart = 1,
    IReadOnlyList<(string Name, int Count)>? Sections = null, IReadOnlyList<ushort>? National = null,
    IReadOnlyList<int>? RegionalNumbers = null);

/// <summary>Une case du living dex : un Pokémon (et sa forme) demandé par le mode.</summary>
/// <param name="AnyForm">Remplie par n'importe quelle forme de l'espèce, même en Complet (Deoxys de 3e génération, Arceus).</param>
/// <param name="KeyItem">
/// Case remplie par un <b>autre</b> exemplaire de l'espèce que celui de la case précédente, avec cet objet rare dans le
/// sac de la sauvegarde : Shaymin Forme Céleste (Gracidée), qui redevient Terrestre dans une boîte en 4e génération ; un
/// second Shaymin et la Gracidée suffisent, sans le bloquer dans l'équipe.
/// </param>
/// <param name="Hint">Conseil affiché tant que la case manque (saisons de Vivaldaim et Haydaim : « à garder en boîte »).</param>
/// <param name="MegaItem">
/// Case Méga-Évolution ou Primo-Résurgence (formes de combat, jamais dans une sauvegarde) : remplie par un exemplaire
/// <b>de plus</b> de l'espèce qui tient cette Méga-Gemme (Gemme Rouge / Bleue pour les Primo) ; la case de l'espèce en
/// prend un autre.
/// </param>
/// <param name="MegaMove">Pareil, pour Méga-Rayquaza, qui n'a pas de gemme : un Rayquaza de plus qui connaît cette capacité.</param>
/// <param name="MegaInBag">
/// Case Méga de Let's Go, où les Pokémon ne tiennent pas d'objet : remplie par un exemplaire de plus de l'espèce, avec
/// la gemme (<paramref name="MegaItem"/>) dans le sac.
/// </param>
/// <param name="Gigamax">
/// Case Gigamax d'Épée / Bouclier (le facteur Gigamax est porté par le Pokémon, ce n'est pas une forme) : remplie par un
/// exemplaire <b>de plus</b> de l'espèce qui a le facteur Gigamax ; la case de l'espèce en prend un autre.
/// </param>
/// <param name="FormsFrom">
/// Case remplie par n'importe quelle forme de <paramref name="FormsFrom"/> à <paramref name="Form"/> (Pikachu Cosplayeur :
/// un seul par partie, qui change de costume) ; 0 : la forme exacte.
/// </param>
public sealed record LivingDexSlot(ushort Species, byte Form, bool AnyForm = false, ushort KeyItem = 0, string? Hint = null,
    ushort MegaItem = 0, ushort MegaMove = 0, byte FormsFrom = 0, bool MegaInBag = false, bool Gigamax = false);

/// <summary>Case du living dex avec le Pokémon de la sauvegarde qui la remplit, s'il y en a un.</summary>
/// <param name="Number">Numéro affiché : celui du Pokédex régional en mode Régional (Germignon : 1 à Johto), sinon le national.</param>
/// <param name="Name">« Pikachu », « Zarbi B » (Complet), « Deoxys (forme Vitesse) » (Émeraude).</param>
/// <param name="Sprite">Sprite du jeu, propre à la forme.</param>
/// <param name="BankId">Pokémon de la sauvegarde (sa fiche), null : manquant.</param>
/// <param name="Location">« Boîte 3, case 12 », « Équipe 2 ».</param>
/// <param name="Fallback">Image de la même forme sur le site, si le sprite du jeu manque (images pas encore récupérées).</param>
/// <param name="Note">
/// Condition de la case (<see cref="LivingDexSlot.KeyItem"/>) : « Gracidée dans le sac », « il faut un deuxième Shaymin » ;
/// ou conseil d'une case manquante (<see cref="LivingDexSlot.Hint"/>) ; gemme d'une case Méga (tenue, ou dans le sac en
/// Let's Go).
/// </param>
/// <param name="Section">Sous-Pokédex régional de la case (Kalos Centre, Côtes, Montagnes), null s'il n'y en a qu'un.</param>
public sealed record LivingDexEntry(int Number, ushort Species, byte Form, string Name, string Sprite, int? BankId, string? Location,
    string Fallback = "", string? Note = null, string? Section = null)
{
    public bool Owned => BankId is not null;
}

/// <summary>Carte de la vue générale : progression dans un mode.</summary>
/// <param name="Mode">Mode compté : celui demandé, ou le National si le jeu ne le propose pas (1re génération).</param>
public sealed record LivingDexSummary(int Id, LivingDexGameInfo Game, string SaveLabel, int Owned, int Total,
    LivingDexMode Mode = LivingDexMode.National);

public sealed record LivingDexView(int Id, LivingDexGameInfo Game, string SaveLabel, IReadOnlyList<LivingDexMode> Modes,
    LivingDexMode Mode, IReadOnlyList<LivingDexEntry> Entries)
{
    public int Owned => Entries.Count(e => e.Owned);
}

/// <summary>Sauvegarde stockée qui peut recevoir un living dex, et les jeux possibles (Rouge ou Bleu, Or ou Argent : au choix).</summary>
public sealed record LivingDexCandidate(int SaveId, string Label, IReadOnlyList<LivingDexGame> Games);

public sealed record LivingDexResult(bool Ok, string Message, int? Id = null);

/// <summary>Jeux qui ont un living dex : 1re à 9e génération (Écarlate / Violet ; pas encore Légendes Z-A).</summary>
public static class LivingDexGames
{
    private static readonly ushort[] Kanto = Enumerable.Range(1, 151).Select(n => (ushort)n).ToArray();

    /// <summary>
    /// Pokédex de Johto d'Or, Argent et Cristal : les 251 Pokémon, dans l'ordre du jeu (Germignon d'abord, Mew et Celebi à
    /// la fin). Repris des données de PokéAPI (pokemon_dex_numbers.csv, Pokédex 3 « original-johto »).
    /// </summary>
    private static readonly ushort[] Johto =
    [
        152, 153, 154, 155, 156, 157, 158, 159, 160, 16, 17, 18, 21, 22, 163, 164, 19, 20, 161, 162, 172, 25, 26, 10, 11,
        12, 13, 14, 15, 165, 166, 167, 168, 74, 75, 76, 41, 42, 169, 173, 35, 36, 174, 39, 40, 175, 176, 27, 28, 23,
        24, 206, 179, 180, 181, 194, 195, 92, 93, 94, 201, 95, 208, 69, 70, 71, 187, 188, 189, 46, 47, 60, 61, 62, 186,
        129, 130, 118, 119, 79, 80, 199, 43, 44, 45, 182, 96, 97, 63, 64, 65, 132, 204, 205, 29, 30, 31, 32, 33, 34,
        193, 191, 192, 102, 103, 185, 202, 48, 49, 123, 212, 127, 214, 109, 110, 88, 89, 81, 82, 100, 101, 190, 209, 210, 37,
        38, 58, 59, 234, 183, 184, 50, 51, 56, 57, 52, 53, 54, 55, 66, 67, 68, 236, 106, 107, 237, 203, 128, 241, 240,
        126, 238, 124, 239, 125, 122, 235, 83, 177, 178, 211, 72, 73, 98, 99, 213, 120, 121, 90, 91, 222, 223, 224, 170, 171,
        86, 87, 108, 114, 133, 134, 135, 136, 196, 197, 116, 117, 230, 207, 225, 220, 221, 216, 217, 231, 232, 226, 227, 84, 85,
        77, 78, 104, 105, 115, 111, 112, 198, 228, 229, 218, 219, 215, 200, 137, 233, 113, 242, 131, 138, 139, 140, 141, 142, 143,
        1, 2, 3, 4, 5, 6, 7, 8, 9, 144, 145, 146, 243, 244, 245, 147, 148, 149, 246, 247, 248, 249, 250, 150, 151,
        251,
    ];

    private const ushort Pichu = 172, Unown = 201, Deoxys = 386, Burmy = 412, Wormadam = 413, Shellos = 422, Gastrodon = 423,
        Rotom = 479, Giratina = 487, Shaymin = 492, Arceus = 493, Basculin = 550, Deerling = 585, Sawsbuck = 586, Tornadus = 641,
        Thundurus = 642, Landorus = 645, Kyurem = 646, Keldeo = 647, Genesect = 649, Pikachu = 25, Flabebe = 669,
        Floette = 670, Florges = 671, Pumpkaboo = 710, Gourgeist = 711, Hoopa = 720, Zygarde = 718, Oricorio = 741,
        Lycanroc = 745, Silvally = 773, Minior = 774, Necrozma = 800, Dialga = 483, Palkia = 484, Toxtricity = 849,
        Alcremie = 869, Urshifu = 892, Calyrex = 898, Enamorus = 905, Ursaluna = 901, Maushold = 925, Squawkabilly = 931,
        Tatsugiri = 978, Dudunsparce = 982, Gimmighoul = 999, Ogerpon = 1017;

    /// <summary>Espèces qui ont une forme d'Alola (forme 1), de Rattata à Ossatueur.</summary>
    private static readonly ushort[] AlolanForms = [19, 20, 26, 27, 28, 37, 38, 50, 51, 52, 53, 74, 75, 76, 88, 89, 103, 105];

    /// <summary>Rayquaza, et Draco-Ascension : sa Méga-Évolution n'a pas de gemme, elle vient de cette capacité.</summary>
    public const ushort Rayquaza = 384, DragonAscent = 620;

    /// <summary>Vase Scellé : objet rare qui libère Hoopa (Hoopa Déchaîné).</summary>
    public const ushort PrisonBottle = 765;

    /// <summary>Gracidée : objet rare qui donne à Shaymin sa Forme Céleste.</summary>
    private const ushort Gracidea = 466;

    /// <summary>
    /// Pokédex de Hoenn de Rubis, Saphir et Émeraude : 202 Pokémon, dans l'ordre du jeu (Arcko d'abord, Jirachi et Deoxys
    /// à la fin). Repris des données de PokéAPI (pokemon_dex_numbers.csv, Pokédex 4 « hoenn »).
    /// </summary>
    private static readonly ushort[] Hoenn =
    [
        252, 253, 254, 255, 256, 257, 258, 259, 260, 261, 262, 263, 264, 265, 266, 267, 268, 269, 270, 271, 272, 273, 274, 275, 276,
        277, 278, 279, 280, 281, 282, 283, 284, 285, 286, 287, 288, 289, 63, 64, 65, 290, 291, 292, 293, 294, 295, 296, 297, 118,
        119, 129, 130, 298, 183, 184, 74, 75, 76, 299, 300, 301, 41, 42, 169, 72, 73, 302, 303, 304, 305, 306, 66, 67, 68,
        307, 308, 309, 310, 311, 312, 81, 82, 100, 101, 313, 314, 43, 44, 45, 182, 84, 85, 315, 316, 317, 318, 319, 320,
        321, 322, 323, 218, 219, 324, 88, 89, 109, 110, 325, 326, 27, 28, 327, 227, 328, 329, 330, 331, 332, 333, 334, 335,
        336, 337, 338, 339, 340, 341, 342, 343, 344, 345, 346, 347, 348, 174, 39, 40, 349, 350, 351, 120, 121, 352, 353, 354,
        355, 356, 357, 358, 359, 37, 38, 172, 25, 26, 54, 55, 360, 202, 177, 178, 203, 231, 232, 127, 214, 111, 112, 361,
        362, 363, 364, 365, 366, 367, 368, 369, 222, 170, 171, 370, 116, 117, 230, 371, 372, 373, 374, 375, 376, 377, 378, 379,
        380, 381, 382, 383, 384, 385, 386,
    ];

    /// <summary>
    /// Pokédex de Sinnoh de Diamant et Perle : 151 Pokémon (Tortipouss d'abord, Manaphy à la fin). Repris des données de
    /// PokéAPI (pokemon_dex_numbers.csv, Pokédex 5 « original-sinnoh »).
    /// </summary>
    private static readonly ushort[] Sinnoh =
    [
        387, 388, 389, 390, 391, 392, 393, 394, 395, 396, 397, 398, 399, 400, 401, 402, 403, 404, 405, 63, 64, 65, 129, 130, 406,
        315, 407, 41, 42, 169, 74, 75, 76, 95, 208, 408, 409, 410, 411, 66, 67, 68, 54, 55, 412, 413, 414, 265, 266, 267,
        268, 269, 415, 416, 417, 418, 419, 420, 421, 422, 423, 214, 190, 424, 425, 426, 427, 428, 92, 93, 94, 200, 429, 198, 430,
        431, 432, 118, 119, 339, 340, 433, 358, 434, 435, 307, 308, 436, 437, 77, 78, 438, 185, 439, 122, 440, 113, 242, 173, 35,
        36, 441, 172, 25, 26, 163, 164, 442, 443, 444, 445, 446, 143, 201, 447, 448, 194, 195, 278, 279, 203, 449, 450, 298, 183,
        184, 451, 452, 453, 454, 455, 223, 224, 456, 457, 72, 73, 349, 350, 458, 226, 459, 460, 215, 461, 480, 481, 482, 483, 484,
        490,
    ];

    /// <summary>
    /// Pokédex de Sinnoh de Platine, agrandi : 210 Pokémon (Giratina à la fin). Repris des données de PokéAPI
    /// (pokemon_dex_numbers.csv, Pokédex 6 « extended-sinnoh »).
    /// </summary>
    private static readonly ushort[] SinnohPlatinum =
    [
        387, 388, 389, 390, 391, 392, 393, 394, 395, 396, 397, 398, 399, 400, 401, 402, 403, 404, 405, 63, 64, 65, 129, 130, 406,
        315, 407, 41, 42, 169, 74, 75, 76, 95, 208, 408, 409, 410, 411, 66, 67, 68, 54, 55, 412, 413, 414, 265, 266, 267,
        268, 269, 415, 416, 417, 418, 419, 420, 421, 422, 423, 214, 190, 424, 425, 426, 427, 428, 92, 93, 94, 200, 429, 198, 430,
        431, 432, 118, 119, 339, 340, 433, 358, 434, 435, 307, 308, 436, 437, 77, 78, 438, 185, 439, 122, 440, 113, 242, 173, 35,
        36, 441, 172, 25, 26, 163, 164, 442, 443, 444, 445, 446, 143, 201, 447, 448, 194, 195, 278, 279, 203, 449, 450, 298, 183,
        184, 451, 452, 453, 454, 455, 223, 224, 456, 457, 72, 73, 349, 350, 458, 226, 459, 460, 215, 461, 480, 481, 482, 483, 484,
        490, 479, 207, 472, 299, 476, 280, 281, 282, 475, 108, 463, 133, 134, 135, 136, 196, 197, 470, 471, 333, 334, 175, 176, 468,
        228, 229, 81, 82, 462, 114, 465, 193, 469, 357, 111, 112, 464, 355, 356, 477, 137, 233, 474, 123, 212, 239, 125, 466, 240,
        126, 467, 220, 221, 473, 361, 362, 478, 359, 487,
    ];

    /// <summary>
    /// Pokédex de Johto de HeartGold et SoulSilver : 256 Pokémon (Germignon d'abord, Celebi à la fin). Repris des données
    /// de PokéAPI (pokemon_dex_numbers.csv, Pokédex 7 « updated-johto »).
    /// </summary>
    private static readonly ushort[] JohtoHgss =
    [
        152, 153, 154, 155, 156, 157, 158, 159, 160, 16, 17, 18, 21, 22, 163, 164, 19, 20, 161, 162, 172, 25, 26, 10, 11,
        12, 13, 14, 15, 165, 166, 167, 168, 74, 75, 76, 41, 42, 169, 173, 35, 36, 174, 39, 40, 175, 176, 27, 28, 23,
        24, 206, 179, 180, 181, 194, 195, 92, 93, 94, 201, 95, 208, 69, 70, 71, 187, 188, 189, 46, 47, 60, 61, 62, 186,
        129, 130, 118, 119, 79, 80, 199, 43, 44, 45, 182, 96, 97, 63, 64, 65, 132, 204, 205, 29, 30, 31, 32, 33, 34,
        193, 469, 191, 192, 102, 103, 185, 202, 48, 49, 123, 212, 127, 214, 109, 110, 88, 89, 81, 82, 100, 101, 190, 424, 209,
        210, 37, 38, 58, 59, 234, 183, 184, 50, 51, 56, 57, 52, 53, 54, 55, 66, 67, 68, 236, 106, 107, 237, 203, 128,
        241, 240, 126, 238, 124, 239, 125, 122, 235, 83, 177, 178, 211, 72, 73, 98, 99, 213, 120, 121, 90, 91, 222, 223, 224,
        170, 171, 86, 87, 108, 463, 114, 465, 133, 134, 135, 136, 196, 197, 116, 117, 230, 207, 225, 220, 221, 473, 216, 217, 231,
        232, 226, 227, 84, 85, 77, 78, 104, 105, 115, 111, 112, 198, 228, 229, 218, 219, 215, 200, 137, 233, 113, 242, 131, 138,
        139, 140, 141, 142, 143, 1, 2, 3, 4, 5, 6, 7, 8, 9, 144, 145, 146, 243, 244, 245, 147, 148, 149, 246, 247,
        248, 249, 250, 150, 151, 251,
    ];

    /// <summary>
    /// Pokédex d'Unys de Noire et Blanche : 156 Pokémon, numérotés à partir de 0 (Victini n° 0, Vipélierre n° 1, Genesect
    /// n° 155). Repris des données de PokéAPI (pokemon_dex_numbers.csv, Pokédex 8 « original-unova »).
    /// </summary>
    private static readonly ushort[] Unova =
    [
        494, 495, 496, 497, 498, 499, 500, 501, 502, 503, 504, 505, 506, 507, 508, 509, 510, 511, 512, 513, 514, 515, 516, 517, 518,
        519, 520, 521, 522, 523, 524, 525, 526, 527, 528, 529, 530, 531, 532, 533, 534, 535, 536, 537, 538, 539, 540, 541, 542, 543,
        544, 545, 546, 547, 548, 549, 550, 551, 552, 553, 554, 555, 556, 557, 558, 559, 560, 561, 562, 563, 564, 565, 566, 567, 568,
        569, 570, 571, 572, 573, 574, 575, 576, 577, 578, 579, 580, 581, 582, 583, 584, 585, 586, 587, 588, 589, 590, 591, 592, 593,
        594, 595, 596, 597, 598, 599, 600, 601, 602, 603, 604, 605, 606, 607, 608, 609, 610, 611, 612, 613, 614, 615, 616, 617, 618,
        619, 620, 621, 622, 623, 624, 625, 626, 627, 628, 629, 630, 631, 632, 633, 634, 635, 636, 637, 638, 639, 640, 641, 642, 643,
        644, 645, 646, 647, 648, 649,
    ];

    /// <summary>
    /// Pokédex d'Unys de Noire 2 et Blanche 2 : 301 Pokémon, de Victini (n° 0) à Genesect (n° 300). Repris des données de
    /// PokéAPI (pokemon_dex_numbers.csv, Pokédex 9 « updated-unova »).
    /// </summary>
    private static readonly ushort[] Unova2 =
    [
        494, 495, 496, 497, 498, 499, 500, 501, 502, 503, 504, 505, 509, 510, 519, 520, 521, 540, 541, 542, 191, 192, 506, 507, 508,
        179, 180, 181, 54, 55, 298, 183, 184, 447, 448, 206, 531, 511, 512, 513, 514, 515, 516, 543, 544, 545, 109, 110, 81, 82,
        462, 58, 59, 240, 126, 467, 239, 125, 466, 19, 20, 41, 42, 169, 88, 89, 527, 528, 524, 525, 526, 95, 208, 532, 533,
        534, 529, 530, 300, 301, 427, 428, 546, 547, 548, 549, 517, 518, 173, 35, 36, 133, 134, 135, 136, 196, 197, 470, 471, 551,
        552, 553, 554, 555, 550, 568, 569, 572, 573, 627, 628, 629, 630, 27, 28, 557, 558, 559, 560, 556, 561, 328, 329, 330, 562,
        563, 564, 565, 566, 567, 599, 600, 601, 406, 315, 407, 574, 575, 576, 577, 578, 579, 415, 416, 587, 214, 127, 522, 523, 418,
        419, 570, 571, 580, 581, 588, 589, 616, 617, 585, 586, 590, 591, 351, 299, 476, 304, 305, 306, 343, 344, 636, 637, 595, 596,
        597, 598, 602, 603, 604, 592, 593, 594, 610, 611, 612, 335, 336, 605, 606, 607, 608, 609, 631, 632, 613, 614, 615, 641, 642,
        645, 451, 452, 227, 322, 323, 325, 326, 425, 426, 353, 354, 278, 279, 337, 338, 359, 114, 465, 619, 620, 207, 472, 624, 625,
        638, 639, 640, 535, 536, 537, 618, 213, 458, 226, 223, 224, 222, 120, 121, 320, 321, 131, 363, 364, 365, 333, 334, 37, 38,
        436, 437, 215, 461, 225, 582, 583, 584, 220, 221, 473, 132, 374, 375, 376, 86, 87, 538, 539, 626, 621, 622, 623, 633, 634,
        635, 287, 288, 289, 341, 342, 174, 39, 40, 108, 463, 193, 469, 357, 455, 453, 454, 246, 247, 248, 643, 644, 646, 647, 648,
        649,
    ];

    /// <summary>
    /// Pokédex de Kalos de X et Y : trois sous-Pokédex à la suite, Centre (153), Côtes (153) et Montagnes (151), chacun
    /// numéroté à partir de 1 ; 457 Pokémon, sans doublon. Repris des données de PokéAPI (pokemon_dex_numbers.csv,
    /// Pokédex 12, 13 et 14 « kalos-central », « kalos-coastal », « kalos-mountain »).
    /// </summary>
    private static readonly ushort[] Kalos =
    [
        // Centre
        650, 651, 652, 653, 654, 655, 656, 657, 658, 659, 660, 263, 264, 661, 662, 663, 16, 17, 18, 664, 665, 666, 10, 11, 12,
        13, 14, 15, 511, 512, 513, 514, 515, 516, 172, 25, 26, 399, 400, 206, 298, 183, 184, 412, 413, 414, 283, 284, 129, 130,
        341, 342, 118, 119, 318, 319, 667, 668, 54, 55, 83, 447, 448, 280, 281, 282, 475, 669, 670, 671, 406, 315, 407, 165, 166,
        415, 416, 300, 301, 1, 2, 3, 4, 5, 6, 7, 8, 9, 672, 673, 674, 675, 676, 84, 85, 311, 312, 316, 317, 559,
        560, 63, 64, 65, 43, 44, 45, 182, 161, 162, 290, 291, 292, 677, 678, 352, 679, 680, 681, 543, 544, 545, 531, 235, 453,
        454, 580, 581, 682, 683, 684, 685, 313, 314, 187, 188, 189, 446, 143, 293, 294, 295, 307, 308, 41, 42, 169, 610, 611, 612,
        719, 720, 721,
        // Côtes
        425, 426, 619, 620, 335, 336, 325, 326, 359, 686, 687, 337, 338, 371, 372, 373, 278, 279, 276, 277, 688, 689, 557, 558, 72,
        73, 320, 321, 370, 690, 691, 692, 693, 120, 121, 90, 91, 211, 116, 117, 230, 369, 551, 552, 553, 694, 695, 449, 450, 111,
        112, 464, 95, 208, 527, 528, 66, 67, 68, 104, 105, 115, 303, 696, 697, 698, 699, 142, 597, 598, 209, 210, 309, 310, 228,
        229, 133, 134, 135, 136, 196, 197, 470, 471, 700, 587, 193, 469, 701, 561, 622, 623, 299, 476, 296, 297, 538, 539, 396, 397,
        398, 434, 435, 29, 30, 31, 32, 33, 34, 702, 433, 358, 439, 122, 577, 578, 579, 360, 202, 524, 525, 526, 302, 703, 128,
        241, 179, 180, 181, 127, 214, 417, 79, 80, 199, 102, 103, 441, 458, 226, 366, 367, 368, 223, 224, 222, 170, 171, 594, 131,
        144, 145, 146,
        // Montagnes
        50, 51, 328, 329, 330, 443, 444, 445, 74, 75, 76, 218, 219, 213, 451, 452, 194, 195, 704, 705, 706, 588, 589, 616, 617,
        69, 70, 71, 455, 92, 93, 94, 60, 61, 62, 186, 23, 24, 618, 339, 340, 509, 510, 261, 262, 504, 505, 624, 625, 707,
        198, 430, 590, 591, 270, 271, 272, 418, 419, 550, 708, 709, 710, 711, 607, 608, 609, 479, 81, 82, 462, 100, 101, 568, 569,
        220, 221, 473, 712, 713, 613, 614, 238, 124, 582, 583, 584, 459, 460, 225, 215, 461, 532, 533, 534, 324, 27, 28, 304, 305,
        306, 246, 247, 248, 631, 632, 167, 168, 21, 22, 615, 227, 714, 715, 207, 472, 163, 164, 174, 39, 40, 353, 354, 570, 571,
        574, 575, 576, 438, 185, 327, 216, 217, 108, 463, 123, 212, 132, 333, 334, 621, 633, 634, 635, 147, 148, 149, 716, 717, 718,
        150,
    ];

    private static readonly (string Name, int Count)[] KalosSections = [("Kalos Centre", 153), ("Kalos Côtes", 153), ("Kalos Montagnes", 151)];

    /// <summary>
    /// Pokédex de Hoenn de Rubis Oméga et Saphir Alpha : 211 Pokémon (Arcko d'abord, Deoxys à la fin). Repris des données
    /// de PokéAPI (pokemon_dex_numbers.csv, Pokédex 15 « updated-hoenn »).
    /// </summary>
    private static readonly ushort[] HoennOras =
    [
        252, 253, 254, 255, 256, 257, 258, 259, 260, 261, 262, 263, 264, 265, 266, 267, 268, 269, 270, 271, 272, 273, 274, 275, 276,
        277, 278, 279, 280, 281, 282, 475, 283, 284, 285, 286, 287, 288, 289, 63, 64, 65, 290, 291, 292, 293, 294, 295, 296, 297,
        118, 119, 129, 130, 298, 183, 184, 74, 75, 76, 299, 476, 300, 301, 41, 42, 169, 72, 73, 302, 303, 304, 305, 306, 66,
        67, 68, 307, 308, 309, 310, 311, 312, 81, 82, 462, 100, 101, 313, 314, 43, 44, 45, 182, 84, 85, 406, 315, 407, 316,
        317, 318, 319, 320, 321, 322, 323, 218, 219, 324, 88, 89, 109, 110, 325, 326, 27, 28, 327, 227, 328, 329, 330, 331, 332,
        333, 334, 335, 336, 337, 338, 339, 340, 341, 342, 343, 344, 345, 346, 347, 348, 174, 39, 40, 349, 350, 351, 120, 121, 352,
        353, 354, 355, 356, 477, 357, 433, 358, 359, 37, 38, 172, 25, 26, 54, 55, 360, 202, 177, 178, 203, 231, 232, 127, 214,
        111, 112, 464, 361, 362, 478, 363, 364, 365, 366, 367, 368, 369, 222, 170, 171, 370, 116, 117, 230, 371, 372, 373, 374, 375,
        376, 377, 378, 379, 380, 381, 382, 383, 384, 385, 386,
    ];

    /// <summary>
    /// Pokédex d'Alola de Soleil et Lune : 302 Pokémon (Brindibou d'abord, Marshadow à la fin), une seule liste (les
    /// Pokédex des îles se recoupent). Repris des données de PokéAPI (pokemon_dex_numbers.csv, Pokédex 16 « original-alola »).
    /// </summary>
    private static readonly ushort[] Alola =
    [
        722, 723, 724, 725, 726, 727, 728, 729, 730, 731, 732, 733, 734, 735, 19, 20, 10, 11, 12, 165, 166, 167, 168, 172, 25,
        26, 736, 737, 738, 438, 185, 440, 113, 242, 446, 143, 79, 80, 199, 278, 279, 63, 64, 65, 52, 53, 81, 82, 462, 88,
        89, 58, 59, 96, 97, 296, 297, 235, 739, 740, 92, 93, 94, 425, 426, 200, 429, 41, 42, 169, 50, 51, 21, 22, 627,
        628, 629, 630, 56, 57, 225, 741, 742, 743, 548, 549, 546, 547, 54, 55, 129, 130, 339, 340, 66, 67, 68, 524, 525, 526,
        703, 302, 744, 745, 327, 72, 73, 456, 457, 746, 370, 222, 747, 748, 90, 91, 371, 372, 373, 506, 507, 508, 133, 134, 135,
        136, 196, 197, 470, 471, 700, 749, 750, 174, 39, 40, 128, 241, 283, 284, 751, 752, 753, 754, 755, 756, 46, 47, 60, 61,
        62, 186, 118, 119, 349, 350, 594, 661, 662, 663, 757, 758, 104, 105, 115, 240, 126, 467, 759, 760, 761, 762, 763, 764, 127,
        765, 766, 704, 705, 706, 351, 767, 768, 120, 121, 769, 770, 408, 409, 410, 411, 566, 567, 564, 565, 708, 709, 299, 476, 771,
        170, 171, 772, 773, 718, 568, 569, 227, 132, 173, 35, 36, 774, 374, 375, 376, 137, 233, 474, 674, 675, 775, 324, 776, 777,
        239, 125, 466, 74, 75, 76, 551, 552, 553, 328, 329, 330, 443, 444, 445, 707, 778, 779, 780, 359, 361, 362, 478, 215, 461,
        27, 28, 37, 38, 582, 583, 584, 209, 210, 422, 423, 369, 781, 318, 319, 320, 321, 131, 102, 103, 782, 783, 784, 587, 123,
        212, 198, 430, 447, 448, 147, 148, 149, 142, 785, 786, 787, 788, 789, 790, 791, 792, 793, 794, 795, 796, 797, 798, 799, 800,
        801, 802,
    ];

    /// <summary>
    /// Pokédex d'Alola d'Ultra-Soleil et Ultra-Lune : 403 Pokémon (Zeraora à la fin). Repris des données de PokéAPI
    /// (pokemon_dex_numbers.csv, Pokédex 21 « updated-alola »).
    /// </summary>
    private static readonly ushort[] AlolaUltra =
    [
        722, 723, 724, 725, 726, 727, 728, 729, 730, 731, 732, 733, 734, 735, 19, 20, 10, 11, 12, 165, 166, 167, 168, 427, 428,
        686, 687, 570, 571, 676, 172, 25, 26, 736, 737, 738, 438, 185, 440, 113, 242, 446, 143, 79, 80, 199, 278, 279, 63, 64,
        65, 52, 53, 81, 82, 462, 88, 89, 439, 122, 23, 24, 206, 58, 59, 96, 97, 296, 297, 235, 739, 740, 92, 93, 94,
        425, 426, 198, 430, 41, 42, 169, 714, 715, 50, 51, 21, 22, 627, 628, 629, 630, 56, 57, 225, 701, 741, 742, 743, 669,
        670, 671, 548, 549, 546, 547, 54, 55, 238, 124, 129, 130, 339, 340, 86, 87, 66, 67, 68, 524, 525, 526, 703, 302, 303,
        744, 745, 327, 72, 73, 456, 457, 746, 370, 222, 747, 748, 90, 91, 366, 367, 368, 223, 224, 458, 226, 371, 372, 373, 506,
        507, 508, 133, 134, 135, 136, 196, 197, 470, 471, 700, 179, 180, 181, 749, 750, 174, 39, 40, 128, 241, 283, 284, 751, 752,
        753, 754, 755, 756, 46, 47, 60, 61, 62, 186, 118, 119, 550, 349, 350, 594, 661, 662, 663, 757, 758, 104, 105, 115, 240,
        126, 467, 636, 637, 759, 760, 761, 762, 763, 764, 127, 163, 164, 352, 765, 766, 704, 705, 706, 351, 767, 768, 120, 121, 769,
        770, 138, 139, 140, 141, 345, 346, 347, 348, 408, 409, 410, 411, 566, 567, 564, 565, 696, 697, 698, 699, 246, 247, 248, 708,
        709, 177, 178, 299, 476, 771, 170, 171, 772, 773, 803, 804, 718, 568, 569, 572, 573, 204, 205, 227, 132, 173, 35, 36, 605,
        606, 774, 374, 375, 376, 137, 233, 474, 674, 675, 775, 324, 776, 228, 229, 702, 777, 309, 310, 239, 125, 466, 74, 75, 76,
        551, 552, 553, 328, 329, 330, 443, 444, 445, 343, 344, 622, 623, 707, 778, 353, 354, 592, 593, 779, 780, 359, 361, 362, 478,
        215, 461, 27, 28, 37, 38, 582, 583, 584, 559, 560, 624, 625, 209, 210, 422, 423, 369, 781, 318, 319, 690, 691, 692, 693,
        320, 321, 131, 357, 102, 103, 341, 342, 619, 620, 782, 783, 784, 587, 123, 212, 214, 190, 424, 667, 668, 200, 429, 621, 108,
        463, 447, 448, 147, 148, 149, 142, 785, 786, 787, 788, 789, 790, 791, 792, 793, 805, 806, 794, 795, 796, 797, 798, 799, 800,
        801, 802, 807,
    ];

    /// <summary>
    /// Pokédex de Let's Go : les 151 de Kanto, Meltan et Melmetal ; aussi tout ce que le jeu peut contenir (son national).
    /// Repris des données de PokéAPI (pokemon_dex_numbers.csv, Pokédex 26 « letsgo-kanto »).
    /// </summary>
    private static readonly ushort[] LetsGoKanto = [.. Kanto, 808, 809];

    /// <summary>
    /// Pokédex de Galar d'Épée et Bouclier : celui du jeu (400), puis ce que les Pokédex de l'Isolarmure (110 de plus sur
    /// 211) et de la Couronneige (74 de plus sur 210) ajoutent, sans doublon, chacun avec ses numéros (<see cref="GalarNumbers"/>).
    /// Repris des données de PokéAPI (pokemon_dex_numbers.csv, Pokédex 27, 28 et 29 « galar », « isle-of-armor »,
    /// « crown-tundra »).
    /// </summary>
    private static readonly ushort[] Galar =
    [
        810, 811, 812, 813, 814, 815, 816, 817, 818, 824, 825, 826, 10, 11, 12, 736, 737, 738, 163, 164, 821, 822, 823, 819, 820,
        519, 520, 521, 827, 828, 263, 264, 862, 831, 832, 270, 271, 272, 273, 274, 275, 833, 834, 509, 510, 835, 836, 659, 660, 572,
        573, 761, 762, 763, 43, 44, 45, 182, 406, 315, 407, 278, 279, 595, 596, 309, 310, 37, 38, 58, 59, 582, 583, 584, 220,
        221, 473, 225, 361, 362, 478, 343, 344, 749, 750, 557, 558, 622, 623, 517, 518, 177, 178, 759, 760, 459, 460, 98, 99, 194,
        195, 341, 342, 290, 291, 292, 236, 106, 107, 237, 674, 675, 599, 600, 601, 415, 416, 436, 437, 280, 281, 282, 475, 425, 426,
        829, 830, 420, 421, 434, 435, 535, 536, 537, 355, 356, 477, 66, 67, 68, 92, 93, 94, 129, 130, 118, 119, 223, 224, 90,
        91, 349, 350, 550, 746, 771, 568, 569, 850, 851, 837, 838, 839, 50, 51, 529, 530, 524, 525, 526, 532, 533, 534, 527, 528,
        714, 715, 95, 208, 846, 847, 52, 863, 53, 868, 869, 742, 743, 597, 598, 710, 711, 172, 25, 26, 133, 134, 135, 136, 196,
        197, 470, 471, 700, 840, 841, 842, 677, 678, 684, 685, 682, 683, 751, 752, 360, 202, 83, 865, 170, 171, 453, 454, 559, 560,
        618, 213, 339, 340, 422, 423, 767, 768, 688, 689, 222, 864, 859, 860, 861, 856, 857, 858, 757, 758, 624, 625, 538, 539, 109,
        110, 438, 185, 173, 35, 36, 175, 176, 468, 446, 143, 546, 547, 111, 112, 464, 574, 575, 576, 577, 578, 579, 588, 589, 616,
        617, 605, 606, 613, 614, 627, 628, 629, 630, 451, 452, 607, 608, 609, 686, 687, 215, 461, 302, 303, 556, 561, 447, 448, 324,
        778, 878, 879, 211, 592, 593, 747, 748, 845, 848, 849, 843, 844, 449, 450, 632, 631, 694, 695, 701, 328, 329, 330, 610, 611,
        612, 562, 867, 563, 679, 680, 681, 77, 78, 854, 855, 876, 708, 709, 755, 756, 765, 766, 877, 870, 780, 776, 777, 872, 873,
        852, 853, 871, 458, 226, 320, 321, 712, 713, 781, 131, 337, 338, 439, 122, 866, 554, 555, 874, 875, 884, 479, 132, 880, 881,
        882, 883, 4, 5, 6, 772, 773, 246, 247, 248, 633, 634, 635, 704, 705, 706, 782, 783, 784, 885, 886, 887, 888, 889, 890,
        79, 80, 199, 427, 428, 440, 113, 242, 174, 39, 40, 753, 754, 661, 662, 663, 403, 404, 405, 707, 63, 64, 65, 72, 73,
        206, 626, 108, 463, 621, 1, 2, 3, 7, 8, 9, 543, 544, 545, 590, 591, 764, 114, 465, 570, 571, 120, 121, 891, 892,
        587, 702, 81, 82, 462, 318, 319, 506, 507, 508, 128, 241, 123, 212, 127, 214, 769, 770, 298, 183, 184, 60, 61, 62, 186,
        54, 55, 293, 294, 295, 227, 744, 745, 619, 620, 27, 28, 104, 105, 115, 551, 552, 553, 636, 637, 690, 691, 692, 693, 116,
        117, 230, 548, 549, 102, 103, 137, 233, 474, 893, 238, 124, 239, 125, 466, 240, 126, 467, 531, 615, 333, 334, 442, 29, 30,
        31, 32, 33, 34, 696, 697, 698, 699, 359, 371, 372, 373, 443, 444, 445, 138, 139, 140, 141, 142, 703, 374, 375, 376, 41,
        42, 169, 564, 565, 566, 567, 363, 364, 365, 345, 346, 347, 348, 369, 304, 305, 306, 147, 148, 149, 377, 378, 379, 894, 895,
        144, 145, 146, 638, 639, 640, 896, 897, 898,
    ];

    /// <summary>Numéro de chaque Pokémon de <see cref="Galar"/> dans le Pokédex du jeu où il apparaît en premier.</summary>
    private static readonly int[] GalarNumbers =
    [
        1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25,
        26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50,
        51, 52, 53, 54, 55, 56, 57, 58, 59, 60, 61, 62, 63, 64, 65, 66, 67, 68, 69, 70, 71, 72, 73, 74, 75,
        76, 77, 78, 79, 80, 81, 82, 83, 84, 85, 86, 87, 88, 89, 90, 91, 92, 93, 94, 95, 96, 97, 98, 99, 100,
        101, 102, 103, 104, 105, 106, 107, 108, 109, 110, 111, 112, 113, 114, 115, 116, 117, 118, 119, 120, 121, 122, 123, 124, 125,
        126, 127, 128, 129, 130, 131, 132, 133, 134, 135, 136, 137, 138, 139, 140, 141, 142, 143, 144, 145, 146, 147, 148, 149, 150,
        151, 152, 153, 154, 155, 156, 157, 158, 159, 160, 161, 162, 163, 164, 165, 166, 167, 168, 169, 170, 171, 172, 173, 174, 175,
        176, 177, 178, 179, 180, 181, 182, 183, 184, 185, 186, 187, 188, 189, 190, 191, 192, 193, 194, 195, 196, 197, 198, 199, 200,
        201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 214, 215, 216, 217, 218, 219, 220, 221, 222, 223, 224, 225,
        226, 227, 228, 229, 230, 231, 232, 233, 234, 235, 236, 237, 238, 239, 240, 241, 242, 243, 244, 245, 246, 247, 248, 249, 250,
        251, 252, 253, 254, 255, 256, 257, 258, 259, 260, 261, 262, 263, 264, 265, 266, 267, 268, 269, 270, 271, 272, 273, 274, 275,
        276, 277, 278, 279, 280, 281, 282, 283, 284, 285, 286, 287, 288, 289, 290, 291, 292, 293, 294, 295, 296, 297, 298, 299, 300,
        301, 302, 303, 304, 305, 306, 307, 308, 309, 310, 311, 312, 313, 314, 315, 316, 317, 318, 319, 320, 321, 322, 323, 324, 325,
        326, 327, 328, 329, 330, 331, 332, 333, 334, 335, 336, 337, 338, 339, 340, 341, 342, 343, 344, 345, 346, 347, 348, 349, 350,
        351, 352, 353, 354, 355, 356, 357, 358, 359, 360, 361, 362, 363, 364, 365, 366, 367, 368, 369, 370, 371, 372, 373, 374, 375,
        376, 377, 378, 379, 380, 381, 382, 383, 384, 385, 386, 387, 388, 389, 390, 391, 392, 393, 394, 395, 396, 397, 398, 399, 400,
        1, 2, 3, 4, 5, 6, 7, 8, 11, 12, 13, 17, 18, 22, 23, 24, 25, 26, 27, 28, 31, 32, 33, 40, 41,
        52, 53, 54, 55, 63, 68, 69, 70, 71, 72, 73, 74, 75, 76, 77, 78, 79, 80, 81, 87, 88, 98, 99, 100, 101,
        102, 103, 105, 106, 107, 111, 112, 113, 114, 115, 116, 117, 118, 119, 120, 121, 133, 134, 139, 140, 141, 142, 143, 144, 145,
        146, 147, 148, 149, 150, 153, 157, 158, 163, 164, 168, 169, 170, 171, 172, 176, 177, 178, 186, 187, 194, 195, 196, 197, 198,
        199, 200, 201, 202, 205, 206, 208, 209, 210, 211, 13, 14, 15, 16, 17, 18, 19, 20, 21, 30, 35, 36, 47, 65, 66,
        67, 68, 69, 70, 83, 84, 85, 86, 107, 113, 114, 115, 116, 117, 118, 123, 124, 125, 126, 127, 128, 129, 130, 131, 144,
        145, 146, 147, 148, 149, 150, 159, 160, 161, 183, 184, 185, 186, 187, 191, 192, 193, 194, 195, 196, 197, 198, 199, 200, 201,
        202, 203, 204, 205, 206, 207, 208, 209, 210,
    ];

    private static readonly (string Name, int Count)[] GalarSections = [("Galar", 400), ("Isolarmure", 110), ("Couronneige", 74)];

    /// <summary>Espèces qu'Épée et Bouclier peuvent contenir (664 : PKHeX, sous n'importe quelle forme).</summary>
    private static readonly ushort[] SwordShieldNational = Enumerable.Range(1, 898).Select(n => (ushort)n)
        .Where(n => Enumerable.Range(0, PersonalTable.SWSH[n].FormCount).Any(f => PersonalTable.SWSH.IsPresentInGame(n, (byte)f)))
        .ToArray();

    /// <summary>
    /// Pokédex de Paldea d'Écarlate et Violet : celui du jeu (400), puis ce que les Pokédex de Septentria (99 de plus sur
    /// 200) et de la Myrtille (165 de plus sur 243) ajoutent, sans doublon, chacun avec ses numéros (<see cref="PaldeaNumbers"/>).
    /// Repris des données de PokéAPI (pokemon_dex_numbers.csv, Pokédex 31, 32 et 33 « paldea », « kitakami », « blueberry »).
    /// </summary>
    private static readonly ushort[] Paldea =
    [
        906, 907, 908, 909, 910, 911, 912, 913, 914, 915, 916, 917, 918, 919, 920, 187, 188, 189, 661, 662, 663, 921, 922, 923, 228,
        229, 734, 735, 819, 820, 191, 192, 401, 402, 664, 665, 666, 415, 416, 821, 822, 823, 440, 113, 242, 298, 183, 184, 283, 284,
        418, 419, 194, 980, 54, 55, 833, 834, 174, 39, 40, 280, 281, 282, 475, 96, 97, 92, 93, 94, 924, 925, 172, 25, 26,
        926, 927, 287, 288, 289, 761, 762, 763, 928, 929, 930, 438, 185, 744, 745, 837, 838, 839, 403, 404, 405, 396, 397, 398, 741,
        179, 180, 181, 548, 549, 285, 286, 840, 841, 842, 325, 326, 931, 200, 429, 296, 297, 739, 740, 757, 758, 231, 232, 878, 879,
        443, 444, 445, 932, 933, 934, 278, 279, 129, 130, 846, 847, 550, 316, 317, 52, 53, 425, 426, 669, 670, 671, 50, 51, 324,
        322, 323, 436, 437, 610, 611, 612, 56, 57, 979, 307, 308, 447, 448, 935, 936, 937, 339, 340, 938, 939, 704, 705, 706, 453,
        454, 940, 941, 133, 134, 135, 136, 196, 197, 470, 471, 700, 206, 982, 585, 586, 203, 981, 88, 89, 942, 943, 848, 849, 702,
        417, 944, 945, 234, 590, 591, 100, 101, 81, 82, 462, 132, 58, 59, 216, 217, 335, 336, 333, 334, 672, 673, 128, 667, 668,
        434, 435, 570, 571, 215, 461, 198, 430, 574, 575, 576, 854, 855, 778, 707, 876, 946, 947, 948, 949, 357, 753, 754, 950, 951,
        952, 331, 332, 953, 954, 48, 49, 204, 205, 123, 212, 214, 955, 956, 449, 450, 551, 552, 553, 843, 844, 749, 750, 636, 637,
        371, 372, 373, 957, 958, 959, 856, 857, 858, 859, 860, 861, 960, 961, 962, 963, 964, 965, 966, 967, 968, 302, 353, 354, 870,
        701, 442, 714, 715, 885, 886, 887, 969, 970, 479, 971, 972, 765, 766, 775, 246, 247, 248, 874, 875, 871, 769, 770, 79, 80,
        199, 422, 423, 90, 91, 211, 370, 456, 457, 779, 594, 690, 691, 692, 693, 602, 603, 604, 747, 748, 973, 147, 148, 149, 872,
        873, 459, 460, 225, 613, 614, 361, 362, 478, 615, 974, 975, 712, 713, 627, 628, 624, 625, 983, 633, 634, 635, 976, 977, 978,
        984, 985, 986, 987, 988, 989, 990, 991, 992, 993, 994, 995, 996, 997, 998, 999, 1000, 1001, 1002, 1003, 1004, 1005, 1006, 1007, 1008,
        167, 168, 193, 469, 195, 261, 262, 313, 314, 341, 342, 540, 541, 542, 742, 743, 23, 24, 69, 70, 71, 161, 162, 1011, 37,
        38, 60, 61, 62, 186, 163, 164, 190, 424, 220, 221, 473, 273, 274, 275, 708, 709, 1012, 1013, 74, 75, 76, 532, 533, 534,
        877, 446, 143, 270, 271, 272, 299, 476, 736, 737, 738, 27, 28, 207, 472, 629, 630, 782, 783, 784, 109, 110, 619, 620, 355,
        356, 477, 433, 358, 218, 219, 607, 608, 609, 173, 35, 36, 349, 350, 703, 580, 581, 845, 902, 901, 1014, 1015, 1016, 1017, 84,
        85, 102, 103, 111, 112, 464, 239, 125, 466, 240, 126, 467, 900, 522, 523, 235, 868, 869, 328, 329, 330, 731, 732, 733, 72,
        73, 116, 117, 230, 546, 547, 764, 43, 44, 45, 182, 170, 171, 686, 687, 751, 752, 236, 106, 107, 237, 529, 530, 677, 678,
        774, 408, 409, 410, 411, 572, 573, 227, 311, 312, 559, 560, 622, 623, 137, 233, 474, 595, 596, 374, 375, 376, 86, 87, 131,
        904, 577, 578, 579, 209, 210, 884, 1018, 1019, 1, 2, 3, 4, 5, 6, 7, 8, 9, 152, 153, 154, 155, 156, 157, 158,
        159, 160, 252, 253, 254, 255, 256, 257, 258, 259, 260, 387, 388, 389, 390, 391, 392, 393, 394, 395, 495, 496, 497, 498, 499,
        500, 501, 502, 503, 650, 651, 652, 653, 654, 655, 656, 657, 658, 722, 723, 724, 725, 726, 727, 728, 729, 730, 810, 811, 812,
        813, 814, 815, 816, 817, 818, 1020, 1021, 1023, 1022, 1024, 1009, 1010, 1025,
    ];

    /// <summary>Numéro de chaque Pokémon de <see cref="Paldea"/> dans le Pokédex du jeu où il apparaît en premier.</summary>
    private static readonly int[] PaldeaNumbers =
    [
        1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25,
        26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50,
        51, 52, 53, 54, 55, 56, 57, 58, 59, 60, 61, 62, 63, 64, 65, 66, 67, 68, 69, 70, 71, 72, 73, 74, 75,
        76, 77, 78, 79, 80, 81, 82, 83, 84, 85, 86, 87, 88, 89, 90, 91, 92, 93, 94, 95, 96, 97, 98, 99, 100,
        101, 102, 103, 104, 105, 106, 107, 108, 109, 110, 111, 112, 113, 114, 115, 116, 117, 118, 119, 120, 121, 122, 123, 124, 125,
        126, 127, 128, 129, 130, 131, 132, 133, 134, 135, 136, 137, 138, 139, 140, 141, 142, 143, 144, 145, 146, 147, 148, 149, 150,
        151, 152, 153, 154, 155, 156, 157, 158, 159, 160, 161, 162, 163, 164, 165, 166, 167, 168, 169, 170, 171, 172, 173, 174, 175,
        176, 177, 178, 179, 180, 181, 182, 183, 184, 185, 186, 187, 188, 189, 190, 191, 192, 193, 194, 195, 196, 197, 198, 199, 200,
        201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 214, 215, 216, 217, 218, 219, 220, 221, 222, 223, 224, 225,
        226, 227, 228, 229, 230, 231, 232, 233, 234, 235, 236, 237, 238, 239, 240, 241, 242, 243, 244, 245, 246, 247, 248, 249, 250,
        251, 252, 253, 254, 255, 256, 257, 258, 259, 260, 261, 262, 263, 264, 265, 266, 267, 268, 269, 270, 271, 272, 273, 274, 275,
        276, 277, 278, 279, 280, 281, 282, 283, 284, 285, 286, 287, 288, 289, 290, 291, 292, 293, 294, 295, 296, 297, 298, 299, 300,
        301, 302, 303, 304, 305, 306, 307, 308, 309, 310, 311, 312, 313, 314, 315, 316, 317, 318, 319, 320, 321, 322, 323, 324, 325,
        326, 327, 328, 329, 330, 331, 332, 333, 334, 335, 336, 337, 338, 339, 340, 341, 342, 343, 344, 345, 346, 347, 348, 349, 350,
        351, 352, 353, 354, 355, 356, 357, 358, 359, 360, 361, 362, 363, 364, 365, 366, 367, 368, 369, 370, 371, 372, 373, 374, 375,
        376, 377, 378, 379, 380, 381, 382, 383, 384, 385, 386, 387, 388, 389, 390, 391, 392, 393, 394, 395, 396, 397, 398, 399, 400,
        1, 2, 3, 4, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 23, 24, 25, 26, 27, 36, 37,
        38, 39, 40, 41, 42, 45, 46, 47, 48, 50, 51, 52, 54, 55, 56, 68, 69, 76, 77, 80, 81, 82, 85, 86, 87,
        95, 102, 103, 104, 105, 106, 107, 108, 112, 113, 114, 116, 117, 121, 122, 127, 128, 131, 132, 133, 135, 136, 137, 138, 139,
        140, 141, 142, 143, 144, 145, 146, 147, 148, 151, 152, 153, 158, 159, 167, 181, 182, 185, 195, 196, 197, 198, 199, 200, 1,
        2, 3, 4, 5, 6, 7, 10, 11, 12, 13, 14, 15, 21, 23, 24, 40, 42, 43, 44, 45, 46, 47, 48, 49, 50,
        51, 52, 53, 54, 56, 57, 58, 62, 63, 64, 65, 78, 79, 80, 81, 90, 91, 92, 93, 94, 95, 99, 100, 104, 105,
        106, 107, 108, 109, 110, 111, 112, 113, 119, 120, 121, 122, 123, 124, 129, 130, 131, 132, 133, 137, 138, 139, 143, 144, 145,
        147, 148, 149, 150, 151, 152, 161, 162, 163, 164, 165, 166, 167, 168, 169, 170, 171, 172, 173, 174, 175, 176, 177, 178, 179,
        180, 181, 182, 183, 184, 185, 186, 187, 188, 189, 190, 191, 192, 193, 194, 195, 196, 197, 198, 199, 200, 201, 202, 203, 204,
        205, 206, 207, 208, 209, 210, 211, 212, 213, 214, 215, 216, 217, 218, 219, 220, 221, 222, 223, 224, 225, 226, 227, 228, 229,
        230, 231, 232, 233, 234, 235, 236, 237, 238, 239, 240, 241, 242, 243,
    ];

    private static readonly (string Name, int Count)[] PaldeaSections = [("Paldea", 400), ("Septentria", 99), ("Myrtille", 165)];

    /// <summary>Espèces qu'Écarlate et Violet peuvent contenir (733 : PKHeX, sous n'importe quelle forme).</summary>
    private static readonly ushort[] ScarletVioletNational = Enumerable.Range(1, 1025).Select(n => (ushort)n)
        .Where(n => Enumerable.Range(0, PersonalTable.SV[n].FormCount).Any(f => PersonalTable.SV.IsPresentInGame(n, (byte)f)))
        .ToArray();

    /// <summary>
    /// Pokédex de Hisui de Légendes Arceus : 242 Pokémon (Brindibou d'abord, Arceus à la fin), tout ce que le jeu peut
    /// contenir. Repris des données de PokéAPI (pokemon_dex_numbers.csv, Pokédex 30 « hisui »).
    /// </summary>
    private static readonly ushort[] Hisui =
    [
        722, 723, 724, 155, 156, 157, 501, 502, 503, 399, 400, 396, 397, 398, 403, 404, 405, 265, 266, 267, 268, 269, 77, 78, 133,
        134, 135, 136, 196, 197, 470, 471, 700, 41, 42, 169, 425, 426, 401, 402, 418, 419, 412, 413, 414, 74, 75, 76, 234, 899,
        446, 143, 46, 47, 172, 25, 26, 63, 64, 65, 390, 391, 392, 427, 428, 420, 421, 54, 55, 415, 416, 123, 900, 212, 214,
        439, 122, 190, 424, 129, 130, 422, 423, 211, 904, 440, 113, 242, 406, 315, 407, 455, 548, 549, 114, 465, 339, 340, 453, 454,
        280, 281, 282, 475, 193, 469, 449, 450, 417, 434, 435, 216, 217, 901, 704, 705, 706, 95, 208, 111, 112, 464, 438, 185, 108,
        463, 175, 176, 468, 387, 388, 389, 137, 233, 474, 92, 93, 94, 442, 198, 430, 201, 363, 364, 365, 223, 224, 451, 452, 58,
        59, 431, 432, 66, 67, 68, 441, 355, 356, 477, 393, 394, 395, 458, 226, 550, 902, 37, 38, 72, 73, 456, 457, 240, 126,
        467, 81, 82, 462, 436, 437, 239, 125, 466, 207, 472, 443, 444, 445, 299, 476, 100, 101, 479, 433, 358, 200, 429, 173, 35,
        36, 215, 903, 461, 361, 362, 478, 408, 409, 410, 411, 220, 221, 473, 712, 713, 459, 460, 570, 571, 627, 628, 447, 448, 480,
        481, 482, 485, 486, 488, 641, 642, 645, 905, 483, 484, 487, 493, 489, 490, 492, 491,
    ];

    // Dossiers et fichiers : à garder en accord avec outils/images.py (LIVINGDEX_SPRITES, LIVINGDEX_COVERS).
    public static readonly IReadOnlyList<LivingDexGameInfo> All =
    [
        new(LivingDexGame.Red, "Pokémon Rouge", "images/livingdex/jaquettes/rouge.png", "images/livingdex/rouge-bleu/", 1, "Kanto", Kanto, 151),
        new(LivingDexGame.Blue, "Pokémon Bleu", "images/livingdex/jaquettes/bleu.png", "images/livingdex/rouge-bleu/", 1, "Kanto", Kanto, 151),
        new(LivingDexGame.Yellow, "Pokémon Jaune", "images/livingdex/jaquettes/jaune.png", "images/livingdex/jaune/", 1, "Kanto", Kanto, 151),
        new(LivingDexGame.Gold, "Pokémon Or", "images/livingdex/jaquettes/or.png", "images/livingdex/or/", 2, "Johto", Johto, 251),
        new(LivingDexGame.Silver, "Pokémon Argent", "images/livingdex/jaquettes/argent.png", "images/livingdex/argent/", 2, "Johto", Johto, 251),
        new(LivingDexGame.Crystal, "Pokémon Cristal", "images/livingdex/jaquettes/cristal.png", "images/livingdex/cristal/", 2, "Johto", Johto, 251),
        // 3e génération : Rubis et Saphir ont les mêmes sprites, Rouge Feu et Vert Feuille aussi (Deoxys à part).
        new(LivingDexGame.Ruby, "Pokémon Rubis", "images/livingdex/jaquettes/rubis.png", "images/livingdex/rubis-saphir/", 3, "Hoenn", Hoenn, 386),
        new(LivingDexGame.Sapphire, "Pokémon Saphir", "images/livingdex/jaquettes/saphir.png", "images/livingdex/rubis-saphir/", 3, "Hoenn", Hoenn, 386),
        new(LivingDexGame.Emerald, "Pokémon Émeraude", "images/livingdex/jaquettes/emeraude.png", "images/livingdex/emeraude/", 3, "Hoenn", Hoenn, 386,
            DeoxysForm: 3),
        new(LivingDexGame.FireRed, "Pokémon Rouge Feu", "images/livingdex/jaquettes/rouge-feu.png", "images/livingdex/rouge-feu-vert-feuille/", 3,
            "Kanto", Kanto, 386, DeoxysForm: 1),
        new(LivingDexGame.LeafGreen, "Pokémon Vert Feuille", "images/livingdex/jaquettes/vert-feuille.png", "images/livingdex/rouge-feu-vert-feuille/", 3,
            "Kanto", Kanto, 386, DeoxysForm: 2),
        // 4e génération : Diamant et Perle ont les mêmes sprites, HeartGold et SoulSilver aussi.
        new(LivingDexGame.Diamond, "Pokémon Diamant", "images/livingdex/jaquettes/diamant.png", "images/livingdex/diamant-perle/", 4, "Sinnoh", Sinnoh, 493),
        new(LivingDexGame.Pearl, "Pokémon Perle", "images/livingdex/jaquettes/perle.png", "images/livingdex/diamant-perle/", 4, "Sinnoh", Sinnoh, 493),
        new(LivingDexGame.Platinum, "Pokémon Platine", "images/livingdex/jaquettes/platine.png", "images/livingdex/platine/", 4, "Sinnoh", SinnohPlatinum, 493),
        new(LivingDexGame.HeartGold, "Pokémon Or HeartGold", "images/livingdex/jaquettes/heartgold.png", "images/livingdex/heartgold-soulsilver/", 4,
            "Johto", JohtoHgss, 493),
        new(LivingDexGame.SoulSilver, "Pokémon Argent SoulSilver", "images/livingdex/jaquettes/soulsilver.png", "images/livingdex/heartgold-soulsilver/", 4,
            "Johto", JohtoHgss, 493),
        // 5e génération : PokéAPI n'a que les sprites de Noire / Blanche, repris par Noire 2 / Blanche 2.
        new(LivingDexGame.Black, "Pokémon Noire", "images/livingdex/jaquettes/noire.png", "images/livingdex/noire-blanche/", 5, "Unys", Unova, 649,
            RegionalStart: 0),
        new(LivingDexGame.White, "Pokémon Blanche", "images/livingdex/jaquettes/blanche.png", "images/livingdex/noire-blanche/", 5, "Unys", Unova, 649,
            RegionalStart: 0),
        new(LivingDexGame.Black2, "Pokémon Noire 2", "images/livingdex/jaquettes/noire-2.png", "images/livingdex/noire-blanche/", 5, "Unys", Unova2, 649,
            RegionalStart: 0),
        new(LivingDexGame.White2, "Pokémon Blanche 2", "images/livingdex/jaquettes/blanche-2.png", "images/livingdex/noire-blanche/", 5, "Unys", Unova2, 649,
            RegionalStart: 0),
        // 6e génération : les images de la collection (pas de sprites du jeu, SpriteFolder null).
        new(LivingDexGame.X, "Pokémon X", "images/livingdex/jaquettes/x.png", null, 6, "Kalos", Kalos, 721, Sections: KalosSections),
        new(LivingDexGame.Y, "Pokémon Y", "images/livingdex/jaquettes/y.png", null, 6, "Kalos", Kalos, 721, Sections: KalosSections),
        new(LivingDexGame.OmegaRuby, "Pokémon Rubis Oméga", "images/livingdex/jaquettes/rubis-omega.png", null, 6, "Hoenn", HoennOras, 721),
        new(LivingDexGame.AlphaSapphire, "Pokémon Saphir Alpha", "images/livingdex/jaquettes/saphir-alpha.png", null, 6, "Hoenn", HoennOras, 721),
        // 7e génération : les images de la collection aussi.
        new(LivingDexGame.Sun, "Pokémon Soleil", "images/livingdex/jaquettes/soleil.png", null, 7, "Alola", Alola, 802),
        new(LivingDexGame.Moon, "Pokémon Lune", "images/livingdex/jaquettes/lune.png", null, 7, "Alola", Alola, 802),
        new(LivingDexGame.UltraSun, "Pokémon Ultra-Soleil", "images/livingdex/jaquettes/ultra-soleil.png", null, 7, "Alola", AlolaUltra, 807),
        new(LivingDexGame.UltraMoon, "Pokémon Ultra-Lune", "images/livingdex/jaquettes/ultra-lune.png", null, 7, "Alola", AlolaUltra, 807),
        new(LivingDexGame.LetsGoPikachu, "Pokémon Let's Go, Pikachu", "images/livingdex/jaquettes/lets-go-pikachu.png", null, 7, "Kanto",
            LetsGoKanto, 809, National: LetsGoKanto),
        new(LivingDexGame.LetsGoEevee, "Pokémon Let's Go, Évoli", "images/livingdex/jaquettes/lets-go-evoli.png", null, 7, "Kanto",
            LetsGoKanto, 809, National: LetsGoKanto),
        // 8e génération : les images de la collection aussi.
        new(LivingDexGame.Sword, "Pokémon Épée", "images/livingdex/jaquettes/epee.png", null, 8, "Galar", Galar, 898,
            Sections: GalarSections, National: SwordShieldNational, RegionalNumbers: GalarNumbers),
        new(LivingDexGame.Shield, "Pokémon Bouclier", "images/livingdex/jaquettes/bouclier.png", null, 8, "Galar", Galar, 898,
            Sections: GalarSections, National: SwordShieldNational, RegionalNumbers: GalarNumbers),
        new(LivingDexGame.BrilliantDiamond, "Pokémon Diamant Étincelant", "images/livingdex/jaquettes/diamant-etincelant.png", null, 8,
            "Sinnoh", Sinnoh, 493),
        new(LivingDexGame.ShiningPearl, "Pokémon Perle Scintillante", "images/livingdex/jaquettes/perle-scintillante.png", null, 8,
            "Sinnoh", Sinnoh, 493),
        new(LivingDexGame.LegendsArceus, "Légendes Pokémon : Arceus", "images/livingdex/jaquettes/legendes-arceus.png", null, 8,
            "Hisui", Hisui, 905, National: [.. Hisui.Order()]),
        // 9e génération : les images de la collection aussi.
        new(LivingDexGame.Scarlet, "Pokémon Écarlate", "images/livingdex/jaquettes/ecarlate.png", null, 9, "Paldea", Paldea, 1025,
            Sections: PaldeaSections, National: ScarletVioletNational, RegionalNumbers: PaldeaNumbers),
        new(LivingDexGame.Violet, "Pokémon Violet", "images/livingdex/jaquettes/violet.png", null, 9, "Paldea", Paldea, 1025,
            Sections: PaldeaSections, National: ScarletVioletNational, RegionalNumbers: PaldeaNumbers),
    ];

    /// <summary>Let's Go, Pikachu ou Évoli : sauvegarde de Switch, Pokémon sans objet tenu, Méga par la gemme du sac.</summary>
    public static bool IsLetsGo(LivingDexGameInfo game) => game.Game is LivingDexGame.LetsGoPikachu or LivingDexGame.LetsGoEevee;

    public static LivingDexGameInfo Get(LivingDexGame game) => All.Single(g => g.Game == game);

    /// <summary>
    /// Jeux possibles pour une sauvegarde : PKHeX reconnaît Jaune, Cristal, Émeraude et Platine, mais pas Rouge de Bleu, Or
    /// d'Argent, Rubis de Saphir, Rouge Feu de Vert Feuille, Diamant de Perle ni HeartGold de SoulSilver (même format) : au
    /// joueur de choisir. Pour Rouge Feu / Vert Feuille,
    /// PKHeX répond « Rouge Feu » faute de mieux : les deux sont toujours proposés. À partir de la 5e génération, chaque
    /// sauvegarde dit son jeu. Aucun pour une sauvegarde d'une autre génération, ni pour Colosseum / XD.
    /// </summary>
    public static IReadOnlyList<LivingDexGame> For(SaveFile sav) => sav.Generation switch
    {
        1 => sav.Version switch
        {
            GameVersion.YW => [LivingDexGame.Yellow],
            GameVersion.RD => [LivingDexGame.Red],
            GameVersion.BU or GameVersion.GN => [LivingDexGame.Blue], // Bleu japonais ou Vert : les sprites de Rouge / Bleu
            _ => [LivingDexGame.Red, LivingDexGame.Blue], // RB (ou version inconnue)
        },
        2 => sav.Version switch
        {
            GameVersion.C => [LivingDexGame.Crystal],
            GameVersion.GD => [LivingDexGame.Gold],
            GameVersion.SI => [LivingDexGame.Silver],
            GameVersion.GS => [LivingDexGame.Gold, LivingDexGame.Silver],
            _ => [LivingDexGame.Gold, LivingDexGame.Silver, LivingDexGame.Crystal], // version inconnue
        },
        3 => sav.Version switch
        {
            GameVersion.E => [LivingDexGame.Emerald],
            GameVersion.R => [LivingDexGame.Ruby],
            GameVersion.S => [LivingDexGame.Sapphire],
            GameVersion.RS => [LivingDexGame.Ruby, LivingDexGame.Sapphire],
            GameVersion.FR or GameVersion.LG or GameVersion.FRLG => [LivingDexGame.FireRed, LivingDexGame.LeafGreen],
            _ => [], // Colosseum, XD
        },
        4 => sav.Version switch
        {
            GameVersion.Pt => [LivingDexGame.Platinum],
            GameVersion.D => [LivingDexGame.Diamond],
            GameVersion.P => [LivingDexGame.Pearl],
            GameVersion.DP => [LivingDexGame.Diamond, LivingDexGame.Pearl],
            GameVersion.HG => [LivingDexGame.HeartGold],
            GameVersion.SS => [LivingDexGame.SoulSilver],
            GameVersion.HGSS => [LivingDexGame.HeartGold, LivingDexGame.SoulSilver],
            _ => [], // Battle Revolution
        },
        // Les vraies sauvegardes disent leur jeu ; une sauvegarde vide de PKHeX répond « Any » : les deux jeux de son format.
        5 => sav.Version switch
        {
            GameVersion.B => [LivingDexGame.Black],
            GameVersion.W => [LivingDexGame.White],
            GameVersion.B2 => [LivingDexGame.Black2],
            GameVersion.W2 => [LivingDexGame.White2],
            _ when sav is SAV5B2W2 => [LivingDexGame.Black2, LivingDexGame.White2],
            _ => [LivingDexGame.Black, LivingDexGame.White],
        },
        6 => sav.Version switch
        {
            GameVersion.X => [LivingDexGame.X],
            GameVersion.Y => [LivingDexGame.Y],
            GameVersion.OR => [LivingDexGame.OmegaRuby],
            GameVersion.AS => [LivingDexGame.AlphaSapphire],
            _ when sav is SAV6AO => [LivingDexGame.OmegaRuby, LivingDexGame.AlphaSapphire],
            _ => [LivingDexGame.X, LivingDexGame.Y],
        },
        7 => sav.Version switch
        {
            GameVersion.SN => [LivingDexGame.Sun],
            GameVersion.MN => [LivingDexGame.Moon],
            GameVersion.US => [LivingDexGame.UltraSun],
            GameVersion.UM => [LivingDexGame.UltraMoon],
            GameVersion.GP => [LivingDexGame.LetsGoPikachu],
            GameVersion.GE => [LivingDexGame.LetsGoEevee],
            _ when sav is SAV7b => [LivingDexGame.LetsGoPikachu, LivingDexGame.LetsGoEevee],
            _ when sav is SAV7USUM => [LivingDexGame.UltraSun, LivingDexGame.UltraMoon],
            _ => [LivingDexGame.Sun, LivingDexGame.Moon],
        },
        8 => sav.Version switch
        {
            GameVersion.SW => [LivingDexGame.Sword],
            GameVersion.SH => [LivingDexGame.Shield],
            GameVersion.BD => [LivingDexGame.BrilliantDiamond],
            GameVersion.SP => [LivingDexGame.ShiningPearl],
            _ when sav is SAV8LA => [LivingDexGame.LegendsArceus],
            _ when sav is SAV8BS => [LivingDexGame.BrilliantDiamond, LivingDexGame.ShiningPearl],
            _ => [LivingDexGame.Sword, LivingDexGame.Shield],
        },
        // Légendes Z-A (9e génération aussi chez PKHeX) n'a pas encore de living dex.
        9 when sav is SAV9SV => sav.Version switch
        {
            GameVersion.SL => [LivingDexGame.Scarlet],
            GameVersion.VL => [LivingDexGame.Violet],
            _ => [LivingDexGame.Scarlet, LivingDexGame.Violet],
        },
        _ => [],
    };

    /// <summary>
    /// Pokémon demandés par un mode : en Régional et National, un par espèce ; en Complet, chaque forme qu'une sauvegarde
    /// du jeu peut contenir (voir <see cref="LivingDexMode.Complete"/>), dans l'ordre du national puis des formes.
    /// </summary>
    public static IReadOnlyList<LivingDexSlot> Slots(LivingDexGameInfo game, LivingDexMode mode)
    {
        var national = game.National ?? Enumerable.Range(1, game.NationalMax).Select(n => (ushort)n);
        return mode switch
        {
            LivingDexMode.Regional => game.Regional.Select(n => Base(game, n)).ToList(),
            LivingDexMode.National => national.Select(n => Base(game, n)).ToList(),
            _ => national.SelectMany(n => Forms(game, n)).ToList(),
        };
    }

    /// <summary>
    /// Case d'une espèce : sa forme de base, sauf Deoxys en 3e génération (la forme de son jeu) et les espèces qui
    /// n'existent qu'en forme régionale dans le jeu (Caninos de Hisui dans Légendes Arceus).
    /// </summary>
    private static LivingDexSlot Base(LivingDexGameInfo game, ushort species) =>
        species == Deoxys && game.Generation == 3 ? new(species, game.DeoxysForm, AnyForm: true) : new(species, FirstForm(game, species));

    /// <summary>Première forme de l'espèce présente dans le jeu (0, sauf Caninos, Typhlosion... de Hisui dans Légendes Arceus).</summary>
    private static byte FirstForm(LivingDexGameInfo game, ushort species) =>
        (byte)Enumerable.Range(0, FormCount(game, species)).FirstOrDefault(f => Present(game, species, (byte)f));

    /// <summary>
    /// Formes d'une espèce en mode Complet, réduites à celles qu'une sauvegarde du jeu peut contenir : les lettres de Zarbi
    /// (26 en 2e génération, A à Z ; 28 ensuite, avec « ! » et « ? »), enregistrées dans chaque Zarbi. Rien d'autre jusqu'à
    /// la 3e génération : ni forme régionale, ni Méga-Évolution, ni Gigamax ; les formes de Morphéo n'existent qu'en combat,
    /// Deoxys a celle de son jeu (<see cref="Base"/>). À partir de la 4e, celles de <see cref="AspectForms"/> que le jeu
    /// connaît, et Shaymin Céleste à part (<see cref="LivingDexSlot.KeyItem"/>) ; Arceus et Genesect n'ont qu'une case,
    /// quelle que soit leur Plaque ou leur Module. En 6e : Hoopa Déchaîné comme Shaymin Céleste (Vase Scellé), une case
    /// Pikachu Cosplayeur pour tous ses costumes, une case par Méga-Évolution et Primo-Résurgence (<see cref="Megas"/>) ;
    /// Prismillon (et ses pré-évolutions) et Couafarel n'ont qu'une case, quel que soit leur motif ou leur coupe. En 7e :
    /// les formes d'Alola, Shaymin Céleste et Hoopa Déchaîné en case ordinaire (ils tiennent en boîte), Zygarde 10 % et 50 %,
    /// les couleurs de noyau de Météno ; Silvallié n'a qu'une case, comme Arceus ; ni Pikachu à casquette (distributions
    /// terminées), ni Pokémon Dominant (seule la taille change). En Let's Go, la Méga par la gemme du sac. En 8e :
    /// seulement les formes que PKHeX dit présentes dans le jeu (<see cref="Present"/>), les formes régionales (Alola, Galar,
    /// Hisui), Salarsen, Shifours, Sylveroy fusionné, Dialga et Palkia Originels, Amovénus Totémique, les 9 crèmes de
    /// Charmilly (pas ses décorations), et les cases Gigamax (<see cref="Gigamax"/>) ; ni Zarude Papa ni Magearna Couleur du
    /// Passé (distributions terminées), ni Papilord, Théffroi ou Polthégeist Authentique (même aspect), ni les formes Roi
    /// de Légendes Arceus (impossibles à capturer). En 9e : les formes de Paldea (les 3 races de Tauros, Axoloto), Ursaking
    /// Lune Vermeille, Famignol Famille de Quatre, les plumages de Tapatoès, les formes de Nigirigon, Deusolourdo Triple,
    /// Mordudor Marche, les masques d'Ogerpon ; une seule case pour Koraidon et Miraidon (leurs autres formes sont celles
    /// de la monture, jamais en boîte).
    /// </summary>
    private static IEnumerable<LivingDexSlot> Forms(LivingDexGameInfo game, ushort species) => species switch
    {
        Unown when UnownForms(game) > 1 => Enumerable.Range(0, UnownForms(game)).Select(f => new LivingDexSlot(species, (byte)f)),
        (Shaymin or Hoopa) when game.Generation is >= 4 and <= 6 && FormCount(game, species) > 1 =>
            [new(species, 0, AnyForm: true), new(species, 1, AnyForm: true, KeyItem: species == Shaymin ? Gracidea : PrisonBottle)],
        Arceus or Genesect or Silvally when game.Generation >= 4 => [new(species, 0, AnyForm: true)],
        // Zygarde 50 % et 10 % (avec ou sans Rassemblement, voir SlotForm) ; Parfait n'existe qu'en combat.
        Zygarde when game.Generation >= 7 => [new(species, 0), new(species, 1)],
        _ when game.Generation == 7 && AlolanForms.Contains(species) => [new(species, 0), new(species, 1)],
        _ when game.Generation >= 8 && RegionalForms(game, species) is { Count: > 0 } regional =>
            [.. Present(game, species, 0) ? [new LivingDexSlot(species, 0)] : Array.Empty<LivingDexSlot>(),
             .. regional.Select(f => new LivingDexSlot(species, f)), .. Gigamax(game, species)],
        // Pikachu Cosplayeur (Rubis Oméga / Saphir Alpha) : un seul par partie, qui change de costume (formes 1 à 6).
        Pikachu when game.Generation == 6 && FormCount(game, species) > 1 =>
            [new(species, 0), new(species, (byte)(FormCount(game, species) - 1), FormsFrom: 1)],
        // Floette Éternelle (forme 5) : jamais distribuée.
        Floette when game.Generation >= 6 => Enumerable.Range(0, 5).Select(f => new LivingDexSlot(species, (byte)f)),
        // 5e génération : seuls ceux de l'équipe prennent la saison du jeu (au chargement) ; en boîte, ils gardent la leur.
        Deerling or Sawsbuck when game.Generation == 5 =>
            Enumerable.Range(0, FormCount(game, species)).Select(f => new LivingDexSlot(species, (byte)f,
                Hint: "à garder en boîte (dans l'équipe, il prend la saison du jeu)")),
        _ when game.Generation >= 4 && AspectForms.Contains(species) =>
            [.. Enumerable.Range(0, FormCount(game, species)).Select(f => (byte)f)
                .Where(f => Present(game, species, f) && !FormInfo.IsBattleOnlyForm(species, f, game.Generation))
                .Select(f => new LivingDexSlot(species, f)), .. Gigamax(game, species)],
        // 6e génération : n'importe quelle forme remplit la case de l'espèce (motifs de Prismillon, coupes de Couafarel,
        // Mistigrix femelle...) ; et ses Méga-Évolutions, ses Gigamax.
        _ when game.Generation >= 6 => [new(species, FirstForm(game, species), AnyForm: true), .. Megas(game, species), .. Gigamax(game, species)],
        _ => [Base(game, species)],
    };

    /// <summary>
    /// Cases Méga-Évolution et Primo-Résurgence du jeu (formes de combat, que PKHeX connaît pour ce jeu) : chacune remplie
    /// par un exemplaire de plus qui tient sa gemme (<see cref="LivingDexSlot.MegaItem"/>), ou, pour Méga-Rayquaza qui n'en a
    /// pas, qui connaît Draco-Ascension. En 7e, seulement les gemmes qu'un Pokémon du jeu peut tenir (liste de PKHeX) ; en
    /// Let's Go, la gemme est dans le sac (<see cref="LivingDexSlot.MegaInBag"/>).
    /// </summary>
    private static IEnumerable<LivingDexSlot> Megas(LivingDexGameInfo game, ushort species) =>
        Enumerable.Range(1, Math.Max(0, FormCount(game, species) - 1)).Select(f => (byte)f)
            .Where(f => (FormInfo.IsMegaForm(species, f) || FormInfo.IsPrimalForm(species, f)) && Present(game, species, f))
            .Select(f => (Form: f, Item: (ushort)ItemStorage9ZA.GetExpectedMegaStoneOrPrimalOrb(species, f)))
            .Where(m => m.Item == 0 || CanHold(game, m.Item))
            .Select(m => m.Item > 0
                ? new LivingDexSlot(species, m.Form, AnyForm: true, MegaItem: m.Item, MegaInBag: IsLetsGo(game))
                : new LivingDexSlot(species, m.Form, AnyForm: true, MegaMove: DragonAscent));

    /// <summary>Gemme qu'un Pokémon du jeu peut tenir (7e génération : listes de PKHeX) ; Let's Go : toutes, elles restent au sac.</summary>
    private static bool CanHold(LivingDexGameInfo game, ushort item) => game.Game switch
    {
        LivingDexGame.Sun or LivingDexGame.Moon => HeldSunMoon.Contains(item),
        LivingDexGame.UltraSun or LivingDexGame.UltraMoon => HeldUltra.Contains(item),
        _ => true,
    };

    private static readonly HashSet<ushort> HeldSunMoon = [.. ItemStorage7SM.GetAllHeld()];
    private static readonly HashSet<ushort> HeldUltra = [.. ItemStorage7USUM.GetAllHeld()];

    /// <summary>
    /// Cases Gigamax d'Épée / Bouclier (<see cref="LivingDexSlot.Gigamax"/>) : une par forme qui peut se gigantamaxer
    /// (PKHeX), sauf Charmilly et Salarsen, qui n'ont qu'un aspect Gigamax quelle que soit leur forme (une case, n'importe
    /// laquelle) ; Shifours en a deux (Poing Final, Mille Poings).
    /// </summary>
    private static IEnumerable<LivingDexSlot> Gigamax(LivingDexGameInfo game, ushort species)
    {
        if (game.Game is not (LivingDexGame.Sword or LivingDexGame.Shield))
            return [];
        var forms = Enumerable.Range(0, FormCount(game, species)).Select(f => (byte)f)
            .Where(f => Present(game, species, f) && Gigantamax.CanToggle(species, f)).ToList();
        return species is Alcremie or Toxtricity
            ? forms.Take(1).Select(f => new LivingDexSlot(species, f, AnyForm: true, Gigamax: true))
            : forms.Select(f => new LivingDexSlot(species, f, Gigamax: true));
    }

    /// <summary>Espèces qui ont une case Gigamax : leurs octets disent si un exemplaire a le facteur Gigamax.</summary>
    public static readonly ushort[] GigamaxSpecies = SwordShieldNational
        .Where(s => Enumerable.Range(0, PersonalTable.SWSH[s].FormCount).Any(f => Gigantamax.CanToggle(s, (byte)f))).ToArray();

    /// <summary>
    /// Formes régionales de l'espèce présentes dans le jeu (à partir de la 8e génération : Alola, Galar, Hisui, Paldea et
    /// les races de Tauros, d'après leur nom anglais
    /// chez PKHeX), sans les formes de combat (Darumacho de Galar en Transe). Pas Pikachu : sa casquette d'Alola s'appelle
    /// aussi « Alola ».
    /// </summary>
    private static List<byte> RegionalForms(LivingDexGameInfo game, ushort species)
    {
        if (species == Pikachu)
            return [];
        var names = FormConverter.GetFormList(species, EnglishStrings.types, EnglishStrings.forms, GameInfo.GenderSymbolASCII, Context(game));
        return Enumerable.Range(1, Math.Max(0, FormCount(game, species) - 1)).Select(f => (byte)f)
            .Where(f => f < names.Length && (names[f] is "Alola" or "Galar" or "Hisui" || names[f].StartsWith("Paldea"))
                        && Present(game, species, f) && !FormInfo.IsBattleOnlyForm(species, f, game.Generation))
            .ToList();
    }

    private static readonly GameStrings EnglishStrings = GameInfo.GetStrings("en");

    /// <summary>
    /// Numéro régional de la case <paramref name="index"/> et son sous-Pokédex (Kalos : chacun recommence à 1), null sans
    /// sous-Pokédex.
    /// </summary>
    public static (string? Section, int Number) RegionalNumber(LivingDexGameInfo game, int index)
    {
        var number = game.RegionalNumbers is { } numbers && index < numbers.Count ? numbers[index] : (int?)null;
        if (game.Sections is null)
            return (null, number ?? index + game.RegionalStart);
        foreach (var (name, count) in game.Sections)
        {
            if (index < count)
                return (name, number ?? index + game.RegionalStart);
            index -= count;
        }
        return (null, number ?? index + game.RegionalStart);
    }

    private static int UnownForms(LivingDexGameInfo game) => game.Generation switch { 2 => 26, >= 3 => 28, _ => 1 };

    /// <summary>
    /// Espèces dont la forme change l'aspect, hors combat, à partir de la 4e génération : Pichu Troizépi (HeartGold /
    /// SoulSilver, il ne va pas dans les autres jeux), Deoxys (chaque Deoxys garde sa forme), capes de Cheniti et
    /// Cheniselle, mers de Sancoki et Tritosor, Motisma, Giratina Originel (tient l'Orbe Platiné) ; ces deux derniers
    /// n'existent qu'à partir de Platine. En 5e, Bargantua (motifs), et dans Noire 2 / Blanche 2 seulement les formes
    /// Totémiques de Boréas, Fulguris et Démétéros, Kyurem Blanc et Noir, Keldeo Décidé. Le nombre de formes de chaque jeu
    /// vient de PKHeX (<see cref="FormCount"/>). En 6e, les fleurs de Flabébé et Florges (Floette à part, sans
    /// l'Éternelle) et les tailles de Pitrouille et Banshitrouye. En 7e, Shaymin et Hoopa (avant, à part : ils ne tenaient
    /// pas en boîte), les styles de Plumeline, les formes de Lougaroc (Crépusculaire en Ultra-Soleil / Ultra-Lune), les
    /// noyaux de Météno, Necrozma Crinière du Couchant et Ailes de l'Aurore. Pas les formes de combat (Météno Météore,
    /// Ultra-Necrozma, Morphéo, Ceriflor, Darumacho, Meloetta, Exagide, Xerneas, Froussardine, Mimiqui...) ; ni
    /// Mistigrix, dont la forme est le sexe. En 8e, Salarsen Grave, Shifours Mille Poings, Sylveroy du Froid et d'Effroi,
    /// les crèmes de Charmilly, Dialga et Palkia Originels et Amovénus Totémique (Légendes Arceus), Bargantua Blanc
    /// (Légendes Arceus, qui n'a que lui). En 9e, Ursaking Lune Vermeille, Famignol, Tapatoès, Nigirigon, Deusolourdo,
    /// Mordudor, Ogerpon (masques ; pas ses formes Téracristal, de combat).
    /// </summary>
    private static readonly ushort[] AspectForms =
        [Pichu, Deoxys, Burmy, Wormadam, Shellos, Gastrodon, Rotom, Giratina, Shaymin, Basculin, Tornadus, Thundurus, Landorus,
         Kyurem, Keldeo, Deerling, Sawsbuck, Flabebe, Florges, Pumpkaboo, Gourgeist, Hoopa, Oricorio, Lycanroc, Minior, Necrozma,
         Dialga, Palkia, Toxtricity, Alcremie, Urshifu, Calyrex, Enamorus, Ursaluna, Maushold, Squawkabilly, Tatsugiri, Dudunsparce,
         Gimmighoul, Ogerpon];

    /// <summary>Formes de l'espèce que le jeu connaît (données de PKHeX : Motisma n'en a qu'une en Diamant / Perle).</summary>
    private static int FormCount(LivingDexGameInfo game, ushort species) => Table(game)[species].FormCount;

    /// <summary>
    /// La forme existe dans le jeu : à partir de la 8e génération, PKHeX le dit (Épée / Bouclier n'ont pas tous les
    /// Pokémon, Légendes Arceus n'a que des Caninos de Hisui) ; avant, toutes celles qu'il connaît (<see cref="FormCount"/>).
    /// </summary>
    private static bool Present(LivingDexGameInfo game, ushort species, byte form) =>
        game.Generation < 8 || Table(game).IsPresentInGame(species, form);

    private static IPersonalTable Table(LivingDexGameInfo game) => game.Game switch
    {
        LivingDexGame.Diamond or LivingDexGame.Pearl => PersonalTable.DP,
        LivingDexGame.Platinum => PersonalTable.Pt,
        LivingDexGame.HeartGold or LivingDexGame.SoulSilver => PersonalTable.HGSS,
        LivingDexGame.Black or LivingDexGame.White => PersonalTable.BW,
        LivingDexGame.Black2 or LivingDexGame.White2 => PersonalTable.B2W2,
        LivingDexGame.X or LivingDexGame.Y => PersonalTable.XY,
        LivingDexGame.OmegaRuby or LivingDexGame.AlphaSapphire => PersonalTable.AO,
        LivingDexGame.Sun or LivingDexGame.Moon => PersonalTable.SM,
        LivingDexGame.UltraSun or LivingDexGame.UltraMoon => PersonalTable.USUM,
        LivingDexGame.LetsGoPikachu or LivingDexGame.LetsGoEevee => PersonalTable.GG,
        LivingDexGame.Sword or LivingDexGame.Shield => PersonalTable.SWSH,
        LivingDexGame.BrilliantDiamond or LivingDexGame.ShiningPearl => PersonalTable.BDSP,
        LivingDexGame.LegendsArceus => PersonalTable.LA,
        _ => PersonalTable.SV,
    };

    /// <summary>
    /// La case est remplie par ce Pokémon : même espèce ; en Complet, même forme (sauf <see cref="LivingDexSlot.AnyForm"/>,
    /// ou l'une des formes de <see cref="LivingDexSlot.FormsFrom"/> à <see cref="LivingDexSlot.Form"/>). Les cases Méga
    /// demandent en plus la gemme (vérifiée par le service).
    /// </summary>
    public static bool Fills(LivingDexSlot slot, ushort species, byte form, LivingDexMode mode)
    {
        if (species != slot.Species)
            return false;
        form = SlotForm(species, form);
        return mode != LivingDexMode.Complete || slot.AnyForm || form == slot.Form
               || slot.FormsFrom != 0 && form >= slot.FormsFrom && form <= slot.Form;
    }

    /// <summary>
    /// Case d'une forme qui n'a pas la sienne : un Pokémon Dominant remplit celle de sa forme ordinaire (Rattatac Dominant :
    /// Rattatac d'Alola), Zygarde avec Rassemblement celle de son pourcentage (10 %-C : 10 %, 50 %-C : 50 %).
    /// </summary>
    private static byte SlotForm(ushort species, byte form) => species switch
    {
        Zygarde when form == 2 => 1,
        Zygarde when form == 3 => 0,
        _ when FormInfo.IsTotemForm(species, form) => FormInfo.GetTotemBaseForm(species, form),
        _ => form,
    };

    /// <summary>
    /// Sprite d'une case. Jusqu'à la 5e génération, celui du jeu : « 201.png » (Zarbi A), « 201-b.png », « 201-question.png »,
    /// « 386-speed.png » (Deoxys Vitesse), « 492-sky.png » (Shaymin Céleste) : noms de PokéAPI, à garder en accord avec
    /// outils/images.py (LIVINGDEX_FORMS). Ensuite, l'image de la collection (<see cref="Fallback"/>).
    /// </summary>
    public static string Sprite(LivingDexGameInfo game, LivingDexSlot slot) => game.SpriteFolder is null
        ? Fallback(game, slot)
        : $"{game.SpriteFolder}{slot.Species}{FormSuffix(slot)}.png";

    private static string FormSuffix(LivingDexSlot slot) => (slot.Species, slot.Form) switch
    {
        (_, 0) => "",
        (Unown, 26) => "-exclamation",
        (Unown, 27) => "-question",
        (Unown, var letter) => $"-{(char)('a' + letter)}",
        (Deoxys, 1) => "-attack",
        (Deoxys, 2) => "-defense",
        (Deoxys, 3) => "-speed",
        (Pichu, 1) => "-spiky-eared",
        (Burmy or Wormadam, 1) => "-sandy",
        (Burmy or Wormadam, 2) => "-trash",
        (Shellos or Gastrodon, 1) => "-east",
        (Rotom, var f) when f <= RotomForms.Length => $"-{RotomForms[f - 1]}",
        (Giratina, 1) => "-origin",
        (Shaymin, 1) => "-sky",
        (Basculin, 1) => "-blue-striped",
        (Deerling or Sawsbuck, var f) when f <= Seasons.Length => $"-{Seasons[f - 1]}",
        (Tornadus or Thundurus or Landorus, 1) => "-therian",
        (Kyurem, 1) => "-white",
        (Kyurem, 2) => "-black",
        (Keldeo, 1) => "-resolute",
        _ => $"-{slot.Form}",
    };

    /// <summary>Vivaldaim et Haydaim Été, Automne, Hiver (formes 1 à 3 ; Printemps : 0).</summary>
    private static readonly string[] Seasons = ["summer", "autumn", "winter"];

    /// <summary>Motisma Chaleur, Lavage, Froid, Hélice, Tonte (formes 1 à 5).</summary>
    private static readonly string[] RotomForms = ["heat", "wash", "frost", "fan", "mow"];

    /// <summary>
    /// Nom d'une case : celui de l'espèce, avec la lettre de Zarbi en Complet (« Zarbi B ») et la forme quand ce n'est pas
    /// celle de base (« Deoxys (forme Vitesse) », « Motisma (Chaleur) ») ; en Complet, la cape ou la mer de chaque Cheniti,
    /// Cheniselle, Sancoki et Tritosor (« Sancoki (mer Occident) »), le motif de Bargantua et la saison de Vivaldaim et
    /// Haydaim, le style de Plumeline, la forme de Lougaroc et de Zygarde, le noyau de Météno, la crème de Charmilly ;
    /// « Kyurem Blanc », « Rattata d'Alola », « Caninos de Hisui », « Dracaufeu Gigamax ». Noms de formes de PKHeX, en
    /// français.
    /// </summary>
    public static string Name(LivingDexGameInfo game, LivingDexSlot slot, LivingDexMode mode)
    {
        var species = FrenchStrings.specieslist[slot.Species];
        var forms = FormConverter.GetFormList(slot.Species, FrenchStrings.types, FrenchStrings.forms, GameInfo.GenderSymbolUnicode, Context(game));
        var form = slot.Form < forms.Length ? forms[slot.Form] : $"{slot.Form}";
        if (slot.MegaItem != 0 || slot.MegaMove != 0)
            return FormInfo.IsPrimalForm(slot.Species, slot.Form) ? $"Primo-{species}"
                : form.EndsWith(" X") || form.EndsWith(" Y") ? $"Méga-{species} {form[^1]}" : $"Méga-{species}";
        if (slot.Gigamax)
            return slot.Species == Urshifu ? $"{species} Gigamax ({FormLabel(slot.Species, form)})" : $"{species} Gigamax";
        return slot.Species switch
        {
            Unown when mode == LivingDexMode.Complete && UnownForms(game) > 1 => $"{species} {form}",
            Burmy or Wormadam or Shellos or Gastrodon or Basculin or Deerling or Sawsbuck or Flabebe or Floette or Florges or Pumpkaboo
                or Gourgeist or Oricorio or Lycanroc or Minior or Toxtricity or Urshifu or Maushold or Squawkabilly or Tatsugiri
                or Dudunsparce or Gimmighoul or Ogerpon
                when mode == LivingDexMode.Complete => $"{species} ({FormLabel(slot.Species, form)})",
            Alcremie when mode == LivingDexMode.Complete => $"{species} ({AlcremieCreams[Math.Min(slot.Form, (byte)(AlcremieCreams.Length - 1))]})",
            Zygarde when mode == LivingDexMode.Complete && game.Generation >= 7 => $"{species} ({FormLabel(slot.Species, form)})",
            Kyurem or Hoopa or Necrozma or Calyrex when slot.Form != 0 => $"{species} {form}",
            _ when slot.Form != 0 && form is "Alola" or "Galar" or "Hisui" or "Paldea" => $"{species} {(form == "Alola" ? "d'" : "de ")}{form}",
            _ when slot.Form != 0 && form.StartsWith("Paldea ") => $"{species} de Paldea (race {form[7..]})", // Tauros
            Pikachu when slot.FormsFrom != 0 => "Pikachu Cosplayeur",
            _ when slot.Form != 0 => $"{species} ({FormLabel(slot.Species, form)})",
            _ => species,
        };
    }

    /// <summary>Crèmes de Charmilly (formes 0 à 8 ; PKHeX abrège : deux « Ruby »).</summary>
    private static readonly string[] AlcremieCreams =
        ["Lait Vanille", "Lait Ruby", "Lait Matcha", "Lait Menthe", "Lait Citron", "Lait Salé", "Mélange Ruby", "Mélange Caramel",
         "Mélange Tricolore"];

    private static string FormLabel(ushort species, string form) => species switch
    {
        Deoxys or Giratina or Shaymin or Deerling or Sawsbuck or Tornadus or Thundurus or Landorus => $"forme {form}",
        Burmy or Wormadam => $"cape {form}",
        Shellos or Gastrodon => $"mer {form}",
        Basculin => $"motif {(form == "Blanche" ? "Blanc" : form)}", // PKHeX : « Blanche » (Légendes Arceus)
        Flabebe or Floette or Florges => $"fleur {form}",
        Pumpkaboo or Gourgeist => $"taille {form}",
        Oricorio => $"style {form}",
        Lycanroc => form.StartsWith("Forme ") ? $"forme {form[6..]}" : $"forme {form}", // PKHeX : « Forme Crépusculaire »
        Zygarde or Toxtricity or Dialga or Palkia or Enamorus => $"forme {form}",
        Urshifu => $"style {form}",
        Tatsugiri or Dudunsparce or Gimmighoul => $"forme {form}",
        Squawkabilly => $"plumage {form switch { "Bleue" => "Bleu", "Blanche" => "Blanc", _ => form }}", // PKHeX : « Bleue », « Blanche »
        Ogerpon => $"masque {form switch { "Puits" => "du Puits", "Fourneau" => "du Fourneau", "Pierre" => "de la Pierre", _ => form }}",
        Minior => $"noyau {form.Replace("N-", "")}", // PKHeX : « N-Rouge »
        Pichu => "Troizépi", // PKHeX n'a que l'anglais (« Spiky »)
        Keldeo => "forme Décidée", // PKHeX : « Décidé »
        _ => form,
    };

    /// <summary>
    /// Image de la même forme dans la collection : celle des cases à partir de la 6e génération, et le remplacement d'un
    /// sprite du jeu absent avant (images pas encore récupérées). Méga par la gemme, comme un Pokémon qui la tient.
    /// </summary>
    public static string Fallback(LivingDexGameInfo game, LivingDexSlot slot) => slot switch
    {
        { MegaItem: not 0 } => PokemonSprites.Url(slot.Species, 0, Context(game), female: false, heldItem: slot.MegaItem, shiny: false), // Méga par la gemme
        { Gigamax: true } => PokemonSprites.GigamaxUrl(slot.Species, slot.Form, Context(game)),
        _ => PokemonSprites.Url(slot.Species, slot.Form, Context(game), female: false, heldItem: 0, shiny: false),
    };

    private static EntityContext Context(LivingDexGameInfo game) => game.Generation switch
    {
        1 => EntityContext.Gen1,
        2 => EntityContext.Gen2,
        3 => EntityContext.Gen3,
        4 => EntityContext.Gen4,
        5 => EntityContext.Gen5,
        6 => EntityContext.Gen6,
        7 => IsLetsGo(game) ? EntityContext.Gen7b : EntityContext.Gen7,
        _ => game.Game switch
        {
            LivingDexGame.BrilliantDiamond or LivingDexGame.ShiningPearl => EntityContext.Gen8b,
            LivingDexGame.LegendsArceus => EntityContext.Gen8a,
            LivingDexGame.Scarlet or LivingDexGame.Violet => EntityContext.Gen9,
            _ => EntityContext.Gen8,
        },
    };

    private static readonly GameStrings FrenchStrings = GameInfo.GetStrings("fr");

    /// <summary>
    /// Modes proposés, dans l'ordre Régional, National, Complet : un mode qui demande exactement les mêmes Pokémon
    /// qu'un autre, dans le même ordre, est caché. En 1re génération, il ne reste que le National ; en 2e, le Pokédex de
    /// Johto a les mêmes 251 Pokémon que le national mais dans un autre ordre, et Zarbi ses 26 lettres : les trois modes ;
    /// à partir de la 3e, le Pokédex régional, le national et les formes : les trois. Let's Go : son Pokédex est tout ce
    /// qu'il peut contenir, le National et le Complet (formes d'Alola, Méga). Légendes Arceus : comme Johto, le Pokédex de
    /// Hisui a les mêmes Pokémon que son national, dans un autre ordre : les trois.
    /// </summary>
    public static IReadOnlyList<LivingDexMode> Modes(LivingDexGameInfo game)
    {
        var national = Slots(game, LivingDexMode.National);
        var modes = new List<LivingDexMode>();
        if (!Slots(game, LivingDexMode.Regional).SequenceEqual(national))
            modes.Add(LivingDexMode.Regional);
        modes.Add(LivingDexMode.National);
        if (!Slots(game, LivingDexMode.Complete).SequenceEqual(national))
            modes.Add(LivingDexMode.Complete);
        return modes;
    }

    public static string ModeName(LivingDexMode mode) => mode switch
    {
        LivingDexMode.Regional => "Régional",
        LivingDexMode.National => "National",
        _ => "Complet",
    };

    /// <summary>Mode dans l'adresse (?mode=regional, complet) ; le National n'y figure pas (null), c'est le mode par défaut.</summary>
    public static string? ModeSlug(LivingDexMode mode) => mode switch
    {
        LivingDexMode.Regional => "regional",
        LivingDexMode.Complete => "complet",
        _ => null,
    };

    /// <summary>Mode lu dans l'adresse ; absent ou inconnu : le National.</summary>
    public static LivingDexMode ModeFromSlug(string? slug) => slug switch
    {
        "regional" => LivingDexMode.Regional,
        "complet" => LivingDexMode.Complete,
        _ => LivingDexMode.National,
    };

    /// <summary>
    /// Cartes triées. Par génération : l'ordre des jeux, ou son exact inverse (Violet, Écarlate... Rouge). Par
    /// complétion : le pourcentage dans le mode de chaque carte (celui affiché, le National pour « National seul »), du
    /// plus petit au plus grand ou l'inverse, et à égalité toujours dans l'ordre des jeux.
    /// </summary>
    public static List<LivingDexSummary> Sort(IEnumerable<LivingDexSummary> dexes, LivingDexSort sort, bool descending)
    {
        var byGame = dexes.OrderBy(d => d.Game.Game).ToList();
        if (sort == LivingDexSort.Generation)
        {
            if (descending)
                byGame.Reverse();
            return byGame;
        }
        // Comparaison exacte des fractions (owned / total), sans arrondi : 1 / 3 avant 34 / 100.
        Comparison<LivingDexSummary> completion = (a, b) =>
            ((long)a.Owned * Math.Max(b.Total, 1)).CompareTo((long)b.Owned * Math.Max(a.Total, 1));
        return [.. byGame.Order(Comparer<LivingDexSummary>.Create((a, b) => descending ? completion(b, a) : completion(a, b)))];
    }

    /// <summary>Tri dans l'adresse (?tri=completion) ; celui par génération n'y figure pas (null), c'est le tri par défaut.</summary>
    public static string? SortSlug(LivingDexSort sort) => sort == LivingDexSort.Completion ? "completion" : null;

    /// <summary>Tri lu dans l'adresse ; absent ou inconnu : par génération.</summary>
    public static LivingDexSort SortFromSlug(string? slug) => slug == "completion" ? LivingDexSort.Completion : LivingDexSort.Generation;

    /// <summary>
    /// Progression affichée dans les barres, avec une décimale arrondie vers le bas : « 57,3 % » ; 100 % seulement quand
    /// tout y est (999 sur 1 000 donne 99,9 %).
    /// </summary>
    public static string Percent(int owned, int total)
    {
        var tenths = total == 0 ? 0 : (int)((long)owned * 1000 / total);
        return $"{tenths / 10},{tenths % 10} %";
    }
}

/// <summary>
/// Living dex : un exemplaire de chaque Pokémon du Pokédex d'un jeu, compté dans une sauvegarde stockée (équipe et
/// boîtes, telle qu'envoyée sur le site). Au plus un par jeu et par compte ; supprimé avec sa sauvegarde.
/// </summary>
public sealed class LivingDexService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    ILogger<LivingDexService> logger)
{
    /// <summary>
    /// Living dex du compte, progression comptée dans un mode (vue générale) : le National pour un jeu qui ne propose pas
    /// ce mode (<see cref="LivingDexSummary.Mode"/>), comme sur sa page.
    /// </summary>
    public async Task<List<LivingDexSummary>> ListAsync(string ownerId, LivingDexMode mode = LivingDexMode.National)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var dexes = await db.LivingDexes.AsNoTracking().Where(d => d.OwnerId == ownerId)
            .Select(d => new { d.Id, d.Game, d.StoredSaveId, d.StoredSave!.TrainerName, d.StoredSave.FileName })
            .ToListAsync();
        var summaries = new List<LivingDexSummary>();
        foreach (var d in dexes.OrderBy(d => d.Game))
        {
            var game = LivingDexGames.Get(d.Game);
            var shown = LivingDexGames.Modes(game).Contains(mode) ? mode : LivingDexMode.National;
            var entries = await EntriesAsync(db, game, d.StoredSaveId, shown);
            summaries.Add(new(d.Id, game, SaveLabel(d.TrainerName, d.FileName), entries.Count(e => e.Owned), entries.Count, shown));
        }
        return summaries;
    }

    /// <summary>Sauvegardes stockées qui peuvent recevoir un living dex : d'un jeu qui en a, pas encore liées, avec un jeu libre.</summary>
    public async Task<List<LivingDexCandidate>> ListCandidatesAsync(string ownerId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var taken = await db.LivingDexes.Where(d => d.OwnerId == ownerId).Select(d => d.Game).ToListAsync();
        var linked = db.LivingDexes.Select(d => d.StoredSaveId);
        var saves = await db.StoredSaves.AsNoTracking()
            .Where(s => s.OwnerId == ownerId && s.Generation <= 9 && !linked.Contains(s.Id))
            .OrderBy(s => s.UploadedAt)
            .Select(s => new { s.Id, s.GameName, s.TrainerName, s.FileName, s.Data })
            .ToListAsync();
        var candidates = new List<LivingDexCandidate>();
        foreach (var s in saves)
        {
            if (!StoredSaveService.TryRead(s.Data, s.FileName, out var sav))
                continue;
            var games = LivingDexGames.For(sav).Except(taken).ToList();
            if (games.Count > 0)
                candidates.Add(new(s.Id, $"{s.GameName} — {SaveLabel(s.TrainerName, s.FileName)}", games));
        }
        return candidates;
    }

    public async Task<LivingDexResult> CreateAsync(string ownerId, int saveId, LivingDexGame game)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var save = await db.StoredSaves.AsNoTracking().Where(s => s.Id == saveId && s.OwnerId == ownerId)
            .Select(s => new { s.FileName, s.Data }).FirstOrDefaultAsync();
        if (save is null)
            return new(false, "Cette sauvegarde n'existe plus.");
        var name = LivingDexGames.Get(game).Name;
        if (!StoredSaveService.TryRead(save.Data, save.FileName, out var sav) || !LivingDexGames.For(sav).Contains(game))
            return new(false, $"Cette sauvegarde n'est pas une partie de {name}.");
        if (await db.LivingDexes.AnyAsync(d => d.OwnerId == ownerId && d.Game == game))
            return new(false, $"Tu as déjà un living dex {name} : supprime-le pour en créer un autre.");
        if (await db.LivingDexes.AnyAsync(d => d.StoredSaveId == saveId))
            return new(false, "Cette sauvegarde a déjà un living dex.");

        var dex = new LivingDex { OwnerId = ownerId, Game = game, StoredSaveId = saveId, CreatedAt = DateTime.UtcNow };
        db.LivingDexes.Add(dex);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException) // créé au même moment depuis un autre onglet (index uniques)
        {
            return new(false, $"Tu as déjà un living dex {name}, ou cette sauvegarde en a déjà un.");
        }
        logger.LogInformation("Utilisateur {User} : living dex {Game} créé sur la sauvegarde {Save}.", ownerId, game, saveId);
        return new(true, $"Living dex {name} créé.", dex.Id);
    }

    /// <summary>Living dex dans un mode (le National si celui demandé n'est pas proposé pour ce jeu), null s'il n'existe pas.</summary>
    public async Task<LivingDexView?> GetAsync(string ownerId, int id, LivingDexMode mode = LivingDexMode.National)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var dex = await db.LivingDexes.AsNoTracking().Where(d => d.Id == id && d.OwnerId == ownerId)
            .Select(d => new { d.Id, d.Game, d.StoredSaveId, d.StoredSave!.TrainerName, d.StoredSave.FileName })
            .FirstOrDefaultAsync();
        if (dex is null)
            return null;
        var game = LivingDexGames.Get(dex.Game);
        var modes = LivingDexGames.Modes(game);
        if (!modes.Contains(mode))
            mode = LivingDexMode.National;
        var entries = await EntriesAsync(db, game, dex.StoredSaveId, mode);
        return new(dex.Id, game, SaveLabel(dex.TrainerName, dex.FileName), modes, mode, entries);
    }

    /// <summary>
    /// Cases d'un mode avec le Pokémon de la sauvegarde qui remplit chacune : le même comptage partout (page, vue générale).
    /// Les cases Méga (<see cref="LivingDexSlot.MegaItem"/>, <see cref="LivingDexSlot.MegaMove"/>) prennent d'abord leurs
    /// exemplaires, que les autres cases ne reprennent pas : il faut un exemplaire de plus par Méga-Évolution. En Let's Go
    /// (<see cref="LivingDexSlot.MegaInBag"/>), n'importe quel exemplaire convient : la case de l'espèce garde le premier,
    /// chaque Méga dont la gemme est dans le sac prend le suivant. Les cases Gigamax (<see cref="LivingDexSlot.Gigamax"/>)
    /// prennent aussi d'abord le leur, qui a le facteur Gigamax.
    /// </summary>
    private static async Task<List<LivingDexEntry>> EntriesAsync(ApplicationDbContext db, LivingDexGameInfo game, int saveId, LivingDexMode mode)
    {
        var owned = await OwnedAsync(db, saveId);
        var slots = LivingDexGames.Slots(game, mode);
        var bag = slots.Any(s => s.KeyItem != 0 || s.MegaInBag) ? await BagItemsAsync(db, saveId) : new HashSet<int>();

        var megaCopies = new Dictionary<int, OwnedCopy>(); // index de la case Méga ou Gigamax → son exemplaire
        for (var i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            if (slot.MegaItem == 0 && slot.MegaMove == 0 && !slot.Gigamax || slot.MegaInBag)
                continue;
            var copy = owned.FirstOrDefault(p => p.Species == slot.Species && !megaCopies.ContainsValue(p)
                && (slot.Gigamax ? p.CanGigantamax && LivingDexGames.Fills(slot, p.Species, p.Form, LivingDexMode.Complete)
                    : slot.MegaItem != 0 ? p.HeldItem == slot.MegaItem : p.KnowsMegaMove));
            if (copy is not null)
                megaCopies[i] = copy;
        }
        var available = megaCopies.Count == 0 ? owned : owned.Except(megaCopies.Values).ToList();
        var bagMegas = BagMegas(slots, available, bag);

        return slots.Select((slot, index) =>
        {
            var (copy, note) = slot switch
            {
                { MegaInBag: true } => bagMegas[index],
                { MegaItem: not 0 } or { MegaMove: not 0 } or { Gigamax: true } =>
                    (megaCopies.GetValueOrDefault(index), MegaNote(slot, megaCopies.ContainsKey(index))),
                { KeyItem: not 0 } => FindWithItem(available, slot, mode, bag),
                _ => (Find(available, slot, mode), null),
            };
            if (copy is null && slot.Hint is not null)
                note = slot.Hint;
            var (section, number) = LivingDexGames.RegionalNumber(game, index);
            return new LivingDexEntry(mode == LivingDexMode.Regional ? number : slot.Species, slot.Species, slot.Form,
                LivingDexGames.Name(game, slot, mode), LivingDexGames.Sprite(game, slot), copy?.Id, copy?.Location,
                LivingDexGames.Fallback(game, slot), note, mode == LivingDexMode.Regional ? section : null);
        }).ToList();
    }

    /// <summary>
    /// Infobulle d'une case Méga : « avec Dracaufite X » ; manquante, « il faut un Dracaufeu de plus avec Dracaufite X »
    /// (Méga-Rayquaza : « qui connaît Draco-Ascension » ; Gigamax : « avec le facteur Gigamax »).
    /// </summary>
    private static string MegaNote(LivingDexSlot slot, bool filled)
    {
        var condition = slot.Gigamax ? "avec le facteur Gigamax"
            : slot.MegaItem != 0 ? $"avec {FrenchStrings.itemlist[slot.MegaItem]}" : $"qui connaît {FrenchStrings.movelist[slot.MegaMove]}";
        return filled ? condition : $"il faut un {FrenchStrings.specieslist[slot.Species]} de plus {condition}";
    }

    /// <summary>
    /// Cases Méga de Let's Go (<see cref="LivingDexSlot.MegaInBag"/>) : pour chaque espèce, le premier exemplaire reste à la
    /// case de l'espèce, et chaque Méga dont la gemme est dans le sac prend l'exemplaire suivant (Dracaufeu : trois pour
    /// les deux Méga). Avec ce qui manque, pour l'infobulle : « il faut un Dracaufeu de plus et la Dracaufite X dans le sac ».
    /// </summary>
    private static Dictionary<int, (OwnedCopy? Copy, string Note)> BagMegas(IReadOnlyList<LivingDexSlot> slots,
        List<OwnedCopy> owned, IReadOnlySet<int> bag)
    {
        var result = new Dictionary<int, (OwnedCopy?, string)>();
        foreach (var species in slots.Select((slot, index) => (slot, index)).Where(s => s.slot.MegaInBag).GroupBy(s => s.slot.Species))
        {
            var copies = owned.Where(p => p.Species == species.Key).ToList();
            var next = 1; // le premier remplit la case de l'espèce
            foreach (var (slot, index) in species)
            {
                var item = FrenchStrings.itemlist[slot.MegaItem];
                var theItem = "AEIOUÉ".Contains(item[0]) ? $"l'{item}" : $"la {item}"; // « la Dracaufite X », « l'Alakazamite »
                var name = FrenchStrings.specieslist[slot.Species];
                var copy = next < copies.Count ? copies[next] : null;
                var inBag = bag.Contains(slot.MegaItem);
                result[index] = (copy is not null, inBag) switch
                {
                    (true, true) => (copy, $"{item} dans le sac"),
                    (true, false) => (null, $"il faut {theItem} dans le sac"),
                    (false, true) => (null, $"il faut un {name} de plus"),
                    (false, false) => (null, $"il faut un {name} de plus et {theItem} dans le sac"),
                };
                if (copy is not null && inBag)
                    next++;
            }
        }
        return result;
    }

    public async Task<bool> DeleteAsync(string ownerId, int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var deleted = await db.LivingDexes.Where(d => d.Id == id && d.OwnerId == ownerId).ExecuteDeleteAsync();
        if (deleted > 0)
            logger.LogInformation("Utilisateur {User} : living dex {Id} supprimé.", ownerId, id);
        return deleted > 0;
    }

    /// <summary>Living dex de chaque sauvegarde (nom du jeu), pour prévenir qu'il partira avec elle (« Supprimer »).</summary>
    public async Task<Dictionary<int, string>> NamesBySaveAsync(string ownerId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return (await db.LivingDexes.Where(d => d.OwnerId == ownerId).Select(d => new { d.StoredSaveId, d.Game }).ToListAsync())
            .ToDictionary(d => d.StoredSaveId, d => LivingDexGames.Get(d.Game).Name);
    }

    /// <summary>Pokémon de la sauvegarde qui remplit une case.</summary>
    /// <param name="HeldItem">Objet tenu (Méga-Gemme des cases Méga).</param>
    /// <param name="KnowsMegaMove">Rayquaza qui connaît Draco-Ascension (case Méga-Rayquaza, qui n'a pas de gemme).</param>
    /// <param name="CanGigantamax">A le facteur Gigamax (cases Gigamax d'Épée / Bouclier).</param>
    private sealed record OwnedCopy(int Id, ushort Species, byte Form, string? Location, int HeldItem, bool KnowsMegaMove,
        bool CanGigantamax);

    /// <summary>Pokémon de la sauvegarde (les œufs ne comptent pas), dans son ordre : équipe, puis boîtes.</summary>
    private static async Task<List<OwnedCopy>> OwnedAsync(ApplicationDbContext db, int saveId)
    {
        var rows = await db.BankPokemon.AsNoTracking()
            .Where(p => p.StoredSaveId == saveId && !p.IsEgg)
            .OrderBy(p => p.Position)
            // Octets seulement pour les Rayquaza (leurs capacités disent s'ils peuvent méga-évoluer) et les espèces qui ont
            // une case Gigamax (le facteur est dans les octets).
            .Select(p => new
            {
                p.Id, p.Species, p.Form, p.Location, p.HeldItem, p.Context,
                Data = p.Species == LivingDexGames.Rayquaza || LivingDexGames.GigamaxSpecies.Contains(p.Species) ? p.Data : null,
            })
            .ToListAsync();
        return rows.Select(p =>
        {
            var pk = p.Data is null ? null : EntityFormat.GetFromBytes(p.Data, (EntityContext)p.Context);
            return new OwnedCopy(p.Id, p.Species, p.Form, p.Location, p.HeldItem ?? 0,
                p.Species == LivingDexGames.Rayquaza && pk is not null && pk.HasMove(LivingDexGames.DragonAscent),
                pk is IGigantamax { CanGigantamax: true });
        }).ToList();
    }

    /// <summary>
    /// Premier Pokémon de la sauvegarde qui remplit la case : la même espèce, sous n'importe quelle forme en Régional et
    /// National (un Raichu d'Alola est un Raichu), la forme exacte en Complet (<see cref="LivingDexGames.Fills"/>).
    /// </summary>
    private static OwnedCopy? Find(List<OwnedCopy> owned, LivingDexSlot slot, LivingDexMode mode) =>
        owned.FirstOrDefault(p => LivingDexGames.Fills(slot, p.Species, p.Form, mode));

    /// <summary>
    /// Case à objet rare (<see cref="LivingDexSlot.KeyItem"/>, Shaymin Céleste) : le deuxième exemplaire de l'espèce (le
    /// premier remplit la case précédente), si l'objet est dans le sac ; avec ce qui manque, pour l'infobulle.
    /// </summary>
    private static (OwnedCopy? Copy, string? Note) FindWithItem(List<OwnedCopy> owned, LivingDexSlot slot, LivingDexMode mode,
        IReadOnlySet<int> bag)
    {
        var second = owned.Where(p => LivingDexGames.Fills(slot, p.Species, p.Form, mode)).Skip(1).FirstOrDefault();
        var itemName = FrenchStrings.itemlist[slot.KeyItem];
        var item = $"{(slot.KeyItem == LivingDexGames.PrisonBottle ? "le" : "la")} {itemName}"; // « la Gracidée », « le Vase Scellé »
        var species = FrenchStrings.specieslist[slot.Species];
        return (second, bag.Contains(slot.KeyItem)) switch
        {
            (not null, true) => (second, $"{itemName} dans le sac"),
            (not null, false) => (null, $"il faut {item} dans le sac"),
            (null, true) => (null, $"il faut un deuxième {species}"),
            (null, false) => (null, $"il faut un deuxième {species} et {item} dans le sac"),
        };
    }

    private static readonly GameStrings FrenchStrings = GameInfo.GetStrings("fr");

    /// <summary>Objets du sac de la sauvegarde (toutes les poches, quantité non nulle).</summary>
    private static async Task<IReadOnlySet<int>> BagItemsAsync(ApplicationDbContext db, int saveId)
    {
        var save = await db.StoredSaves.AsNoTracking().Where(s => s.Id == saveId).Select(s => new { s.Data, s.FileName }).FirstOrDefaultAsync();
        if (save is null || !StoredSaveService.TryRead(save.Data, save.FileName, out var sav))
            return new HashSet<int>();
        return sav.Inventory.SelectMany(p => p.Items).Where(i => i.Count > 0).Select(i => i.Index).ToHashSet();
    }

    private static string SaveLabel(string trainer, string fileName) => $"{trainer} · {fileName}";
}
