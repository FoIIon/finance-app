using FinanceApp.API.Models;
using FinanceApp.API.Services;
using Xunit;

namespace FinanceApp.Tests;

public class GoCardlessRequisitionStatusTests
{
    [Theory]
    [InlineData("LN", BankConnectionStatus.Linked)]
    [InlineData("EX", BankConnectionStatus.Expired)]
    [InlineData("SU", BankConnectionStatus.Expired)]
    [InlineData("RJ", BankConnectionStatus.Error)]
    [InlineData("CR", BankConnectionStatus.PendingAuthorization)]
    [InlineData("GC", BankConnectionStatus.PendingAuthorization)]
    [InlineData("UA", BankConnectionStatus.PendingAuthorization)]
    [InlineData("GA", BankConnectionStatus.PendingAuthorization)]
    [InlineData("SA", BankConnectionStatus.PendingAuthorization)]
    public void Map_KnownStatus_ReturnsTheMatchingConnectionStatus(string status, BankConnectionStatus expected)
    {
        Assert.Equal(expected, GoCardlessRequisitionStatus.Map(status));
    }

    [Theory]
    [InlineData("")]
    [InlineData("ZZ")]
    public void Map_UnknownStatus_FallsBackOnError(string status)
    {
        // Un statut inconnu ne doit pas passer pour une autorisation en cours : l'utilisateur
        // garderait une connexion jaune et rassurante là où il faut un bouton de reprise.
        Assert.Equal(BankConnectionStatus.Error, GoCardlessRequisitionStatus.Map(status));
    }

    [Fact]
    public void Describe_Rejected_PointsAtTheBankingGroupIntegration()
    {
        // L'apprentissage du 22/08/2026 : six RJ d'affilée sans écran de login voulaient dire
        // que l'intégration de la banque était cassée, pas les identifiants de l'utilisateur.
        var message = GoCardlessRequisitionStatus.Describe("RJ");

        Assert.Contains("groupe bancaire", message);
    }

    [Fact]
    public void Describe_UnknownStatus_NamesTheCodeReceived()
    {
        var message = GoCardlessRequisitionStatus.Describe("ZZ");

        Assert.Contains("ZZ", message);
    }
}
