using FinanceApp.API.Models;
using FinanceApp.API.Services.Reporting;
using Xunit;

namespace FinanceApp.Tests;

/// <summary>
/// Le matcher pur de la routine : trois règles dans l'ordre (lien, montant au centime, mot et fourchette),
/// un candidat par occurrence, jamais deux occurrences sur la même transaction. Sans base.
/// </summary>
public class RecurringSettlementTests
{
    private static readonly DateOnly Le24Sept = new(2026, 9, 24);

    private static RecurringTransaction Engie(int id = 1) => new()
    {
        Id = id, Description = "ENGIE — gaz/électricité", Amount = 400m, Type = TransactionType.Expense,
        Frequency = RecurringFrequency.Monthly, DayOfMonth = 24, StartDate = new DateOnly(2025, 1, 24), IsActive = true,
    };

    private static SettlementCandidate Tx(int id, DateOnly date, decimal amount, string description = "Paiement Bancontact",
        string? counterparty = null, TransactionType type = TransactionType.Expense, int? recurringId = null) =>
        new(id, date, amount, type, description, counterparty, recurringId);

    private static HashSet<int> Claimed() => new();

    [Fact]
    public void Regle1_Lien_GagneSansConditionDeMontantNiDeLibelle()
    {
        var r = Engie();
        var lie = Tx(10, new DateOnly(2026, 9, 3), 12.34m, "Rien à voir", recurringId: 1);
        var auMontant = Tx(11, new DateOnly(2026, 9, 24), 400m, "ENGIE");

        var settled = RecurringSettlement.Settle(r, Le24Sept, new[] { auMontant, lie }, Claimed());

        Assert.Equal(10, settled!.Id);
    }

    [Fact]
    public void Regle2_MontantAuCentime_SansMot()
    {
        var r = Engie();
        var settled = RecurringSettlement.Settle(r, Le24Sept, new[] { Tx(20, new DateOnly(2026, 9, 16), 400m, "Paiement carte 1234") }, Claimed());
        Assert.Equal(20, settled!.Id);

        Assert.Null(RecurringSettlement.Settle(r, Le24Sept, new[] { Tx(21, new DateOnly(2026, 9, 16), 400.01m, "Paiement carte 1234") }, Claimed()));
    }

    [Fact]
    public void Regle3_MotEtFourchette_DansLeLibelleOuLaContrepartie()
    {
        var r = Engie();
        var parLibelle = Tx(30, new DateOnly(2026, 9, 16), 357.06m, "Domiciliation Engie Electrabel");
        Assert.Equal(30, RecurringSettlement.Settle(r, Le24Sept, new[] { parLibelle }, Claimed())!.Id);

        var parContrepartie = Tx(31, new DateOnly(2026, 9, 16), 357.06m, "Domiciliation", counterparty: "ENGIE ELECTRABEL SA");
        Assert.Equal(31, RecurringSettlement.Settle(r, Le24Sept, new[] { parContrepartie }, Claimed())!.Id);

        // Le montant dans la fourchette sans le mot ne suffit pas, le mot sans la fourchette non plus.
        Assert.Null(RecurringSettlement.Settle(r, Le24Sept, new[] { Tx(32, new DateOnly(2026, 9, 16), 357.06m, "Colruyt") }, Claimed()));
        Assert.Null(RecurringSettlement.Settle(r, Le24Sept, new[] { Tx(33, new DateOnly(2026, 9, 16), 872.34m, "Engie régularisation") }, Claimed()));
    }

    [Fact]
    public void Priorite_LienPuisMontantPuisMot()
    {
        var r = Engie();
        var parMot = Tx(40, new DateOnly(2026, 9, 24), 380m, "Engie");
        var auMontant = Tx(41, new DateOnly(2026, 9, 20), 400m, "Carte");
        var lie = Tx(42, new DateOnly(2026, 9, 1), 5m, "Lien", recurringId: 1);

        Assert.Equal(40, RecurringSettlement.Settle(r, Le24Sept, new[] { parMot }, Claimed())!.Id);
        Assert.Equal(41, RecurringSettlement.Settle(r, Le24Sept, new[] { parMot, auMontant }, Claimed())!.Id);
        Assert.Equal(42, RecurringSettlement.Settle(r, Le24Sept, new[] { parMot, auMontant, lie }, Claimed())!.Id);
    }

