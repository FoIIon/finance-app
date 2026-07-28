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
}
