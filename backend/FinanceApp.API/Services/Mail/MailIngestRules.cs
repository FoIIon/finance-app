using FinanceApp.API.Services.Text;

namespace FinanceApp.API.Services.Mail;

public enum MailRejectReason
{
    /// <summary>L'adresse de From n'est pas dans AllowedSenders.</summary>
    ExpediteurInconnu,
    /// <summary>Aucun X-Forwarded-For ne porte ForwardedFrom : le mail n'est pas passé par le transfert.</summary>
    PasTransfere,
    /// <summary>L'objet ne contient aucun des mots acceptés.</summary>
    ObjetHorsListe
}

public sealed record MailDecision(bool Accepted, MailRejectReason? Reason)
{
    public static readonly MailDecision Yes = new(true, null);
    public static MailDecision No(MailRejectReason reason) => new(false, reason);
}

/// <summary>
/// Les règles pures du tri, sans aucune entrée-sortie : trois verrous dans l'ordre (expéditeur, transfert,
/// objet) et l'année fiscale d'après la date du mail. Le type d'une pièce jointe ne se tranche pas ici mais
/// sur ses octets, par DocumentStorage.StageAsync.
/// </summary>
public static class MailIngestRules
{
    /// <summary>Mots de l'objet acceptés, comparés sans casse et sans accents.</summary>
    public static readonly IReadOnlyList<string> SubjectWords = new[] { "décompte", "facture", "rappel" };

    public static MailDecision Decide(IncomingMail mail, MailIngestOptions options)
    {
        var from = AddressOf(mail.From);
        if (!options.AllowedSenders.Any(s => string.Equals(s.Trim(), from, StringComparison.OrdinalIgnoreCase)))
            return MailDecision.No(MailRejectReason.ExpediteurInconnu);

        var forwardedFrom = options.ForwardedFrom.Trim();
        if (forwardedFrom.Length == 0
            || !mail.ForwardedFor.Any(h => h.Contains(forwardedFrom, StringComparison.OrdinalIgnoreCase)))
            return MailDecision.No(MailRejectReason.PasTransfere);

        var subject = Fold(mail.Subject);
        if (!SubjectWords.Any(w => subject.Contains(Fold(w), StringComparison.Ordinal)))
            return MailDecision.No(MailRejectReason.ObjetHorsListe);

        return MailDecision.Yes;
    }

    /// <summary>La partie entre chevrons d'un From, ou tout le champ s'il n'en a pas, trimée.</summary>
    public static string AddressOf(string from)
    {
        var value = from ?? string.Empty;
        var open = value.IndexOf('<');
        var close = open >= 0 ? value.IndexOf('>', open + 1) : -1;
        if (open >= 0 && close > open) value = value[(open + 1)..close];
        return value.Trim();
    }

    /// <summary>Minuscules sans marques diacritiques : « Décompte », « DECOMPTE » et « decompte » se confondent. Délègue à <see cref="TextFold"/>.</summary>
    public static string Fold(string? value) => TextFold.Fold(value);

    /// <summary>
    /// Une date de mail qu'on accepte pour l'année fiscale : après 2000 et pas plus de deux jours dans le futur
    /// du relevé. MinValue (en-tête absent ou illisible), l'époque Unix et 2099 sont hors fenêtre.
    /// </summary>
    public static bool IsPlausibleMailDate(DateTimeOffset mailDate, DateTimeOffset now) =>
        mailDate.Year >= 2000 && mailDate <= now.AddDays(2);

    /// <summary>L'année de la date du mail en heure locale, sauf janvier qui range en N-1 : une facture de décembre arrive en janvier.</summary>
    public static int FiscalYearFor(DateTimeOffset mailDate, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(mailDate, zone);
        return local.Month == 1 ? local.Year - 1 : local.Year;
    }
}
