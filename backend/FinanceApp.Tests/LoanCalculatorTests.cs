using FinanceApp.API.Models;
using FinanceApp.API.Services;
using Xunit;

namespace FinanceApp.Tests;

public class LoanCalculatorTests
{
    /// <summary>
    /// Le crédit logement CBC réel, ancré sur l'échéance du 10/09/2026 du tableau
    /// d'amortissement : amortissement 1046,82, intérêt 185,90, solde après 172856,22.
    /// </summary>
    private static Loan CreditLogement() => new()
    {
        Name = "Crédit logement",
        Kind = LoanKind.Mortgage,
        InitialPrincipal = 265000m,
        AnnualRatePercent = 1.2828m,
        MonthlyPayment = 1232.72m,
        AnchorDate = new DateTime(2026, 9, 10),
        AnchorPrincipal = 172856.22m
    };

    /// <summary>
    /// Le prêt aux beaux-parents. Ni capital initial ni encours connus, seule la dernière
    /// échéance l'est : on ancre donc à la fin, solde nul, et le calcul remonte le temps.
    /// Le 08/07/2040 tombe un dimanche, la banque exécutera le lundi, l'échéance nominale
    /// reste le 8 comme sur tous les relevés.
    /// </summary>
    private static Loan PretFamilial() => new()
    {
        Name = "Prêt maison beaux-parents",
        Kind = LoanKind.Family,
        AnnualRatePercent = 2m,
        MonthlyPayment = 888.98m,
        AnchorDate = new DateTime(2040, 7, 8),
        AnchorPrincipal = 0m
    };

    [Fact]
    public void PrincipalAt_RemonteUneEcheance_RetrouveLEncoursActuel()
    {
        // Avant l'échéance du 10/09, l'encours est celui du relevé : 173 903,04.
        var result = LoanCalculator.PrincipalAt(CreditLogement(), new DateTime(2026, 8, 26));
        Assert.Equal(173903.04m, result);
    }

    [Fact]
    public void PrincipalAt_SurLAncrage_RendLeSoldeSaisi()
    {
        var result = LoanCalculator.PrincipalAt(CreditLogement(), new DateTime(2026, 9, 10));
        Assert.Equal(172856.22m, result);
    }

    [Fact]
    public void RemainingSchedule_PremiereEcheance_ReproduitLaLigneDuTableau()
    {
        var first = LoanCalculator.RemainingSchedule(CreditLogement(), new DateTime(2026, 8, 26))[0];

        Assert.Equal(new DateTime(2026, 9, 10), first.DueDate);
        Assert.Equal(1232.72m, first.Payment);
        Assert.Equal(185.90m, first.Interest);
        Assert.Equal(1046.82m, first.Principal);
        Assert.Equal(172856.22m, first.RemainingPrincipal);
    }

    [Fact]
    public void Summarize_CreditLogement_TombeSurLaDureeResiduelleAnnoncee()
    {
        // 12 ans et 9 mois annoncés par la banque au 26/08/2026, soit 153 échéances.
        var summary = LoanCalculator.Summarize(CreditLogement(), new DateTime(2026, 8, 26));

        Assert.Equal(173903.04m, summary.RemainingPrincipal);
        Assert.Equal(153, summary.RemainingInstallments);
        Assert.Equal(new DateTime(2039, 5, 10), summary.FinalDueDate);
        Assert.Equal(new DateTime(2026, 9, 10), summary.NextDueDate);
    }

    [Fact]
    public void Summarize_CreditLogement_LaDerniereEcheanceSoldeSansDepasser()
    {
        var schedule = LoanCalculator.RemainingSchedule(CreditLogement(), new DateTime(2026, 8, 26));
        var last = schedule[^1];

        Assert.Equal(0m, last.RemainingPrincipal);
        Assert.True(last.Payment <= 1232.72m, $"La dernière échéance ({last.Payment}) dépasse la mensualité.");
        // Capital remboursé sur la période = encours de départ, au centime près.
        Assert.Equal(173903.04m, schedule.Sum(i => i.Principal));
    }

    [Fact]
    public void PrincipalAt_AncrageALaFin_RemonteJusquALEncoursDuJour()
    {
        // 167 échéances de 888,98 restent à courir, mais à 2 % elles ne valent pas
        // leur somme : l'encours est la valeur actuelle, pas le total à décaisser.
        var result = LoanCalculator.PrincipalAt(PretFamilial(), new DateTime(2026, 8, 26));
        Assert.Equal(129495.99m, result);
    }

    [Fact]
    public void Summarize_PretFamilial_SepareLeCapitalDesInterets()
    {
        var summary = LoanCalculator.Summarize(PretFamilial(), new DateTime(2026, 8, 26));

        Assert.Equal(167, summary.RemainingInstallments);
        Assert.Equal(new DateTime(2040, 7, 8), summary.FinalDueDate);
        Assert.Equal(18963.67m, summary.RemainingInterest);
        Assert.Equal(148459.66m, summary.RemainingPayments);
        // Le total décaissé se répartit entre capital du jour et intérêts, sans reliquat.
        Assert.Equal(summary.RemainingPayments, summary.RemainingPrincipal + summary.RemainingInterest);
    }

