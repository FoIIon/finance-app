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

    /// <summary>Compte du bénéficiaire, normalisé (sans espaces, majuscules) comme
    /// <see cref="Transaction.CounterpartyIban"/>. Saisi au formulaire. Avec le montant, c'est la clé
    /// ordinaire du rapprochement automatique.</summary>
    public string? CounterpartyIban { get; set; }

    /// <summary>Communication structurée attendue, douze chiffres, même normalisation que sur
    /// <see cref="Transaction.StructuredCommunication"/>. Clé forte : elle rapproche même sans montant.</summary>
    public string? StructuredCommunication { get; set; }

    /// <summary>Instant UTC où le rapprocheur a lié <see cref="TransactionId"/>. Null quand le lien est
    /// manuel ou absent. Dit qui a lié, jamais si c'est payé : le statut reste dérivé par
    /// <see cref="Services.EcheanceStatusRules"/>.</summary>
    public DateTime? MatchedAt { get; set; }

    /// <summary>Transaction refusée par « Finalement non » après un rapprochement automatique. Le
    /// rapprocheur ne la propose plus jamais pour cette échéance, une autre transaction peut encore la
    /// rapprocher. Supprimer la transaction efface le refus (SetNull).</summary>
    public int? RejectedTransactionId { get; set; }

    public int CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Dashboard Dashboard { get; set; } = null!;
    public Transaction? Transaction { get; set; }
    public Transaction? RejectedTransaction { get; set; }
    public User CreatedByUser { get; set; } = null!;
    public ICollection<Document> Documents { get; set; } = new List<Document>();
}
