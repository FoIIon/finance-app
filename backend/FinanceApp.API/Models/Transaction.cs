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

    /// <summary>Compte de la contrepartie (IBAN normalisé, sans espaces), quand la banque le sert.
    /// Null sur les paiements par carte et sur les lignes Trade Republic. Seul identifiant stable d'un
    /// bénéficiaire : la commune de Marche facture tantôt « Ville de Marche-en-Famenne », tantôt
    /// « ADMINISTRATION COMMUNALE DE MARCHE- », avec un libellé de virement vide.</summary>
    public string? CounterpartyIban { get; set; }

    public bool IsExceptional { get; set; }

    /// <summary>Remboursement d'une dépense (avance rendue, mutuelle, régularisation). Sur un revenu,
    /// la ligne sort du bloc ENTRÉES du bilan et s'impute en négatif sur le bloc de sa catégorie.
    /// Posé à la main, jamais deviné : les allocations familiales sont un revenu, pas un remboursement.
    /// Voir <see cref="Services.Refunds"/>.</summary>
    public bool IsRefund { get; set; }

    /// <summary>Date de la dernière correction manuelle de catégorie. Null si la catégorie vient d'une
    /// règle ou de l'import. Chaque correction signale une règle qui manque ou se trompe, le tri suivant
    /// commence par les relire (voir <see cref="Services.ManualCategoryTrace"/>).</summary>
    public DateTime? CategorySetManuallyAt { get; set; }

    /// <summary>Première catégorie que la ligne portait avant toute correction manuelle : celle qu'une
    /// règle avait posée, donc celle qui s'est trompée.</summary>
    public int? CategoryBeforeManualId { get; set; }

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

    /// <summary>Catégorie d'origine, avant correction manuelle. Sert au tri suivant.</summary>
    public Category? CategoryBeforeManual { get; set; }
    public Account Account { get; set; } = null!;
    public BankAccount? BankAccount { get; set; }
    public ProjectEnvelope? ProjectEnvelope { get; set; }
    public RecurringTransaction? RecurringTransaction { get; set; }
}
