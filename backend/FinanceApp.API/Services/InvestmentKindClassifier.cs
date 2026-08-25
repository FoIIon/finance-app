using FinanceApp.API.Models;

namespace FinanceApp.API.Services;

/// <summary>
/// Détermine le type d'un actif importé de Trade Republic.
///
/// Sans ce classement, tout arrivait en Titre coté et la répartition par type d'actif
/// affichait « Titre coté 100 % » sur un portefeuille dont un tiers, mesuré le 25/08/2026,
/// n'est ni action ni fonds.
/// </summary>
public static class InvestmentKindClassifier
{
    /// <summary>Plage d'identifiants que Trade Republic réserve aux cryptomonnaies.</summary>
    private const string CryptoIsinPrefix = "XF000";

    public static InvestmentKind FromTradeRepublic(string isin, string instrumentType)
    {
        if (!string.IsNullOrWhiteSpace(instrumentType)
            && instrumentType.Contains("crypto", StringComparison.OrdinalIgnoreCase))
            return InvestmentKind.Crypto;

        // Repli quand le type d'instrument n'est pas renseigné : l'identifiant, lui, l'est.
        if (!string.IsNullOrWhiteSpace(isin)
            && isin.StartsWith(CryptoIsinPrefix, StringComparison.OrdinalIgnoreCase))
            return InvestmentKind.Crypto;

        // Un ETC adossé à un métal reste un titre coté : ce n'est pas du métal détenu en
        // propre, et le classer en Métal fausserait la répartition dans l'autre sens.
        return InvestmentKind.Security;
    }
}
