using System.ComponentModel.DataAnnotations;

namespace FinanceApp.API.DTOs;

/// <summary>
/// Une transaction du mois qu'Audrey ou Sébastien peut désigner comme le règlement d'une récurrente
/// (GET api/agenda/recurring/{id}/candidates). Date dans le fuseau du ménage.
/// </summary>
public class RecurringCandidateDto
{
    public int Id { get; set; }
    public DateOnly Date { get; set; }
    public decimal Amount { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? CounterpartyName { get; set; }
    /// <summary>Vrai quand Transaction.RecurringTransactionId vaut déjà cette récurrente (provisionnement ou geste manuel).</summary>
    public bool LinkedToThisRecurring { get; set; }
}

/// <summary>Corps de POST api/agenda/recurring/{id}/link : la transaction qui règle la récurrente.</summary>
public class LinkRecurringDto
{
    [Required]
    public int DashboardId { get; set; }

    [Required]
    public int TransactionId { get; set; }
}
