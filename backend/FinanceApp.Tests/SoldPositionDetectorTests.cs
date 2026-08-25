using FinanceApp.API.Models;
using FinanceApp.API.Services;
using Xunit;

namespace FinanceApp.Tests;

public class SoldPositionDetectorTests
{
    private static Investment Ligne(int id, string isin, InvestmentSource source = InvestmentSource.TradeRepublic,
        bool archivee = false) =>
        new()
        {
            Id = id,
            Isin = isin,
            ExternalId = source == InvestmentSource.TradeRepublic ? isin : null,
            Source = source,
            IsArchived = archivee,
        };

    [Fact]
    public void LinesToArchive_PositionAbsenteDuPortefeuille_EstArchivee()
    {
        // Une ligne vendue disparaît de la réponse Trade Republic. Sans détection elle
        // resterait active avec sa dernière valorisation, à gonfler le portefeuille
        // indéfiniment.
        var lignes = new[] { Ligne(1, "US0378331005"), Ligne(2, "XF000BTC0017") };

        var aArchiver = SoldPositionDetector.LinesToArchive(lignes, new HashSet<string> { "US0378331005" });

        Assert.Single(aArchiver);
        Assert.Equal(2, aArchiver[0].Id);
    }

    [Fact]
    public void LinesToArchive_PortefeuilleVide_NArchiveRien()
    {
        // Garde indispensable : une réponse vide est une panne d'API, pas une vente
        // générale. Sans ce garde, un incident réseau viderait tout le portefeuille.
        var lignes = new[] { Ligne(1, "US0378331005"), Ligne(2, "XF000BTC0017") };

        Assert.Empty(SoldPositionDetector.LinesToArchive(lignes, new HashSet<string>()));
    }

    [Fact]
    public void LinesToArchive_LigneSaisieALaMain_NestJamaisArchivee()
    {
        // L'import ne fait autorité que sur ce qu'il gère : une ligne manuelle absente
        // de Trade Republic est normale, pas vendue.
        var lignes = new[] { Ligne(1, "FR0000000000", InvestmentSource.Manual) };

        Assert.Empty(SoldPositionDetector.LinesToArchive(lignes, new HashSet<string> { "US0378331005" }));
    }

    [Fact]
    public void LinesToArchive_LigneDejaArchivee_NestPasRelistee()
    {
        var lignes = new[] { Ligne(1, "XF000BTC0017", archivee: true) };

        Assert.Empty(SoldPositionDetector.LinesToArchive(lignes, new HashSet<string> { "US0378331005" }));
    }

    [Fact]
    public void LinesToArchive_PortefeuilleInchange_NArchiveRien()
    {
        var lignes = new[] { Ligne(1, "US0378331005"), Ligne(2, "XF000BTC0017") };

        var aArchiver = SoldPositionDetector.LinesToArchive(
            lignes, new HashSet<string> { "US0378331005", "XF000BTC0017" });

        Assert.Empty(aArchiver);
    }
}
