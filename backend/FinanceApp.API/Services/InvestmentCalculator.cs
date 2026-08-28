using FinanceApp.API.Models;

namespace FinanceApp.API.Services;

/// <summary>
/// Ligne du portefeuille réduite à ce que la courbe du patrimoine exige : le coût de revient,
/// le fait d'être archivée, et ses valorisations datées. Découplée de l'entité pour que le
/// calcul reste pur.
/// </summary>
public record PortfolioLine(
    decimal CostBasis,
    bool IsArchived,
    IReadOnlyList<(DateTime AsOf, decimal MarketValue)> Valuations);

/// <summary>
/// Point de la courbe du patrimoine. LinesIncluded dit sur combien de lignes le point
/// repose : sans lui, une courbe qui monte parce qu'une ligne vient d'être valorisée
/// pour la première fois se lirait comme une performance.
/// </summary>
public record PortfolioHistoryPoint(
    DateTime AsOf,
    decimal Value,
    decimal? Invested,
    int LinesIncluded);

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

    /// <summary>
    /// Courbe agrégée du patrimoine investi. Un point par date de valorisation (union des
    /// dates de toutes les lignes, croissante, dédupliquée). Règles d'honnêteté :
    /// une ligne n'entre dans un point qu'à partir de sa première valorisation (jamais
    /// de valeur inventée avant la première mesure), sa dernière valeur connue est reportée
    /// entre deux mesures, et une ligne archivée cesse d'être reportée après sa dernière
    /// mesure (on ne traîne pas éternellement une position qu'on ne suit plus).
    /// Invested somme les coûts de revient des mêmes lignes que Value : comparer un investi
    /// total à une valeur partielle ferait mentir l'écart.
    /// </summary>
    public static List<PortfolioHistoryPoint> ComputePortfolioHistory(IReadOnlyList<PortfolioLine> lines)
    {
        // Une ligne jamais valorisée n'a rien à apporter à la courbe, à aucune date.
        var measuredLines = lines
            .Where(l => l.Valuations.Count > 0)
            .Select(l => (l.CostBasis, l.IsArchived, Valuations: l.Valuations.OrderBy(v => v.AsOf).ToList()))
            .ToList();

        var dates = measuredLines
            .SelectMany(l => l.Valuations.Select(v => v.AsOf))
            .Distinct()
            .OrderBy(d => d)
            .ToList();

        var result = new List<PortfolioHistoryPoint>(dates.Count);

        foreach (var t in dates)
        {
            var value = 0m;
            var invested = 0m;
            var included = 0;

            foreach (var line in measuredLines)
            {
                if (line.Valuations[0].AsOf > t) continue;
                if (line.IsArchived && t > line.Valuations[^1].AsOf) continue;

                var last = line.Valuations[0];
                foreach (var v in line.Valuations)
                {
                    if (v.AsOf > t) break;
                    last = v;
                }

                value += last.MarketValue;
                invested += line.CostBasis;
                included++;
            }

            result.Add(new PortfolioHistoryPoint(t, value, invested, included));
        }

        return result;
    }

    /// <summary>
    /// Fusionne la série réelle du portefeuille Trade Republic (valeur agrégée servie par TR, qui
    /// tient compte des quantités détenues chaque jour et des positions vendues) avec la courbe
    /// reconstituée des autres lignes (métaux, contrats, saisies manuelles).
    ///
    /// Jusqu'au dernier point TR : valeur = TR du jour + dernière valorisation connue des autres
    /// lignes. Au-delà (snapshot du jour pris après la clôture, par exemple) : la courbe
    /// reconstituée complète prend le relais. Invested est null sur les points portés par la
    /// série TR : on ne connaît pas le capital investi à ces dates, et un chiffre inventé
    /// (le coût de revient actuel) se lirait comme un écart de performance qu'il n'est pas.
    /// Sans série TR, la courbe reconstituée est renvoyée telle quelle.
    /// </summary>
    public static List<PortfolioHistoryPoint> MergeWithPortfolioSeries(
        IReadOnlyList<(DateTime AsOf, decimal Value)> trSeries,
        IReadOnlyList<PortfolioHistoryPoint> otherLines,
        IReadOnlyList<PortfolioHistoryPoint> allLines)
    {
        if (trSeries.Count == 0) return allLines.ToList();

        var tr = trSeries.OrderBy(p => p.AsOf).ToList();
        var lastTr = tr[^1].AsOf;
        var others = otherLines.OrderBy(p => p.AsOf).ToList();

        var result = new List<PortfolioHistoryPoint>(tr.Count + 4);
        var oi = -1;
        foreach (var (asOf, value) in tr)
        {
            while (oi + 1 < others.Count && others[oi + 1].AsOf <= asOf) oi++;
            var autres = oi >= 0 ? others[oi] : null;
            result.Add(new PortfolioHistoryPoint(
                asOf,
                value + (autres?.Value ?? 0m),
                null,
                autres?.LinesIncluded ?? 0));
        }

        foreach (var p in allLines.Where(p => p.AsOf > lastTr).OrderBy(p => p.AsOf))
            result.Add(p);

        return result;
    }
}
