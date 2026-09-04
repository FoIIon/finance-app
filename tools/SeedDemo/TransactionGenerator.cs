using FinanceApp.API.Models;

namespace FinanceApp.SeedDemo;

/// <summary>
/// Trois mois glissants de vie d'un ménage belge à trois enfants, à partir d'une graine fixe. Chaque
/// ligne est tirée dans le même ordre à chaque passe, puis gardée seulement si sa date tombe dans la
/// fenêtre [aujourd'hui − 3 mois, aujourd'hui]. Les montants varient autour d'un centre pour ne pas
/// afficher que des chiffres ronds. Aucun nom réel : les contreparties sont des enseignes publiques ou
/// des libellés génériques, les IBAN sont des IBAN de test à clé valide.
/// </summary>
public sealed class TransactionGenerator
{
    public const decimal SebSalary = 3412.67m;
    public const decimal AudreySalary = 2418.35m;
    public const decimal Rent = 1185.00m;
    public const decimal Energy = 186.40m;
    public const int SalaryDay = 27;
    public const int EnergyDay = 12;

    private const string RentIban = "BE68539007547034";
    private const string EnergyIban = "BE71096123456769";

    private readonly Random _rng;
    private readonly DateOnly _today;
    private readonly DateOnly _start;
    private readonly IReadOnlyDictionary<string, int> _categories;
    private readonly int _sebPrimary;
    private readonly int _sebPerso;
    private readonly int _audreyPrimary;
    private readonly List<Transaction> _lines = new();
    private int _next = 1;

    public TransactionGenerator(Random rng, DateOnly today, IReadOnlyDictionary<string, int> categories,
        int sebPrimaryAccountId, int sebPersoAccountId, int audreyPrimaryAccountId)
    {
        _rng = rng;
        _today = today;
        _start = today.AddMonths(-3);
        _categories = categories;
        _sebPrimary = sebPrimaryAccountId;
        _sebPerso = sebPersoAccountId;
        _audreyPrimary = audreyPrimaryAccountId;
    }

    public List<Transaction> Generate()
    {
        foreach (var month in Months())
            GenerateMonthly(month);

        foreach (var week in Weeks())
            GenerateWeekly(week);

        GenerateScattered();

        return _lines;
    }

    // ----- Rythme mensuel : salaires, charges fixes, allocations ---------------------------------

    private void GenerateMonthly(DateOnly firstOfMonth)
    {
        var salaryDate = PreviousWeekday(firstOfMonth.AddDays(SalaryDay - 1));
        Income(salaryDate, Vary(SebSalary, 55m), "Salaire", "Salaire", _sebPrimary, counterparty: "Employeur Demo SA");
        Income(PreviousWeekday(firstOfMonth.AddDays(27)), Vary(AudreySalary, 35m), "Salaire", "Salaire", _audreyPrimary, counterparty: "Institut Demo ASBL");

        Expense(firstOfMonth, Rent, "Loyer maison", "Logement", _sebPrimary,
            counterparty: "Gestion immobilière Demo", iban: RentIban, isFixed: true);
        Expense(firstOfMonth.AddDays(EnergyDay - 1), Vary(Energy, 28m), "Acompte gaz et électricité", "Logement", _sebPrimary,
            counterparty: "Fournisseur énergie Demo", iban: EnergyIban, isFixed: true);
        Expense(firstOfMonth.AddDays(4), Vary(412.50m, 25m), "Crèche Les Petits Pas", "Éducation", _sebPrimary,
            counterparty: "Crèche Les Petits Pas ASBL");
        Expense(firstOfMonth.AddDays(14), 13.99m, "Abonnement streaming", "Loisirs", _sebPrimary, isFixed: true);
        Expense(firstOfMonth.AddDays(7), 62.00m, "Internet et mobile", "Logement", _sebPrimary,
            counterparty: "Opérateur télécom Demo", isFixed: true);
        Expense(firstOfMonth.AddDays(2), 48.73m, "Assurance auto", "Transport", _sebPrimary,
            counterparty: "Assureur Demo", isFixed: true);

        Income(firstOfMonth.AddDays(8), Vary(578.44m, 0m), "Allocations familiales", "Autres", _sebPrimary,
            counterparty: "Caisse d'allocations Demo");

        Expense(firstOfMonth.AddDays(5 + _rng.Next(0, 3)), Vary(78.20m, 12m), "Carburant", "Transport", _sebPrimary,
            counterparty: "Station-service Demo");
        Expense(firstOfMonth.AddDays(19 + _rng.Next(0, 3)), Vary(74.60m, 12m), "Carburant", "Transport", _sebPrimary,
            counterparty: "Station-service Demo");
        Expense(firstOfMonth.AddDays(11 + _rng.Next(0, 6)), Vary(52.30m, 9m), "Carburant", "Transport", _audreyPrimary,
            counterparty: "Station-service Demo");
    }

