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

    /// <summary>Communication structurée attendue, douze chiffres (<see cref="Services.StructuredCommunication.Normalize"/>).
    /// Clé forte : elle rapproche même sans montant, comparée à celle que le rapprocheur extrait du libellé
    /// de chaque candidat, en mémoire.</summary>
    public string? StructuredCommunication { get; set; }

    /// <summary>Nom du bénéficiaire tel que sa banque le connaît, 70 caractères au plus (limite EPC), trimé,
    /// vide → null. Ne sert qu'au QR code de virement de la fiche, jamais au rapprochement.</summary>
    public string? CounterpartyName { get; set; }

    /// <summary>Instant UTC où le rapprocheur a lié <see cref="TransactionId"/>. Null quand le lien est
    /// absent (ou posé à la main avant la v4, quand le PUT le permettait encore). Dit qui a lié, jamais si
    /// c'est payé : le statut reste dérivé par <see cref="Services.EcheanceStatusRules"/>.</summary>
    public DateTime? MatchedAt { get; set; }

    /// <summary>Instant UTC où l'utilisateur a défait un rapprochement automatique (« Finalement non », soit
    /// POST unpay, sur un lien posé par le rapprocheur). Le geste veut dire « arrête de deviner pour
    /// celle-ci » : le rapprocheur l'ignore tant que c'est posé. Corriger l'IBAN ou la communication
    /// remet à null, la clé a changé, on peut redeviner. Un paiement à la main n'y touche pas, et « Modifier »
    /// (PUT) ne touche jamais au lien.</summary>
    public DateTime? AutoMatchRefusedAt { get; set; }

    public int CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Dashboard Dashboard { get; set; } = null!;
    public Transaction? Transaction { get; set; }
    public User CreatedByUser { get; set; } = null!;
    public ICollection<Document> Documents { get; set; } = new List<Document>();
}
