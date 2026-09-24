using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using FinanceApp.API.Data;
using FinanceApp.API.Services;
using FinanceApp.API.Services.Calendar;
using FinanceApp.API.Services.Mail;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Vérification critique : JWT Key doit être configurée
var jwtKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrEmpty(jwtKey))
    throw new InvalidOperationException("JWT Key not configured. Set Jwt__Key environment variable.");

// Même exigence pour la racine des documents : obligatoire, résolue depuis le ContentRoot, refusée
// sous wwwroot. Le dossier est créé et .incoming nettoyé ici, au démarrage, pas à la première requête.
// [RequestSizeLimit] sur l'envoi est une constante (DefaultMaxFileBytes) : une valeur configurée
// au-dessus serait coupée par Kestrel avant d'arriver au contrôleur, on la ramène au plafond et on le dit.
var configuredMaxFileBytes = builder.Configuration.GetValue<long?>("Documents:MaxFileBytes") ?? DocumentStorageOptions.DefaultMaxFileBytes;
string? documentsWarning = null;
if (configuredMaxFileBytes > DocumentStorageOptions.DefaultMaxFileBytes)
{
    documentsWarning = $"Documents:MaxFileBytes ({configuredMaxFileBytes}) dépasse le plafond de la requête ({DocumentStorageOptions.DefaultMaxFileBytes}), ramené au plafond.";
    configuredMaxFileBytes = DocumentStorageOptions.DefaultMaxFileBytes;
}
var documentsOptions = new DocumentStorageOptions
{
    Root = DocumentStorage.ResolveRoot(
        builder.Configuration["Documents:Root"],
        builder.Environment.ContentRootPath,
        builder.Environment.WebRootPath,
        AppContext.BaseDirectory,
        builder.Environment.IsProduction()),
    MaxFileBytes = configuredMaxFileBytes,
    QuotaBytesPerDashboard = builder.Configuration.GetValue<long?>("Documents:QuotaBytesPerDashboard") ?? DocumentStorageOptions.DefaultQuotaBytesPerDashboard,
};
builder.Services.AddSingleton(documentsOptions);
builder.Services.AddSingleton(new DocumentStorage(documentsOptions));
builder.Services.AddScoped<DocumentDeposit>();
// Un peu au-dessus de la limite du fichier : un fichier trop gros doit atteindre DocumentStorage, qui
// répond 413 avec un message, au lieu d'un 400 du binder de formulaire.
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
    o.MultipartBodyLengthLimit = documentsOptions.MaxFileBytes + DocumentStorageOptions.RequestOverheadBytes);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Entity Framework
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

// JWT Authentication
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!))
        };
    });

builder.Services.AddScoped<TokenService>();
if (builder.Environment.IsDevelopment())
    builder.Services.AddScoped<IEmailService, DevEmailService>();
else
    builder.Services.AddScoped<IEmailService, SmtpEmailService>();
builder.Services.AddScoped<IAccountService, AccountService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IInvitationService, InvitationService>();
builder.Services.AddScoped<RecurringTransactionService>();
builder.Services.AddScoped<ProvisionService>();
builder.Services.AddScoped<EcheanceReconciliationService>();
builder.Services.AddScoped<FinanceApp.API.Services.Reporting.AccountBalanceService>();
builder.Services.AddScoped<FinanceApp.API.Services.Reporting.ReportingService>();
builder.Services.AddHttpClient<GoCardlessClient>();
builder.Services.AddDataProtection();
// UseCookies = false : on gère les cookies manuellement via les headers
// pour pouvoir injecter tr_session dans les requêtes de synchronisation
builder.Services.AddHttpClient<TradeRepublicClient>()
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { UseCookies = false });
builder.Services.AddSingleton<TradeRepublicAuthStore>();
builder.Services.AddSingleton<BankSyncService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<BankSyncService>());

