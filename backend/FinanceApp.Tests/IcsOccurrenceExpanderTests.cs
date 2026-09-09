using System.Text.Json;
using FinanceApp.API.Models;
using FinanceApp.API.Services.Calendar;
using Xunit;

namespace FinanceApp.Tests;

/// <summary>
/// L'expansion de la fixture anonymisée Fixtures/famille.ics par Ical.Net 5, sur la fenêtre du 9 juin
/// 2026 au 10 septembre 2027, dans le fuseau de Bruxelles. Chaque cas nommé par le brief a son test.
/// </summary>
public class IcsOccurrenceExpanderTests
{
    private static readonly DateTime FromUtc = new(2026, 6, 9, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ToUtc = new(2027, 9, 10, 0, 0, 0, DateTimeKind.Utc);

    private const string Danse = "serie-danse-0001@test.invalid";
    private const string Piscine = "serie-piscine-0002@test.invalid";
    private const string Weekend = "weekend-cousins-0003@test.invalid";
    private const string Feu = "feu-artifice-0004@test.invalid";
    private const string Reunion = "reunion-parents-0005@test.invalid";
    private const string Anniversaire = "anniv-leonie-0006@test.invalid";
    private const string Flottant = "flottant-0007@test.invalid";

    private static readonly Lazy<IcsExpansion> Expanded = new(() =>
        IcsOccurrenceExpander.Expand(AgendaTestSupport.FamilleIcs(), FromUtc, ToUtc, AgendaTestSupport.Brussels, AgendaTestSupport.Options()));

    private static List<CalendarOccurrence> Of(string uid) =>
        Expanded.Value.Occurrences.Where(o => o.Uid == uid).OrderBy(o => o.OccurrenceStart).ToList();

    [Fact]
    public void LeNomDuCalendrier_VientDeXWrCalName()
    {
        Assert.Equal("Famille", Expanded.Value.CalendarName);
        Assert.Null(Expanded.Value.Warning);
    }

    [Fact]
    public void SerieHebdomadaire_ExpanseeAuBonNombre_ExdateEtDeplacementCompris()
    {
        // Seize jeudis du 3 septembre au 17 décembre, moins le 5 novembre (EXDATE). Le 1er octobre est
        // remplacé par la séance déplacée du 3 : quinze occurrences en tout, chacune une seule fois.
        var danse = Of(Danse);
        Assert.Equal(15, danse.Count);
        Assert.All(danse, o => Assert.Equal(CalendarRecurrence.Weekly, o.Recurrence));
        Assert.DoesNotContain(danse, o => o.LocalDate == new DateOnly(2026, 11, 5));
        Assert.Equal(14, danse.Count(o => !o.IsException && o.LocalDate.DayOfWeek == DayOfWeek.Thursday));
    }

    [Fact]
    public void OccurrenceDeplacee_PresenteUneFoisASaNouvelleDate_AbsenteALAncienne()
    {
        var danse = Of(Danse);
        var deplacee = Assert.Single(danse, o => o.IsException);
        Assert.Equal(new DateOnly(2026, 10, 3), deplacee.LocalDate);
        Assert.Equal(new TimeOnly(10, 0), deplacee.LocalStart);
        Assert.Equal(new TimeOnly(11, 0), deplacee.LocalEnd);
        Assert.Equal("Danse Clothilde (déplacée)", deplacee.Summary);
        Assert.Equal(CalendarRecurrence.Weekly, deplacee.Recurrence);
        Assert.DoesNotContain(danse, o => o.LocalDate == new DateOnly(2026, 10, 1));
        Assert.Single(danse, o => o.LocalDate == new DateOnly(2026, 10, 3));
    }

    [Fact]
    public void ChangementDHeureDOctobre_LHeureLocaleNeBougePas_LUtcSi()
    {
        var danse = Of(Danse);
        var avant = Assert.Single(danse, o => o.LocalDate == new DateOnly(2026, 10, 22));
        var apres = Assert.Single(danse, o => o.LocalDate == new DateOnly(2026, 10, 29));
        Assert.Equal(new TimeOnly(16, 45), avant.LocalStart);
        Assert.Equal(new TimeOnly(16, 45), apres.LocalStart);
        Assert.Equal(new TimeOnly(17, 45), apres.LocalEnd);
        Assert.Equal(14, avant.OccurrenceStart.Hour);
        Assert.Equal(15, apres.OccurrenceStart.Hour);
        Assert.Equal(DateTimeKind.Utc, apres.OccurrenceStart.Kind);

        // Même chose pour la piscine du samedi, qui enjambe le 25 octobre.
        var piscine = Of(Piscine);
        Assert.Equal(6, piscine.Count);
        Assert.All(piscine, o => Assert.Equal(new TimeOnly(11, 30), o.LocalStart));
    }

    [Fact]
    public void JourneeEntiereSurDeuxJours_DtendExclusif_LocalEndDateInclusif()
    {
        // DTSTART 10/10, DTEND 12/10 (exclusif) : le week-end couvre le 10 et le 11.
        var weekend = Assert.Single(Of(Weekend));
        Assert.True(weekend.IsAllDay);
        Assert.Null(weekend.LocalStart);
        Assert.Null(weekend.LocalEnd);
        Assert.Equal(new DateOnly(2026, 10, 10), weekend.LocalDate);
        Assert.Equal(new DateOnly(2026, 10, 11), weekend.LocalEndDate);
        // Minuit à Bruxelles (CEST) : 22h la veille en UTC.
        Assert.Equal(new DateTime(2026, 10, 9, 22, 0, 0, DateTimeKind.Utc), weekend.OccurrenceStart);
        Assert.Equal(CalendarRecurrence.None, weekend.Recurrence);
    }

    [Fact]
    public void InstantUtc_2130Z_Rendu_2330_EnEteBelge()
    {
        var feu = Assert.Single(Of(Feu));
        Assert.False(feu.IsAllDay);
        Assert.Equal(new DateOnly(2026, 7, 14), feu.LocalDate);
        Assert.Equal(new TimeOnly(23, 30), feu.LocalStart);
        Assert.Equal(new TimeOnly(0, 30), feu.LocalEnd);
        Assert.Equal(new DateOnly(2026, 7, 15), feu.LocalEndDate);
        Assert.Equal(new DateTime(2026, 7, 14, 21, 30, 0, DateTimeKind.Utc), feu.OccurrenceStart);
    }

    [Fact]
    public void HeureFlottante_LueCommeHeureDuMenage()
    {
        var flottant = Assert.Single(Of(Flottant));
        Assert.Equal(new TimeOnly(19, 0), flottant.LocalStart);
        Assert.Equal(new DateTime(2026, 11, 20, 18, 0, 0, DateTimeKind.Utc), flottant.OccurrenceStart);
    }

    [Fact]
    public void SerieAnnuelle_DeuxOccurrencesDansLaFenetre()
    {
        var anniv = Of(Anniversaire);
        Assert.Equal(2, anniv.Count);
        Assert.All(anniv, o => Assert.Equal(CalendarRecurrence.Yearly, o.Recurrence));
        Assert.All(anniv, o => Assert.True(o.IsAllDay));
        Assert.Equal(new DateOnly(2026, 8, 20), anniv[0].LocalDate);
        Assert.Equal(new DateOnly(2027, 8, 20), anniv[1].LocalDate);
    }

    [Fact]
    public void Description_NullePartDansLaSortie()
    {
        // Ni dans un champ, ni dans le type : CalendarOccurrence n'a pas de propriété qui la porterait.
        var json = JsonSerializer.Serialize(Expanded.Value.Occurrences);
        Assert.DoesNotContain("SECRETDESCRIPTION", json);
        Assert.DoesNotContain("Ordre du jour", json);
        Assert.DoesNotContain("exemple.invalid", json);
        Assert.DoesNotContain(typeof(CalendarOccurrence).GetProperties(), p => p.Name.Contains("Description", StringComparison.OrdinalIgnoreCase));

        var reunion = Assert.Single(Of(Reunion));
        Assert.Equal("Réunion parents d'élèves", reunion.Summary);
        Assert.Equal("École du village", reunion.Location);
        Assert.Equal(new TimeOnly(19, 30), reunion.LocalStart);
    }

    [Fact]
    public void LieuVide_DevientNull_EtLeTotalEstCoherent()
    {
        Assert.Null(Of(Piscine)[0].Location);
        Assert.Equal("Salle des fêtes", Of(Danse)[0].Location);
        Assert.Equal(27, Expanded.Value.Occurrences.Count);
    }

    [Fact]
    public void AucunDoublon_SurLaCleUnique()
    {
        var keys = Expanded.Value.Occurrences.Select(o => (o.Uid, o.OccurrenceStart)).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Fact]
    public void FenetreVide_EstRefusee()
    {
        Assert.Throws<ArgumentException>(() =>
            IcsOccurrenceExpander.Expand(AgendaTestSupport.FamilleIcs(), ToUtc, FromUtc, AgendaTestSupport.Brussels, AgendaTestSupport.Options()));
    }

    [Fact]
    public void FenetreEtroite_NeRendQueCeQuElleContient()
    {
        var from = new DateTime(2026, 9, 9, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 9, 16, 0, 0, 0, DateTimeKind.Utc);
        var result = IcsOccurrenceExpander.Expand(AgendaTestSupport.FamilleIcs(), from, to, AgendaTestSupport.Brussels, AgendaTestSupport.Options());
        Assert.Equal(2, result.Occurrences.Count);
        Assert.Contains(result.Occurrences, o => o.Uid == Danse && o.LocalDate == new DateOnly(2026, 9, 10));
        Assert.Contains(result.Occurrences, o => o.Uid == Reunion && o.LocalDate == new DateOnly(2026, 9, 15));
    }

    [Fact]
    public void PlafondParEvenement_TronqueLaSerie_EtLeDit()
    {
        var result = IcsOccurrenceExpander.Expand(AgendaTestSupport.FamilleIcs(), FromUtc, ToUtc, AgendaTestSupport.Brussels,
            AgendaTestSupport.Options(o => o.MaxOccurrencesPerEvent = 5));
        // La serie maitre est coupee a cinq (3, 10, 17, 24 septembre, 1er octobre), le 1er octobre est
        // remplace par l'exception du 3 : quatre regulieres plus une exception.
        Assert.Equal(4, result.Occurrences.Count(o => o.Uid == Danse && !o.IsException));
        Assert.Equal(5, result.Occurrences.Count(o => o.Uid == Danse));
        Assert.Equal(5, result.Occurrences.Count(o => o.Uid == Piscine));
        Assert.NotNull(result.Warning);
        Assert.Contains("tronquée à 5", result.Warning);
        Assert.True(result.Warning!.Length <= IcsOccurrenceExpander.WarningMaxLength);
    }

    [Fact]
    public void PlafondGlobal_TronqueLeCalendrier_EtLeDit()
    {
        var result = IcsOccurrenceExpander.Expand(AgendaTestSupport.FamilleIcs(), FromUtc, ToUtc, AgendaTestSupport.Brussels,
            AgendaTestSupport.Options(o => o.MaxOccurrences = 3));
        Assert.Equal(3, result.Occurrences.Count);
        Assert.StartsWith("Calendrier tronqué à 3 occurrences.", result.Warning);
    }

    [Fact]
    public void FluxSansCalendrier_Leve()
    {
        Assert.ThrowsAny<Exception>(() =>
            IcsOccurrenceExpander.Expand("<html>pas un calendrier</html>", FromUtc, ToUtc, AgendaTestSupport.Brussels, AgendaTestSupport.Options()));
    }

    [Fact]
    public void RegleDeRecurrencePathologique_EstIgnoreeAvecAvertissement_LesAutresPassent()
    {
        const string ics = """
            BEGIN:VCALENDAR
            VERSION:2.0
            PRODID:-//Test//FR
            BEGIN:VEVENT
            UID:impossible@test.invalid
            DTSTART;TZID=Europe/Brussels:20260901T100000
            DTEND;TZID=Europe/Brussels:20260901T110000
            RRULE:FREQ=MONTHLY;BYMONTHDAY=31;BYMONTH=2
            SUMMARY:Jamais le 31 février
            END:VEVENT
            BEGIN:VEVENT
            UID:normal@test.invalid
            DTSTART;TZID=Europe/Brussels:20260920T100000
            DTEND;TZID=Europe/Brussels:20260920T110000
            SUMMARY:Rendez-vous normal
            END:VEVENT
            END:VCALENDAR
            """;
        var result = IcsOccurrenceExpander.Expand(ics, FromUtc, ToUtc, AgendaTestSupport.Brussels, AgendaTestSupport.Options());
        Assert.Single(result.Occurrences);
        Assert.Equal("normal@test.invalid", result.Occurrences[0].Uid);
        Assert.NotNull(result.Warning);
        Assert.Contains("Jamais le 31 février", result.Warning);
    }
}
