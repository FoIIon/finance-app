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
            var sameIban = accounts.Where(a => NormalizeIban(a.Iban) == normalizedIban).ToList();

            // Deux passes : la devise exacte d'abord. Sinon une ligne ancienne sans devise
            // raflerait le rapprochement d'une devise portée explicitement par une autre.
            var exact = sameIban.FirstOrDefault(a =>
                !string.IsNullOrWhiteSpace(a.Currency)
                && !string.IsNullOrWhiteSpace(currency)
                && string.Equals(a.Currency.Trim(), currency.Trim(), StringComparison.OrdinalIgnoreCase));
            if (exact != null)
                return exact;

            var tolerant = sameIban.FirstOrDefault(a => CurrenciesAgree(a.Currency, currency));
            if (tolerant != null)
                return tolerant;
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

    /// <summary>
    /// Deux IBAN désignent-ils le même compte, quelle que soit leur mise en forme.
    /// Exposé pour que les comparaisons faites hors de cette classe passent par la
    /// même normalisation, au lieu de comparer des chaînes brutes.
    /// </summary>
    public static bool SameIban(string? left, string? right)
    {
        var a = NormalizeIban(left);
        var b = NormalizeIban(right);
        return a.Length > 0 && a == b;
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
