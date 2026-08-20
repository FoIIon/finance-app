using FinanceApp.API.Models;
using FinanceApp.API.Services;
using Xunit;

namespace FinanceApp.Tests;

public class InvestmentCalculatorTests
{
    [Fact]
    public void ComputeUnitCost_Security_DivisesCostBasisByQuantity()
    {
        var result = InvestmentCalculator.ComputeUnitCost(InvestmentKind.Security, 1000m, 8m);
        Assert.Equal(125m, result);
    }

    [Fact]
    public void ComputeUnitCost_InsuranceContract_ReturnsNull()
    {
        // Un contrat a une quantité de 1 par convention : le PRU se confondrait
        // avec le montant versé sans rien apprendre.
        var result = InvestmentCalculator.ComputeUnitCost(InvestmentKind.InsuranceContract, 5000m, 1m);
        Assert.Null(result);
    }

    [Fact]
    public void ComputeUnitCost_ZeroQuantity_ReturnsNull()
    {
        var result = InvestmentCalculator.ComputeUnitCost(InvestmentKind.Metal, 3000m, 0m);
        Assert.Null(result);
    }

    [Fact]
    public void ComputeUnitCost_FractionalQuantity_KeepsPrecision()
    {
        // Les quantités Trade Republic descendent à six décimales.
        var result = InvestmentCalculator.ComputeUnitCost(InvestmentKind.Security, 100m, 0.512345m);
        Assert.Equal(195.18m, Math.Round(result!.Value, 2));
    }

    [Fact]
    public void ComputeGain_PositiveGain_ReturnsAmountAndPercent()
    {
        var (amount, percent) = InvestmentCalculator.ComputeGain(1000m, 1250m);
        Assert.Equal(250m, amount);
        Assert.Equal(25m, percent);
    }

    [Fact]
    public void ComputeGain_Loss_ReturnsNegativeValues()
    {
        var (amount, percent) = InvestmentCalculator.ComputeGain(1000m, 800m);
        Assert.Equal(-200m, amount);
        Assert.Equal(-20m, percent);
    }

    [Fact]
    public void ComputeGain_NoValuation_ReturnsNulls()
    {
        var (amount, percent) = InvestmentCalculator.ComputeGain(1000m, null);
        Assert.Null(amount);
        Assert.Null(percent);
    }

    [Fact]
    public void ComputeGain_ZeroCostBasis_ReturnsAmountButNoPercent()
    {
        // Une ligne reçue en donation a un coût nul : le pourcentage n'a pas de sens,
        // le gain en euros si.
        var (amount, percent) = InvestmentCalculator.ComputeGain(0m, 500m);
        Assert.Equal(500m, amount);
        Assert.Null(percent);
    }

    [Fact]
    public void ComputeCagr_NoFirstPurchaseDate_ReturnsNull()
    {
        // Règle non négociable de la spec : pas de date d'entrée, pas de rendement.
        // Une case vide vaut mieux qu'un chiffre reposant sur une hypothèse invisible.
        var result = InvestmentCalculator.ComputeCagr(1000m, 1500m, null, new DateTime(2026, 7, 28));
        Assert.Null(result);
    }

    [Fact]
    public void ComputeCagr_HoldingShorterThanOneYear_ReturnsNull()
    {
        // Annualiser six mois de détention produit un chiffre spectaculaire et faux.
        var result = InvestmentCalculator.ComputeCagr(
            1000m, 1200m, new DateTime(2026, 3, 1), new DateTime(2026, 7, 28));
        Assert.Null(result);
    }

    [Fact]
    public void ComputeCagr_TwoYearsDoubling_ReturnsAboutFortyOnePercent()
    {
        // 1000 qui devient 2000 sur deux ans. L'année conventionnelle de 365,25 jours donne 41,45 %.
        var result = InvestmentCalculator.ComputeCagr(
            1000m, 2000m, new DateTime(2024, 7, 28), new DateTime(2026, 7, 28));
        Assert.NotNull(result);
        Assert.Equal(41.45m, Math.Round(result!.Value, 2));
    }

    [Fact]
    public void ComputeCagr_NoValuation_ReturnsNull()
    {
        var result = InvestmentCalculator.ComputeCagr(1000m, null, new DateTime(2020, 1, 1), new DateTime(2026, 7, 28));
        Assert.Null(result);
    }

    [Fact]
    public void ComputeCagr_ZeroOrNegativeCostBasis_ReturnsNull()
    {
        var result = InvestmentCalculator.ComputeCagr(0m, 500m, new DateTime(2020, 1, 1), new DateTime(2026, 7, 28));
        Assert.Null(result);
    }

    [Fact]
    public void IsStale_ManualWithinThirtyDays_IsFresh()
    {
        var now = new DateTime(2026, 7, 28);
        Assert.False(InvestmentCalculator.IsStale(ValuationSource.Manual, now.AddDays(-29), now));
    }

    [Fact]
    public void IsStale_ManualBeyondThirtyDays_IsStale()
    {
        var now = new DateTime(2026, 7, 28);
        Assert.True(InvestmentCalculator.IsStale(ValuationSource.Manual, now.AddDays(-31), now));
    }

    [Fact]
    public void IsStale_AutomaticBeyondFortyEightHours_IsStale()
    {
        var now = new DateTime(2026, 7, 28);
        Assert.True(InvestmentCalculator.IsStale(ValuationSource.SpotApi, now.AddHours(-49), now));
        Assert.False(InvestmentCalculator.IsStale(ValuationSource.SpotApi, now.AddHours(-47), now));
    }
}
