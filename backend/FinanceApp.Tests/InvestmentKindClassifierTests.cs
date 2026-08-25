using FinanceApp.API.Models;
using FinanceApp.API.Services;
using Xunit;

namespace FinanceApp.Tests;

public class InvestmentKindClassifierTests
{
    [Theory]
    [InlineData("crypto")]
    [InlineData("cryptocurrency")]
    [InlineData("CRYPTO")]
    public void FromTradeRepublic_InstrumentTypeSaysCrypto_ReturnsCrypto(string instrumentType)
    {
        // La source qui fait foi quand Trade Republic la renseigne.
        Assert.Equal(InvestmentKind.Crypto,
            InvestmentKindClassifier.FromTradeRepublic("US0378331005", instrumentType));
    }

    [Theory]
    [InlineData("XF000BTC0017")]
    [InlineData("XF000ETH0019")]
    [InlineData("XF000DOGE012")]
    public void FromTradeRepublic_CryptoIsinRange_ReturnsCrypto(string isin)
    {
        // Repli mesuré sur le portefeuille réel du 25/08/2026 : les trois cryptos y portent
        // toutes un identifiant de la plage XF000, que Trade Republic réserve à cet usage.
        // Le type d'instrument n'est pas toujours renseigné, l'identifiant l'est.
        Assert.Equal(InvestmentKind.Crypto, InvestmentKindClassifier.FromTradeRepublic(isin, ""));
    }

    [Theory]
    [InlineData("US0378331005", "stock")]
    [InlineData("IE00B53SZB19", "fund")]
    [InlineData("PLOPTTC00011", "")]
    public void FromTradeRepublic_EverythingElse_StaysSecurity(string isin, string instrumentType)
    {
        Assert.Equal(InvestmentKind.Security, InvestmentKindClassifier.FromTradeRepublic(isin, instrumentType));
    }

    [Fact]
    public void FromTradeRepublic_GoldEtc_StaysSecurity()
    {
        // Un ETC adossé à l'or reste un titre coté : ce n'est pas du métal détenu en propre,
        // le classer en Métal donnerait une répartition fausse dans l'autre sens.
        Assert.Equal(InvestmentKind.Security, InvestmentKindClassifier.FromTradeRepublic("IE00B4ND3602", "fund"));
    }
}
