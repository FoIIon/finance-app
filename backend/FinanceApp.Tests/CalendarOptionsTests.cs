using FinanceApp.API.Services.Calendar;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FinanceApp.Tests;

/// <summary>
/// La liaison de configuration concatène un tableau d'appsettings au tableau par défaut de la classe.
/// Avec « calendar.google.com » en défaut, le même hôte configuré donnait une liste doublée et un hôte
/// différent ne pouvait jamais retirer Google. La classe n'a donc plus de défaut, et le démarrage refuse
/// une liste vide.
/// </summary>
public class CalendarOptionsTests
{
    private static CalendarOptions Bind(params (string Key, string Value)[] entries)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(entries.ToDictionary(e => e.Key, e => (string?)e.Value))
            .Build();
        var options = new CalendarOptions();
        config.GetSection(CalendarOptions.SectionName).Bind(options);
        return options;
    }

    [Fact]
    public void LaListeConfiguree_EstExactementLaListeEffective()
    {
        var options = Bind(("Calendar:AllowedHosts:0", "calendar.google.com"));
        Assert.Equal(new[] { "calendar.google.com" }, options.AllowedHosts);

        var autre = Bind(("Calendar:AllowedHosts:0", "cal.exemple.invalid"), ("Calendar:AllowedHosts:1", "agenda.exemple.invalid"));
        Assert.Equal(new[] { "cal.exemple.invalid", "agenda.exemple.invalid" }, autre.AllowedHosts);
        Assert.DoesNotContain("calendar.google.com", autre.AllowedHosts);
    }

    [Fact]
    public void LeMessageDeRefus_NeListePasLHoteDeuxFois()
    {
        var options = Bind(("Calendar:AllowedHosts:0", "calendar.google.com"));
        var reason = IcsUrlPolicy.Refuse("https://autre.example/basic.ics", options.AllowedHosts, out _);
        Assert.Equal("Hôte non autorisé. Hôtes acceptés : calendar.google.com.", reason);
    }

    [Fact]
    public void SansHoteConfigure_LaClasseEstVide_EtLeDemarrageRefuse()
    {
        var options = new CalendarOptions();
        Assert.Empty(options.AllowedHosts);
        var result = new CalendarOptionsValidator().Validate(null, options);
        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("AllowedHosts"));

        var sansSection = Bind(("Calendar:MaxBytes", "1000"));
        Assert.Empty(sansSection.AllowedHosts);
        Assert.True(new CalendarOptionsValidator().Validate(null, sansSection).Failed);
    }

    [Fact]
    public void AvecUnHote_LaValidationPasse()
    {
        var options = Bind(("Calendar:AllowedHosts:0", "calendar.google.com"));
        Assert.True(new CalendarOptionsValidator().Validate(null, options).Succeeded);
    }
}
