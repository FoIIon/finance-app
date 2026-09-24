using System.Net.Sockets;
using FinanceApp.API.Controllers;
using FinanceApp.API.Data;
using FinanceApp.API.Models;
using FinanceApp.API.Services.Calendar;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FinanceApp.API.Services.Mail;

/// <summary>Le bilan d'un relevé : des compteurs, rien d'autre. C'est tout ce qui est journalisé.</summary>
public sealed record MailRunSummary(int Seen, int Processed, int Ignored, int Failed, int Created, int Duplicates, int SkippedAttachments)
{
    public static readonly MailRunSummary Empty = new(0, 0, 0, 0, 0, 0, 0);
}

/// <summary>
/// Relève la boîte factures toutes les six heures, deux minutes après le démarrage (déjà chargé par
/// GoCardless), et range chaque PDF d'un mail accepté en Document Facture du dashboard configuré, par
/// DocumentDeposit. Sans section MailIngest : une ligne d'information et rien d'autre, ni en base ni sur
/// le réseau. Un sémaphore unique, partagé avec le relevé manuel du contrôleur.
///
/// Rien de ce qui est journalisé ou écrit dans LastError ne porte un objet de mail, un nom de fichier, une
/// adresse d'expéditeur, un Message-ID ni le mot de passe : des compteurs et des types d'exception.
/// LastSyncAt ne bouge que sur un relevé qui a ouvert la boîte et parcouru les messages ; LastAttemptAt
/// à chaque tentative.
/// </summary>
public class MailIngestService : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(6);
    public static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(2);
    public const int LastErrorMaxLength = 200;
    private const int MailMessageIdMaxLength = 300;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMailReader _reader;
    private readonly IOptions<MailIngestOptions> _options;
    private readonly IOptions<HouseholdOptions> _household;
    private readonly TimeProvider _clock;
    private readonly ILogger<MailIngestService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public MailIngestService(
        IServiceScopeFactory scopeFactory,
        IMailReader reader,
        IOptions<MailIngestOptions> options,
        IOptions<HouseholdOptions> household,
        TimeProvider clock,
        ILogger<MailIngestService> logger)
    {
        _scopeFactory = scopeFactory;
        _reader = reader;
        _options = options;
        _household = household;
        _clock = clock;
        _logger = logger;
    }

    public bool IsConfigured => _options.Value.IsConfigured;

    /// <summary>Le dashboard qui reçoit les documents, null si le service n'est pas configuré.</summary>
    public int? ConfiguredDashboardId => IsConfigured ? _options.Value.DashboardId : null;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!IsConfigured)
        {
            _logger.LogInformation("Ingestion mail désactivée, section MailIngest absente.");
            return;
        }

        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError("Relevé de la boîte factures en échec : {Type}.", ex.GetType().FullName);
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>Un relevé, en attendant le sémaphore si un autre est en cours.</summary>
    public async Task<MailRunSummary> RunOnceAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            return await RunLockedAsync(ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Un relevé si le sémaphore est libre, null sinon, sans attendre : c'est le 409 du contrôleur.</summary>
    public async Task<MailRunSummary?> TryRunOnceAsync(CancellationToken ct)
    {
        if (!await _gate.WaitAsync(0, ct)) return null;
        try
        {
            return await RunLockedAsync(ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<MailRunSummary> RunLockedAsync(CancellationToken ct)
    {
        var options = _options.Value;
        if (!options.IsConfigured) return MailRunSummary.Empty;

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<DocumentStorage>();
        var deposit = scope.ServiceProvider.GetRequiredService<DocumentDeposit>();

        var now = _clock.GetUtcNow().UtcDateTime;
        var source = await context.MailSources
            .FirstOrDefaultAsync(s => s.DashboardId == options.DashboardId && s.Address == options.User, ct);
        if (source == null)
        {
            source = new MailSource { DashboardId = options.DashboardId, Address = options.User, CreatedAt = now };
            context.MailSources.Add(source);
        }
        source.LastAttemptAt = now;
        await context.SaveChangesAsync(ct);

        var run = new RunState(options, context, storage, deposit, source);
        try
        {
            await _reader.ReadUnseenAsync(options, (mail, token) => HandleAsync(mail, run, token), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var (status, error) = Classify(ex);
            _logger.LogWarning("Boîte factures : ouverture en échec, {Type}.", ex.GetType().FullName);
            source.LastSyncStatus = status;
            source.LastError = Truncate(error);
            await context.SaveChangesAsync(CancellationToken.None);
            return run.Summary;
        }

        source.LastSyncAt = _clock.GetUtcNow().UtcDateTime;
        source.LastSyncStatus = MailSyncStatus.Ok;
        source.LastError = run.Failed == 0 ? null : Truncate(FailureText(run));
        await context.SaveChangesAsync(ct);

        var summary = run.Summary;
        _logger.LogInformation(
            "Boîte factures relevée : {Seen} vu(s), {Processed} traité(s), {Ignored} ignoré(s), {Failed} en échec, {Created} déposé(s), {Duplicates} doublon(s), {Skipped} pièce(s) écartée(s).",
            summary.Seen, summary.Processed, summary.Ignored, summary.Failed, summary.Created, summary.Duplicates, summary.SkippedAttachments);
        return summary;
    }

    private async Task<MailOutcome> HandleAsync(IncomingMail mail, RunState run, CancellationToken ct)
    {
        run.Seen++;
        try
        {
            var decision = MailIngestRules.Decide(mail, run.Options);
            if (!decision.Accepted)
            {
                run.Ignored++;
                return MailOutcome.Ignored;
            }

            // Sans en-tête Date lisible, MimeKit rend MinValue (année fiscale 0), et un expéditeur mal réglé peut
            // dater de 1970 ou de 2099 : hors d'une fenêtre plausible, l'heure du relevé range le document.
            var now = _clock.GetUtcNow();
            var mailDate = MailIngestRules.IsPlausibleMailDate(mail.Date, now) ? mail.Date : now;
            var fiscalYear = MailIngestRules.FiscalYearFor(mailDate, _household.Value.Zone);
            var messageId = mail.MessageId is { Length: > MailMessageIdMaxLength } ? mail.MessageId[..MailMessageIdMaxLength] : mail.MessageId;
            var deposited = 0;
            var duplicates = 0;

            foreach (var attachment in mail.Attachments)
            {
                StageResult staged;
                await using (var stream = attachment.Open())
                    staged = await run.Storage.StageAsync(stream, ct);

                // Hors Ok, StageAsync a déjà effacé le .part. Une pièce au-delà du plafond met le mail en échec :
                // il reste non lu, LastError le dit, et un plafond relevé le récupère au relevé suivant. Un contenu
                // vide ou d'un type inconnu est écarté, et un type reconnu mais pas PDF l'est juste en dessous.
                if (staged.Outcome == StageOutcome.TooLarge)
                    throw new MailAttachmentTooLargeException();
                if (staged.Outcome != StageOutcome.Ok)
                {
                    run.SkippedAttachments++;
                    continue;
                }
                if (staged.File!.Kind != FileKind.Pdf)
                {
                    run.Storage.Discard(staged.File);
                    run.SkippedAttachments++;
                    continue;
                }

                var request = new DepositRequest(
                    run.Options.DashboardId, null, DocumentKind.Facture, fiscalYear,
                    DocumentController.DisplayName(attachment.FileName), null, DocumentSource.Mail, messageId);
                var result = await run.Deposit.DepositAsync(staged.File, request, ct);
                switch (result.Outcome)
                {
                    case DepositOutcome.Created:
                        deposited++;
                        run.Created++;
                        // Le Document rangé n'a plus rien à faire dans le contexte du relevé : le tracker ne suit
                        // que la MailSource, et la garde HasChanges du dépôt suivant ne scanne pas tout le relevé.
                        run.Context.Entry(result.Document!).State = EntityState.Detached;
                        run.Source.LastDepositAt = _clock.GetUtcNow().UtcDateTime;
                        run.Source.DepositedCount++;
                        // Écrit tout de suite : le dépôt suivant partage ce contexte et sauve sous sa propre
                        // transaction. Une modification laissée en attente y serait écrite puis acceptée par le
                        // tracker, et perdue en base si le rangement du fichier suivant échoue et annule tout.
                        await run.Context.SaveChangesAsync(ct);
                        break;
                    case DepositOutcome.Duplicate:
                        // Un mail relu après un plantage : le fichier est déjà là, le mail est traité.
                        duplicates++;
                        run.Duplicates++;
                        break;
                    case DepositOutcome.QuotaExceeded:
                        throw new StorageQuotaExceededException();
                }
            }

            if (deposited + duplicates == 0)
            {
                run.Ignored++;
                return MailOutcome.Ignored;
            }

            run.Processed++;
            return MailOutcome.Processed;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            run.Failed++;
            run.FailureTypes.Add(ex.GetType().Name);
            _logger.LogWarning("Boîte factures : un message n'a pas pu être traité, {Type}.", ex.GetType().FullName);
            // Si c'est le save de la MailSource qui a lâché, elle reste Modified et chaque dépôt suivant du relevé
            // buterait sur la garde de DocumentDeposit : on repart des valeurs en base, un mail perdu et pas dix.
            if (run.Context.ChangeTracker.HasChanges())
                await run.Context.Entry(run.Source).ReloadAsync(CancellationToken.None);
            return MailOutcome.Failed;
        }
    }

    /// <summary>AuthenticationException de MailKit → AuthError, réseau/TLS/délai → ConnectionError, le reste → Error. Le texte ne porte que le type.</summary>
    public static (MailSyncStatus Status, string Error) Classify(Exception ex) => ex switch
    {
        MailKit.Security.AuthenticationException => (MailSyncStatus.AuthError, "Identifiants refusés par le serveur : régénérer le mot de passe d'application."),
        SocketException or IOException or TimeoutException
            or MailKit.Security.SslHandshakeException
            or System.Security.Authentication.AuthenticationException
            or MailKit.ProtocolException
            or MailKit.ServiceNotConnectedException
            or MailKit.ServiceNotAuthenticatedException
            => (MailSyncStatus.ConnectionError, $"Boîte injoignable ({ex.GetType().Name})."),
        _ => (MailSyncStatus.Error, $"Relevé en échec ({ex.GetType().Name})."),
    };

    private static string FailureText(RunState run)
    {
        var types = string.Join(", ", run.FailureTypes.Distinct());
        return run.Failed == 1
            ? $"Un message n'a pas pu être traité ({types})."
            : $"{run.Failed} messages n'ont pas pu être traités ({types}).";
    }

    private static string? Truncate(string? value) =>
        value == null ? null : value.Length <= LastErrorMaxLength ? value : value[..LastErrorMaxLength];

    /// <summary>Une pièce jointe au-delà de Documents:MaxFileBytes. Son nom de type suffit à LastError.</summary>
    private sealed class MailAttachmentTooLargeException : Exception
    {
        public MailAttachmentTooLargeException() : base("Pièce jointe au-delà du plafond de taille des documents.") { }
    }

    /// <summary>Le quota de stockage du dashboard est atteint. LastError ne porte que des noms de types, celui-ci doit se distinguer des autres.</summary>
    private sealed class StorageQuotaExceededException : Exception
    {
        public StorageQuotaExceededException() : base("Quota de stockage du dashboard atteint.") { }
    }

    /// <summary>Tout ce qu'un relevé traîne d'un mail au suivant : les dépendances du scope et les compteurs.</summary>
    private sealed class RunState
    {
        public RunState(MailIngestOptions options, AppDbContext context, DocumentStorage storage, DocumentDeposit deposit, MailSource source)
        {
            Options = options;
            Context = context;
            Storage = storage;
            Deposit = deposit;
            Source = source;
        }

        public MailIngestOptions Options { get; }
        public AppDbContext Context { get; }
        public DocumentStorage Storage { get; }
        public DocumentDeposit Deposit { get; }
        public MailSource Source { get; }

        public int Seen, Processed, Ignored, Failed, Created, Duplicates, SkippedAttachments;
        public List<string> FailureTypes { get; } = new();

        public MailRunSummary Summary => new(Seen, Processed, Ignored, Failed, Created, Duplicates, SkippedAttachments);
    }
}
