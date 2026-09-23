using FinanceApp.API.Services.Mail;

namespace FinanceApp.Tests;

/// <summary>
/// Un lecteur en mémoire : livre les mails qu'on lui a donnés, note l'issue rendue par le service pour
/// chaque Uid, et peut lever une exception configurée à l'ouverture pour simuler une boîte injoignable.
/// </summary>
internal sealed class FakeMailReader : IMailReader
{
    public List<IncomingMail> Mails { get; } = new();
    public Dictionary<string, MailOutcome> Outcomes { get; } = new();
    public Exception? OpenFailure { get; set; }
    public int Opens { get; private set; }

    public FakeMailReader With(params IncomingMail[] mails)
    {
        Mails.AddRange(mails);
        return this;
    }

    public async Task ReadUnseenAsync(MailIngestOptions options, Func<IncomingMail, CancellationToken, Task<MailOutcome>> handle, CancellationToken ct)
    {
        Opens++;
        if (OpenFailure != null) throw OpenFailure;
        foreach (var mail in Mails.ToList())
        {
            ct.ThrowIfCancellationRequested();
            Outcomes[mail.Uid] = await handle(mail, ct);
        }
    }
}

internal static class MailIngestTestSupport
{
    public const string School = "ind2@proecoles.be";
    public const string Forwarder = "sb.dupont@gmail.com";
    public const string Mailbox = "boite-factures@test.invalid";

    /// <summary>La section MailIngest telle que le Pi la porte, hors mot de passe (inutile au lecteur simulé).</summary>
    public static MailIngestOptions Options(int dashboardId, Action<MailIngestOptions>? tune = null)
    {
        var o = new MailIngestOptions
        {
            Host = "imap.test.invalid",
            Port = 993,
            User = Mailbox,
            ForwardedFrom = Forwarder,
            AllowedSenders = new[] { School, "ind2@elmarche.be" },
            DashboardId = dashboardId,
        };
        tune?.Invoke(o);
        return o;
    }

    /// <summary>Le 4 septembre 2026 à 9h00 à Bruxelles.</summary>
    public static readonly DateTimeOffset MailDate = new(2026, 9, 4, 9, 0, 0, TimeSpan.FromHours(2));

    public static IncomingAttachment Pdf(string name, string seed) =>
        new(name, () => new MemoryStream(TestHousehold.PdfBytes(seed)));

    public static IncomingAttachment Png(string name) =>
        new(name, () => new MemoryStream(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D }));

    public static IncomingMail Mail(
        string uid,
        string from = "\"École\" <" + School + ">",
        string subject = "Décompte septembre 2026",
        string? forwardedFor = Forwarder + " " + Mailbox,
        DateTimeOffset? date = null,
        params IncomingAttachment[] attachments) =>
        new(uid, $"<{uid}@mail.test.invalid>", from,
            forwardedFor == null ? Array.Empty<string>() : new[] { forwardedFor },
            subject, date ?? MailDate, attachments);
}
