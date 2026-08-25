using FinanceApp.API.Models;
using FinanceApp.API.Services;
using Xunit;

namespace FinanceApp.Tests;

public class BankAccountReconcilerTests
{
    private static BankAccount Existing(string externalId, string iban) =>
        new() { Id = 1, ExternalAccountId = externalId, Iban = iban };

    [Fact]
    public void FindMatch_SameIban_DifferentExternalId_ReturnsExistingAccount()
    {
        // Cas réel du 22/08/2026 : la reconnexion CBC puis KBC a rendu le même compte
        // sous un identifiant GoCardless différent. Sans ce rapprochement, le callback
        // créait un doublon et laissait l'ancien compte orphelin avec ses transactions.
        var existing = new[] { Existing("ancien-id-gocardless", "BE68539007547034") };

        var match = BankAccountReconciler.FindMatch(existing, "nouvel-id-gocardless", "BE68539007547034");

        Assert.NotNull(match);
        Assert.Equal(1, match!.Id);
    }

    [Fact]
    public void FindMatch_UnknownAccount_ReturnsNull()
    {
        var existing = new[] { Existing("ancien-id-gocardless", "BE68539007547034") };

        var match = BankAccountReconciler.FindMatch(existing, "id-inconnu", "BE29NWBK60161331926819");

        Assert.Null(match);
    }

    [Fact]
    public void FindMatch_BankReturnsNoIban_FallsBackOnExternalAccountId()
    {
        // GoCardless ne renvoie pas toujours l'IBAN dans les détails du compte.
        var existing = new[] { Existing("id-gocardless", "BE68539007547034") };

        var match = BankAccountReconciler.FindMatch(existing, "id-gocardless", "");

        Assert.NotNull(match);
        Assert.Equal(1, match!.Id);
    }

    [Fact]
    public void FindMatch_IbanFormattedWithSpacesAndLowercase_StillMatches()
    {
        var existing = new[] { Existing("ancien-id-gocardless", "BE68539007547034") };

        var match = BankAccountReconciler.FindMatch(existing, "nouvel-id-gocardless", "be68 5390 0754 7034");

        Assert.NotNull(match);
        Assert.Equal(1, match!.Id);
    }

    [Fact]
    public void FindMatch_ExistingAccountWithoutIban_FallsBackOnExternalAccountId()
    {
        // Comptes créés avant que l'IBAN soit renseigné de façon fiable.
        var existing = new[] { Existing("id-gocardless", "") };

        var match = BankAccountReconciler.FindMatch(existing, "id-gocardless", "BE68539007547034");

        Assert.NotNull(match);
        Assert.Equal(1, match!.Id);
    }
}