    [Theory]
    [InlineData(300.00, true)]
    [InlineData(299.99, false)]
    [InlineData(500.00, true)]
    [InlineData(500.01, false)]
    public void Fourchette_BornesIncluses_JusteAuDelaExclu(double amount, bool expected)
    {
        var r = Engie();
        var settled = RecurringSettlement.Settle(r, Le24Sept, new[] { Tx(50, new DateOnly(2026, 9, 16), (decimal)amount, "Engie") }, Claimed());
        Assert.Equal(expected, settled != null);
    }

    [Fact]
    public void Sens_UneRecetteNeRegleJamaisUneDepense_EtInversement()
    {
        var r = Engie();
        var recette = Tx(60, new DateOnly(2026, 9, 24), 400m, "Engie remboursement", type: TransactionType.Income);
        Assert.Null(RecurringSettlement.Settle(r, Le24Sept, new[] { recette }, Claimed()));
        // Même un lien ne traverse pas le sens : les candidats du bon sens sont préparés en amont, le matcher le vérifie quand même.
        Assert.Null(RecurringSettlement.Settle(r, Le24Sept, new[] { Tx(61, new DateOnly(2026, 9, 24), 400m, "Engie", type: TransactionType.Income, recurringId: 1) }, Claimed()));

        var salaire = new RecurringTransaction { Id = 2, Description = "Salaire Sébastien", Amount = 3428m, Type = TransactionType.Income, Frequency = RecurringFrequency.Monthly, DayOfMonth = 28, StartDate = new DateOnly(2025, 1, 28), IsActive = true };
        Assert.Null(RecurringSettlement.Settle(salaire, new DateOnly(2026, 9, 28), new[] { Tx(62, new DateOnly(2026, 9, 28), 3428m, "Salaire") }, Claimed()));
        Assert.Equal(63, RecurringSettlement.Settle(salaire, new DateOnly(2026, 9, 28), new[] { Tx(63, new DateOnly(2026, 9, 28), 3428m, "Salaire", type: TransactionType.Income) }, Claimed())!.Id);
    }

    [Fact]
    public void Mot_LePremierDAuMoinsQuatreLettres_LesPlusCourtsSontIgnores()
    {
        // « Audrey 45€ (Santé) » : « Audrey » fait six lettres, c'est lui.
        var audrey = new RecurringTransaction { Id = 3, Description = "Audrey 45€ (Santé)", Amount = 45m, Type = TransactionType.Expense, Frequency = RecurringFrequency.Monthly, DayOfMonth = 7, StartDate = new DateOnly(2025, 1, 7), IsActive = true };
        Assert.Equal(70, RecurringSettlement.Settle(audrey, new DateOnly(2026, 9, 7), new[] { Tx(70, new DateOnly(2026, 9, 8), 40m, "Virement AUDREY santé") }, Claimed())!.Id);
        Assert.Equal("audrey", RecurringSettlement.Keyword(audrey.Description));

        // « TV Proximus » : « TV » fait deux lettres, on passe au mot suivant.
        Assert.Equal("proximus", RecurringSettlement.Keyword("TV Proximus"));
        // Rien d'assez long : pas de mot, la règle 3 ne s'applique pas.
        Assert.Null(RecurringSettlement.Keyword("TV 12 ab"));
        var tv = new RecurringTransaction { Id = 4, Description = "TV 12 ab", Amount = 20m, Type = TransactionType.Expense, Frequency = RecurringFrequency.Monthly, DayOfMonth = 1, StartDate = new DateOnly(2025, 1, 1), IsActive = true };
        Assert.Null(RecurringSettlement.Settle(tv, new DateOnly(2026, 9, 1), new[] { Tx(71, new DateOnly(2026, 9, 1), 19m, "TV 12 ab") }, Claimed()));
        // Le repli enlève les accents et saute les mots génériques : « Crédit logement » cherche « logement ».
        Assert.Equal("logement", RecurringSettlement.Keyword("Crédit logement"));
        Assert.Equal("engie", RecurringSettlement.Keyword("ENGIE — gaz/électricité"));
    }

