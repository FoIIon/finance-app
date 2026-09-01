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
    public string? CounterpartyIban { get; set; }
    public bool IsExceptional { get; set; }
    public bool IsRefund { get; set; }
    /// <summary>Renseigné quand la catégorie a été corrigée à la main.</summary>
    public DateTime? CategorySetManuallyAt { get; set; }
    /// <summary>Catégorie posée par la règle avant la première correction manuelle.</summary>
    public string? CategoryBeforeManualName { get; set; }
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

public class SetRefundDto
{
    public bool IsRefund { get; set; }
}

/// <summary>Jusqu'où l'historique du dashboard est exploitable comme un bilan.</summary>
public class CoverageDto
{
    /// <summary>Date de la première transaction rattachée à un compte bancaire. Avant elle, aucun revenu
    /// n'a été importé : les banques ne servent que la fenêtre de leur consentement.</summary>
    public DateTime? FirstBankTransactionDate { get; set; }

    /// <summary>Date de la toute première transaction, courtier compris. Peut remonter bien plus loin.</summary>
    public DateTime? FirstTransactionDate { get; set; }
}

public class SetCategoryDto
{
    public int CategoryId { get; set; }
}

/// <summary>Une correction manuelle de catégorie, pour le tri suivant.</summary>
public class ManualRecategorizationDto
{
    public int TransactionId { get; set; }
    public DateTime Date { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? CounterpartyName { get; set; }
    public decimal Amount { get; set; }
    public string? FromCategory { get; set; }
    public string ToCategory { get; set; } = string.Empty;
    public DateTime CorrectedAt { get; set; }
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

/// <summary>Un mois de l'historique deux sens d'une catégorie, avec son homologue N-1 quand il est comparable.</summary>
public class CategoryFlowMonthDto
{
    /// <summary>Clé triable "yyyy-MM".</summary>
    public string Month { get; set; } = string.Empty;
    /// <summary>Libellé court fr-FR "juil. 2026".</summary>
    public string Label { get; set; } = string.Empty;
    /// <summary>Entrées de la catégorie, remboursements exclus.</summary>
    public decimal Income { get; set; }
    /// <summary>Sorties de la catégorie, nettes des remboursements.</summary>
    public decimal Expenses { get; set; }
    /// <summary>Mises de côté (catégorie de transfert), nettes des retraits.</summary>
    public decimal Savings { get; set; }
    /// <summary>Entrées − sorties − mises de côté, sauf catégorie hors bilan.</summary>
    public decimal Net { get; set; }

    /// <summary>Mêmes montants douze mois plus tôt. Null tant que le mois N-1 n'est pas couvert par la banque.</summary>
    public decimal? IncomePreviousYear { get; set; }
    public decimal? ExpensesPreviousYear { get; set; }
    public decimal? NetPreviousYear { get; set; }
}

/// <summary>Historique deux sens d'une catégorie et ce que la couverture bancaire autorise à comparer.</summary>
public class CategoryFlowHistoryDto
{
    public List<CategoryFlowMonthDto> Months { get; set; } = new();

    /// <summary>La catégorie est une catégorie de transfert (ses montants vont en mises de côté).</summary>
    public bool IsTransferCategory { get; set; }
    /// <summary>Catégorie hors bilan : ses mises de côté ne sont jamais soustraites du net.</summary>
    public bool IsOffBalanceCategory { get; set; }

    /// <summary>Au moins un mois de la fenêtre a un N-1 comparable.</summary>
    public bool PreviousYearAvailable { get; set; }
    /// <summary>Premier mois dont le N-1 deviendra comparable. Sert à le dire à l'écran plutôt qu'à le laisser deviner.</summary>
    public DateTime? PreviousYearAvailableFrom { get; set; }
    /// <summary>Première transaction bancaire du dashboard. Avant elle, l'historique n'est qu'un relevé de carte.</summary>
    public DateTime? FirstBankTransactionDate { get; set; }

    /// <summary>Premier mois entièrement couvert par la banque. Les mois d'avant ne portent que la carte
    /// Trade Republic : le graphe le signale plutôt que de laisser lire une chute de dépenses.</summary>
    public DateTime? FirstFullBankMonth { get; set; }
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
