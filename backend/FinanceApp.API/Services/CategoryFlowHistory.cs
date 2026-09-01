using FinanceApp.API.Models;

namespace FinanceApp.API.Services;

/// <summary>Une transaction réduite à ce dont l'historique deux sens a besoin.</summary>
public readonly record struct FlowLine(
    TransactionType Type,
    decimal Amount,
    bool IsTransfer,
    bool IsRefund);

/// <summary>Les trois sens d'un mois pour une catégorie.</summary>
public readonly record struct FlowTotals(decimal Income, decimal Expenses, decimal Savings);

/// <summary>
/// L'historique mensuel d'une catégorie dans les deux sens, avec les règles du bilan et pas d'autres.
///
/// Pourquoi ce service existe (01/09/2026) : l'onglet Entrées/Sorties nette les remboursements sur le
/// bloc de dépense de leur catégorie (voir <see cref="Refunds"/>), alors que l'endpoint
/// category-history n'agrège que les dépenses brutes. Un graphe branché sur le second sous une ligne
/// calculée par le premier afficherait deux chiffres différents pour le même mois. Les 271,50 € de
/// places de foot avancées le 07/08 et remboursées le 10/08 suffisent à créer l'écart.
///
/// La comparaison N-1 est bornée par la couverture bancaire. La timeline Trade Republic remonte à
/// novembre 2023, les comptes bancaires n'ont été connectés que le 30/01/2026 : sur Alimentation,
/// août 2025 pèse 422,60 € (carte seule) contre 1 165,22 € en août 2026 (carte + banque). Comparer les
/// deux annoncerait +176 % de courses là où c'est le périmètre qui a changé. Tant que le mois N-1
/// n'est pas entièrement couvert par la banque, il n'est pas rendu.
/// </summary>
public static class CategoryFlowHistory
{
    /// <summary>
    /// Mêmes règles que GetSummary : un transfert va aux mises de côté, un remboursement s'impute en
    /// négatif sur les dépenses, jamais en entrée.
    /// </summary>
    public static FlowTotals Aggregate(IEnumerable<FlowLine> lines)
    {
        decimal income = 0, expenses = 0, savings = 0;

        foreach (var l in lines)
        {
            if (l.IsTransfer)
                savings += l.Type == TransactionType.Expense ? l.Amount : -l.Amount;
            else if (Refunds.Applies(l.Type, l.IsRefund))
                expenses -= l.Amount;
            else if (l.Type == TransactionType.Expense)
                expenses += l.Amount;
            else
                income += l.Amount;
        }

        return new FlowTotals(income, expenses, savings);
    }

    /// <summary>
    /// Net = entrées − sorties − mises de côté, la formule affichée en pied de l'onglet. Une catégorie
    /// hors bilan (balayage vers le livret, alimentation de la carte) n'est jamais soustraite : sa
    /// contrepartie est déjà comptée ailleurs.
    /// </summary>
    public static decimal Net(FlowTotals t, bool offBalance) =>
        t.Income - t.Expenses - (offBalance ? 0m : t.Savings);

    /// <summary>
    /// Premier mois calendaire entièrement couvert par la banque. Le 30/01/2026 ne couvre que deux
    /// jours de janvier, le premier mois entier est février.
    /// </summary>
    public static DateTime? FirstFullBankMonth(DateTime? firstBankTransaction)
    {
        if (firstBankTransaction is not { } d) return null;
        var month = new DateTime(d.Year, d.Month, 1);
        return d.Day == 1 ? month : month.AddMonths(1);
    }

    /// <summary>
    /// Premier mois dont l'homologue N-1 est comparable, soit douze mois après le début de la
    /// couverture bancaire. Null tant qu'aucune banque n'est connectée.
    /// </summary>
    public static DateTime? FirstComparableMonth(DateTime? firstBankTransaction) =>
        FirstFullBankMonth(firstBankTransaction)?.AddMonths(12);

    /// <summary>Ce mois-ci peut-il montrer son N-1 sans mentir.</summary>
    public static bool IsComparable(DateTime month, DateTime? firstComparableMonth) =>
        firstComparableMonth is { } seuil && month >= seuil;
}
