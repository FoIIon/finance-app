namespace FinanceApp.API.Models;

/// <summary>
/// Cadence de la série dont une occurrence de calendrier est issue, stockée en texte. Distinct de
/// <see cref="RecurringFrequency"/>, qui appartient aux transactions récurrentes et n'a pas de Daily.
/// </summary>
public enum CalendarRecurrence
{
    None,
    Daily,
    Weekly,
    Monthly,
    Yearly
}
