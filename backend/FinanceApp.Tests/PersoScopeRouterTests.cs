using FinanceApp.API.Models;
using FinanceApp.API.Services;
using Xunit;

namespace FinanceApp.Tests;

/// <summary>
/// Le routage perso/commun d'une transaction. Les cas viennent des lignes réelles d'août 2026 : le
/// compte Argenta perso connecté le 05/08, la carte Trade Republic à usage mixte, l'ordre permanent
/// des 830 €. Règle posée le 27/08 : commun par défaut, le perso se désigne explicitement.
/// Depuis le 28/08, le périmètre se lit sur la règle de catégorisation gagnante, pas sur une seconde
/// recherche de mots-clés.
/// </summary>
public class PersoScopeRouterTests
{
    /// <summary>Les abos perso de Sébastien, payés carte TR et non remboursés depuis le commun.</summary>
    private static readonly List<CategoryRule> Regles =
    [
        new() { Id = 1, Keyword = "ORANGE BELGIUM", CategoryId = 12, RouteToPerso = false },
        new() { Id = 2, Keyword = "Anthropic", CategoryId = 20, RouteToPerso = true },
        new() { Id = 3, Keyword = "Orange", CategoryId = 20, RouteToPerso = true },
        new() { Id = 4, Keyword = "Colruyt", CategoryId = 7, RouteToPerso = false },
    ];

    private static TransactionScope Route(bool bankPerso, string? externalId, TransactionType type, string description)
    {
        var ordered = Regles.OrderByDescending(r => r.Keyword.Length).ThenBy(r => r.Id);
        var matched = CategoryRuleMatcher.FirstMatch(ordered, description, description);
        return PersoScopeRouter.Decide(bankPerso, externalId, type, matched);
    }

    // ---------------------------------------------------------------- compte bancaire perso

    [Fact]
    public void CompteBancairePerso_ToujoursPerso_QuelQueSoitLeSens()
    {
        // Les 830 € qui arrivent sur l'Argenta perso (jambe entrante, id 522). Elle ne doit jamais
        // compter en revenu commun : c'est côté Perso qu'elle vit.
        Assert.Equal(TransactionScope.Perso, Route(true, "gc-522", TransactionType.Income, ""));
        // Le dépôt de 720 € vers TR sortant du perso (id 521).
        Assert.Equal(TransactionScope.Perso, Route(true, "gc-521", TransactionType.Expense, "Sebastien Jean R Libert"));
    }

    [Fact]
    public void CompteBancairePerso_PrimeSurLAbsenceDeRegle()
    {
        // Aucune règle perso ne matche « Courses », mais le compte est perso : le compte tranche.
        Assert.Equal(TransactionScope.Perso, Route(true, "gc-999", TransactionType.Expense, "Courses"));
    }

    // ---------------------------------------------------------------- carte Trade Republic

    [Fact]
    public void DepenseTrMatchantUneReglePerso_EstPerso()
    {
        // Anthropic 21,78 (id 580) et Orange 10,00 (id 552), les deux abos perso d'août.
        Assert.Equal(TransactionScope.Perso, Route(false, "tr-580", TransactionType.Expense, "Anthropic"));
        Assert.Equal(TransactionScope.Perso, Route(false, "tr-552", TransactionType.Expense, "Orange"));
    }

    [Fact]
    public void DepenseTrSansReglePerso_ResteCommune_MemeNonRemboursee()
    {
        // STEONE, Colruyt, VINCI : des achats communs faits carte TR. Certains ne sont pas remboursés
        // au centime au moment du bilan. Ils restent communs : on ne fait jamais disparaître une
        // dépense commune en la devinant perso.
        Assert.Equal(TransactionScope.Common, Route(false, "tr-565", TransactionType.Expense, "STEONE"));
        Assert.Equal(TransactionScope.Common, Route(false, "tr-549", TransactionType.Expense, "Colruyt"));
        Assert.Equal(TransactionScope.Common, Route(false, "tr-563", TransactionType.Expense, "VINCI Autoroutes"));
    }