    // ----- Rythme hebdomadaire : courses, boulangerie, sorties -----------------------------------

    private void GenerateWeekly(DateOnly monday)
    {
        var shops = new[] { "Colruyt", "Delhaize", "Aldi" };
        Expense(monday.AddDays(5), Between(96m, 168m), shops[_rng.Next(shops.Length)], "Alimentation", _sebPrimary);
        Expense(monday.AddDays(2), Between(22m, 58m), "Proxy Delhaize", "Alimentation", _sebPrimary);
        Expense(monday.AddDays(6), Between(8.40m, 17.90m), "Boulangerie", "Alimentation", _sebPrimary);

        if (_rng.NextDouble() < 0.8)
        {
            var places = new[] { "Brasserie du Centre", "Pizzeria Demo", "Friterie", "Restaurant asiatique" };
            var day = _rng.Next(2) == 0 ? 4 : 5;
            Expense(monday.AddDays(day), Between(42m, 118m), places[_rng.Next(places.Length)], "Loisirs", _sebPrimary);
        }

        if (_rng.NextDouble() < 0.5)
            Expense(monday.AddDays(5), 6.00m, "Piscine communale", "Loisirs", _sebPrimary);
    }

    // ----- Épars : santé, shopping, remboursements, exceptionnel, perso ---------------------------

    private void GenerateScattered()
    {
        for (var i = 0; i < 6; i++)
            Expense(RandomDay(), Between(9.80m, 46.30m), "Pharmacie", "Santé", _sebPrimary);
        Expense(RandomDay(), 27.50m, "Médecin généraliste", "Santé", _sebPrimary);
        Expense(RandomDay(), 27.50m, "Pédiatre", "Santé", _sebPrimary);
        Expense(RandomDay(), Between(12m, 38m), "Pharmacie", "Santé", _audreyPrimary);
        Expense(RandomDay(), Between(12m, 38m), "Pharmacie", "Santé", _audreyPrimary);

        // Remboursements : posés à la main dans l'app, jamais devinés. Quatre, de natures différentes.
        Income(_start.AddDays(18), 24.60m, "Remboursement mutuelle", "Santé", _sebPrimary, counterparty: "Mutualité Demo", isRefund: true);
        Income(_start.AddDays(52), 58.20m, "Remboursement mutuelle", "Santé", _sebPrimary, counterparty: "Mutualité Demo", isRefund: true);
        Expense(_start.AddDays(30), 34.99m, "Vêtements enfants en ligne", "Shopping", _sebPrimary, counterparty: "Boutique en ligne Demo");
        Income(_start.AddDays(41), 34.99m, "Retour colis", "Shopping", _sebPrimary, counterparty: "Boutique en ligne Demo", isRefund: true);
        Income(_start.AddDays(66), 12.00m, "Remboursement sortie scolaire", "Éducation", _sebPrimary, counterparty: "École communale Demo", isRefund: true);

        var clothes = new[] { "Zeeman", "JBC", "Tape à l'œil", "Decathlon" };
        for (var i = 0; i < 4; i++)
            Expense(RandomDay(), Between(29.95m, 89.90m), clothes[_rng.Next(clothes.Length)], "Shopping", _sebPrimary);
        Expense(RandomDay(), Between(35m, 120m), "Vêtements", "Shopping", _audreyPrimary);
        Expense(RandomDay(), Between(35m, 120m), "Chaussures", "Shopping", _audreyPrimary);
        Expense(RandomDay(), 47.50m, "Cadre et déco", "Shopping", _sebPrimary);

        Expense(RandomDay(), 31.00m, "Cinéma", "Loisirs", _sebPrimary);
        Expense(RandomDay(), 31.00m, "Cinéma", "Loisirs", _sebPrimary);
        Expense(RandomDay(), Between(19.99m, 44.99m), "Dreamland", "Loisirs", _sebPrimary);
        Expense(RandomDay(), Between(19.99m, 44.99m), "Jouets", "Loisirs", _sebPrimary);
        Expense(RandomDay(), 24.90m, "Livres jeunesse", "Éducation", _sebPrimary);
        Expense(_today.AddDays(-_rng.Next(1, 12)), 68.35m, "Fournitures scolaires rentrée", "Éducation", _sebPrimary);
        Expense(RandomDay(), 35.00m, "Sortie collègues", "Loisirs", _audreyPrimary);
        Expense(RandomDay(), 28.50m, "Restaurant midi", "Loisirs", _audreyPrimary);

        Expense(RandomDay(), Between(18m, 65m), "Cadeau anniversaire", "Autres", _sebPrimary);
        Expense(RandomDay(), Between(18m, 65m), "Cadeau anniversaire", "Autres", _sebPrimary);
        Expense(RandomDay(), 28.00m, "Coiffeur", "Autres", _sebPrimary);
        Expense(RandomDay(), 45.00m, "Coiffeur", "Autres", _audreyPrimary);
        Expense(RandomDay(), 9.60m, "Poste", "Autres", _sebPrimary);
        Expense(RandomDay(), 21.75m, "Frais bancaires trimestriels", "Autres", _sebPrimary, isFixed: true);

        for (var i = 0; i < 4; i++)
            Expense(RandomDay(), Between(2.50m, 6.00m), "Parking", "Transport", _sebPrimary);
        Expense(RandomDay(), 18.40m, "Train Bruxelles", "Transport", _sebPrimary);

        // La dépense qui sort du budget du mois et qu'on marque pour ne pas fausser la moyenne.
        Expense(_start.AddDays(40), 1240.80m, "Réparation chaudière", "Logement", _sebPrimary,
            counterparty: "Chauffagiste Demo", isExceptional: true);

        // Le périmètre perso de Sébastien : son compte Perso, hors bilan commun.
        Income(_start.AddDays(25), 640.00m, "Facture site vitrine", "Freelance", _sebPerso, counterparty: "Client Demo SPRL");
        Expense(_start.AddDays(3), 95.00m, "Cotisation football", "Loisirs", _sebPerso);
        for (var i = 0; i < 3; i++)
            Expense(RandomDay(), Between(12m, 26m), "Bar après match", "Loisirs", _sebPerso);
        Expense(RandomDay(), 59.99m, "Jeu vidéo", "Loisirs", _sebPerso);
        Expense(RandomDay(), 22.00m, "Livre", "Loisirs", _sebPerso);
        Expense(RandomDay(), 74.90m, "Cadeau Audrey", "Shopping", _sebPerso);
        Expense(RandomDay(), Between(6m, 14m), "Sandwich midi", "Alimentation", _sebPerso);
        Expense(RandomDay(), Between(6m, 14m), "Sandwich midi", "Alimentation", _sebPerso);
    }

