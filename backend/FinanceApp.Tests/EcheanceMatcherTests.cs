using FinanceApp.API.Services;
using Xunit;

namespace FinanceApp.Tests;

/// <summary>
/// Le rapprochement d'une échéance avec le virement qui la règle. Les cas viennent des factures de
/// l'école d'août 2026 : deux enfants, même IBAN, deux montants proches, et un placeholder de
/// communication qui échoue au contrôle 97.
/// </summary>
public class EcheanceMatcherTests
{
    private const string Ecole = "BE98068243670693";
    private const string Commune = "BE68539007547034";
    private static readonly string Com = StructuredCommunicationTests.Sc(2026080001);
    private static readonly string AutreCom = StructuredCommunicationTests.Sc(2026080002);

    private static readonly DateOnly Due = new(2026, 8, 31);

    private static PaymentCandidate C(int id, decimal amount, int daysFromDue, string? iban = Ecole, string? com = null) =>
        new(id, amount, Due.AddDays(daysFromDue).ToDateTime(new TimeOnly(9, 0)), iban, com);

    private static PaymentCandidate? Chercher(
        decimal? amount, string? iban, string? com, IEnumerable<PaymentCandidate> candidats,
        int? rejected = null, ISet<int>? dejaPris = null) =>
        EcheanceMatcher.FindPayment(Due, amount, iban, com, rejected, candidats, dejaPris ?? new HashSet<int>());

    [Fact]
    public void CommunicationEgale_RapprocheSansMontant()
    {
        var trouve = Chercher(null, null, Com, [C(1, 2.60m, 3, com: Com), C(2, 2.60m, 1)]);
        Assert.Equal(1, trouve?.Id);
    }

    [Fact]
    public void CommunicationEgale_MaisMontantDifferent_NeRapprochePas() =>
        Assert.Null(Chercher(2.60m, null, Com, [C(1, 2.61m, 0, com: Com)]));

    [Fact]
    public void CommunicationDifferente_NeRapprochePas() =>
        Assert.Null(Chercher(null, null, Com, [C(1, 2.60m, 0, com: AutreCom)]));

    [Fact]
    public void IbanEtMontant_Rapprochent()
    {
        var trouve = Chercher(2.60m, Ecole, null, [C(1, 1.40m, 0), C(2, 2.60m, 2), C(3, 2.60m, 4, iban: Commune)]);
        Assert.Equal(2, trouve?.Id);
    }

    [Fact]
    public void IbanSeul_SansMontant_NeRapprochePas() =>
        Assert.Null(Chercher(null, Ecole, null, [C(1, 2.60m, 0)]));

    [Fact]
    public void MontantSeul_SansIbanNiCommunication_NeRapprochePas() =>
        Assert.Null(Chercher(2.60m, null, null, [C(1, 2.60m, 0), C(2, 2.60m, 0, iban: null)]));

    [Fact]
    public void IbanDuCandidatAbsent_NeRapprochePas() =>
        Assert.Null(Chercher(2.60m, Ecole, null, [C(1, 2.60m, 0, iban: null)]));

    [Fact]
    public void FenetreOrdinaireDepassee_NeRapprochePas()
    {
        Assert.Null(Chercher(2.60m, Ecole, null, [C(1, 2.60m, -46)]));
        Assert.Null(Chercher(2.60m, Ecole, null, [C(1, 2.60m, 61)]));
        Assert.NotNull(Chercher(2.60m, Ecole, null, [C(1, 2.60m, -45)]));
        Assert.NotNull(Chercher(2.60m, Ecole, null, [C(1, 2.60m, 60)]));
    }

    [Fact]
    public void FenetreForte_AccepteCeQueLOrdinaireRefuse()
    {
        // À 120 jours, l'IBAN ne suffit plus, la communication oui.
        Assert.Null(Chercher(2.60m, Ecole, null, [C(1, 2.60m, 120)]));
        Assert.Equal(1, Chercher(2.60m, Ecole, Com, [C(1, 2.60m, 120, com: Com)])?.Id);
        Assert.Null(Chercher(2.60m, Ecole, Com, [C(1, 2.60m, 181, com: Com)]));
        Assert.Null(Chercher(2.60m, Ecole, Com, [C(1, 2.60m, -91, com: Com)]));
    }

    [Fact]
    public void Rejetee_EstEcartee_UneAutreConformePasse()
    {
        Assert.Null(Chercher(2.60m, Ecole, null, [C(1, 2.60m, 0)], rejected: 1));
        Assert.Equal(2, Chercher(2.60m, Ecole, null, [C(1, 2.60m, 0), C(2, 2.60m, 5)], rejected: 1)?.Id);
    }

    [Fact]
    public void DejaRevendiquee_EstEcartee()
    {
        var pris = new HashSet<int> { 1 };
        Assert.Equal(2, Chercher(2.60m, Ecole, null, [C(1, 2.60m, 0), C(2, 2.60m, 5)], dejaPris: pris)?.Id);
        Assert.Null(Chercher(2.60m, Ecole, null, [C(1, 2.60m, 0)], dejaPris: pris));
    }

    [Fact]
    public void CleForte_AvantLOrdinaire_PuisLaPlusProche_PuisLePlusPetitId()
    {
        // Ordinaire à J+0, forte à J+10 : la forte gagne malgré la distance.
        Assert.Equal(2, Chercher(2.60m, Ecole, Com, [C(1, 2.60m, 0), C(2, 2.60m, 10, com: Com)])?.Id);
        // Deux ordinaires : la plus proche.
        Assert.Equal(2, Chercher(2.60m, Ecole, null, [C(1, 2.60m, 7), C(2, 2.60m, -1)])?.Id);
        // Même distance : le plus petit Id.
        Assert.Equal(4, Chercher(2.60m, Ecole, null, [C(9, 2.60m, 2), C(4, 2.60m, -2)])?.Id);
    }

    [Fact]
    public void TriDeterministe_SurOrdreInverseDesCandidats()
    {
        PaymentCandidate[] candidats = [C(1, 2.60m, 7), C(2, 2.60m, -1), C(3, 2.60m, 30, com: Com), C(4, 2.60m, 1)];
        var direct = Chercher(2.60m, Ecole, Com, candidats);
        var inverse = Chercher(2.60m, Ecole, Com, candidats.Reverse());
        Assert.Equal(3, direct?.Id);
        Assert.Equal(direct, inverse);

        var sansCom = Chercher(2.60m, Ecole, null, candidats);
        Assert.Equal(sansCom, Chercher(2.60m, Ecole, null, candidats.Reverse()));
        Assert.Equal(2, sansCom?.Id);
    }

    [Fact]
    public void AucunCandidat_RendNull() =>
        Assert.Null(Chercher(2.60m, Ecole, Com, []));
}
