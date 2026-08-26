using System.ComponentModel.DataAnnotations;
using FinanceApp.API.Models;

namespace FinanceApp.API.DTOs;

public class LoanDto
{
    public int Id { get; set; }
    public int DashboardId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Holder { get; set; } = string.Empty;
    public LoanKind Kind { get; set; }
    public string? Lender { get; set; }
    public string? Reference { get; set; }
    public decimal? InitialPrincipal { get; set; }
    public decimal AnnualRatePercent { get; set; }
    public decimal MonthlyPayment { get; set; }
    public DateTime AnchorDate { get; set; }
    public decimal AnchorPrincipal { get; set; }
    public string? DebitIban { get; set; }
    public bool IsArchived { get; set; }

    // === Dérivé du tableau d'amortissement, jamais stocké ===
    public decimal RemainingPrincipal { get; set; }
    public int RemainingInstallments { get; set; }
    public DateTime? FinalDueDate { get; set; }
    public decimal RemainingInterest { get; set; }
    public decimal RemainingPayments { get; set; }
    public DateTime? NextDueDate { get; set; }
    public decimal? NextPayment { get; set; }
    /// <summary>Part du capital déjà remboursée, quand le capital emprunté est connu.</summary>
    public decimal? RepaidPercent { get; set; }
}

/// <summary>Une ligne du tableau d'amortissement.</summary>
public class LoanInstallmentDto
{
    public DateTime DueDate { get; set; }
    public decimal Payment { get; set; }
    public decimal Interest { get; set; }
    public decimal Principal { get; set; }
    public decimal RemainingPrincipal { get; set; }
}

/// <summary>Le passif du dashboard, tous emprunts confondus.</summary>
public class DebtSummaryDto
{
    public decimal TotalRemainingPrincipal { get; set; }
    public decimal TotalMonthlyPayment { get; set; }
    public decimal TotalRemainingInterest { get; set; }
    /// <summary>Date de la dernière échéance, tous emprunts confondus.</summary>
    public DateTime? DebtFreeDate { get; set; }
    public int LoanCount { get; set; }
}

public class CreateLoanDto
{
    [Required]
    public int DashboardId { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(50)]
    public string Holder { get; set; } = string.Empty;

    [Required]
    public LoanKind Kind { get; set; }

    [MaxLength(100)]
    public string? Lender { get; set; }

    [MaxLength(60)]
    public string? Reference { get; set; }

    [Range(0, 99999999.99)]
    public decimal? InitialPrincipal { get; set; }

    [Required, Range(0, 30)]
    public decimal AnnualRatePercent { get; set; }

    [Required, Range(0.01, 999999.99)]
    public decimal MonthlyPayment { get; set; }

    [Required]
    public DateTime AnchorDate { get; set; }

    [Required, Range(0, 99999999.99)]
    public decimal AnchorPrincipal { get; set; }

    [MaxLength(34)]
    public string? DebitIban { get; set; }
}

/// <summary>
/// Mise à jour partielle : un champ absent reste inchangé. Pour effacer un champ optionnel,
/// envoyer une chaîne vide, ou 0 pour le capital emprunté.
/// </summary>
public class UpdateLoanDto
{
    [MaxLength(100)]
    public string? Name { get; set; }

    [MaxLength(50)]
    public string? Holder { get; set; }

    public LoanKind? Kind { get; set; }

    [MaxLength(100)]
    public string? Lender { get; set; }

    [MaxLength(60)]
    public string? Reference { get; set; }

    [Range(0, 99999999.99)]
    public decimal? InitialPrincipal { get; set; }

    [Range(0, 30)]
    public decimal? AnnualRatePercent { get; set; }

    [Range(0.01, 999999.99)]
    public decimal? MonthlyPayment { get; set; }

    public DateTime? AnchorDate { get; set; }

    [Range(0, 99999999.99)]
    public decimal? AnchorPrincipal { get; set; }

    [MaxLength(34)]
    public string? DebitIban { get; set; }

    public bool? IsArchived { get; set; }
}