    // ----- Outils ---------------------------------------------------------------------------------

    private void Income(DateOnly date, decimal amount, string description, string category, int accountId,
        string? counterparty = null, bool isRefund = false)
        => Add(date, amount, description, TransactionType.Income, category, accountId, counterparty, null, false, isRefund, false);

    private void Expense(DateOnly date, decimal amount, string description, string category, int accountId,
        string? counterparty = null, string? iban = null, bool isFixed = false, bool isExceptional = false)
        => Add(date, amount, description, TransactionType.Expense, category, accountId, counterparty, iban, isFixed, false, isExceptional);

    private void Add(DateOnly date, decimal amount, string description, TransactionType type, string category, int accountId,
        string? counterparty, string? iban, bool isFixed, bool isRefund, bool isExceptional)
    {
        if (date < _start || date > _today) return;

        _lines.Add(new Transaction
        {
            Amount = amount,
            Description = description,
            Date = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            Type = type,
            CategoryId = _categories[category],
            AccountId = accountId,
            ExternalId = DemoSeeder.ExternalIdPrefix + _next++,
            IsImported = true,
            CounterpartyName = counterparty,
            CounterpartyIban = iban,
            IsFixed = isFixed,
            IsRefund = isRefund,
            IsExceptional = isExceptional,
            CreatedAt = date.ToDateTime(new TimeOnly(6, 0), DateTimeKind.Utc)
        });
    }

    private IEnumerable<DateOnly> Months()
    {
        for (var m = new DateOnly(_start.Year, _start.Month, 1); m <= _today; m = m.AddMonths(1))
            yield return m;
    }

    private IEnumerable<DateOnly> Weeks()
    {
        var monday = _start.AddDays(-(((int)_start.DayOfWeek + 6) % 7));
        for (var w = monday; w <= _today; w = w.AddDays(7))
            yield return w;
    }

    private DateOnly RandomDay() => _start.AddDays(_rng.Next(0, _today.DayNumber - _start.DayNumber + 1));

    private static DateOnly PreviousWeekday(DateOnly date)
    {
        while (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            date = date.AddDays(-1);
        return date;
    }

    private decimal Vary(decimal center, decimal spread)
        => spread == 0m ? center : Cents(center + (decimal)(_rng.NextDouble() * 2 - 1) * spread);

    private decimal Between(decimal min, decimal max) => Cents(min + (decimal)_rng.NextDouble() * (max - min));

    private static decimal Cents(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
