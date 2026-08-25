using FinanceApp.API.Models;

namespace FinanceApp.API.Services;

/// <summary>
/// Repère les positions vendues : Trade Republic cesse simplement de les renvoyer.
///
/// Sans cette détection, une ligne vendue restait active avec sa dernière valorisation
/// et continuait d'être comptée dans la valeur du portefeuille, indéfiniment. Le
/// mécanisme d'archivage existait déjà et arrête correctement de compter une ligne après
/// sa dernière valorisation, mais rien ne le déclenchait.
/// </summary>
public static class SoldPositionDetector
{
    /// <summary>Proportion de disparitions au-delà de laquelle on suppose une anomalie.</summary>
    private const double SeuilDisparitionMassive = 0.25;

    public static IReadOnlyList<Investment> LinesToArchive(
        IEnumerable<Investment> lines, IReadOnlyCollection<string> isinsPresents)
    {
        // Une réponse vide est une panne, pas une vente générale. Sans ce garde, un
        // incident réseau archiverait la totalité du portefeuille.
        if (isinsPresents.Count == 0) return [];

        var suivies = lines
            .Where(line => !line.IsArchived && line.Source == InvestmentSource.TradeRepublic)
            .ToList();

        var absentes = suivies
            .Where(line => !isinsPresents.Contains(line.ExternalId ?? line.Isin ?? string.Empty))
            .ToList();

        // Une réponse partielle n'est pas une vente générale non plus. Le parseur saute en
        // silence une position sans ISIN, sans quantité ou sans prix de revient : un
        // changement de forme chez Trade Republic ferait disparaître des lignes vivantes.
        //
        // Une disparition isolée reste une vente ordinaire, quel que soit la taille du
        // portefeuille : sur deux lignes, en vendre une fait déjà la moitié. C'est la
        // disparition simultanée de plusieurs lignes, au-delà du quart des lignes suivies,
        // qu'on refuse de prendre pour argent comptant.
        if (absentes.Count > 1 && absentes.Count > suivies.Count * SeuilDisparitionMassive)
            return [];

        return absentes;
    }
}