    [Fact]
    public void Mot_LesMotsGeneriquesSontSautes_SurLesOnzeRecurrentesReelles()
    {
        Assert.Contains("credit", RecurringSettlement.ExcludedKeywords);
        Assert.Contains("salaire", RecurringSettlement.ExcludedKeywords);
        Assert.Contains("epargne", RecurringSettlement.ExcludedKeywords);
        Assert.Contains("mensualite", RecurringSettlement.ExcludedKeywords);
        Assert.Equal(19, RecurringSettlement.ExcludedKeywords.Count);

        Assert.Equal("engie", RecurringSettlement.Keyword("ENGIE — gaz/électricité"));
        Assert.Equal("netflix", RecurringSettlement.Keyword("Netflix"));
        Assert.Equal("besos", RecurringSettlement.Keyword("Besos"));
        Assert.Equal("internet", RecurringSettlement.Keyword("Internet"));
        Assert.Equal("logement", RecurringSettlement.Keyword("Crédit logement"));
        Assert.Equal("sebastien", RecurringSettlement.Keyword("Salaire Sébastien"));
        // « epargne » et « perso » sont génériques, « cbc » fait trois lettres : pas de mot, pas de règle 3.
        Assert.Null(RecurringSettlement.Keyword("Épargne perso CBC"));
        Assert.Null(RecurringSettlement.Keyword("Mensualité"));

        var epargne = new RecurringTransaction { Id = 5, Description = "Épargne perso CBC", Amount = 200m, Type = TransactionType.Expense, Frequency = RecurringFrequency.Monthly, DayOfMonth = 5, StartDate = new DateOnly(2025, 1, 5), IsActive = true };
        Assert.Null(RecurringSettlement.Settle(epargne, new DateOnly(2026, 9, 5), new[] { Tx(72, new DateOnly(2026, 9, 5), 180m, "Épargne perso CBC") }, Claimed()));
        // Le montant au centime reste la règle 2, sans mot.
        Assert.Equal(73, RecurringSettlement.Settle(epargne, new DateOnly(2026, 9, 5), new[] { Tx(73, new DateOnly(2026, 9, 5), 200m, "Virement") }, Claimed())!.Id);
    }

    [Fact]
    public void Mot_Entier_PasUneSousChaine()
    {
        var r = Engie();
        Assert.Null(RecurringSettlement.Settle(r, Le24Sept, new[] { Tx(74, new DateOnly(2026, 9, 16), 380m, "Domiciliation ENGIEX") }, Claimed()));
        Assert.Null(RecurringSettlement.Settle(r, Le24Sept, new[] { Tx(75, new DateOnly(2026, 9, 16), 380m, "Paiement", counterparty: "REENGIE SA") }, Claimed()));
        Assert.Equal(76, RecurringSettlement.Settle(r, Le24Sept, new[] { Tx(76, new DateOnly(2026, 9, 16), 380m, "Domiciliation ENGIE/Electrabel") }, Claimed())!.Id);

        var logement = new RecurringTransaction { Id = 6, Description = "Crédit logement", Amount = 1232.72m, Type = TransactionType.Expense, Frequency = RecurringFrequency.Monthly, DayOfMonth = 10, StartDate = new DateOnly(2025, 1, 10), IsActive = true };
        // Le décompte de la carte de crédit dans la fourchette : « creditcard » n'est pas « credit », et « credit » est générique de toute façon.
        Assert.Null(RecurringSettlement.Settle(logement, new DateOnly(2026, 9, 10), new[] { Tx(77, new DateOnly(2026, 9, 10), 1300m, "Creditcard decompte") }, Claimed()));
        Assert.Null(RecurringSettlement.Settle(logement, new DateOnly(2026, 9, 10), new[] { Tx(78, new DateOnly(2026, 9, 10), 1300m, "Accreditation logements") }, Claimed()));
        Assert.Equal(79, RecurringSettlement.Settle(logement, new DateOnly(2026, 9, 10), new[] { Tx(79, new DateOnly(2026, 9, 10), 1300m, "Pret logement mensualite") }, Claimed())!.Id);
    }

