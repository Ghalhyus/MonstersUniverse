namespace MonsutaPotaru.Data;

/// <summary>
/// Combat terminé entre deux joueurs (Multijoueurs → Combats), pour leurs statistiques : qui, quand, dans quel format, et
/// le vainqueur (null : match nul). Écrit à la fin du combat, y compris par abandon ou temps écoulé ; pas pour un combat
/// annulé (simulateur arrêté). Jamais modifié ; sans clé étrangère vers les comptes, comme <see cref="TradeRecord"/>.
/// </summary>
public class BattleRecord
{
    public int Id { get; set; }
    public DateTime At { get; set; }

    /// <summary>Celui qui a demandé le combat.</summary>
    public string FirstId { get; set; } = "";
    public string SecondId { get; set; } = "";

    /// <summary>Format du simulateur (« gen3customgame » : la génération des sauvegardes).</summary>
    public string Format { get; set; } = "";

    /// <summary><see cref="FirstId"/> ou <see cref="SecondId"/> ; null pour un match nul.</summary>
    public string? WinnerId { get; set; }
}