    [Fact]
    public void RegleCommuneLongue_BatRegleCourtePerso()
    {
        // Revue du 28/08 : « Orange » (perso) est un sous-mot de « ORANGE BELGIUM » (règle commune du
        // jeu par défaut). La règle la plus longue catégorise, et c'est elle qui route : la facture
        // commune payée carte TR reste au Commun.
        Assert.Equal(TransactionScope.Common, Route(false, "tr-600", TransactionType.Expense, "ORANGE BELGIUM SA"));
    }

    [Fact]
    public void RevenuTr_ResteCommun_MemeSiUneRegleMatche()
    {
        // Un revenu TR (dividende, intérêts) n'est pas un achat perso. Le remboursement Apple de 1,20 €
        // du 13/08 est un dividende en espèces, pas une dépense : il ne se route pas côté Perso.
        Assert.Equal(TransactionScope.Common, Route(false, "tr-571", TransactionType.Income, "Anthropic"));
    }

    // ---------------------------------------------------------------- garde-fous

    [Fact]
    public void DepenseBancaireCommune_MatchantUneReglePerso_ResteCommune()
    {
        // Une facture Orange domiciliée sur le compte joint n'est pas une ligne TR : la règle perso ne
        // s'applique qu'à la carte Trade Republic. Sans ce garde, un abo commun basculerait en perso.
        Assert.Equal(TransactionScope.Common, Route(false, "gc-300", TransactionType.Expense, "Orange"));
    }

    [Fact]
    public void JambeSortanteDes830_SurCompteJoint_ResteUneDepenseCommune()
    {
        // Les 830 € qui partent du compte joint (id 527, ordre permanent). Compte commun, pas une ligne
        // TR : reste une dépense du Commun. Sa jambe entrante part côté Perso par le compte perso.
        Assert.Equal(TransactionScope.Common,
            Route(false, "gc-527", TransactionType.Expense, "LIBERT SEBASTIEN Ordre permanent"));
    }

    [Fact]
    public void SansRegleGagnante_ToutEstCommunSaufCompteBancairePerso()
    {
        var scope = PersoScopeRouter.Decide(
            bankAccountIsPersonal: false, externalId: "tr-580", type: TransactionType.Expense, matchedRule: null);
        Assert.Equal(TransactionScope.Common, scope);
    }

    // ---------------------------------------------------------------- titres Trade Republic

    [Fact]
    public void MouvementDeTitresTr_EstPerso_DansLesDeuxSens()
    {
        // Les 200 € de Bitcoin du 17/08 (id 556) : la catégorie Investissement est forcée à l'import,
        // aucune règle ne matche, et pourtant le portefeuille est celui de Sébastien.
        var achat = PersoScopeRouter.Decide(false, "tr-556", TransactionType.Expense, matchedRule: null, isInvestment: true);
        Assert.Equal(TransactionScope.Perso, achat);
        var vente = PersoScopeRouter.Decide(false, "tr-557", TransactionType.Income, matchedRule: null, isInvestment: true);
        Assert.Equal(TransactionScope.Perso, vente);
    }

    [Fact]
    public void MouvementDeTitres_HorsTr_ResteCommun()
    {
        // Un achat de titres saisi à la main ou venu d'une banque GoCardless ne bascule pas : la règle
        // ne parle que du portefeuille Trade Republic.
        var scope = PersoScopeRouter.Decide(false, "gc-42", TransactionType.Expense, matchedRule: null, isInvestment: true);
        Assert.Equal(TransactionScope.Common, scope);
    }

    [Fact]
    public void LignesTr_ReconnuesParLePrefixe()
    {
        Assert.True(PersoScopeRouter.IsTradeRepublicLine($"{PersoScopeRouter.TradeRepublicExternalIdPrefix}42"));
        Assert.False(PersoScopeRouter.IsTradeRepublicLine("gc-42"));
        Assert.False(PersoScopeRouter.IsTradeRepublicLine(null));
    }
}
