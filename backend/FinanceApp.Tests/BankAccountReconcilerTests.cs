using FinanceApp.API.Models;
using FinanceApp.API.Services;
using Xunit;

namespace FinanceApp.Tests;

public class BankAccountReconcilerTests
{
    private static BankAccount Existing(int id, string externalId, string iban, string currency = "EUR") =>
        new() { Id = id, ExternalAccountId = externalId, Iban = iban, Currency = currency };

    // Deux comptes dans chaque jeu : avec un seul, une assertion sur l'identifiant
    // ne prouve pas qu'on a rapproché le bon, seulement qu'on a rapproché quelque chose.
    private static BankAccount[] DeuxComptes() =>
    [
        Existing(1, "ancien-id-courant", "BE68539007547034"),
        Existing(2, "ancien-id-epargne", "BE29NWBK60161331926819"),
    ];

    [Fact]
    public void FindMatch_SameIban_DifferentExternalId_ReturnsTheAccountHoldingThatIban()
    {
        // Cas visé : à la reconnexion d'une connexion existante, GoCardless renvoie le même
        // compte sous un identifiant neuf. Sans rapprochement par IBAN, le callback crée un
        // doublon et laisse l'ancien compte orphelin avec ses transactions.
        var match = BankAccountReconciler.FindMatch(DeuxComptes(), "nouvel-id-gocardless", "BE29NWBK60161331926819", "EUR");

        Assert.NotNull(match);
        Assert.Equal(2, match!.Id);
    }

    [Fact]
    public void FindMatch_UnknownAccount_ReturnsNull()
    {
        var match = BankAccountReconciler.FindMatch(DeuxComptes(), "id-inconnu", "BE43068999999501", "EUR");

        Assert.Null(match);
    }

    [Fact]
    public void FindMatch_BankReturnsNoIban_FallsBackOnExternalAccountId()
    {
        // GoCardless ne renvoie pas toujours l'IBAN dans les détails du compte.
        var match = BankAccountReconciler.FindMatch(DeuxComptes(), "ancien-id-epargne", "", "EUR");

        Assert.NotNull(match);
        Assert.Equal(2, match!.Id);
    }

    [Fact]
    public void FindMatch_IbanFormattedWithSpacesAndLowercase_StillMatches()
    {
        var match = BankAccountReconciler.FindMatch(DeuxComptes(), "nouvel-id-gocardless", "be68 5390 0754 7034", "EUR");

        Assert.NotNull(match);
        Assert.Equal(1, match!.Id);
    }

    [Fact]
    public void FindMatch_ExistingAccountWithoutIban_FallsBackOnExternalAccountId()
    {
        // Comptes créés avant que l'IBAN soit renseigné de façon fiable.
        var existing = new[] { Existing(7, "id-gocardless", ""), Existing(8, "autre-id", "BE68539007547034") };

        var match = BankAccountReconciler.FindMatch(existing, "id-gocardless", "BE43068999999501", "EUR");

        Assert.NotNull(match);
        Assert.Equal(7, match!.Id);
    }

    [Fact]
    public void FindMatch_SameIbanButDifferentCurrency_DoesNotMatch()
    {
        // Un compte multidevise expose deux comptes sur le même IBAN. Les rapprocher
        // reviendrait à écraser l'un par l'autre et à en perdre un définitivement.
        var existing = new[] { Existing(1, "id-eur", "BE68539007547034", "EUR") };

        var match = BankAccountReconciler.FindMatch(existing, "id-usd", "BE68539007547034", "USD");

        Assert.Null(match);
    }

    [Fact]
    public void FindMatch_SameIbanAndCurrencyMissingOnOneSide_StillMatches()
    {
        // La devise n'est pas toujours renseignée : son absence ne doit pas empêcher
        // le rapprochement, seulement une contradiction le doit.
        var existing = new[] { Existing(1, "ancien-id", "BE68539007547034", "") };

        var match = BankAccountReconciler.FindMatch(existing, "nouvel-id", "BE68539007547034", "EUR");

        Assert.NotNull(match);
        Assert.Equal(1, match!.Id);
    }

    [Fact]
    public void FindMatch_SameExternalIdButContradictoryIban_DoesNotMatch()
    {
        // Le repli sur l'identifiant externe ne doit pas réécrire l'IBAN d'un compte
        // par celui d'un autre : mieux vaut créer une ligne visible qu'en corrompre une.
        var existing = new[] { Existing(1, "id-gocardless", "BE68539007547034") };

        var match = BankAccountReconciler.FindMatch(existing, "id-gocardless", "BE29NWBK60161331926819", "EUR");

        Assert.Null(match);
    }

    [Fact]
    public void FindMatch_IbanAndExternalIdPointToDifferentAccounts_TheIbanWins()
    {
        // La décision fondatrice de la classe : l'identifiant GoCardless bouge, l'IBAN non.
        // Sans ce test, inverser les deux blocs de FindMatch ne ferait rien tomber.
        var existing = new[] { Existing(1, "E1", "BE68539007547034"), Existing(2, "E2", "BE29NWBK60161331926819") };

        var match = BankAccountReconciler.FindMatch(existing, "E1", "BE29NWBK60161331926819", "EUR");

        Assert.NotNull(match);
        Assert.Equal(2, match!.Id);
    }

    [Fact]
    public void FindMatch_MultiCurrencyIban_ReturnsTheAccountOfThatCurrency()
    {
        // Le pendant positif du refus multidevise : c'est la raison d'être du paramètre.
        var existing = new[] { Existing(1, "id-eur", "BE68539007547034", "EUR"), Existing(2, "id-usd", "BE68539007547034", "USD") };

        var match = BankAccountReconciler.FindMatch(existing, "nouvel-id", "BE68539007547034", "USD");

        Assert.NotNull(match);
        Assert.Equal(2, match!.Id);
    }

    [Fact]
    public void FindMatch_ExactCurrency_WinsOverAnAccountWithoutCurrency()
    {
        // Une ligne ancienne sans devise ne doit pas rafler le rapprochement d'une devise
        // explicitement portée par une autre ligne du même IBAN.
        var existing = new[] { Existing(1, "ancien-id", "BE68539007547034", ""), Existing(2, "id-usd", "BE68539007547034", "USD") };

        var match = BankAccountReconciler.FindMatch(existing, "nouvel-id", "BE68539007547034", "USD");

        Assert.NotNull(match);
        Assert.Equal(2, match!.Id);
    }
}