    [Theory]
    [InlineData(14, true)]
    [InlineData(13, false)]
    public void Proximite_DixJoursAuPlus_BorneBasseIncluse_PourLeMontantEtLeMot(int day, bool expected)
    {
        // Occurrence du 24 : le 14 est à dix jours, le 13 à onze.
        var r = Engie();
        var auMontant = RecurringSettlement.Settle(r, Le24Sept, new[] { Tx(80, new DateOnly(2026, 9, day), 400m, "Carte") }, Claimed());
        Assert.Equal(expected, auMontant != null);
        var parMot = RecurringSettlement.Settle(r, Le24Sept, new[] { Tx(81, new DateOnly(2026, 9, day), 380m, "Engie") }, Claimed());
        Assert.Equal(expected, parMot != null);
    }

    [Fact]
    public void Proximite_BorneHauteIncluse_EtLeLienNEstPasBorne()
    {
        var r = Engie();
        var le15 = new DateOnly(2026, 9, 15);
        Assert.Equal(10, RecurringSettlement.MaxDaysFromDue);
        // Occurrence du 15 : le 25 est à dix jours, le 26 à onze, pour le montant comme pour le mot.
        Assert.NotNull(RecurringSettlement.Settle(r, le15, new[] { Tx(82, new DateOnly(2026, 9, 25), 400m, "Carte") }, Claimed()));
        Assert.Null(RecurringSettlement.Settle(r, le15, new[] { Tx(83, new DateOnly(2026, 9, 26), 400m, "Carte") }, Claimed()));
        Assert.NotNull(RecurringSettlement.Settle(r, le15, new[] { Tx(87, new DateOnly(2026, 9, 25), 380m, "Engie") }, Claimed()));
        Assert.Null(RecurringSettlement.Settle(r, le15, new[] { Tx(88, new DateOnly(2026, 9, 26), 380m, "Engie") }, Claimed()));
        // Un lien règle à n'importe quelle distance dans le mois.
        Assert.Equal(84, RecurringSettlement.Settle(r, le15, new[] { Tx(84, new DateOnly(2026, 9, 1), 12m, "Lien", recurringId: 1) }, Claimed())!.Id);

        // Les deux cas du ménage : un virement à Audrey le 20 pour le 7, une dépense de 45,00 le 25, aucun ne règle.
        var audrey = new RecurringTransaction { Id = 3, Description = "Audrey 45€ (Santé)", Amount = 45m, Type = TransactionType.Expense, Frequency = RecurringFrequency.Monthly, DayOfMonth = 7, StartDate = new DateOnly(2025, 1, 7), IsActive = true };
        Assert.Null(RecurringSettlement.Settle(audrey, new DateOnly(2026, 9, 7), new[] { Tx(85, new DateOnly(2026, 9, 20), 45m, "Virement Audrey") }, Claimed()));
        Assert.Null(RecurringSettlement.Settle(audrey, new DateOnly(2026, 9, 7), new[] { Tx(86, new DateOnly(2026, 9, 25), 45m, "Pharmacie") }, Claimed()));
    }

    [Fact]
    public void DejaPris_UneTransactionNeRegleQuUneOccurrence()
    {
        var r = Engie();
        var claimed = new HashSet<int> { 80 };
        var seul = Tx(80, new DateOnly(2026, 9, 16), 400m, "Engie");
        Assert.Null(RecurringSettlement.Settle(r, Le24Sept, new[] { seul }, claimed));

        var autre = Tx(81, new DateOnly(2026, 9, 17), 400m, "Engie");
        Assert.Equal(81, RecurringSettlement.Settle(r, Le24Sept, new[] { seul, autre }, claimed)!.Id);
    }

