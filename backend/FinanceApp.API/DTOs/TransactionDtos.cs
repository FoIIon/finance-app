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
    public bool IsFixed { get; set; }
    public bool IsProvisional { get; set; }
    public string? BankAccountName { get; set; }
    public string? BankInstitutionName { get; set; }
    public int? ProjectEnvelopeId { get; set; }
    public string? ProjectEnvelopeName { get; set; }
}

public class SetExceptionalDto
{
    public bool IsExceptional { get; set; }
}

public class SetFixedDto
{
    public bool IsFixed { get; set; }
}

public class SetEnvelopeDto
{
    /// <summary>Enveloppe projet à rattacher. null = détacher.</summary>
    public int? ProjectEnvelopeId { get; set; }
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
    /// <summary>Mises de côté nettes sur la période : dépenses transfert interne (épargne, etc.)
    /// moins les retraits (Income transfert).</summary>
    public decimal TotalSavings { get; set; }
    /// <summary>Somme des dépenses exceptionnelles (non-transfert) sur la période. Toujours calculée, indépendamment du filtre includeExceptional.</summary>
    public decimal ExceptionalExpenses { get; set; }
    public List<CategoryBreakdownDto> CategoryBreakdown { get; set; } = new();
    /// <summary>Détail des rentrées (Income non-transfert) par catégorie.</summary>
    public List<CategoryBreakdownDto> IncomeBreakdown { get; set; } = new();
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
    /// <summary>Revenus non-transfert du mois (hors régularisations fixes).</summary>
    public decimal Entrees { get; set; }
    /// <summary>Dépenses non-transfert marquées « charge fixe » (Transaction.IsFixed),
    /// nettes des régularisations (revenus fixes : remboursement énergie…).</summary>
    public decimal Fixe { get; set; }
    /// <summary>Catégories transfert gardées au bilan (achat de titres, ordre permanent) — les
    /// « mises de côté » d'Audrey, nettes des retraits (Income transfert compté en négatif).
    /// Le balayage automatique vers le livret n'est PAS ici, voir <see cref="HorsBilan"/>.</summary>
    public decimal MisesDeCote { get; set; }
    /// <summary>Dépenses non-transfert non-fixes — le reste variable.</summary>
    public decimal Variable { get; set; }
    /// <summary>Part exceptionnelle du bloc variable (dépenses IsExceptional).</summary>
    public decimal VariableExceptionnel { get; set; }
    /// <summary>
    /// Mouvements sortis du bilan (Category.ExcludeFromMonthlyReport) : balayage du compte joint vers
    /// le livret, virements entre comptes suivis. Rendu pour affichage en information sous le total,
    /// jamais soustrait — c'est le reliquat du mois précédent, pas une charge du mois en cours.
    /// </summary>
    public decimal HorsBilan { get; set; }
    /// <summary>Entrées − Fixe − MisesDeCote − Variable : ce qu'il reste sur le mois.</summary>
    public decimal Total { get; set; }
    public List<CategoryBreakdownDto> EntreesByCategory { get; set; } = new();
    public List<CategoryBreakdownDto> FixeByCategory { get; set; } = new();
    public List<CategoryBreakdownDto> MisesDeCoteByCategory { get; set; } = new();
    public List<CategoryBreakdownDto> VariableByCategory { get; set; } = new();
    public List<CategoryBreakdownDto> HorsBilanByCategory { get; set; } = new();
}

/// <summary>Un point de la courbe burn-down : cumul du mois au soir du jour <see cref="Day"/>.</summary>
public class BurndownDayDto
{
    /// <summary>Jour du mois (1 → dernier jour).</summary>
    public int Day { get; set; }
    /// <summary>Date ISO "yyyy-MM-dd".</summary>
    public string Date { get; set; } = string.Empty;
    /// <summary>Cumul dépenses non-transfert du 1er à ce jour inclus. null si jour futur (mois courant).</summary>
    public decimal? Spent { get; set; }
    /// <summary>Cumul entrées non-transfert du 1er à ce jour inclus. null si jour futur.</summary>
    public decimal? Income { get; set; }
    /// <summary>Income − Spent. null si jour futur.</summary>
    public decimal? Remaining { get; set; }
}

/// <summary>
/// Burn-down du « reste du mois » : courbe jour par jour + projection de fin de mois.
/// Remplace le rituel manuel d'Audrey (note à la main ce qui reste pour finir le mois).
/// </summary>
public class BurndownDto
{
    public int Year { get; set; }
    public int Month { get; set; }
    public List<BurndownDayDto> Days { get; set; } = new();
    /// <summary>Reste (income − spent) au jour courant (mois courant) ou valeur finale (mois passé).</summary>
    public decimal RemainingToday { get; set; }
    /// <summary>Moyenne journalière des dépenses variables (non-transfert, catégorie non-fixe) des 14 derniers jours.</summary>
    public decimal DailyPaceVariable { get; set; }
    /// <summary>Dépenses récurrentes connues restant à tomber ce mois-ci. 0 si non calculable.</summary>
    public decimal UpcomingRecurringExpenses { get; set; }
    /// <summary>Entrées récurrentes connues restant à tomber ce mois-ci. 0 si non calculable.</summary>
    public decimal UpcomingRecurringIncome { get; set; }
    /// <summary>true si les récurrentes ont pu être intégrées à la projection.</summary>
    public bool RecurringIncluded { get; set; }
    /// <summary>remainingToday − dailyPaceVariable × joursRestants − upcomingExpenses + upcomingIncome. Mois passé : valeur finale réelle.</summary>
    public decimal ProjectedEndOfMonth { get; set; }
    /// <summary>Nombre de jours restants après aujourd'hui (mois courant), 0 si mois passé.</summary>
    public int DaysRemaining { get; set; }
    /// <summary>true = mois entièrement passé, projection = valeur finale réelle.</summary>
    public bool IsPast { get; set; }
    /// <summary>Jour du mois « aujourd'hui » (ancre de la projection). null hors mois courant.</summary>
    public int? TodayDay { get; set; }
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
