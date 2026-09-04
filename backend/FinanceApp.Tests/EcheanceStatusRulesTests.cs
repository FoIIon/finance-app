using FinanceApp.API.Models;
using FinanceApp.API.Services;
using Xunit;

namespace FinanceApp.Tests;

/// <summary>Le statut d'une échéance n'est jamais stocké : voici la règle qui le calcule.</summary>
public class EcheanceStatusRulesTests
{
    static readonly DateOnly Today = new(2026, 9, 4);

    static Echeance E(DateOnly due, DateTime? paidAt = null, int? transactionId = null) =>
        new() { Label = "Taxe", DueDate = due, PaidAt = paidAt, TransactionId = transactionId };

    [Fact]
    public void EcheanceFuture_EstAVenir() =>
        Assert.Equal(EcheanceStatus.AVenir, EcheanceStatusRules.Of(E(Today.AddDays(10)), Today));

    [Fact]
    public void EcheancePassee_NonPayee_EstEnRetard() =>
        Assert.Equal(EcheanceStatus.EnRetard, EcheanceStatusRules.Of(E(Today.AddDays(-1)), Today));

    [Fact]
    public void LeJourMeme_EstEncoreAVenir() =>
        Assert.Equal(EcheanceStatus.AVenir, EcheanceStatusRules.Of(E(Today), Today));

    [Fact]
    public void PaidAt_RendPayee_MemeEnRetard() =>
        Assert.Equal(EcheanceStatus.Payee, EcheanceStatusRules.Of(E(Today.AddDays(-30), paidAt: DateTime.UtcNow), Today));

    [Fact]
    public void TransactionLiee_SansPaidAt_SuffitAPayer() =>
        Assert.Equal(EcheanceStatus.Payee, EcheanceStatusRules.Of(E(Today.AddDays(-30), transactionId: 42), Today));

    [Fact]
    public void TransactionLiee_SurEcheanceFuture_EstPayee() =>
        Assert.Equal(EcheanceStatus.Payee, EcheanceStatusRules.Of(E(Today.AddDays(5), transactionId: 42), Today));

    [Fact]
    public void LeStatut_SuitLaDateDuJour_SansEcriture()
    {
        // La même ligne, lue deux jours différents, change de statut sans qu'on l'ait touchée.
        var e = E(new DateOnly(2026, 9, 10));
        Assert.Equal(EcheanceStatus.AVenir, EcheanceStatusRules.Of(e, new DateOnly(2026, 9, 10)));
        Assert.Equal(EcheanceStatus.EnRetard, EcheanceStatusRules.Of(e, new DateOnly(2026, 9, 11)));
    }
}
