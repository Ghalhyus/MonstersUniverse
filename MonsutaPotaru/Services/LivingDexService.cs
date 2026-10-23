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
    /// <see cref="LivingDexGames"/> (« Forms ») en 4e.
    /// </summary>
    Complete,
}

/// <summary>Un jeu de living dex : nom, jaquette, sprites, et Pokémon de chaque mode.</summary>
/// <param name="Cover">Jaquette officielle (outils/images.py, partie « livingdex »).</param>
/// <param name="SpriteFolder">Sprites du jeu, « images/livingdex/rouge-bleu/25.png » (Pikachu).</param>
/// <param name="Region">Région du Pokédex régional (« Kanto », « Johto », « Hoenn »).</param>
/// <param name="Regional">Pokédex régional (numéros nationaux, dans l'ordre du Pokédex du jeu).</param>
/// <param name="NationalMax">Dernier numéro national que le jeu peut contenir.</param>
/// <param name="DeoxysForm">
/// Forme de Deoxys dans ce jeu (3e génération) : un Pokémon de 3e génération n'enregistre pas sa forme, c'est le jeu qui
/// la donne (Normale dans Rubis / Saphir, Attaque dans Rouge Feu, Défense dans Vert Feuille, Vitesse dans Émeraude). Une
/// sauvegarde ne peut donc avoir que celle-là : une seule case, remplie par n'importe quel Deoxys.
/// </param>
/// <param name="RegionalStart">Premier numéro du Pokédex régional : 1, sauf Unys qui commence à 0 (Victini).</param>
public sealed record LivingDexGameInfo(LivingDexGame Game, string Name, string Cover, string SpriteFolder, byte Generation,
    string Region, IReadOnlyList<ushort> Regional, ushort NationalMax, byte DeoxysForm = 0, int RegionalStart = 1);

/// <summary>Une case du living dex : un Pokémon (et sa forme) demandé par le mode.</summary>
/// <param name="AnyForm">Remplie par n'importe quelle forme de l'espèce, même en Complet (Deoxys de 3e génération, Arceus).</param>
/// <param name="KeyItem">
/// Case remplie par un <b>autre</b> exemplaire de l'espèce que celui de la case précédente, avec cet objet rare dans le
/// sac de la sauvegarde : Shaymin Forme Céleste (Gracidée), qui redevient Terrestre dans une boîte en 4e génération ; un
/// second Shaymin et la Gracidée suffisent, sans le bloquer dans l'équipe.
/// </param>
/// <param name="Hint">Conseil affiché tant que la case manque (saisons de Vivaldaim et Haydaim : « à garder en boîte »).</param>
public sealed record LivingDexSlot(ushort Species, byte Form, bool AnyForm = false, ushort KeyItem = 0, string? Hint = null);

/// <summary>Case du living dex avec le Pokémon de la sauvegarde qui la remplit, s'il y en a un.</summary>
/// <param name="Number">Numéro affiché : celui du Pokédex régional en mode Régional (Germignon : 1 à Johto), sinon le national.</param>
/// <param name="Name">« Pikachu », « Zarbi B » (Complet), « Deoxys (forme Vitesse) » (Émeraude).</param>
/// <param name="Sprite">Sprite du jeu, propre à la forme.</param>
/// <param name="BankId">Pokémon de la sauvegarde (sa fiche), null : manquant.</param>
/// <param name="Location">« Boîte 3, case 12 », « Équipe 2 ».</param>
/// <param name="Fallback">Image de la même forme sur le site, si le sprite du jeu manque (images pas encore récupérées).</param>
/// <param name="Note">
/// Condition de la case (<see cref="LivingDexSlot.KeyItem"/>) : « Gracidée dans le sac », « il faut un deuxième Shaymin » ;
/// ou conseil d'une case manquante (<see cref="LivingDexSlot.Hint"/>).
/// </param>
public sealed record LivingDexEntry(int Number, ushort Species, byte Form, string Name, string Sprite, int? BankId, string? Location,
    string Fallback = "", string? Note = null)
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

