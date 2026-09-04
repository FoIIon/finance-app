namespace FinanceApp.API.Models;

/// <summary>
/// Une échéance : ce que le ménage doit payer à une date (facture, taxe, cotisation), avant que la
/// transaction qui la règle n'existe. Elle n'entre jamais dans le bilan, seule la transaction compte,
/// via BilanClassifier. Aucune colonne de statut : voir <see cref="EcheanceStatus"/>.
/// </summary>
public class Echeance
{
    public int Id { get; set; }
    public int DashboardId { get; set; }
    public string Label { get; set; } = string.Empty;
    public DateOnly DueDate { get; set; }
    /// <summary>Montant attendu. Null : inconnu (la facture n'est pas encore arrivée).</summary>
    public decimal? Amount { get; set; }
    public string? Notes { get; set; }
    /// <summary>Marquée payée à la main. Null tant que rien ne prouve le paiement.</summary>
    public DateTime? PaidAt { get; set; }
    /// <summary>La transaction qui règle l'échéance. Une transaction ne prouve qu'une échéance (index unique filtré).
    /// Supprimer la transaction détache l'échéance, elle redevient à payer.</summary>
    public int? TransactionId { get; set; }
    public int CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Dashboard Dashboard { get; set; } = null!;
    public Transaction? Transaction { get; set; }
    public User CreatedByUser { get; set; } = null!;
    public ICollection<Document> Documents { get; set; } = new List<Document>();
}
