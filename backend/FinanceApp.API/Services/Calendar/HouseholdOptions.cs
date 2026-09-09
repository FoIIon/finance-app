using Microsoft.Extensions.Options;

namespace FinanceApp.API.Services.Calendar;

/// <summary>
/// Le fuseau du ménage (Household:TimeZone), lu une fois. Toute date affichée passe par lui, jamais par
/// l'UTC nu : « aujourd'hui » à 0h30 heure belge n'est pas la veille parce que le Pi tourne en UTC.
/// Identifiant IANA : natif sous Linux, accepté sous Windows par .NET 8 via ICU.
/// </summary>
public sealed class HouseholdOptions
{
    public const string SectionName = "Household";

    public string TimeZone { get; set; } = "Europe/Brussels";

    private TimeZoneInfo? _zone;

    public TimeZoneInfo Zone => _zone ??= TimeZoneInfo.FindSystemTimeZoneById(TimeZone);

    /// <summary>La date du jour dans le fuseau du ménage, à partir d'un instant UTC.</summary>
    public DateOnly TodayLocal(DateTime utcNow) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), Zone));
}

public sealed class HouseholdOptionsValidator : IValidateOptions<HouseholdOptions>
{
    public ValidateOptionsResult Validate(string? name, HouseholdOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.TimeZone))
            return ValidateOptionsResult.Fail("Household:TimeZone est obligatoire (identifiant IANA, ex. Europe/Brussels).");
        try
        {
            _ = options.Zone;
        }
        catch (TimeZoneNotFoundException)
        {
            return ValidateOptionsResult.Fail("Household:TimeZone n'est pas un fuseau connu du système.");
        }
        catch (InvalidTimeZoneException)
        {
            return ValidateOptionsResult.Fail("Household:TimeZone désigne un fuseau dont la définition est invalide.");
        }
        return ValidateOptionsResult.Success;
    }
}
