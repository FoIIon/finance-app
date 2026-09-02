using FinanceApp.API.Models;
using FinanceApp.API.Services.Reporting;
using Xunit;

namespace FinanceApp.Tests;

/// <summary>Le reste du mois, jour par jour, et sa projection.</summary>
public class BurndownBuilderTests
{
    static readonly DateTime Now = new(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc);
    static readonly IReadOnlyList<ReportLine> Rien = Array.Empty<ReportLine>();
    static readonly IReadOnlyList<RecurringTransaction> AucuneRecurrente = Array.Empty<RecurringTransaction>();
    static readonly IReadOnlySet<int> AucuneProvision = new HashSet<int>();

    static ReportLine L(TransactionType type, decimal montant, int jour, bool transfert = false, bool fixe = false, bool remboursement = false, bool horsBilan = false) =>
        new()
        {
            Date = new DateTime(2026, 8, jour),
            Type = type,
            Amount = montant,
            CategoryId = 1,
            CategoryName = "Cat",
            IsTransfer = transfert,
            IsFixed = fixe,
            IsRefund = remboursement,
            ExcludeFromMonthlyReport = horsBilan,
        };

    [Fact]
    public void LeReste_EstEntreesMoinsDepenses_EtLesJoursFutursSontVides()
    {
        var mois = new[]
        {
            L(TransactionType.Income, 2000m, 1),
            L(TransactionType.Expense, 500m, 10),
            L(TransactionType.Expense, 400m, 7, transfert: true),        // mise de côté : hors du reste
            L(TransactionType.Expense, 1200m, 7, transfert: true, horsBilan: true),
        };

        var b = BurndownBuilder.Build(2026, 8, Now, mois, Rien, AucuneRecurrente, AucuneProvision);

        Assert.Equal(31, b.Days.Count);
        Assert.Equal(2000m, b.Days[0].Remaining);
        Assert.Equal(1500m, b.Days[9].Remaining);
        Assert.Equal(1500m, b.Days[14].Remaining);
        Assert.Null(b.Days[15].Remaining);
        Assert.Equal(1500m, b.RemainingToday);
        Assert.Equal(16, b.DaysRemaining);
        Assert.Equal(15, b.TodayDay);
        Assert.False(b.IsPast);
    }

    [Fact]
    public void UnRemboursement_ReduitLesDepenses_SansChangerLeReste()
    {
        var mois = new[]
        {
            L(TransactionType.Income, 2000m, 1),
            L(TransactionType.Expense, 271.50m, 6),
            L(TransactionType.Income, 271.50m, 10, remboursement: true),
        };

        var b = BurndownBuilder.Build(2026, 8, Now, mois, Rien, AucuneRecurrente, AucuneProvision);

        Assert.Equal(2000m, b.RemainingToday);
        Assert.Equal(0m, b.Days[14].Spent);
        Assert.Equal(2000m, b.Days[14].Income);
    }

    [Fact]
    public void LaProjection_SoustraitLeRythmeVariable_EtLesRecurrentesAVenir()
    {
        var mois = new[] { L(TransactionType.Income, 2000m, 1) };
        // 140 € de variable sur 14 jours → 10 € par jour. Une charge fixe dans la fenêtre ne compte pas.
        var rythme = new[]
        {
            L(TransactionType.Expense, 140m, 12),
            L(TransactionType.Expense, 900m, 3, fixe: true),
        };
        var recurrentes = new[]
        {
            new RecurringTransaction { Id = 1, Type = TransactionType.Expense, Amount = 100m, Frequency = RecurringFrequency.Monthly, DayOfMonth = 25, StartDate = new DateOnly(2026, 1, 25) },
            new RecurringTransaction { Id = 2, Type = TransactionType.Income, Amount = 60m, Frequency = RecurringFrequency.Monthly, DayOfMonth = 28, StartDate = new DateOnly(2026, 1, 28) },
            // Déjà passée ce mois-ci : pas « à venir ».
            new RecurringTransaction { Id = 3, Type = TransactionType.Expense, Amount = 999m, Frequency = RecurringFrequency.Monthly, DayOfMonth = 5, StartDate = new DateOnly(2026, 1, 5) },
        };

        var b = BurndownBuilder.Build(2026, 8, Now, mois, rythme, recurrentes, AucuneProvision);

        Assert.Equal(10m, b.DailyPaceVariable);
        Assert.Equal(100m, b.UpcomingRecurringExpenses);
        Assert.Equal(60m, b.UpcomingRecurringIncome);
        // 2000 − 10 × 16 jours − 100 + 60
        Assert.Equal(1800m, b.ProjectedEndOfMonth);
    }

