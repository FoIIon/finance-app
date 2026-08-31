using FinanceApp.API.Models;
using FinanceApp.API.Services;
using Xunit;

namespace FinanceApp.Tests;

/// <summary>
/// Ce qui compte comme remboursement au bilan. Cas réel du 31/08/2026 : avance de 271,50 € de places
/// de foot le 07/08, remboursée par les beaux-parents le 10/08, les deux lignes en Sorties. Sans le
/// drapeau, la dépense gonflait VARIABLE et le remboursement gonflait ENTRÉES.
/// </summary>
public class RefundsTests
{
    [Fact]
    public void UnRevenuMarque_EstUnRemboursement()
    {
        // Les 271,50 € rendus par Luc et Jacqueline : à déduire de Sorties, pas à compter en entrée.
        Assert.True(Refunds.Applies(TransactionType.Income, isRefund: true));
    }

    [Fact]
    public void UnRevenuNonMarque_ResteUneEntree()
    {
        // Les allocations familiales (578 à 691 € par mois, rangées en Enfants) sont un vrai revenu.
        Assert.False(Refunds.Applies(TransactionType.Income, isRefund: false));
    }

    [Fact]
    public void UneDepenseMarquee_EstIgnoree()
    {
        // Drapeau posé par erreur sur une dépense : une dépense n'a rien à rembourser, on ne va
        // surtout pas la transformer en négatif et la faire disparaître du bloc.
        Assert.False(Refunds.Applies(TransactionType.Expense, isRefund: true));
        Assert.False(Refunds.Applies(TransactionType.Expense, isRefund: false));
    }
}
