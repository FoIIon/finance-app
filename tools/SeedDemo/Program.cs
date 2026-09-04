using System.Globalization;
using FinanceApp.API.Data;
using FinanceApp.SeedDemo;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

// Point d'entrée : lire les options, passer les verrous, migrer, semer. Rien ne s'ouvre avant que
// SeedGuard ait dit oui, sur le chemin tel que tapé ET sur le chemin résolu (un chemin relatif
// vers le dossier de prod ne doit pas passer parce qu'il ne commence pas par /home).

string? dbArg = null;
var today = DateOnly.FromDateTime(DateTime.Today);
var seed = DemoSeeder.DefaultSeed;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--db" when i + 1 < args.Length:
            dbArg = args[++i];
            break;
        case "--today" when i + 1 < args.Length:
            if (!DateOnly.TryParseExact(args[++i], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out today))
                return Refuse($"--today attend une date AAAA-MM-JJ, reçu « {args[i]} ».");
            break;
        case "--seed" when i + 1 < args.Length:
            if (!int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out seed))
                return Refuse($"--seed attend un entier, reçu « {args[i]} ».");
            break;
        case "-h" or "--help":
            Console.WriteLine(SeedGuard.Usage);
            return 0;
        default:
            return Refuse($"Argument inconnu ou incomplet : {args[i]}");
    }
}

var aspnetEnv = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
var dotnetEnv = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
var environment = new[] { aspnetEnv, dotnetEnv }
    .FirstOrDefault(e => string.Equals(e, "Production", StringComparison.OrdinalIgnoreCase)) ?? aspnetEnv ?? dotnetEnv;

var refusal = SeedGuard.Refuse(dbArg, environment, Environment.MachineName);
if (refusal == null && dbArg != null)
    refusal = SeedGuard.Refuse(Path.GetFullPath(dbArg), environment, Environment.MachineName);
if (refusal != null)
{
    Console.Error.WriteLine(refusal);
    return 2;
}

var dbPath = Path.GetFullPath(dbArg!);
var directory = Path.GetDirectoryName(dbPath);
if (!string.IsNullOrEmpty(directory))
    Directory.CreateDirectory(directory);

var connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connectionString).Options;

try
{
    await using var ctx = new AppDbContext(options);
    Console.WriteLine($"Base : {dbPath}");
    Console.WriteLine($"Aujourd'hui : {today:yyyy-MM-dd}, graine : {seed}");
    Console.WriteLine("Migrations…");
    await ctx.Database.MigrateAsync();

    Console.WriteLine("Purge des données de démo précédentes et nouveau seed…");
    var summary = await DemoSeeder.RunAsync(ctx, today, seed);

    Console.WriteLine();
    Console.WriteLine($"Utilisateurs           : {summary.Users}");
    Console.WriteLine($"Dashboards             : {summary.Dashboards}");
    Console.WriteLine($"Comptes logiques       : {summary.Accounts}");
    Console.WriteLine($"Comptes bancaires      : {summary.BankAccounts}");
    Console.WriteLine($"Transactions           : {summary.Transactions}");
    Console.WriteLine($"Récurrentes            : {summary.RecurringTransactions}");
    Console.WriteLine();
    Console.WriteLine($"Connexion : {DemoSeeder.SebEmail} ou {DemoSeeder.AudreyEmail}, mot de passe {DemoSeeder.Password}");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Échec du seed : {ex.Message}");
    return 1;
}

static int Refuse(string message)
{
    Console.Error.WriteLine(message);
    Console.Error.WriteLine(SeedGuard.Usage);
    return 2;
}
