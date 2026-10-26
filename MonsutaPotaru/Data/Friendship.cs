namespace MonsutaPotaru.Data;

/// <summary>
/// Amitié entre deux comptes, enregistrée dans les deux sens (une ligne par compte : la liste de chacun se lit sans OU).
/// Créée quand une demande est acceptée, supprimée des deux côtés par « Retirer », et avec l'un des comptes.
/// </summary>
public class Friendship
{
    public string UserId { get; set; } = "";
    public ApplicationUser? User { get; set; }

    public string FriendId { get; set; } = "";
    public ApplicationUser? Friend { get; set; }

    public DateTime Since { get; set; }
}

/// <summary>
/// Demande d'ami en attente (par la recherche ou par le code ami) : acceptée, refusée ou annulée, elle disparaît. Une
/// seule par sens et par paire de comptes.
/// </summary>
public class FriendRequest
{
    public int Id { get; set; }

    public string FromId { get; set; } = "";
    public ApplicationUser? From { get; set; }

    public string ToId { get; set; } = "";
    public ApplicationUser? To { get; set; }

    public DateTime CreatedAt { get; set; }
}
