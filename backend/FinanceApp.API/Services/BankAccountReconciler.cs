using FinanceApp.API.Models;

namespace FinanceApp.API.Services;

/// <summary>
/// Rapproche un compte remonté par la banque d'un compte déjà connu de la connexion.
/// L'identifiant GoCardless change d'une réquisition à l'autre (vérifié le 22/08/2026 sur
/// le compte commun CBC repris via KBC), l'IBAN est la seule clé stable. Le repli sur
/// l'identifiant externe couvre les comptes dont la banque ne renvoie pas l'IBAN et les
/// lignes créées avant que l'IBAN soit renseigné de façon fiable.
/// </summary>
public static class BankAccountReconciler
{
    public static BankAccount? FindMatch(IEnumerable<BankAccount> existingAccounts, string externalAccountId, string iban)
    {
        var accounts = existingAccounts as IList<BankAccount> ?? existingAccounts.ToList();

        var normalizedIban = NormalizeIban(iban);
        if (normalizedIban.Length > 0)
        {
            var byIban = accounts.FirstOrDefault(a => NormalizeIban(a.Iban) == normalizedIban);
            if (byIban != null)
                return byIban;
        }

        if (!string.IsNullOrWhiteSpace(externalAccountId))
            return accounts.FirstOrDefault(a => a.ExternalAccountId == externalAccountId);

        return null;
    }

    private static string NormalizeIban(string? iban)
        => string.IsNullOrWhiteSpace(iban)
            ? string.Empty
            : new string(iban.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();
}
