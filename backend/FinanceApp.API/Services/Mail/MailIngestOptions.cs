using Microsoft.Extensions.Options;

namespace FinanceApp.API.Services.Mail;

/// <summary>
/// Section MailIngest d'appsettings : la boîte dédiée qui reçoit les factures par transfert automatique.
/// Section absente ou Host vide : le service est désactivé, sans erreur. Les constantes du relevé
/// (intervalle, premier départ, mots de l'objet) sont en code, dans MailIngestService et MailIngestRules,
/// pas ici. Le mot de passe ne sort jamais de cette classe que vers l'authentification IMAP.
/// </summary>
public sealed class MailIngestOptions
{
    public const string SectionName = "MailIngest";

    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public string User { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    /// <summary>L'adresse d'origine du transfert, à retrouver dans un en-tête X-Forwarded-For.</summary>
    public string ForwardedFrom { get; set; } = string.Empty;
    /// <summary>Adresses acceptées dans From, comparées sans casse après trim.</summary>
    public string[] AllowedSenders { get; set; } = Array.Empty<string>();
    /// <summary>Le dashboard qui reçoit les documents.</summary>
    public int DashboardId { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host);
}

/// <summary>Muet quand la section est absente. Sinon chaque clé est vérifiée, sans jamais recopier une valeur.</summary>
public sealed class MailIngestOptionsValidator : IValidateOptions<MailIngestOptions>
{
    public ValidateOptionsResult Validate(string? name, MailIngestOptions o)
    {
        if (!o.IsConfigured) return ValidateOptionsResult.Success;

        var errors = new List<string>();
        if (o.Port < 1 || o.Port > 65535) errors.Add("MailIngest:Port doit être compris entre 1 et 65535.");
        if (string.IsNullOrWhiteSpace(o.User)) errors.Add("MailIngest:User est obligatoire.");
        if (string.IsNullOrWhiteSpace(o.Password)) errors.Add("MailIngest:Password est obligatoire.");
        if (string.IsNullOrWhiteSpace(o.ForwardedFrom)) errors.Add("MailIngest:ForwardedFrom est obligatoire.");
        if (o.AllowedSenders == null || o.AllowedSenders.Length == 0 || o.AllowedSenders.Any(string.IsNullOrWhiteSpace))
            errors.Add("MailIngest:AllowedSenders doit contenir au moins une adresse non vide.");
        if (o.DashboardId <= 0) errors.Add("MailIngest:DashboardId doit être strictement positif.");
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