    [Fact]
    public void CandidatLieAUneAutreRecurrente_EcarteDesRegles2Et3()
    {
        var r = Engie();
        var lieAilleurs = Tx(90, new DateOnly(2026, 9, 24), 400m, "Engie", recurringId: 99);
        Assert.Null(RecurringSettlement.Settle(r, Le24Sept, new[] { lieAilleurs }, Claimed()));

        var lieAilleursParMot = Tx(91, new DateOnly(2026, 9, 24), 380m, "Engie", recurringId: 99);
        Assert.Null(RecurringSettlement.Settle(r, Le24Sept, new[] { lieAilleursParMot }, Claimed()));
    }

    [Fact]
    public void Determinisme_DeuxCandidatsEquivalents_LePlusProcheDeLaDatePuisLePlusPetitId()
    {
        var r = Engie();
        var loin = Tx(100, new DateOnly(2026, 9, 15), 400m);
        var proche = Tx(101, new DateOnly(2026, 9, 23), 400m);
        Assert.Equal(101, RecurringSettlement.Settle(r, Le24Sept, new[] { loin, proche }, Claimed())!.Id);
        Assert.Equal(101, RecurringSettlement.Settle(r, Le24Sept, new[] { proche, loin }, Claimed())!.Id);

        // Même distance (le 23 et le 25) : le plus petit Id, quel que soit l'ordre de lecture.
        var avant = Tx(103, new DateOnly(2026, 9, 23), 400m);
        var apres = Tx(102, new DateOnly(2026, 9, 25), 400m);
        Assert.Equal(102, RecurringSettlement.Settle(r, Le24Sept, new[] { avant, apres }, Claimed())!.Id);
        Assert.Equal(102, RecurringSettlement.Settle(r, Le24Sept, new[] { apres, avant }, Claimed())!.Id);
    }

    [Fact]
    public void HorsDuMoisDeLOccurrence_JamaisCandidat()
    {
        var r = Engie();
        var aout = Tx(110, new DateOnly(2026, 8, 31), 400m, "Engie");
        var octobre = Tx(111, new DateOnly(2026, 10, 1), 400m, "Engie");
        Assert.Null(RecurringSettlement.Settle(r, Le24Sept, new[] { aout, octobre }, Claimed()));
    }

    [Fact]
    public void Engie_SurLesQuatreMoisReels_JuinNonRegle_LesTroisAutresRegles()
    {
        // Paiements réels du ménage : 872,34 le 18/06 (régularisation, hors fourchette, c'est voulu), 357,06 le
        // 16/07, 400 le 17/08, 400 le 16/09. La récurrente dit 400 le 24.
        var r = Engie();
        var candidats = new[]
        {
            Tx(200, new DateOnly(2026, 6, 18), 872.34m, "ENGIE ELECTRABEL", counterparty: "ENGIE"),
            Tx(201, new DateOnly(2026, 7, 16), 357.06m, "ENGIE ELECTRABEL", counterparty: "ENGIE"),
            Tx(202, new DateOnly(2026, 8, 17), 400m, "ENGIE ELECTRABEL", counterparty: "ENGIE"),
            Tx(203, new DateOnly(2026, 9, 16), 400m, "ENGIE ELECTRABEL", counterparty: "ENGIE"),
        };
        var claimed = Claimed();

        Assert.Null(RecurringSettlement.Settle(r, new DateOnly(2026, 6, 24), candidats, claimed));
        var juillet = RecurringSettlement.Settle(r, new DateOnly(2026, 7, 24), candidats, claimed);
        Assert.Equal(201, juillet!.Id);
        claimed.Add(juillet.Id);
        Assert.Equal(202, RecurringSettlement.Settle(r, new DateOnly(2026, 8, 24), candidats, claimed)!.Id);
        claimed.Add(202);
        Assert.Equal(203, RecurringSettlement.Settle(r, new DateOnly(2026, 9, 24), candidats, claimed)!.Id);
    }
}
