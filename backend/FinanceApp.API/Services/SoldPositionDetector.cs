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
    public static IReadOnlyList<Investment> LinesToArchive(
        IEnumerable<Investment> lines, IReadOnlyCollection<string> isinsPresents)
    {
        // Une réponse vide est une panne, pas une vente générale. Sans ce garde, un
        // incident réseau archiverait la totalité du portefeuille.
        if (isinsPresents.Count == 0) return [];

        return lines
            .Where(line => !line.IsArchived
                           && line.Source == InvestmentSource.TradeRepublic
                           && !isinsPresents.Contains(line.ExternalId ?? line.Isin ?? string.Empty))
            .ToList();
    }
}