    [Fact]
    public void UneRecurrenteProvisionnee_NestPasAVenir()
    {
        var recurrentes = new[]
        {
            new RecurringTransaction { Id = 1, Type = TransactionType.Income, Amount = 3000m, Frequency = RecurringFrequency.Monthly, DayOfMonth = 28, StartDate = new DateOnly(2026, 1, 28), ProvisionAtMonthStart = true },
            new RecurringTransaction { Id = 2, Type = TransactionType.Expense, Amount = 50m, Frequency = RecurringFrequency.Monthly, DayOfMonth = 28, StartDate = new DateOnly(2026, 1, 28) },
        };
        var provisionnees = new HashSet<int> { 2 };

        var b = BurndownBuilder.Build(2026, 8, Now, Rien, Rien, recurrentes, provisionnees);

        Assert.Equal(0m, b.UpcomingRecurringIncome);
        Assert.Equal(0m, b.UpcomingRecurringExpenses);
    }

    [Fact]
    public void UnMoisPasse_ProjetteSaValeurFinale_SansJourCourant()
    {
        var mois = new[] { L(TransactionType.Income, 1000m, 2), L(TransactionType.Expense, 300m, 20) };
        var recurrentes = new[]
        {
            new RecurringTransaction { Id = 1, Type = TransactionType.Expense, Amount = 100m, Frequency = RecurringFrequency.Monthly, DayOfMonth = 25, StartDate = new DateOnly(2026, 1, 25) },
        };

        var b = BurndownBuilder.Build(2026, 7, Now, mois, Rien, recurrentes, AucuneProvision);

        Assert.True(b.IsPast);
        Assert.Null(b.TodayDay);
        Assert.Equal(0, b.DaysRemaining);
        Assert.Equal(700m, b.RemainingToday);
        Assert.Equal(700m, b.ProjectedEndOfMonth);
        Assert.Equal(0m, b.UpcomingRecurringExpenses);
        Assert.All(b.Days, d => Assert.NotNull(d.Remaining));
    }

    [Fact]
    public void UnMoisFutur_NaAucunJourRealise()
    {
        var b = BurndownBuilder.Build(2026, 9, Now, Rien, Rien, AucuneRecurrente, AucuneProvision);

        Assert.False(b.IsPast);
        Assert.Null(b.TodayDay);
        Assert.Equal(30, b.DaysRemaining);
        Assert.All(b.Days, d => Assert.Null(d.Remaining));
    }

    [Fact]
    public void Mensuelle_LeJourEstRameneAuDernierJourDuMois()
    {
        var r = new RecurringTransaction { Frequency = RecurringFrequency.Monthly, DayOfMonth = 31, StartDate = new DateOnly(2026, 1, 31) };

        var jours = BurndownBuilder.RecurringOccurrenceDays(r, 2026, 4, 1, 30).ToList();

        Assert.Equal(new[] { 30 }, jours);
    }

    [Fact]
    public void Hebdomadaire_TombeTousLesSeptJoursDepuisLeDebut()
    {
        var r = new RecurringTransaction { Frequency = RecurringFrequency.Weekly, StartDate = new DateOnly(2026, 8, 3) };

        var jours = BurndownBuilder.RecurringOccurrenceDays(r, 2026, 8, 1, 31).ToList();

        Assert.Equal(new[] { 3, 10, 17, 24, 31 }, jours);
    }

    [Fact]
    public void Annuelle_NeTombeQueLeMoisAnniversaire_EtRespecteLaFin()
    {
        var r = new RecurringTransaction { Frequency = RecurringFrequency.Yearly, StartDate = new DateOnly(2024, 8, 12), EndDate = new DateOnly(2026, 8, 1) };

        Assert.Empty(BurndownBuilder.RecurringOccurrenceDays(r, 2026, 7, 1, 31));
        Assert.Empty(BurndownBuilder.RecurringOccurrenceDays(r, 2026, 8, 1, 31)); // finie le 01/08
        r.EndDate = null;
        Assert.Equal(new[] { 12 }, BurndownBuilder.RecurringOccurrenceDays(r, 2026, 8, 1, 31));
    }
}
