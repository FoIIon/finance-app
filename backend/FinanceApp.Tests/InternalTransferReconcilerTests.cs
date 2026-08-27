using FinanceApp.API.Services;
using Xunit;

namespace FinanceApp.Tests;

/// <summary>
/// Le rapprochement d'un mouvement interne Trade Republic avec sa jambe bancaire.
/// Les cas viennent d'août 2026 : sept paiements carte comptés deux fois, 545,72 €.
/// </summary>
public class InternalTransferReconcilerTests
{
    private static readonly string[] Titulaires = ["LIBERT - LAMBRECHT", "Mr SÉBASTIEN LIBERT"];

    private static readonly DateTime Aout14 = new(2026, 8, 14);

    /// <summary>
    /// Les débits du compte joint autour du 14/08/2026, tels qu'ils sont en base. Le 171,70 est la
    /// jambe du paiement STEONE, les autres sont là pour qu'un rapprochement réussi prouve qu'on a
    /// pris le bon et pas simplement quelque chose.
    /// </summary>
    private static TransferLeg[] DebitsDuCompteJoint() =>
    [
        new(482, new DateTime(2026, 8, 14), 171.70m, IsExpense: true, "Sebastien Jean R Libert"),
        new(483, new DateTime(2026, 8, 13), 130.90m, IsExpense: true, "Sebastien Jean R Libert"),
        new(484, new DateTime(2026, 8, 13), 32.70m, IsExpense: true, "Sebastien Jean R Libert"),
        new(530, new DateTime(2026, 8, 24), 100.00m, IsExpense: true, "Ensl"),
    ];

    private static TransferLeg? Chercher(
        decimal montant,
        DateTime date,
        bool trEnEntree = true,
        TransferLeg[]? candidats = null,
        ISet<int>? dejaPris = null) =>
        InternalTransferReconciler.FindMirror(
            montant, date, trEnEntree,
            candidats ?? DebitsDuCompteJoint(),
            Titulaires,
            dejaPris ?? new HashSet<int>());

    [Fact]
    public void FindMirror_CreditTrEtDebitBancaireDuMemeJour_TrouveLaBonneJambe()
    {
        // Le cas réel : la carte TR tire 171,70 € sur le compte joint le 14/08, TR livre le crédit,
        // GoCardless le débit, et le paiement chez STEONE arrive en plus. Trois lignes, un seul euro.
        var jambe = Chercher(171.70m, Aout14);

        Assert.NotNull(jambe);
        Assert.Equal(482, jambe!.Id);
    }

    [Fact]
    public void FindMirror_MontantProcheMaisPasEgal_NeRapprochePas()
    {
        // Le B&B Hotels du 13/08 : 130,98 € au débit de la carte, 130,90 € d'alimentation. Huit
        // centimes d'écart suffisent à en faire deux opérations distinctes, et le rapprochement
        // doit s'abstenir plutôt que d'arrondir.
        Assert.Null(Chercher(130.98m, new DateTime(2026, 8, 13)));
    }

    [Fact]
    public void FindMirror_ContrepartieCommercante_NeRapprochePas()
    {
        // Le garde-fou principal. Un débit de 100 € vers l'école existe le 24/08 ; si un mouvement
        // TR de 100 € tombait le même jour, le rapprocher effacerait une dépense réelle du bilan.
        Assert.Null(Chercher(100.00m, new DateTime(2026, 8, 24)));
    }

    [Fact]
    public void FindMirror_AuDelaDeTroisJours_NeRapprochePas()
    {
        // Deux montants identiques à une semaine d'écart n'ont plus de raison d'être la même
        // opération. La banque débite le jour même ou au plus tard après un week-end férié.
        Assert.Null(Chercher(171.70m, Aout14.AddDays(InternalTransferReconciler.MaxDayGap + 1)));
    }

    [Fact]
    public void FindMirror_WeekEnd_RapprocheJusquaTroisJours()
    {
        var jambe = Chercher(171.70m, Aout14.AddDays(InternalTransferReconciler.MaxDayGap));

        Assert.NotNull(jambe);
        Assert.Equal(482, jambe!.Id);
    }

    [Fact]
    public void FindMirror_MauvaisSens_NeRapprochePas()
    {
        // De l'argent qui sort de chez TR a pour miroir un crédit bancaire, pas un débit.
        // Sans ce contrôle, un retrait du courtier neutraliserait une dépense du compte joint.
        Assert.Null(Chercher(171.70m, Aout14, trEnEntree: false));
    }

    [Fact]
    public void FindMirror_JambeDejaPrise_NeLaSertPasDeuxFois()
    {
        // Deux alimentations du même montant le même jour ne peuvent pas neutraliser un seul débit
        // deux fois, sinon une dépense réelle disparaîtrait avec la seconde.
        Assert.Null(Chercher(171.70m, Aout14, dejaPris: new HashSet<int> { 482 }));
    }

    [Fact]
    public void FindMirror_PlusieursCandidats_PrendLePlusProcheDansLeTemps()
    {
        // Le résultat ne doit pas dépendre de l'ordre de lecture en base : deux syncs successives
        // doivent classer identiquement.
        TransferLeg[] candidats =
        [
            new(900, Aout14.AddDays(2), 50.00m, IsExpense: true, "Sebastien Jean R Libert"),
            new(901, Aout14, 50.00m, IsExpense: true, "Sebastien Jean R Libert"),
        ];

        var jambe = Chercher(50.00m, Aout14, candidats: candidats);

        Assert.NotNull(jambe);
        Assert.Equal(901, jambe!.Id);
    }

    [Fact]
    public void FindMirror_ADateEgale_PrendLePlusAncienEnBase()
    {
        TransferLeg[] candidats =
        [
            new(902, Aout14, 50.00m, IsExpense: true, "Sebastien Jean R Libert"),
            new(901, Aout14, 50.00m, IsExpense: true, "Sebastien Jean R Libert"),
        ];

        var jambe = Chercher(50.00m, Aout14, candidats: candidats);

        Assert.NotNull(jambe);
        Assert.Equal(901, jambe!.Id);
    }

    [Fact]
    public void FindMirror_DepenseDeVacancesSansMouvementTrCorrespondant_ResteIntacte()
    {
        // Ce qui protège les 1 300 € de vacances saisis à la main avant le 12/08/2026 : la timeline
        // TR ne remonte pas si loin, donc aucun crédit ne vient les réclamer. Le rapprochement
        // n'est déclenché que par un mouvement TR, et il exige le montant au centime.
        TransferLeg[] vacances =
        [
            new(503, new DateTime(2026, 8, 3), 380.00m, IsExpense: true, "Sebastien Jean R Libert"),
            new(490, new DateTime(2026, 8, 10), 192.44m, IsExpense: true, "Sebastien Jean R Libert"),
        ];

        Assert.Null(Chercher(171.70m, Aout14, candidats: vacances));
    }

    [Fact]
    public void FindMirror_MontantNul_NeRapprochePas()
    {
        Assert.Null(Chercher(0m, Aout14));
    }
}
