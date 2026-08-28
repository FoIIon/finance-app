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
    int LinesIncluded,
    /// <summary>Vrai si le point vient de la série du portefeuille (quantités du jour, ventes comprises), faux s'il est reconstitué depuis les lignes actuelles.</summary>
    bool Reconstructed = false);

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
        IReadOnlyList<(DateTime AsOf, decimal Value, decimal? Invested)> trSeries,
        IReadOnlyList<PortfolioHistoryPoint> otherLines,
        IReadOnlyList<PortfolioHistoryPoint> allLines)
    {
        if (trSeries.Count == 0) return allLines.ToList();

        var tr = trSeries.OrderBy(p => p.AsOf).ToList();
        var lastTr = tr[^1].AsOf;
        var others = otherLines.OrderBy(p => p.AsOf).ToList();

        var result = new List<PortfolioHistoryPoint>(tr.Count + 4);
        var oi = -1;
        foreach (var (asOf, value, invested) in tr)
        {
            while (oi + 1 < others.Count && others[oi + 1].AsOf <= asOf) oi++;
            var autres = oi >= 0 ? others[oi] : null;
            // Investi : celui de la série TR (net, ventes déduites) plus le coût des autres lignes.
            // Inconnu côté TR → inconnu tout court, on n'additionne pas un chiffre à un trou.
            decimal? investi = invested.HasValue ? invested.Value + (autres?.Invested ?? 0m) : null;
            result.Add(new PortfolioHistoryPoint(
                asOf,
                value + (autres?.Value ?? 0m),
                investi,
                autres?.LinesIncluded ?? 0,
                Reconstructed: true));
        }

        foreach (var p in allLines.Where(p => p.AsOf > lastTr).OrderBy(p => p.AsOf))
            result.Add(p);

        return result;
    }

    /// <summary>Mouvement de titres tel que la timeline Trade Republic le donne : montant signé en euros (achat négatif, vente positive), sans quantité.</summary>
    public record TimelineMovement(string Isin, DateTime Date, decimal Amount, bool IsOpening = false);

    /// <summary>Point de la valeur reconstruite du portefeuille.</summary>
    public record ReconstructedPoint(DateTime AsOf, decimal Value, decimal Invested);

    /// <summary>Quantité et cours retenus pour un mouvement, pour l'écrire en InvestmentMovement.</summary>
    public record MovementFill(TimelineMovement Movement, decimal Quantity, decimal UnitPrice);

    /// <summary>
    /// Rebâtit la valeur du portefeuille jour par jour depuis le premier mouvement.
    ///
    /// La timeline ne donne que le montant en euros de chaque ordre. La quantité exécutée est
    /// donc déduite du cours de clôture du jour : quantité = |montant| / cours. Une approximation
    /// (le cours d'exécution n'est pas la clôture), acceptée parce qu'elle porte sur chaque ordre
    /// séparément et ne dérive pas dans le temps. Valeur(t) = Σ quantité détenue(t) × dernier
    /// cours connu(t). Investi(t) = Σ achats − Σ ventes jusqu'à t : l'écart valeur − investi est
    /// le résultat total, pertes et gains réalisés compris, ce que la plus-value latente ne dit pas.
    ///
    /// Un ISIN sans aucun cours est compté dans Investi mais pas dans Valeur, et signalé dans
    /// IsinsSansCours : mieux vaut une valeur visiblement incomplète qu'une valeur inventée.
    /// Une quantité qui passerait sous zéro (vente approchée plus grosse que la position) est
    /// ramenée à zéro.
    /// </summary>
    public static (List<ReconstructedPoint> Points, List<MovementFill> Fills, List<string> IsinsSansCours) ReconstructPortfolioHistory(
        IReadOnlyList<TimelineMovement> movements,
        IReadOnlyDictionary<string, IReadOnlyList<(DateTime AsOf, decimal Close)>> prices,
        DateTime until)
    {
        var points = new List<ReconstructedPoint>();
        var fills = new List<MovementFill>();
        var sansCours = new List<string>();
        if (movements.Count == 0) return (points, fills, sansCours);
        return Reconstruct(movements, prices, until);
    }

    /// <summary>
    /// Même reconstruction, calibrée sur les quantités réellement détenues aujourd'hui.
    ///
    /// Pourquoi (28/08/2026) : Trade Republic borne sa timeline au 24/11/2023 (curseur « after »
    /// nul à la page 56), et une part du portefeuille a été achetée avant : 1,3 ETH, 3 612 DOGE,
    /// du Bitcoin. Sans ces achats, la courbe finissait à 79 600 € contre 89 800 € réels. Pour
    /// chaque ligne détenue, l'écart entre la quantité réelle (connue de TR) et la quantité rebâtie
    /// devient une position d'ouverture au premier jour de la série, valorisée au cours de ce jour :
    /// achetée comme si elle l'avait été ce jour-là, sans plus-value antérieure (inconnue). L'écart
    /// peut être négatif de quelques pour cent (approximation par le cours de clôture), il se
    /// corrige de la même façon. Les positions vendues ne sont pas calibrables et restent telles
    /// quelles. Les ouvertures sont marquées IsOpening dans les Fills : elles ne sont pas des
    /// mouvements réels et ne s'écrivent pas comme tels.
    /// </summary>
    public static (List<ReconstructedPoint> Points, List<MovementFill> Fills, List<string> IsinsSansCours, List<TimelineMovement> Openings) ReconstructPortfolioHistoryCalibrated(
        IReadOnlyList<TimelineMovement> movements,
        IReadOnlyDictionary<string, IReadOnlyList<(DateTime AsOf, decimal Close)>> prices,
        IReadOnlyDictionary<string, decimal> heldQuantities,
        DateTime until)
    {
        var openings = new List<TimelineMovement>();
        if (movements.Count == 0) return ([], [], [], openings);

        var (_, fills0, _) = Reconstruct(movements, prices, until);
        var reconFinal = fills0.GroupBy(f => f.Movement.Isin).ToDictionary(g => g.Key, g => g.Sum(f => f.Quantity));
        var start = movements.Min(m => m.Date).Date;

        foreach (var (isin, held) in heldQuantities)
        {
            var recon = reconFinal.GetValueOrDefault(isin, 0m);
            var ecart = held - recon;
            if (Math.Abs(ecart) < 0.000001m) continue;
            if (!prices.TryGetValue(isin, out var serie)) continue;
            var close = serie.Where(p => p.Close > 0 && p.AsOf.Date <= start).OrderBy(p => p.AsOf).LastOrDefault();
            if (close == default) close = serie.Where(p => p.Close > 0).OrderBy(p => p.AsOf).FirstOrDefault();
            if (close == default) continue;
            openings.Add(new TimelineMovement(isin, start, -ecart * close.Close, IsOpening: true));
        }

        var all = openings.Concat(movements).ToList();
        var (points, fills, sansCours) = Reconstruct(all, prices, until);
        return (points, fills, sansCours, openings);
    }

    private static (List<ReconstructedPoint> Points, List<MovementFill> Fills, List<string> IsinsSansCours) Reconstruct(
        IReadOnlyList<TimelineMovement> movements,
        IReadOnlyDictionary<string, IReadOnlyList<(DateTime AsOf, decimal Close)>> prices,
        DateTime until)
    {
        var points = new List<ReconstructedPoint>();
        var fills = new List<MovementFill>();
        var sansCours = new List<string>();

        var series = prices.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.Where(p => p.Close > 0).OrderBy(p => p.AsOf).ToList());

        var ordered = movements.OrderBy(m => m.Date).ThenBy(m => m.IsOpening ? 1 : 0).ToList();
        var first = ordered[0].Date.Date;
        until = until.Date;

        // Dates de la courbe : chaque jour où au moins un cours existe, plus les jours de mouvement.
        var dates = series.Values.SelectMany(l => l.Select(p => p.AsOf.Date))
            .Concat(ordered.Select(m => m.Date.Date))
            .Where(d => d >= first && d <= until)
            .Distinct()
            .OrderBy(d => d)
            .ToList();

        var qty = new Dictionary<string, decimal>();
        var cursor = new Dictionary<string, int>();
        var invested = 0m;
        var mi = 0;

        decimal? CloseAt(string isin, DateTime t)
        {
            if (!series.TryGetValue(isin, out var l) || l.Count == 0) return null;
            var c = cursor.GetValueOrDefault(isin, -1);
            while (c + 1 < l.Count && l[c + 1].AsOf.Date <= t) c++;
            cursor[isin] = c;
            return c >= 0 ? l[c].Close : null;
        }

        foreach (var t in dates)
        {
            while (mi < ordered.Count && ordered[mi].Date.Date <= t)
            {
                var m = ordered[mi++];
                invested += -m.Amount;
                var close = CloseAt(m.Isin, m.Date.Date);
                if (close is null || close.Value <= 0)
                {
                    if (!sansCours.Contains(m.Isin)) sansCours.Add(m.Isin);
                    continue;
                }
                var q = -m.Amount / close.Value;
                var nouvelle = qty.GetValueOrDefault(m.Isin) + q;
                if (nouvelle < 0) { q -= nouvelle; nouvelle = 0; }
                qty[m.Isin] = nouvelle;
                fills.Add(new MovementFill(m, q, close.Value));
            }

            var value = 0m;
            foreach (var (isin, q) in qty)
            {
                if (q <= 0) continue;
                var close = CloseAt(isin, t);
                if (close.HasValue) value += q * close.Value;
            }

            points.Add(new ReconstructedPoint(t, Math.Round(value, 2), Math.Round(invested, 2)));
        }

        return (points, fills, sansCours);
    }
}
