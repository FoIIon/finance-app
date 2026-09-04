namespace FinanceApp.SeedDemo;

/// <summary>
/// Les verrous qui empêchent le seed de toucher la base de production. Fonction pure : elle ne lit
/// ni le disque ni l'environnement, l'appelant lui passe ce qu'il sait et elle rend la raison du
/// refus, ou null si tout est en ordre. Chaque verrou vise un chemin réel vers la prod : le fichier
/// du Pi (/home/admin/finance-app/data/finance.db), la variable d'environnement du service systemd,
/// le nom de la machine. Aucun n'est suffisant seul, ensemble ils couvrent une commande lancée par
/// erreur depuis un shell SSH sur le Pi ou un chemin réseau vers son disque.
/// </summary>
public static class SeedGuard
{
    public const string Usage =
        "Usage : dotnet run --project tools/SeedDemo -- --db <chemin-vers-une-base-de-dev.db> [--today AAAA-MM-JJ] [--seed <entier>]";

    public const string ProductionMachineName = "raspberrypi5";

    public static string? Refuse(string? dbPath, string? environment, string machineName)
    {
        if (string.IsNullOrWhiteSpace(dbPath))
            return "Aucune base cible. L'option --db est obligatoire.\n" + Usage;

        var normalized = dbPath.Trim().Replace('\\', '/').ToLowerInvariant();
        while (normalized.Contains("//"))
            normalized = normalized.Replace("//", "/");

        if (normalized.Contains("finance-app/data"))
            return $"Refus : « {dbPath} » ressemble au dossier de données de production (finance-app/data).";

        if (normalized.StartsWith("/home/"))
            return $"Refus : « {dbPath} » est sous /home, le seed ne s'exécute pas sur le Pi.";

        if (string.Equals(environment?.Trim(), "Production", StringComparison.OrdinalIgnoreCase))
            return "Refus : l'environnement est Production (ASPNETCORE_ENVIRONMENT ou DOTNET_ENVIRONMENT).";

        if (string.Equals(machineName?.Trim(), ProductionMachineName, StringComparison.OrdinalIgnoreCase))
            return $"Refus : cette machine s'appelle « {machineName} », c'est le serveur de production.";

        return null;
    }
}
