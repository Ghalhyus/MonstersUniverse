using PKHeX.Core;
using MonsutaPotaru.Data;

namespace MonsutaPotaru.Services;

/// <summary>Famille de forme d'un Pokémon, pour le filtre « Forme » de « Ma collection ».</summary>
public enum FormKind : byte { Standard, Alola, Galar, Hisui, Paldea, Other }

/// <summary>
/// Caractéristiques d'un Pokémon enregistrées pour les filtres de « Ma collection » (types, nature, talent, IV, forme,
/// Méga-Évolution) : la collection filtre en base sans relire les octets de chaque Pokémon.
/// </summary>
public static class PokemonTraits
{
    /// <summary>Statistiques des IV, dans l'ordre des colonnes de <see cref="BankPokemon"/> et des réglages du filtre.</summary>
    public static readonly string[] StatNames = ["PV", "Attaque", "Défense", "Atq. Spé.", "Déf. Spé.", "Vitesse"];

    /// <summary>Remplit les caractéristiques de <paramref name="entry"/> à partir de son Pokémon.</summary>
    public static void Fill(BankPokemon entry, PKM pk)
    {
        (entry.Type1, entry.Type2) = Types(pk.Species, pk.Form);
        entry.Nature = Nature(pk);
        entry.Ability = Math.Max(0, pk.Ability); // pas de talent en 1re et 2e génération (-1 pour PKHeX)
        var ivs = Ivs(pk);
        (entry.IvHp, entry.IvAtk, entry.IvDef, entry.IvSpA, entry.IvSpD, entry.IvSpe) = (ivs[0], ivs[1], ivs[2], ivs[3], ivs[4], ivs[5]);
        entry.FormKind = KindOf(pk.Species, pk.Form, pk.Context);
        entry.MegaReady = PokemonSprites.MegaForm(pk.Species, pk.Form, pk.HeldItem) is not null;
    }

    /// <summary>
    /// Types de l'espèce et de sa forme dans les jeux récents (table d'Écarlate/Violet, qui a toutes les espèces même absentes
    /// du jeu) : Mélofée est Fée même venu de Rouge/Bleu, Raichu d'Alola Électrik/Psy. Un seul type : deux fois le même.
    /// </summary>
    public static (byte Type1, byte Type2) Types(ushort species, byte form)
    {
        var entry = PersonalTable.SV.GetFormEntry(species, form);
        return (entry.Type1, entry.Type2);
    }

    /// <summary>
    /// Nature ; en 1re et 2e génération, celle que donne leur transfert vers les jeux suivants (Console Virtuelle → Pokémon
    /// Banque) : l'expérience modulo 25. PKHeX n'en donne pas à ces Pokémon.
    /// </summary>
    public static byte Nature(PKM pk) => pk is GBPKM ? (byte)(pk.EXP % 25) : (byte)pk.Nature;

    /// <summary>
    /// IV de 0 à 31 (PV, Attaque, Défense, Atq. Spé., Déf. Spé., Vitesse). En 1re et 2e génération, les IV vont de 0 à 15 :
    /// ramenés à l'échelle actuelle (× 2 + 1, 15 → 31) ; Atq. Spé. et Déf. Spé. y valent toutes deux le « Spécial ».
    /// </summary>
    public static byte[] Ivs(PKM pk)
    {
        int[] ivs = [pk.IV_HP, pk.IV_ATK, pk.IV_DEF, pk.IV_SPA, pk.IV_SPD, pk.IV_SPE];
        return ivs.Select(iv => (byte)(pk is GBPKM ? iv * 2 + 1 : iv)).ToArray();
    }

    /// <summary>
    /// Famille de la forme, d'après son nom anglais dans le jeu du Pokémon : forme 0 standard, formes d'Alola, de Galar, de
    /// Hisui ou de Paldea (Tauros de Paldea, Darumacho de Galar en Mode Transe), toute autre forme (Zarbi, Prismillon...).
    /// </summary>
    public static FormKind KindOf(ushort species, byte form, EntityContext context)
    {
        if (form == 0)
            return FormKind.Standard;
        var names = FormConverter.GetFormList(species, English.types, English.forms, GameInfo.GenderSymbolASCII, context);
        var name = form < names.Length ? names[form] : "";
        return name.Contains("Alola") ? FormKind.Alola
            : name.Contains("Galar") ? FormKind.Galar
            : name.Contains("Hisui") ? FormKind.Hisui
            : name.Contains("Paldea") ? FormKind.Paldea
            : FormKind.Other;
    }

    private static readonly GameStrings English = GameInfo.GetStrings("en");
}
