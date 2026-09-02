namespace FinanceApp.API.Models;

/// <summary>Cadence d'une transaction récurrente. Stockée en texte, comme avant la conversion en enum.</summary>
public enum RecurringFrequency
{
    Weekly,
    Monthly,
    Yearly,
}
