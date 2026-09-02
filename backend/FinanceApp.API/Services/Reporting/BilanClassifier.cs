using FinanceApp.API.Models;

namespace FinanceApp.API.Services.Reporting;

/// <summary>Les cinq blocs du bilan mensuel. Total = Entrées − Fixe − Mises de côté − Variable, le Hors bilan s'affiche à part.</summary>
public enum BilanBlock
{
    /// <summary>Revenus du mois : salaires, allocations, primes. Jamais un remboursement, jamais un transfert.</summary>
    Entrees,
    /// <summary>Charges fixes (prêt, prélèvements, abonnements), nettes des régularisations créditrices.</summary>
    Fixe,
    /// <summary>Transferts vers l'épargne ou vers des titres, décidés dans le mois, nets des retraits.</summary>
    MisesDeCote,
    /// <summary>Le reste des dépenses, net des remboursements reçus sur ces mêmes catégories.</summary>
    Variable,
    /// <summary>
    /// Mouvements dont la contrepartie est déjà comptée ailleurs (balayage vers le livret, virement
    /// entre deux comptes suivis). Rendus en information, jamais soustraits.
    /// </summary>
    HorsBilan,
}

/// <summary>Une transaction réduite à ce qui décide de son bloc.</summary>
public readonly record struct BilanLine(
    TransactionType Type,
    decimal Amount,
    bool IsTransfer,
    bool ExcludeFromMonthlyReport,
    bool IsFixed,
    bool IsRefund);

/// <summary>
/// Le bloc d'une transaction et le montant qu'elle y compte. Positif quand la ligne ajoute au bloc
/// (une dépense en Variable, un salaire en Entrées, un versement en Mises de côté), négatif quand elle
/// le réduit (un remboursement, une régularisation créditrice, un retrait d'épargne).
/// </summary>
public readonly record struct BilanEntry(BilanBlock Block, decimal Amount)
{
    /// <summary>Les deux blocs qui sont des dépenses au sens du résumé : Fixe et Variable.</summary>
    public bool IsExpenseBlock => Block is BilanBlock.Fixe or BilanBlock.Variable;
}

/// <summary>
/// La seule fonction qui décide dans quel bloc du bilan tombe une transaction.
///
/// Pourquoi (02/09/2026) : quatre endpoints faisaient chacun leur propre tri. Le bilan mensuel
/// déduisait une régularisation d'énergie du bloc FIXE, le résumé la comptait en revenu, le
/// burn-down aussi, et l'historique par catégorie ignorait les remboursements. Le total restait
/// juste par compensation, la lecture non : deux onglets donnaient deux chiffres pour le même mois.
/// Toute agrégation passe désormais par ici, et un changement de règle se fait en un endroit.
///
/// L'ordre des tests est l'ordre de priorité. Une catégorie hors bilan l'emporte sur tout, un
/// transfert l'emporte sur le drapeau fixe (un ordre permanent vers l'épargne reste une mise de
/// côté), le drapeau fixe l'emporte sur le sens (une régularisation créditrice réduit le fixe), et
/// un remboursement n'est jamais une entrée, voir <see cref="Refunds"/>.
/// </summary>
public static class BilanClassifier
{
    public static BilanEntry Classify(BilanLine l)
    {
        // Le sens « sortie » : une dépense ajoute au bloc, un revenu le réduit.
        var sortie = l.Type == TransactionType.Expense ? l.Amount : -l.Amount;

        if (l.ExcludeFromMonthlyReport) return new BilanEntry(BilanBlock.HorsBilan, sortie);
        if (l.IsTransfer) return new BilanEntry(BilanBlock.MisesDeCote, sortie);
        if (l.IsFixed) return new BilanEntry(BilanBlock.Fixe, sortie);
        if (l.Type == TransactionType.Income && !Refunds.Applies(l.Type, l.IsRefund))
            return new BilanEntry(BilanBlock.Entrees, l.Amount);
        return new BilanEntry(BilanBlock.Variable, sortie);
    }
}
