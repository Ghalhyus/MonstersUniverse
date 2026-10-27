using System.Text.Json.Serialization;
using PKHeX.Core;

namespace MonsutaPotaru.Services;

/// <summary>Statistiques (IV ou EV), au format de Showdown.</summary>
public sealed record BattleStats(
    [property: JsonPropertyName("hp")] int Hp,
    [property: JsonPropertyName("atk")] int Atk,
    [property: JsonPropertyName("def")] int Def,
    [property: JsonPropertyName("spa")] int SpA,
    [property: JsonPropertyName("spd")] int SpD,
    [property: JsonPropertyName("spe")] int Spe);

/// <summary>
/// Pokémon envoyé au simulateur (<c>showdown/combat.js</c>) : espèce, objet, talent et capacités par numéro (numérotation
/// nationale, celle de PKHeX), traduits en noms de Showdown par le pont. <paramref name="PpUps"/> : PP Plus de chaque
/// capacité (Showdown en suppose 3 sinon).
/// </summary>
public sealed record BattleSet(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("species")] int Species,
    [property: JsonPropertyName("forme")] string Forme,
    [property: JsonPropertyName("item")] int Item,
    [property: JsonPropertyName("ability")] int Ability,
    [property: JsonPropertyName("moves")] IReadOnlyList<int> Moves,
    [property: JsonPropertyName("ppUps")] IReadOnlyList<int> PpUps,
    [property: JsonPropertyName("nature")] string Nature,
    [property: JsonPropertyName("gender")] string Gender,
    [property: JsonPropertyName("shiny")] bool Shiny,
    [property: JsonPropertyName("level")] int Level,
    [property: JsonPropertyName("happiness")] int Happiness,
    [property: JsonPropertyName("ivs")] BattleStats Ivs,
    [property: JsonPropertyName("evs")] BattleStats Evs);

/// <summary>Équipes des combats : Pokémon d'une sauvegarde → Pokémon du simulateur.</summary>
public static class BattleTeam
{
    /// <summary>Plus grande équipe (combat simple, comme en jeu).</summary>
    public const int MaxSize = 6;

    private static readonly IReadOnlyList<string> EnglishNatures = GameInfo.GetStrings("en").natures;

    /// <summary>Format du simulateur pour les sauvegardes de cette génération (null : pas encore de combats).</summary>
    public static string? Format(int generation) => generation switch
    {
        3 => "gen3customgame", // sans validation ni clause : un combat de câble, Pokémon illégaux compris
        _ => null,
    };

    /// <summary>
    /// Forme de Deoxys en 3e génération : celle du jeu (le Pokémon ne l'enregistre pas). Rouge Feu et Vert Feuille ont la même
    /// sauvegarde : PKHeX répond « Rouge Feu », donc la forme Attaque.
    /// </summary>
    public static string DeoxysForme(GameVersion version) => version switch
    {
        GameVersion.FR or GameVersion.FRLG => "Attack",
        GameVersion.LG => "Defense",
        GameVersion.E => "Speed",
        _ => "",
    };

    /// <summary>Nom du Pokémon en combat : son surnom, ou le nom français de l'espèce (pas « ROUCOOL » en majuscules).</summary>
    public static string NameOf(PKM pk) => pk.IsNicknamed ? pk.Nickname : SpeciesName.GetSpeciesName(pk.Species, (int)LanguageID.French);

    /// <summary>Pokémon de <paramref name="sav"/> pour le simulateur (niveau, PV, PP réels ; ni œuf ni case vide).</summary>
    public static BattleSet ToSet(PKM pk, SaveFile sav)
    {
        var moves = new[] { pk.Move1, pk.Move2, pk.Move3, pk.Move4 };
        var ppUps = new[] { pk.Move1_PPUps, pk.Move2_PPUps, pk.Move3_PPUps, pk.Move4_PPUps };
        var kept = Enumerable.Range(0, 4).Where(i => moves[i] != 0).ToList();
        return new BattleSet(
            NameOf(pk),
            pk.Species,
            pk.Species switch
            {
                (ushort)Species.Deoxys when sav.Generation == 3 => DeoxysForme(sav.Version),
                (ushort)Species.Unown => BattleNames.UnownForme(pk.Form), // forme d'aspect : seulement pour l'image
                _ => "",
            },
            HeldItem(pk, sav),
            pk.Ability,
            kept.Select(i => (int)moves[i]).ToList(),
            kept.Select(i => ppUps[i]).ToList(),
            EnglishNatures[(int)pk.Nature],
            pk.Gender switch { 0 => "M", 1 => "F", _ => "" },
            pk.IsShiny,
            pk.CurrentLevel,
            pk.CurrentFriendship,
            new BattleStats(pk.IV_HP, pk.IV_ATK, pk.IV_DEF, pk.IV_SPA, pk.IV_SPD, pk.IV_SPE),
            new BattleStats(pk.EV_HP, pk.EV_ATK, pk.EV_DEF, pk.EV_SPA, pk.EV_SPD, pk.EV_SPE));
    }

    /// <summary>Objet tenu en numérotation nationale (la 3e génération a la sienne).</summary>
    private static int HeldItem(PKM pk, SaveFile sav) =>
        pk.HeldItem == 0 ? 0 : sav.Generation == 3 ? ItemConverter.GetItemFuture3((ushort)pk.HeldItem) : pk.HeldItem;

    /// <summary>Pokémon de la sauvegarde qui peuvent combattre : équipe puis boîtes, sans les œufs ni les cases vides.</summary>
    public static IEnumerable<(PKM Pokemon, int Box, int Slot)> Candidates(SaveFile sav)
    {
        for (var i = 0; i < sav.PartyCount; i++)
        {
            var pk = sav.GetPartySlotAtIndex(i);
            if (pk.Species != 0 && !pk.IsEgg)
                yield return (pk, -1, i);
        }
        for (var box = 0; box < sav.BoxCount; box++)
        {
            for (var slot = 0; slot < sav.BoxSlotCount; slot++)
            {
                var pk = sav.GetBoxSlotAtIndex(box, slot);
                if (pk.Species != 0 && !pk.IsEgg)
                    yield return (pk, box, slot);
            }
        }
    }
}