// Lot 2 Agenda. Fuseau du ménage et bornes du calendrier, validés au démarrage. Le client HTTP du flux
// ICS n'a aucun logger (RemoveAllLoggers) : l'adresse est un secret, elle ne doit apparaître dans aucun
// journal, pas même en Debug. Pas de redirection suivie. La synchronisation de fond est un service
// distinct de BankSyncService, avec son propre rythme. TimeProvider pour des tests à date fixe.
builder.Services.AddOptions<HouseholdOptions>()
    .Bind(builder.Configuration.GetSection(HouseholdOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<HouseholdOptions>, HouseholdOptionsValidator>();
builder.Services.AddOptions<CalendarOptions>()
    .Bind(builder.Configuration.GetSection(CalendarOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<CalendarOptions>, CalendarOptionsValidator>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpClient(CalendarIcsFetcher.HttpClientName, client =>
    {
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("FinanceApp/1.0");
    })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
    .RemoveAllLoggers();
builder.Services.AddSingleton<ICalendarIcsFetcher, CalendarIcsFetcher>();
builder.Services.AddSingleton<CalendarSyncService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<CalendarSyncService>());

// Lot 4, ingestion des factures reçues par mail. Section MailIngest absente : options par défaut, validateur
// muet, le service journalise une ligne et ne fait rien. Configurée : validée au démarrage, relevé IMAP toutes
// les six heures par MailIngestService, même sémaphore que le relevé manuel de DocumentController.
builder.Services.AddOptions<MailIngestOptions>()
    .Bind(builder.Configuration.GetSection(MailIngestOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<MailIngestOptions>, MailIngestOptionsValidator>();
builder.Services.AddSingleton<IMailReader, ImapMailReader>();
builder.Services.AddSingleton<MailIngestService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<MailIngestService>());

builder.Services.AddRateLimiter(options =>
{
    // Les routes d'authentification sont anonymes : la seule clé disponible est l'adresse
    // du client. Sans partition, le compteur était global à toute l'application, cinq
    // requêtes par minute pour tous les utilisateurs réunis.
    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "inconnu",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    // La connexion a son propre budget : partagé avec l'inscription, un enchaînement
    // normal (inscription puis plusieurs connexions) épuisait le compteur et renvoyait
    // un 429 que l'interface affichait en mot de passe incorrect.
    options.AddPolicy("login", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "inconnu",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    // Trade Republic : un envoi de SMS et une vérification de code. Chacun garde son
    // propre budget, serré, pour qu'une rafale de connexions n'ouvre pas la porte sur la
    // vérification du code. Partition par utilisateur, ce qui exige que le limiteur
    // s'exécute APRÈS l'authentification (voir l'ordre du pipeline plus bas).
    static string ParUtilisateur(HttpContext ctx) =>
        ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? ctx.Connection.RemoteIpAddress?.ToString()
            ?? "inconnu";

    // Le budget commun aux actions qui ouvrent une connexion chez un tiers à chaque appel (Trade Republic,
    // Google, Gmail) : cinq par minute et par utilisateur, sans file d'attente. Un seul endroit à régler.
    static RateLimitPartition<string> CinqParMinuteParUtilisateur(HttpContext ctx) =>
        RateLimitPartition.GetFixedWindowLimiter(
            ParUtilisateur(ctx),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });

    options.AddPolicy("tr-login", httpContext =>
        CinqParMinuteParUtilisateur(httpContext));

    options.AddPolicy("tr-verify", httpContext =>
        CinqParMinuteParUtilisateur(httpContext));

    // Rafraîchissement manuel du calendrier : un téléchargement chez Google à chaque appel, même
    // budget serré que tr-login, par utilisateur.
    options.AddPolicy("calendar-refresh", httpContext =>
        CinqParMinuteParUtilisateur(httpContext));

    // Relevé manuel de la boîte factures : une connexion chez Gmail à chaque appel. Par utilisateur, et
    // séparé de « login » : un ménage derrière une seule adresse IP qui clique plusieurs fois sur
    // « Relever maintenant » ne doit pas voir sa prochaine connexion refusée en 429.
    options.AddPolicy("mail-refresh", httpContext =>
        CinqParMinuteParUtilisateur(httpContext));

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy.WithOrigins("http://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .WithExposedHeaders("X-Total-Count");
    });
});

var app = builder.Build();

if (documentsWarning != null) app.Logger.LogWarning("{Message}", documentsWarning);

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
    await next();
});

app.UseCors("Frontend");

// Sert le build frontend depuis wwwroot/ (déploiement Pi : backend + frontend sur la même origine).
// index.html sans Cache-Control laissait le navigateur appliquer son cache heuristique : un déploiement
// restait invisible plusieurs heures (constaté le 16/09/2026). Il est revalidé à chaque chargement,
// les bundles Vite portent un hash dans leur nom et peuvent être gardés un an. Tout le reste (manifest,
// icônes, apple-touch-icon.png) est revalidé aussi : le manifest a déjà changé de nom une fois, et un
// navigateur qui le garde en cache heuristique garderait l'ancien nom sur l'écran d'accueil pendant des jours.
var staticFiles = new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        var headers = ctx.Context.Response.Headers;
        if (ctx.File.Name.Equals("index.html", StringComparison.OrdinalIgnoreCase))
            headers.CacheControl = "no-cache";
        else if (ctx.Context.Request.Path.StartsWithSegments("/assets"))
            headers.CacheControl = "public, max-age=31536000, immutable";
        else
            headers.CacheControl = "no-cache";
    }
};
app.UseDefaultFiles();
app.UseStaticFiles(staticFiles);

app.UseAuthentication();
app.UseAuthorization();

// APRÈS l'authentification : pose User.LastSeenAt, au plus une écriture toutes les cinq minutes par utilisateur.
app.UseMiddleware<LastSeenMiddleware>();

// APRÈS l'authentification : les policies partitionnées par utilisateur lisent
// HttpContext.User, qui est encore vide tant que UseAuthentication n'a pas tourné.
app.UseRateLimiter();

app.MapControllers();

// SPA fallback : toute route non-API renvoie index.html (React Router gère le reste)
if (File.Exists(Path.Combine(app.Environment.WebRootPath ?? "", "index.html")))
    app.MapFallbackToFile("index.html", staticFiles);

app.Run();
