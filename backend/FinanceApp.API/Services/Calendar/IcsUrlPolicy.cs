namespace FinanceApp.API.Services.Calendar;

/// <summary>
/// Ce qu'une adresse ICS doit être pour entrer en base : https, sans identifiants, sur un hôte de la liste
/// blanche (égalité stricte, pas de suffixe). La raison d'un refus ne répète jamais l'adresse : elle
/// contient un secret, et le message finit dans une réponse HTTP et peut-être un journal.
/// Pas de résolution DNS ni de filtrage d'IP privées, excessif pour un ménage.
/// </summary>
public static class IcsUrlPolicy
{
    public const int MaxLength = 2048;

    /// <summary>Null si l'adresse est acceptée (et <paramref name="uri"/> posée), sinon la raison du refus.</summary>
    public static string? Refuse(string? url, IReadOnlyCollection<string> allowedHosts, out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(url)) return "Adresse vide.";
        if (url.Length > MaxLength) return "Adresse trop longue.";
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var parsed)) return "Adresse invalide.";
        if (!string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return "Seul https est accepté.";
        if (!string.IsNullOrEmpty(parsed.UserInfo)) return "L'adresse ne doit pas contenir d'identifiants.";
        if (string.IsNullOrEmpty(parsed.Host)) return "Adresse sans hôte.";
        if (!allowedHosts.Any(h => string.Equals(h, parsed.Host, StringComparison.OrdinalIgnoreCase)))
            return $"Hôte non autorisé. Hôtes acceptés : {string.Join(", ", allowedHosts)}.";
        uri = parsed;
        return null;
    }
}
