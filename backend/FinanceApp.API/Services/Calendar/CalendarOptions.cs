using Microsoft.Extensions.Options;

namespace FinanceApp.API.Services.Calendar;

/// <summary>
/// Bornes de la synchronisation du calendrier (section Calendar d'appsettings), validées au démarrage.
/// Un flux ICS est une entrée externe : taille, nombre d'occurrences et hôtes acceptés sont plafonnés.
/// </summary>
public sealed class CalendarOptions
{
    public const string SectionName = "Calendar";

    /// <summary>
    /// Hôtes acceptés pour l'adresse ICS, égalité stricte insensible à la casse. Sans valeur par défaut
    /// ici : la liaison de configuration concatène un tableau d'appsettings à celui de la classe, un hôte
    /// configuré ne pouvait jamais retirer le défaut (et le message de refus listait l'hôte deux fois). Le
    /// défaut vit dans appsettings.json, et la validation au démarrage refuse une liste vide.
    /// </summary>
    public string[] AllowedHosts { get; set; } = Array.Empty<string>();
    public long MaxBytes { get; set; } = 10 * 1024 * 1024;
    public int MaxOccurrencesPerEvent { get; set; } = 400;
    public int MaxOccurrences { get; set; } = 20_000;
    public int SyncIntervalMinutes { get; set; } = 30;
    public int WindowMonthsBack { get; set; } = 3;
    public int WindowMonthsForward { get; set; } = 12;
}

public sealed class CalendarOptionsValidator : IValidateOptions<CalendarOptions>
{
    public ValidateOptionsResult Validate(string? name, CalendarOptions o)
    {
        var errors = new List<string>();
        if (o.AllowedHosts == null || o.AllowedHosts.Length == 0 || o.AllowedHosts.Any(string.IsNullOrWhiteSpace))
            errors.Add("Calendar:AllowedHosts doit contenir au moins un hôte non vide.");
        if (o.MaxBytes <= 0) errors.Add("Calendar:MaxBytes doit être strictement positif.");
        if (o.MaxOccurrencesPerEvent <= 0) errors.Add("Calendar:MaxOccurrencesPerEvent doit être strictement positif.");
        if (o.MaxOccurrences <= 0) errors.Add("Calendar:MaxOccurrences doit être strictement positif.");
        if (o.SyncIntervalMinutes <= 0) errors.Add("Calendar:SyncIntervalMinutes doit être strictement positif.");
        if (o.WindowMonthsBack < 0) errors.Add("Calendar:WindowMonthsBack ne peut pas être négatif.");
        if (o.WindowMonthsForward <= 0) errors.Add("Calendar:WindowMonthsForward doit être strictement positif.");
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
