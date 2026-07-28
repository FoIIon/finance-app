using FinanceApp.API.Models;

namespace FinanceApp.API.Services;

/// <summary>
/// Calculs de performance des investissements. Volontairement pur : aucune dépendance
/// au DbContext, pour que les règles restent testables unitairement. Ces règles portent
/// le risque d'erreur silencieuse (un chiffre faux reste plausible à l'œil).
/// </summary>
public static class InvestmentCalculator
{
    /// <summary>
    /// Prix de revient unitaire. Null pour un contrat d'assurance-vie (quantité de 1 par
    /// convention) et null si la quantité est nulle.
    /// </summary>
    public static decimal? ComputeUnitCost(InvestmentKind kind, decimal costBasis, decimal quantity)
    {
        if (kind == InvestmentKind.InsuranceContract) return null;
        if (quantity == 0m) return null;
        return costBasis / quantity;
    }

    /// <summary>
    /// Plus-value latente en euros et en pourcentage. Le pourcentage est null quand le
    /// coût de revient est nul, cas où il n'a pas de sens.
    /// </summary>
    public static (decimal? Amount, decimal? Percent) ComputeGain(decimal costBasis, decimal? marketValue)
    {
        if (marketValue is null) return (null, null);

        var amount = marketValue.Value - costBasis;
        var percent = costBasis == 0m ? (decimal?)null : amount / costBasis * 100m;
        return (amount, percent);
    }
}
