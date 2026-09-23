using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using MimeKit;

namespace FinanceApp.API.Services.Mail;

/// <summary>
/// Le lecteur IMAP réel, sur MailKit : TLS implicite sur le port configuré, INBOX en lecture-écriture,
/// messages non lus, message complet récupéré un par un. Marquage : Processed et Ignored passent en \Seen
/// avec une étiquette Gmail (« finance-app », « finance-app/ignoré ») si le dossier annonce l'extension,
/// Failed ne touche à rien. Déconnexion dans un finally. Rien n'est journalisé ici : les exceptions
/// remontent au service, qui n'en garde que le type.
/// </summary>
public sealed class ImapMailReader : IMailReader
{
    public const string ProcessedLabel = "finance-app";
    public const string IgnoredLabel = "finance-app/ignoré";

    public async Task ReadUnseenAsync(MailIngestOptions options, Func<IncomingMail, CancellationToken, Task<MailOutcome>> handle, CancellationToken ct)
    {
        using var client = new ImapClient();
        try
        {
            await client.ConnectAsync(options.Host, options.Port, SecureSocketOptions.SslOnConnect, ct);
            await client.AuthenticateAsync(options.User, options.Password, ct);

            var inbox = client.Inbox;
            await inbox.OpenAsync(FolderAccess.ReadWrite, ct);
            // Les étiquettes n'existent que chez Gmail (extension X-GM-EXT-1) : ailleurs, le drapeau \Seen seul.
            var labels = client.Capabilities.HasFlag(ImapCapabilities.GMailExt1);
            var uids = await inbox.SearchAsync(SearchQuery.NotSeen, ct);

            foreach (var uid in uids)
            {
                ct.ThrowIfCancellationRequested();
                var message = await inbox.GetMessageAsync(uid, ct);
                var outcome = await handle(ToIncoming(uid, message), ct);
                switch (outcome)
                {
                    case MailOutcome.Processed:
                        await MarkAsync(inbox, uid, labels ? ProcessedLabel : null, ct);
                        break;
                    case MailOutcome.Ignored:
                        await MarkAsync(inbox, uid, labels ? IgnoredLabel : null, ct);
                        break;
                    case MailOutcome.Failed:
                        break;
                }
            }
        }
        finally
        {
            if (client.IsConnected)
            {
                try { await client.DisconnectAsync(true, CancellationToken.None); }
                catch (Exception) { /* la connexion est déjà perdue, rien à fermer */ }
            }
        }
    }

    private static async Task MarkAsync(IMailFolder inbox, UniqueId uid, string? label, CancellationToken ct)
    {
        await inbox.AddFlagsAsync(uid, MessageFlags.Seen, true, ct);
        if (label != null) await inbox.AddLabelsAsync(uid, new[] { label }, true, ct);
    }

    private static IncomingMail ToIncoming(UniqueId uid, MimeMessage message)
    {
        var forwardedFor = message.Headers
            .Where(h => string.Equals(h.Field, "X-Forwarded-For", StringComparison.OrdinalIgnoreCase))
            .Select(h => h.Value)
            .ToList();

        var attachments = message.BodyParts
            .OfType<MimePart>()
            .Where(p => p.IsAttachment || !string.IsNullOrEmpty(p.FileName))
            .Select(p => new IncomingAttachment(p.FileName ?? string.Empty, () => p.Content.Open()))
            .ToList();

        return new IncomingMail(
            uid.ToString(),
            message.MessageId,
            message.From?.ToString() ?? string.Empty,
            forwardedFor,
            message.Subject ?? string.Empty,
            message.Date,
            attachments);
    }
}
