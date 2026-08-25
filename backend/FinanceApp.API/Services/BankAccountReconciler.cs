using FinanceApp.API.Models;

namespace FinanceApp.API.Services;

/// <summary>
/// Rapproche un compte remonté par la banque d'un compte déjà connu de la connexion.
/// L'identifiant GoCardless change d'une réquisition à l'autre (vérifié le 22/08/2026),
/// l'IBAN est la seule clé stable. Le repli sur l'identifiant externe couvre les comptes
/// dont la banque ne renvoie pas l'IBAN et les lignes créées avant que l'IBAN soit
/// enregistré de façon fiable.
/// </summary>
public static class BankAccountReconciler
{
    public static BankAccount? FindMatch(IEnumerable<BankAccount> existingAccounts, string externalAccountId, string iban, string currency)
    {
        var accounts = existingAccounts as IList<BankAccount> ?? existingAccounts.ToList();

        // Un IBAN peut porter plusieurs comptes (multidevise) : sans le filtre de devise,
        // le second écraserait le premier et le ferait disparaître de l'application.
        var normalizedIban = NormalizeIban(iban);
        if (normalizedIban.Length > 0)
        {
            var byIban = accounts.FirstOrDefault(a =>
                NormalizeIban(a.Iban) == normalizedIban && CurrenciesAgree(a.Currency, currency));
            if (byIban != null)
                return byIban;
        }

        // Repli sur l'identifiant externe, sauf si les deux IBAN se contredisent : mieux
        // vaut créer une ligne visible que réécrire l'IBAN d'un compte existant.
        if (!string.IsNullOrWhiteSpace(externalAccountId))
        {
            var byExternalId = accounts.FirstOrDefault(a => a.ExternalAccountId == externalAccountId);
            if (byExternalId != null && !IbansContradict(byExternalId.Iban, iban))
                return byExternalId;
        }

        return null;
    }

    /// <summary>Une devise absente d'un côté n'est pas une contradiction.</summary>
    private static bool CurrenciesAgree(string? left, string? right)
        => string.IsNullOrWhiteSpace(left)
           || string.IsNullOrWhiteSpace(right)
           || string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool IbansContradict(string? left, string? right)
    {
        var a = NormalizeIban(left);
        var b = NormalizeIban(right);
        return a.Length > 0 && b.Length > 0 && a != b;
    }

    private static string NormalizeIban(string? iban)
        => string.IsNullOrWhiteSpace(iban)
            ? string.Empty
            : new string(iban.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();
}
