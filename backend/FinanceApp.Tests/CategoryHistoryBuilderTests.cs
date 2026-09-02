using System.Globalization;
using FinanceApp.API.Models;
using FinanceApp.API.Services;
using FinanceApp.API.Services.Reporting;
using Xunit;

namespace FinanceApp.Tests;

/// <summary>L'historique mensuel d'une catégorie, dans ses deux projections, sur les mêmes blocs.</summary>
public class CategoryHistoryBuilderTests
{
    static readonly CultureInfo Fr = new("fr-FR");

    static ReportLine L(TransactionType type, decimal montant, DateTime date, bool transfert = false, bool fixe = false, bool remboursement = false, bool exceptionnel = false, bool horsBilan = false) =>
        new()
        {
            Date = date,
            Type = type,
            Amount = montant,
            CategoryId = 4,
            CategoryName = "Loisirs",
            IsTransfer = transfert,
            IsFixed = fixe,
            IsRefund = remboursement,
            IsExceptional = exceptionnel,
            ExcludeFromMonthlyReport = horsBilan,
        };

    [Fact]
    public void LHistoriqueDesDepenses_NetteLesRemboursements_CommeLeTableau()
    {
        // Août 2026 sur Loisirs : 271,50 avancés le 06/08, rendus le 10/08, plus 5,00 et 3,00 à Troyes.
        // Le tableau affiche 8,00. Les barres affichaient 279,50.
        var lignes = new[]
        {
            L(TransactionType.Expense, 271.50m, new DateTime(2026, 8, 6)),
            L(TransactionType.Income, 271.50m, new DateTime(2026, 8, 10), remboursement: true),
            L(TransactionType.Expense, 5.00m, new DateTime(2026, 8, 18)),
            L(TransactionType.Expense, 3.00m, new DateTime(2026, 8, 18)),
        };

        var h = CategoryHistoryBuilder.ExpenseHistory(new DateTime(2026, 8, 1), 1, lignes, Fr);

        var aout = Assert.Single(h);
        Assert.Equal("2026-08", aout.Month);
        Assert.Equal(8.00m, aout.Total);
        Assert.Equal(8.00m, aout.CurrentTotal);
        Assert.Equal(0m, aout.ExceptionalTotal);
    }

    [Fact]
    public void LesMoisSansMouvement_SontRendusAZero_DansLOrdre()
    {
        var lignes = new[] { L(TransactionType.Expense, 40m, new DateTime(2026, 7, 3)) };

        var h = CategoryHistoryBuilder.ExpenseHistory(new DateTime(2026, 6, 1), 3, lignes, Fr);

        Assert.Equal(new[] { "2026-06", "2026-07", "2026-08" }, h.Select(m => m.Month));
        Assert.Equal(new[] { 0m, 40m, 0m }, h.Select(m => m.Total));
    }

    [Fact]
    public void LExceptionnel_EstSepareDuCourant()
    {
        var lignes = new[]
        {
            L(TransactionType.Expense, 1800m, new DateTime(2026, 8, 2), exceptionnel: true),
            L(TransactionType.Expense, 120m, new DateTime(2026, 8, 9)),
        };

        var h = CategoryHistoryBuilder.ExpenseHistory(new DateTime(2026, 8, 1), 1, lignes, Fr);

        Assert.Equal(1920m, h[0].Total);
        Assert.Equal(120m, h[0].CurrentTotal);
        Assert.Equal(1800m, h[0].ExceptionalTotal);
    }

    [Fact]
    public void LesEntreesEtLesMisesDeCote_NeSontPasDesDepenses()
    {
        var lignes = new[]
        {
            L(TransactionType.Income, 578m, new DateTime(2026, 8, 5)),
            L(TransactionType.Expense, 400m, new DateTime(2026, 8, 7), transfert: true),
            L(TransactionType.Expense, 220m, new DateTime(2026, 8, 12), fixe: true),
        };

        var h = CategoryHistoryBuilder.ExpenseHistory(new DateTime(2026, 8, 1), 1, lignes, Fr);

        Assert.Equal(220m, h[0].Total);
    }

    [Fact]
    public void LeFluxParMois_RendLesTroisSens_AvecLesMemesBlocs()
    {
        var lignes = new[]
        {
            L(TransactionType.Income, 578m, new DateTime(2026, 8, 5)),
            L(TransactionType.Expense, 220m, new DateTime(2026, 8, 12), fixe: true),
            L(TransactionType.Income, 40m, new DateTime(2026, 8, 20), fixe: true), // régularisation : réduit les sorties
            L(TransactionType.Expense, 400m, new DateTime(2026, 8, 7), transfert: true),
            L(TransactionType.Expense, 10m, new DateTime(2026, 7, 30)),
        };

        var flux = CategoryHistoryBuilder.FlowByMonth(lignes);

        var aout = flux[(2026, 8)];
        Assert.Equal(578m, aout.Income);
        Assert.Equal(180m, aout.Expenses);
        Assert.Equal(400m, aout.Savings);
        Assert.Equal(10m, flux[(2026, 7)].Expenses);
    }

    [Fact]
    public void UneCategorieHorsBilan_SAfficheEnMisesDeCote_SansPeserSurLeNet()
    {
        var lignes = new[] { L(TransactionType.Expense, 1837.12m, new DateTime(2026, 8, 7), transfert: true, horsBilan: true) };

        var aout = CategoryHistoryBuilder.FlowByMonth(lignes)[(2026, 8)];

        Assert.Equal(1837.12m, aout.Savings);
        Assert.Equal(0m, CategoryFlowHistory.Net(aout, offBalance: true));
    }
}
