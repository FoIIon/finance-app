namespace FinanceApp.API.Models;

/// <summary>
/// Un emprunt en cours. Le passif ne remonte jamais par Open Banking : PSD2 n'expose que
/// les comptes de paiement, jamais le compte de crédit. Tout part donc d'un ancrage saisi
/// à la main, une ligne du tableau d'amortissement, d'où le reste se recalcule.
/// Rattaché au dashboard, comme Investment et SavingsGoal.
/// </summary>
public class Loan
{
    public int Id { get; set; }
    public int DashboardId { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>Titulaire, texte libre (ex. « Sébastien », « Audrey », « Commun »).</summary>
    public string Holder { get; set; } = string.Empty;
    public LoanKind Kind { get; set; }
    /// <summary>Prêteur, tel qu'il apparaît sur le relevé (ex. « CBC Banque »).</summary>
    public string? Lender { get; set; }
    /// <summary>Numéro de dossier côté prêteur, sert à reconnaître le prélèvement.</summary>
    public string? Reference { get; set; }
    /// <summary>Capital emprunté au départ. Inconnu sur un prêt familial repris en cours de route.</summary>
    public decimal? InitialPrincipal { get; set; }
    /// <summary>Taux nominal annuel, en pourcentage. Vaut 0 sur un prêt sans intérêt.</summary>
    public decimal AnnualRatePercent { get; set; }
    public decimal MonthlyPayment { get; set; }
    /// <summary>
    /// Date de l'échéance de référence. Toutes les autres échéances en dérivent de mois en mois,
    /// ce qui fixe aussi le jour de prélèvement.
    /// </summary>
    public DateTime AnchorDate { get; set; }
    /// <summary>
    /// Capital restant dû juste APRÈS le paiement de l'échéance d'ancrage. C'est la colonne
    /// « solde en capital » du tableau d'amortissement, recopiée telle quelle. Sur un prêt dont
    /// on ne connaît que la date de fin, ancrer à la dernière échéance avec un solde de 0 suffit.
    /// </summary>
    public decimal AnchorPrincipal { get; set; }
    /// <summary>IBAN du compte débité, pour rapprocher la mensualité des transactions importées.</summary>
    public string? DebitIban { get; set; }
    public bool IsArchived { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Dashboard Dashboard { get; set; } = null!;
}
