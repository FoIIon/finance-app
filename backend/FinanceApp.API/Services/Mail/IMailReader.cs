namespace FinanceApp.API.Services.Mail;

/// <summary>Une pièce jointe, ouverte à la demande : Open rend le flux décodé, à disposer par l'appelant.</summary>
public sealed record IncomingAttachment(string FileName, Func<Stream> Open);

/// <summary>
/// Un message tel que le lecteur le livre au service : From brut (nom affiché compris), tous les en-têtes
/// X-Forwarded-For, l'objet, la date du mail et ses pièces jointes. Rien n'est encore décidé ici.
/// </summary>
public sealed record IncomingMail(
    string Uid,
    string? MessageId,
    string From,
    IReadOnlyList<string> ForwardedFor,
    string Subject,
    DateTimeOffset Date,
    IReadOnlyList<IncomingAttachment> Attachments);

/// <summary>
/// Ce que le service dit du message au lecteur, qui marque en conséquence : Processed et Ignored passent le
/// message en lu (avec une étiquette Gmail quand le serveur le permet), Failed ne touche à rien et le
/// message est relu au prochain relevé.
/// </summary>
public enum MailOutcome
{
    Processed,
    Ignored,
    Failed
}

public interface IMailReader
{
    /// <summary>
    /// Ouvre la boîte, parcourt les messages non lus, appelle <paramref name="handle"/> pour chacun,
    /// marque selon l'issue, ferme. Une connexion par relevé. Toute exception de connexion ou
    /// d'authentification remonte à l'appelant, qui la classe.
    /// </summary>
    Task ReadUnseenAsync(MailIngestOptions options, Func<IncomingMail, CancellationToken, Task<MailOutcome>> handle, CancellationToken ct);
}
