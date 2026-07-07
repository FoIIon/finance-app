namespace FinanceApp.API.Models;

public class Transaction
{
    public int Id { get; set; }
    public decimal Amount { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public TransactionType Type { get; set; }
    public int CategoryId { get; set; }
    public int AccountId { get; set; }
    public string? ExternalId { get; set; }
    public bool IsImported { get; set; }
    public string? CounterpartyName { get; set; }
    public bool IsExceptional { get; set; }

    /// <summary>Charge fixe récurrente (prêt, prélèvement, abonnement…). Posé par les règles
    /// (CategoryRule.MarkAsFixed) à l'import, modifiable à la main. Sur un revenu = régularisation
    /// (remboursement énergie…) déduite du bloc FIXE du bilan au lieu de gonfler les entrées.</summary>
    public bool IsFixed { get; set; }

    /// <summary>Transaction provisionnelle (ex: salaire attendu, matérialisé en début de mois).
    /// Supprimée automatiquement quand le versement réel est importé.</summary>
    public bool IsProvisional { get; set; }

    /// <summary>Récurrente source de la provision (null pour les transactions normales).</summary>
    public int? RecurringTransactionId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public int? BankAccountId { get; set; }

    /// <summary>Enveloppe projet à laquelle cette dépense est rattachée (optionnel).</summary>
    public int? ProjectEnvelopeId { get; set; }

    public Category Category { get; set; } = null!;
    public Account Account { get; set; } = null!;
    public BankAccount? BankAccount { get; set; }
    public ProjectEnvelope? ProjectEnvelope { get; set; }
    public RecurringTransaction? RecurringTransaction { get; set; }
}
