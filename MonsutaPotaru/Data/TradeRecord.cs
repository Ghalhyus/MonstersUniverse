namespace MonsutaPotaru.Data;

/// <summary>
/// Échange réussi entre deux joueurs (Multijoueurs → Échanges), pour leurs statistiques et les futurs succès : qui, quand,
/// quelles espèces, et l'évolution éventuelle. Jamais modifié ; sans clé étrangère vers les comptes, pour que les
/// statistiques du partenaire survivent à la suppression d'un compte.
/// </summary>
public class TradeRecord
{
    public int Id { get; set; }
    public DateTime At { get; set; }

    /// <summary>Celui qui a demandé l'échange.</summary>
    public string FirstId { get; set; } = "";
    public string SecondId { get; set; } = "";

    /// <summary>Espèce donnée par <see cref="FirstId"/>, et celle en laquelle elle a évolué chez l'autre (échange).</summary>
    public ushort FirstGave { get; set; }
    public ushort? FirstGaveEvolvedTo { get; set; }
    public ushort SecondGave { get; set; }
    public ushort? SecondGaveEvolvedTo { get; set; }
}
