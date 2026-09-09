using FinanceApp.API.Services.Calendar;
using Xunit;

namespace FinanceApp.Tests;

/// <summary>Une adresse ICS refusée l'est pour une raison courte qui ne la répète jamais : c'est un secret.</summary>
public class IcsUrlPolicyTests
{
    private static readonly string[] Allowed = { "calendar.google.com" };
    private const string Secret = "https://calendar.google.com/calendar/ical/famille%40gmail.com/private-a1b2c3SECRET/basic.ics";

    [Fact]
    public void AdresseGoogleEnHttps_EstAcceptee()
    {
        Assert.Null(IcsUrlPolicy.Refuse(Secret, Allowed, out var uri));
        Assert.NotNull(uri);
        Assert.Equal("calendar.google.com", uri!.Host);
    }

    [Fact]
    public void HoteEnMajuscules_EstAccepte_LaComparaisonIgnoreLaCasse()
    {
        Assert.Null(IcsUrlPolicy.Refuse("https://CALENDAR.GOOGLE.COM/calendar/ical/x/basic.ics", Allowed, out _));
    }

    [Theory]
    [InlineData("http://calendar.google.com/calendar/ical/x/private-SECRET/basic.ics")]
    [InlineData("https://user:pass@calendar.google.com/calendar/ical/x/private-SECRET/basic.ics")]
    [InlineData("https://evil.example/calendar.google.com/private-SECRET/basic.ics")]
    [InlineData("https://calendar.google.com.evil.example/private-SECRET/basic.ics")]
    [InlineData("https://notcalendar.google.com/private-SECRET/basic.ics")]
    [InlineData("ftp://calendar.google.com/private-SECRET/basic.ics")]
    [InlineData("pas une url SECRET")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void AdresseRefusee_AvecUneRaison_QuiNeRepetePasLAdresse(string? url)
    {
        var reason = IcsUrlPolicy.Refuse(url, Allowed, out var uri);
        Assert.NotNull(reason);
        Assert.Null(uri);
        Assert.DoesNotContain("SECRET", reason);
        if (!string.IsNullOrWhiteSpace(url)) Assert.DoesNotContain(url, reason);
        Assert.DoesNotContain("evil.example", reason);
    }

    [Fact]
    public void LaRaisonDUnHoteRefuse_ListeLesHotesAcceptes_PasLHoteRefuse()
    {
        var reason = IcsUrlPolicy.Refuse("https://autre.example/basic.ics", Allowed, out _);
        Assert.Contains("calendar.google.com", reason);
        Assert.DoesNotContain("autre.example", reason);
    }

    [Fact]
    public void PasDeSuffixe_LEgaliteEstStricte()
    {
        Assert.NotNull(IcsUrlPolicy.Refuse("https://google.com/basic.ics", Allowed, out _));
        Assert.NotNull(IcsUrlPolicy.Refuse("https://www.calendar.google.com/basic.ics", Allowed, out _));
    }

    [Fact]
    public void AdresseTropLongue_EstRefusee()
    {
        var url = "https://calendar.google.com/" + new string('a', IcsUrlPolicy.MaxLength);
        Assert.Equal("Adresse trop longue.", IcsUrlPolicy.Refuse(url, Allowed, out _));
    }
}
