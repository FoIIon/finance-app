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

    /// <summary>Seuil de péremption d'une valorisation saisie à la main.</summary>
    private static readonly TimeSpan ManualStaleThreshold = TimeSpan.FromDays(30);

    /// <summary>Seuil de péremption d'une valorisation automatique.</summary>
    private static readonly TimeSpan AutomaticStaleThreshold = TimeSpan.FromHours(48);

    /// <summary>
    /// Rendement annualisé approximatif (CAGR). Renvoie null dans tous les cas où le chiffre
    /// ne serait pas fondé : pas de date d'entrée, pas de valorisation, coût de revient nul,
    /// ou détention de moins d'un an (annualiser une durée courte produit un chiffre absurde).
    /// Le TRI exact viendra avec l'historique de mouvements, au lot Trade Republic.
    /// </summary>
    public static decimal? ComputeCagr(decimal costBasis, decimal? marketValue, DateTime? firstPurchaseDate, DateTime asOf)
    {
        if (firstPurchaseDate is null) return null;
        if (marketValue is null) return null;
        if (costBasis <= 0m) return null;

        var years = (asOf - firstPurchaseDate.Value).TotalDays / 365.25;
        if (years < 1.0) return null;

        var ratio = (double)(marketValue.Value / costBasis);
        if (ratio <= 0) return null;

        var cagr = Math.Pow(ratio, 1.0 / years) - 1.0;
        return (decimal)(cagr * 100.0);
    }

    /// <summary>
    /// Une valorisation périmée doit se voir périmée. Le seuil dépend de la source :
    /// 30 jours pour une saisie manuelle, 48 heures pour une source automatique.
    /// </summary>
    public static bool IsStale(ValuationSource source, DateTime asOf, DateTime now)
    {
        var threshold = source == ValuationSource.Manual ? ManualStaleThreshold : AutomaticStaleThreshold;
        return now - asOf > threshold;
    }
}