/// <summary>Jeux qui ont un living dex : 1re à 5e génération pour l'instant.</summary>
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
        Thundurus = 642, Landorus = 645, Kyurem = 646, Keldeo = 647, Genesect = 649;

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
    ];

    public static LivingDexGameInfo Get(LivingDexGame game) => All.Single(g => g.Game == game);

    /// <summary>
    /// Jeux possibles pour une sauvegarde : PKHeX reconnaît Jaune, Cristal, Émeraude et Platine, mais pas Rouge de Bleu, Or
    /// d'Argent, Rubis de Saphir, Rouge Feu de Vert Feuille, Diamant de Perle ni HeartGold de SoulSilver (même format) : au
    /// joueur de choisir. Pour Rouge Feu / Vert Feuille,
    /// PKHeX répond « Rouge Feu » faute de mieux : les deux sont toujours proposés. En 5e génération, chaque sauvegarde dit
    /// son jeu. Aucun pour une sauvegarde d'une autre génération, ni pour Colosseum / XD.
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
        _ => [],
    };

    /// <summary>
    /// Pokémon demandés par un mode : en Régional et National, un par espèce ; en Complet, chaque forme qu'une sauvegarde
    /// du jeu peut contenir (voir <see cref="LivingDexMode.Complete"/>), dans l'ordre du national puis des formes.
    /// </summary>
    public static IReadOnlyList<LivingDexSlot> Slots(LivingDexGameInfo game, LivingDexMode mode)
    {
        var national = Enumerable.Range(1, game.NationalMax).Select(n => (ushort)n);
        return mode switch
        {
            LivingDexMode.Regional => game.Regional.Select(n => Base(game, n)).ToList(),
            LivingDexMode.National => national.Select(n => Base(game, n)).ToList(),
            _ => national.SelectMany(n => Forms(game, n)).ToList(),
        };
    }

    /// <summary>Case d'une espèce : sa forme de base, sauf Deoxys en 3e génération (la forme de son jeu).</summary>
    private static LivingDexSlot Base(LivingDexGameInfo game, ushort species) =>
        species == Deoxys && game.Generation == 3 ? new(species, game.DeoxysForm, AnyForm: true) : new(species, 0);

    /// <summary>
    /// Formes d'une espèce en mode Complet, réduites à celles qu'une sauvegarde du jeu peut contenir : les lettres de Zarbi
    /// (26 en 2e génération, A à Z ; 28 ensuite, avec « ! » et « ? »), enregistrées dans chaque Zarbi. Rien d'autre jusqu'à
    /// la 3e génération : ni forme régionale, ni Méga-Évolution, ni Gigamax ; les formes de Morphéo n'existent qu'en combat,
    /// Deoxys a celle de son jeu (<see cref="Base"/>). À partir de la 4e, celles de <see cref="AspectForms"/> que le jeu
    /// connaît, et Shaymin Céleste à part (<see cref="LivingDexSlot.KeyItem"/>) ; Arceus et Genesect n'ont qu'une case,
    /// quelle que soit leur Plaque ou leur Module.
    /// </summary>
    private static IEnumerable<LivingDexSlot> Forms(LivingDexGameInfo game, ushort species) => species switch
    {
        Unown when UnownForms(game) > 1 => Enumerable.Range(0, UnownForms(game)).Select(f => new LivingDexSlot(species, (byte)f)),
        Shaymin when game.Generation >= 4 && FormCount(game, species) > 1 =>
            [new(species, 0, AnyForm: true), new(species, 1, AnyForm: true, KeyItem: Gracidea)],
        Arceus or Genesect when game.Generation >= 4 => [new(species, 0, AnyForm: true)],
        // 5e génération : seuls ceux de l'équipe prennent la saison du jeu (au chargement) ; en boîte, ils gardent la leur.
        Deerling or Sawsbuck when game.Generation == 5 =>
            Enumerable.Range(0, FormCount(game, species)).Select(f => new LivingDexSlot(species, (byte)f,
                Hint: "à garder en boîte (dans l'équipe, il prend la saison du jeu)")),
        _ when game.Generation >= 4 && AspectForms.Contains(species) =>
            Enumerable.Range(0, FormCount(game, species)).Select(f => new LivingDexSlot(species, (byte)f)),
        _ => [Base(game, species)],
    };

    private static int UnownForms(LivingDexGameInfo game) => game.Generation switch { 2 => 26, >= 3 => 28, _ => 1 };

    /// <summary>
    /// Espèces dont la forme change l'aspect, hors combat, à partir de la 4e génération : Pichu Troizépi (HeartGold /
    /// SoulSilver, il ne va pas dans les autres jeux), Deoxys (chaque Deoxys garde sa forme), capes de Cheniti et
    /// Cheniselle, mers de Sancoki et Tritosor, Motisma, Giratina Originel (tient l'Orbe Platiné) ; ces deux derniers
    /// n'existent qu'à partir de Platine. En 5e, Bargantua (motifs), et dans Noire 2 / Blanche 2 seulement les formes
    /// Totémiques de Boréas, Fulguris et Démétéros, Kyurem Blanc et Noir, Keldeo Décidé. Le nombre de formes de chaque jeu
    /// vient de PKHeX (<see cref="FormCount"/>). Pas Morphéo, Ceriflor, Darumacho ni Meloetta : leurs formes n'existent
    /// qu'en combat.
    /// </summary>
    private static readonly ushort[] AspectForms =
        [Pichu, Deoxys, Burmy, Wormadam, Shellos, Gastrodon, Rotom, Giratina, Basculin, Tornadus, Thundurus, Landorus, Kyurem, Keldeo];

    /// <summary>Formes de l'espèce que le jeu connaît (données de PKHeX : Motisma n'en a qu'une en Diamant / Perle).</summary>
    private static int FormCount(LivingDexGameInfo game, ushort species) => (game.Game switch
    {
        LivingDexGame.Diamond or LivingDexGame.Pearl => (IPersonalTable)PersonalTable.DP,
        LivingDexGame.Platinum => PersonalTable.Pt,
        LivingDexGame.HeartGold or LivingDexGame.SoulSilver => PersonalTable.HGSS,
        LivingDexGame.Black or LivingDexGame.White => PersonalTable.BW,
        _ => PersonalTable.B2W2,
    })[species].FormCount;

    /// <summary>La case est remplie par ce Pokémon : même espèce ; en Complet, même forme (sauf <see cref="LivingDexSlot.AnyForm"/>).</summary>
    public static bool Fills(LivingDexSlot slot, ushort species, byte form, LivingDexMode mode) =>
        species == slot.Species && (mode != LivingDexMode.Complete || slot.AnyForm || form == slot.Form);

    /// <summary>
    /// Sprite du jeu d'une case : « 201.png » (Zarbi A), « 201-b.png », « 201-question.png », « 386-speed.png » (Deoxys
    /// Vitesse), « 492-sky.png » (Shaymin Céleste) : noms de PokéAPI, à garder en accord avec outils/images.py (LIVINGDEX_FORMS).
    /// </summary>
    public static string Sprite(LivingDexGameInfo game, LivingDexSlot slot) =>
        $"{game.SpriteFolder}{slot.Species}{FormSuffix(slot)}.png";

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
    /// Haydaim ; « Kyurem Blanc ». Noms de formes de PKHeX, en français.
    /// </summary>
    public static string Name(LivingDexGameInfo game, LivingDexSlot slot, LivingDexMode mode)
    {
        var species = FrenchStrings.specieslist[slot.Species];
        var forms = FormConverter.GetFormList(slot.Species, FrenchStrings.types, FrenchStrings.forms, GameInfo.GenderSymbolUnicode, Context(game));
        var form = slot.Form < forms.Length ? forms[slot.Form] : $"{slot.Form}";
        return slot.Species switch
        {
            Unown when mode == LivingDexMode.Complete && UnownForms(game) > 1 => $"{species} {form}",
            Burmy or Wormadam or Shellos or Gastrodon or Basculin or Deerling or Sawsbuck when mode == LivingDexMode.Complete
                => $"{species} ({FormLabel(slot.Species, form)})",
            Kyurem when slot.Form != 0 => $"{species} {form}",
            _ when slot.Form != 0 => $"{species} ({FormLabel(slot.Species, form)})",
            _ => species,
        };
    }

    private static string FormLabel(ushort species, string form) => species switch
    {
        Deoxys or Giratina or Shaymin or Deerling or Sawsbuck or Tornadus or Thundurus or Landorus => $"forme {form}",
        Burmy or Wormadam => $"cape {form}",
        Shellos or Gastrodon => $"mer {form}",
        Basculin => $"motif {form}",
        Pichu => "Troizépi", // PKHeX n'a que l'anglais (« Spiky »)
        Keldeo => "forme Décidée", // PKHeX : « Décidé »
        _ => form,
    };

    /// <summary>Image de la même forme sur le site (formes des jeux récents), si le sprite du jeu manque.</summary>
    public static string Fallback(LivingDexGameInfo game, LivingDexSlot slot) =>
        PokemonSprites.Url(slot.Species, slot.Form, Context(game), female: false, heldItem: 0, shiny: false);

    private static EntityContext Context(LivingDexGameInfo game) => game.Generation switch
    {
        1 => EntityContext.Gen1,
        2 => EntityContext.Gen2,
        3 => EntityContext.Gen3,
        4 => EntityContext.Gen4,
        _ => EntityContext.Gen5,
    };

    private static readonly GameStrings FrenchStrings = GameInfo.GetStrings("fr");

    /// <summary>
    /// Modes proposés, dans l'ordre Régional, National, Complet : un mode qui demande exactement les mêmes Pokémon
    /// qu'un autre, dans le même ordre, est caché. En 1re génération, il ne reste que le National ; en 2e, le Pokédex de
    /// Johto a les mêmes 251 Pokémon que le national mais dans un autre ordre, et Zarbi ses 26 lettres : les trois modes ;
    /// à partir de la 3e, le Pokédex régional, le national et les formes : les trois.
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
            .Where(s => s.OwnerId == ownerId && s.Generation <= 5 &&!linked.Contains(s.Id))
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

    /// <summary>Cases d'un mode avec le Pokémon de la sauvegarde qui remplit chacune : le même comptage partout (page, vue générale).</summary>
    private static async Task<List<LivingDexEntry>> EntriesAsync(ApplicationDbContext db, LivingDexGameInfo game, int saveId, LivingDexMode mode)
    {
        var owned = await OwnedAsync(db, saveId);
        var slots = LivingDexGames.Slots(game, mode);
        var bag = slots.Any(s => s.KeyItem != 0) ? await BagItemsAsync(db, saveId) : new HashSet<int>();
        return slots.Select((slot, index) =>
        {
            var (copy, note) = slot.KeyItem == 0 ? (Find(owned, slot, mode), null) : FindWithItem(owned, slot, mode, bag);
            if (copy is null && slot.Hint is not null)
                note = slot.Hint;
            return new LivingDexEntry(mode == LivingDexMode.Regional ? index + game.RegionalStart : slot.Species, slot.Species, slot.Form,
                LivingDexGames.Name(game, slot, mode), LivingDexGames.Sprite(game, slot), copy?.Id, copy?.Location,
                LivingDexGames.Fallback(game, slot), note);
        }).ToList();
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
    private sealed record OwnedCopy(int Id, ushort Species, byte Form, string? Location);

    /// <summary>Pokémon de la sauvegarde (les œufs ne comptent pas), dans son ordre : équipe, puis boîtes.</summary>
    private static async Task<List<OwnedCopy>> OwnedAsync(ApplicationDbContext db, int saveId) =>
        await db.BankPokemon.AsNoTracking()
            .Where(p => p.StoredSaveId == saveId && !p.IsEgg)
            .OrderBy(p => p.Position)
            .Select(p => new OwnedCopy(p.Id, p.Species, p.Form, p.Location))
            .ToListAsync();

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
        var item = $"la {itemName}"; // « la Gracidée » : seul objet de ce genre pour l'instant
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
