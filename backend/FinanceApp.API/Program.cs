using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using FinanceApp.API.Data;
using FinanceApp.API.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
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

    options.AddPolicy("tr-login", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            ParUtilisateur(httpContext),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    options.AddPolicy("tr-verify", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            ParUtilisateur(httpContext),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy.WithOrigins("http://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod();
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

// Sert le build frontend depuis wwwroot/ (déploiement Pi : backend + frontend sur la même origine)
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

// APRÈS l'authentification : les policies partitionnées par utilisateur lisent
// HttpContext.User, qui est encore vide tant que UseAuthentication n'a pas tourné.
app.UseRateLimiter();

app.MapControllers();

// SPA fallback : toute route non-API renvoie index.html (React Router gère le reste)
if (File.Exists(Path.Combine(app.Environment.WebRootPath ?? "", "index.html")))
    app.MapFallbackToFile("index.html");

app.Run();
