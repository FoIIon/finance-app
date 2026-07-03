using System.ComponentModel.DataAnnotations;
using FinanceApp.API.Models;

namespace FinanceApp.API.DTOs;

public class TransactionDto
{
    public int Id { get; set; }
    public decimal Amount { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public TransactionType Type { get; set; }
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string CategoryIcon { get; set; } = string.Empty;
    public string CategoryColor { get; set; } = string.Empty;
    public int AccountId { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public string? ExternalId { get; set; }
    public bool IsImported { get; set; }
    public string? CounterpartyName { get; set; }
    public bool IsExceptional { get; set; }
    public string? BankAccountName { get; set; }
    public string? BankInstitutionName { get; set; }
}

public class SetExceptionalDto
{
    public bool IsExceptional { get; set; }
}

public class CreateTransactionDto
{
    [Required]
    [Range(0.01, 999999999.99)]
    public decimal Amount { get; set; }

    [Required, MaxLength(500)]
    public string Description { get; set; } = string.Empty;

    [Required]
    public DateTime Date { get; set; }

    [Required]
    [EnumDataType(typeof(TransactionType))]
    public TransactionType Type { get; set; }

    [Required]
    public int CategoryId { get; set; }

    [Required]
    public int AccountId { get; set; }
}

public class UpdateTransactionDto
{
    [Required]
    [Range(0.01, 999999999.99)]
    public decimal Amount { get; set; }

    [Required, MaxLength(500)]
    public string Description { get; set; } = string.Empty;

    [Required]
    public DateTime Date { get; set; }

    [Required]
    [EnumDataType(typeof(TransactionType))]
    public TransactionType Type { get; set; }

    [Required]
    public int CategoryId { get; set; }

    [Required]
    public int AccountId { get; set; }
}

public class TransactionSummaryDto
{
    public decimal TotalIncome { get; set; }
    public decimal TotalExpenses { get; set; }
    public decimal Balance { get; set; }
    /// <summary>Somme des dépenses catégorisées comme transfert interne (épargne, etc.) sur la période.</summary>
    public decimal TotalSavings { get; set; }
    /// <summary>Somme des dépenses exceptionnelles (non-transfert) sur la période. Toujours calculée, indépendamment du filtre includeExceptional.</summary>
    public decimal ExceptionalExpenses { get; set; }
    public List<CategoryBreakdownDto> CategoryBreakdown { get; set; } = new();
    /// <summary>Détail des mises de côté par catégorie de transfert.</summary>
    public List<CategoryBreakdownDto> SavingsBreakdown { get; set; } = new();
    public List<MonthlyBalanceDto> MonthlyBalance { get; set; } = new();
}

public class CategoryBreakdownDto
{
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string CategoryIcon { get; set; } = string.Empty;
    public string CategoryColor { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal Percentage { get; set; }
}

public class MonthlyBalanceDto
{
    public string Month { get; set; } = string.Empty;
    public decimal Income { get; set; }
    public decimal Expenses { get; set; }
    public decimal Balance { get; set; }
    public decimal TotalBalance { get; set; }
}

/// <summary>Dépenses d'une catégorie pour un mois calendaire (courant + exceptionnel séparés).</summary>
public class CategoryMonthHistoryDto
{
    /// <summary>Clé triable "yyyy-MM".</summary>
    public string Month { get; set; } = string.Empty;
    /// <summary>Libellé court fr-FR "juil. 2026".</summary>
    public string Label { get; set; } = string.Empty;
    /// <summary>Total dépenses de la catégorie (courant + exceptionnel).</summary>
    public decimal Total { get; set; }
    /// <summary>Dépenses hors exceptionnel.</summary>
    public decimal CurrentTotal { get; set; }
    /// <summary>Dépenses exceptionnelles seules.</summary>
    public decimal ExceptionalTotal { get; set; }
}

/// <summary>
/// Bilan mensuel en blocs, calqué sur le modèle Excel d'Audrey :
/// ENTRÉES − FIXE − MISES DE CÔTÉ − VARIABLE = TOTAL (le « reste » du mois).
/// </summary>
public class MonthlyReportDto
{
    public int Year { get; set; }
    public int Month { get; set; }
    /// <summary>Revenus non-transfert du mois.</summary>
    public decimal Entrees { get; set; }
    /// <summary>Dépenses non-transfert sur catégories marquées « charge fixe ».</summary>
    public decimal Fixe { get; set; }
    /// <summary>Dépenses sur catégories transfert (épargne, virements perso) — les « mises de côté » d'Audrey.</summary>
    public decimal MisesDeCote { get; set; }
    /// <summary>Dépenses non-transfert non-fixes — le reste variable.</summary>
    public decimal Variable { get; set; }
    /// <summary>Part exceptionnelle du bloc variable (dépenses IsExceptional).</summary>
    public decimal VariableExceptionnel { get; set; }
    /// <summary>Entrées − Fixe − MisesDeCote − Variable : ce qu'il reste sur le mois.</summary>
    public decimal Total { get; set; }
    public List<CategoryBreakdownDto> EntreesByCategory { get; set; } = new();
    public List<CategoryBreakdownDto> FixeByCategory { get; set; } = new();
    public List<CategoryBreakdownDto> MisesDeCoteByCategory { get; set; } = new();
    public List<CategoryBreakdownDto> VariableByCategory { get; set; } = new();
}

public class AccountBalanceDto
{
    public int AccountId { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public string? BankInstitutionName { get; set; }
    public decimal Balance { get; set; }
    /// <summary>true = solde réel banque (GoCardless), false = calcul.</summary>
    public bool IsRealBalance { get; set; }
    public bool IsManual { get; set; }
    public DateTime? LastTransactionDate { get; set; }
    public DateTime? BalanceUpdatedAt { get; set; }
}
