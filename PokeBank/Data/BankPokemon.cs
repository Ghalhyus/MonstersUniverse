namespace PokeBank.Data;

/// <summary>
/// Un Pokémon stocké dans la banque. <see cref="Data"/> contient les octets bruts (déchiffrés, format "boîte")
/// tels que fournis par PKHeX ; les autres colonnes ne servent qu'à l'affichage et à la recherche.
/// </summary>
public class BankPokemon
{
    public int Id { get; set; }

    public string OwnerId { get; set; } = "";
    public ApplicationUser? Owner { get; set; }

    public byte[] Data { get; set; } = [];
    /// <summary>PKHeX.Core.EntityContext du fichier (Gen5, Gen6...).</summary>
    public byte Context { get; set; }
    public string Extension { get; set; } = "";

    public ushort Species { get; set; }
    public byte Form { get; set; }
    public string SpeciesName { get; set; } = "";
    /// <summary>
    /// <see cref="SpeciesName"/> sans accents ni majuscules (« evoli »), pour le tri par nom de « Ma collection » : SQLite
    /// trie les accents après le « z ». Calculé par <see cref="SortKey"/>.
    /// </summary>
    public string SortName { get; set; } = "";
    public string Nickname { get; set; } = "";
    public byte Level { get; set; }
    public bool IsShiny { get; set; }
    public bool IsEgg { get; set; }
    public bool IsLegal { get; set; }
    public string OriginalTrainer { get; set; } = "";
    public string OriginGame { get; set; } = "";

    public DateTime DepositedAt { get; set; }

    /// <summary>Renseigné si le Pokémon vient d'une sauvegarde stockée : lecture seule, absent de « Transfert ».</summary>
    public int? StoredSaveId { get; set; }
    public StoredSave? StoredSave { get; set; }
    /// <summary>Emplacement dans la sauvegarde stockée (« Boîte 3, case 12 », « Équipe 1 »...).</summary>
    public string? Location { get; set; }
    /// <summary>Ordre d'affichage dans la sauvegarde stockée (équipe, puis boîtes, puis le reste).</summary>
    public int Position { get; set; }

    /// <summary>Clé de tri d'un nom : sans accents ni majuscules (« Évoli » → « evoli »).</summary>
    public static string SortKey(string name)
    {
        var decomposed = name.Normalize(System.Text.NormalizationForm.FormD);
        var kept = decomposed.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark);
        return new string(kept.ToArray()).Normalize(System.Text.NormalizationForm.FormC).ToLowerInvariant();
    }
}
