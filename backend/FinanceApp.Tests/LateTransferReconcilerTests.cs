using FinanceApp.API.Services;
using Xunit;

namespace FinanceApp.Tests;

/// <summary>
/// Les virements internes que l'import Trade Republic ne voit pas. Les cas viennent du 24/09/2026 :
/// 211,53 € de paiements carte TR remboursés depuis la CBC, puis la CBC remboursée depuis le joint
/// Argenta, soit 423,06 € de fausses dépenses sur septembre.
/// </summary>
public class LateTransferReconcilerTests
{
    private static readonly string[] Titulaires = ["DUPONT - MARTIN", "Sebastien Jean R Dupont"];

    private static readonly DateTime Sept24 = new(2026, 9, 24);
    private static readonly DateTime ImportTr = new(2026, 9, 24, 13, 6, 34);
    private static readonly DateTime ImportCbc = new(2026, 9, 24, 14, 44, 46);

    private static BrokerTransferLine ArriveeTr(int id, decimal montant, DateTime? importee = null) =>
        new(id, Sept24, montant, IsIncome: true, importee ?? ImportTr);

    private static LateBankLeg DebitCbc(
        int id,
        decimal montant,
        DateTime? importee = null,
        bool neutralise = false,
        bool manuel = false,
        string contrepartie = "SEBASTIEN JEAN R DUPONT") =>
        new(id, Sept24, montant, IsExpense: true, contrepartie, importee ?? ImportCbc, neutralise, manuel);

    [Fact]
    public void FindLateLegs_DebitsCbcImportesApresLesArriveesTr_SontNeutralises()
    {
        // Le cas réel : sept arrivées TR à 13h06, sept débits CBC à 14h44, montants au centime.
        var tr = new[]
        {
            ArriveeTr(2322, 17.80m), ArriveeTr(2323, 2.85m), ArriveeTr(2324, 39.58m), ArriveeTr(2325, 15.84m),
            ArriveeTr(2326, 21.01m), ArriveeTr(2327, 110.40m), ArriveeTr(2328, 4.05m),
        };
        var cbc = new[]
        {
            DebitCbc(2335, 17.80m), DebitCbc(2336, 2.85m), DebitCbc(2337, 39.58m), DebitCbc(2338, 15.84m),
            DebitCbc(2339, 21.01m), DebitCbc(2340, 110.40m), DebitCbc(2341, 4.05m),
            // Une vraie dépense du même jour et d'un montant présent, chez un commerçant : elle reste.
            DebitCbc(2350, 4.05m, contrepartie: "WEX SANDWICHS"),
        };

        var result = LateTransferReconciler.FindLateLegs(tr, cbc, Titulaires);

        Assert.Equal(new[] { 2335, 2336, 2337, 2338, 2339, 2340, 2341 }, result.OrderBy(i => i));
    }

    [Fact]
    public void FindLateLegs_JambeEnBaseAvantLaLigneTr_NEstPasTouchee()
    {
        // Le Leclerc drive du 12/08 : le débit bancaire était là avant la ligne TR, le rapprochement de
        // l'import a décidé de le garder en dépense. La reprise ne doit pas revenir sur cette décision.
        var tr = new[] { ArriveeTr(1, 60.62m, importee: ImportCbc) };
        var cbc = new[] { DebitCbc(2, 60.62m, importee: ImportTr) };

        Assert.Empty(LateTransferReconciler.FindLateLegs(tr, cbc, Titulaires));
    }

    [Fact]
    public void FindLateLegs_LigneTrDejaRapprochee_NeNeutralisePasUneSecondeJambe()
    {
        // Deux débits de 19,90 au même nom, dont un déjà rapproché à l'import TR. Une seule ligne TR :
        // le second débit est une autre opération et doit rester visible.
        var tr = new[] { ArriveeTr(1, 19.90m) };
        var cbc = new[] { DebitCbc(10, 19.90m, neutralise: true), DebitCbc(11, 19.90m) };

        Assert.Empty(LateTransferReconciler.FindLateLegs(tr, cbc, Titulaires));
    }

    [Fact]
    public void FindLateLegs_CategoriePoseeALaMain_NEstPasTouchee()
    {
        var tr = new[] { ArriveeTr(1, 50.00m) };
        var cbc = new[] { DebitCbc(10, 50.00m, manuel: true) };

        Assert.Empty(LateTransferReconciler.FindLateLegs(tr, cbc, Titulaires));
    }

    [Fact]
    public void FindLateLegs_DeuxArriveesDuMemeMontant_PrennentChacuneLeurJambe()
    {
        var tr = new[] { ArriveeTr(1, 4.05m), ArriveeTr(2, 4.05m) };
        var cbc = new[] { DebitCbc(10, 4.05m), DebitCbc(11, 4.05m), DebitCbc(12, 4.05m) };

        var result = LateTransferReconciler.FindLateLegs(tr, cbc, Titulaires);

        Assert.Equal(new[] { 10, 11 }, result.OrderBy(i => i));
    }

    private static readonly (string Iban, bool IsPersonal)[] Comptes =
    [
        ("BE19973176751212", false), // joint Argenta
        ("BE42732050362754", false), // CBC
        ("BE10979641916804", true),  // Argenta perso
    ];

    [Fact]
    public void IsSameScopeTransfer_JointArgentaVersCbc_EstUnVirementInterne()
    {
        // Le 211,53 « Remboursement seb trade republic » du 24/09, dans les deux sens.
        Assert.True(LateTransferReconciler.IsSameScopeTransfer("BE19973176751212", false, "BE42 7320 5036 2754", Comptes));
        Assert.True(LateTransferReconciler.IsSameScopeTransfer("BE42732050362754", false, "BE19973176751212", Comptes));
    }

    [Fact]
    public void IsSameScopeTransfer_PaiementCarteQuiPorteLIbanDuCompte_NEnEstPasUn()
    {
        // La CBC sert son propre IBAN en contrepartie des paiements Bancontact (Colruyt du 30/08).
        Assert.False(LateTransferReconciler.IsSameScopeTransfer("BE42732050362754", false, "BE42732050362754", Comptes));
    }

    [Fact]
    public void IsSameScopeTransfer_JointVersPerso_ResteUnApport()
    {
        // L'ordre permanent de 830 vers le compte perso change de périmètre, il garde sa catégorie.
        Assert.False(LateTransferReconciler.IsSameScopeTransfer("BE42732050362754", false, "BE10979641916804", Comptes));
    }

    [Fact]
    public void IsSameScopeTransfer_BalayageVersLeLivret_ResteEnEpargne()
    {
        // Le livret Argenta est un compte manuel, absent de la liste : « Gros chat » garde sa catégorie.
        Assert.False(LateTransferReconciler.IsSameScopeTransfer("BE19973176751212", false, "BE83973176751515", Comptes));
    }

    [Fact]
    public void IsSameScopeTransfer_SansIbanDeContrepartie_Faux()
    {
        Assert.False(LateTransferReconciler.IsSameScopeTransfer("BE19973176751212", false, null, Comptes));
    }
}