    [Fact]
    public void RemainingSchedule_AncrageALaFin_SeTermineExactementSurLAncrage()
    {
        // Le contrôle qui compte : remonter puis redérouler doit retomber pile sur
        // l'échéance d'ancrage, sans échéance résiduelle de quelques euros à la fin.
        var schedule = LoanCalculator.RemainingSchedule(PretFamilial(), new DateTime(2026, 8, 26));
        var last = schedule[^1];

        Assert.Equal(new DateTime(2040, 7, 8), last.DueDate);
        Assert.Equal(888.98m, last.Payment);
        Assert.Equal(0m, last.RemainingPrincipal);
    }

    [Fact]
    public void RemainingSchedule_PretEteint_RendUneListeVide()
    {
        var schedule = LoanCalculator.RemainingSchedule(PretFamilial(), new DateTime(2040, 8, 1));
        Assert.Empty(schedule);
    }

    [Fact]
    public void RemainingSchedule_PretFamilial_CompteQuinzeAnsDepuisAout2025()
    {
        // Contrat : 15 ans, première traite le 08/08/2025. L'ancrage est posé à l'autre bout
        // (dernière échéance, solde nul) : ce test vérifie que les deux extrémités se rejoignent.
        var loan = PretFamilial();
        var schedule = LoanCalculator.RemainingSchedule(loan, new DateTime(2025, 8, 7));

        Assert.Equal(180, schedule.Count);
        Assert.Equal(new DateTime(2025, 8, 8), schedule[0].DueDate);
        Assert.Equal(new DateTime(2040, 7, 8), schedule[^1].DueDate);
        // Capital emprunté qu'impliquent la mensualité, le taux et la durée.
        Assert.Equal(138145.73m, LoanCalculator.PrincipalAt(loan, new DateTime(2025, 8, 7)));
    }

    [Fact]
    public void RemainingSchedule_MensualiteInferieureALInteret_Leve()
    {
        var loan = CreditLogement();
        loan.MonthlyPayment = 100m;

        Assert.Throws<InvalidOperationException>(
            () => LoanCalculator.RemainingSchedule(loan, new DateTime(2026, 8, 26)));
    }

    [Fact]
    public void Summarize_EmpruntQuiNeSEteintPas_LeveAuLieuDInventerUneDateDeFin()
    {
        // 300 000 à 4 % : l'intérêt mensuel vaut 1 000, la mensualité 1 010 rembourse
        // 10 € de capital par mois. Tronquer le tableau annoncerait une libération en 2126
        // en devant encore 140 000 €, ce qui est pire qu'une erreur.
        var loan = new Loan
        {
            AnnualRatePercent = 4m,
            MonthlyPayment = 1010m,
            AnchorDate = new DateTime(2026, 9, 10),
            AnchorPrincipal = 300000m,
        };

        Assert.Throws<InvalidOperationException>(
            () => LoanCalculator.Summarize(loan, new DateTime(2026, 9, 10)));
    }

    [Fact]
    public void PrincipalAt_AncrageTropLointain_Leve()
    {
        var loan = CreditLogement();
        loan.AnchorDate = new DateTime(9999, 1, 10);

        Assert.Throws<InvalidOperationException>(
            () => LoanCalculator.PrincipalAt(loan, new DateTime(2026, 8, 26)));
    }

    [Fact]
    public void PrincipalAt_AncrageAvecUneHeure_NeDecalePasDUneEcheance()
    {
        // Une heure traînant sur l'ancrage faisait compter l'échéance du jour comme impayée.
        var loan = new Loan
        {
            AnnualRatePercent = 0m,
            MonthlyPayment = 100m,
            AnchorDate = new DateTime(2026, 1, 10, 14, 30, 0),
            AnchorPrincipal = 1000m,
        };

        Assert.Equal(1000m, LoanCalculator.PrincipalAt(loan, new DateTime(2026, 1, 10)));
        Assert.Equal(900m, LoanCalculator.PrincipalAt(loan, new DateTime(2026, 2, 10)));
    }

    [Fact]
    public void PrincipalAt_JourDEcheanceLe31_NeDeriveePasSurLesMoisCourts()
    {
        var loan = new Loan
        {
            AnnualRatePercent = 0m,
            MonthlyPayment = 100m,
            AnchorDate = new DateTime(2026, 1, 31),
            AnchorPrincipal = 1000m
        };

        // Février n'a pas de 31 : l'échéance tombe le 28, mais mars revient au 31.
        Assert.Equal(1000m, LoanCalculator.PrincipalAt(loan, new DateTime(2026, 2, 27)));
        Assert.Equal(900m, LoanCalculator.PrincipalAt(loan, new DateTime(2026, 2, 28)));
        Assert.Equal(900m, LoanCalculator.PrincipalAt(loan, new DateTime(2026, 3, 30)));
        Assert.Equal(800m, LoanCalculator.PrincipalAt(loan, new DateTime(2026, 3, 31)));
    }
}
