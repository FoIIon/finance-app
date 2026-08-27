using FinanceApp.API.Models;
using FinanceApp.API.Services;
using Xunit;

namespace FinanceApp.Tests;

/// <summary>
/// Le classement d'une ligne de timeline Trade Republic. Les cas viennent des 31 lignes réellement
/// importées en prod le 25/08/2026, celles qui ont produit le triple comptage d'août.
/// </summary>
public class TradeRepublicTimelineClassifierTests
{
    /// <summary>Les noms tels que les banques les écrivent en prod : compte joint, compte perso.</summary>
    private static readonly string[] Titulaires = ["LIBERT - LAMBRECHT", "Mr SÉBASTIEN LIBERT", "LIBERT S & LAMBRECHT A"];

    /// <summary>Le portefeuille Trade Republic réel, réduit aux lignes qui comptent pour ces cas.</summary>
    private static Investment[] Portefeuille() =>
    [
        new() { Id = 2, Name = "Physical Gold USD (Acc)", Kind = InvestmentKind.Security },
        new() { Id = 5, Name = "FTSE All-World USD (Acc)", Kind = InvestmentKind.Security },
        new() { Id = 9, Name = "Apple", Kind = InvestmentKind.Security },
        new() { Id = 11, Name = "Bitcoin", Kind = InvestmentKind.Crypto },
        new() { Id = 1, Name = "Dec 2026", Kind = InvestmentKind.Bond },
    ];

    private static TrLineKind Classify(string title, string? eventType = null) =>
        TradeRepublicTimelineClassifier.Classify(
            title, eventType, Titulaires,
            TradeRepublicTimelineClassifier.UnambiguousInstrumentNames(Portefeuille()));

    // ---------------------------------------------------------------- mouvements internes

    [Fact]
    public void Classify_CreditAuNomDuCompteJoint_EstUnVirementInterne()
    {
        // Les huit crédits d'alimentation d'août 2026 (606,26 € au total). Rangés en Épargne par
        // une règle de catégorisation, ils se comportaient comme des retraits d'épargne et
        // faisaient tomber les mises de côté du mois de 1 524,92 à 918,66.
        Assert.Equal(TrLineKind.InternalTransfer, Classify("LIBERT - LAMBRECHT"));
    }

    [Fact]
    public void Classify_DepotAuNomDuTitulaire_IgnoreAccentsEtCivilite()
    {
        // Le dépôt de 720 € du 17/08/2026. TR écrit « SEBASTIEN LIBERT », la banque
        // « Mr SÉBASTIEN LIBERT ». Sans normalisation, les deux noms ne se reconnaissent pas.
        Assert.Equal(TrLineKind.InternalTransfer, Classify("SEBASTIEN LIBERT"));
    }

    [Fact]
    public void Classify_LibelleAvecInitialesEtSeparateur_ReconnaitLeCouple()
    {
        // « LIBERT S + LAMBRECHT A » côté banque, « LIBERT S & LAMBRECHT A » côté relevé :
        // les initiales isolées et le séparateur changent, les deux patronymes non.
        Assert.Equal(TrLineKind.InternalTransfer, Classify("LIBERT S + LAMBRECHT A"));
    }

    [Fact]
    public void Classify_CommercantQuiPartageUnSeulPatronyme_ResteUnFlux()
    {
        // Le garde-fou qui compte le plus. Escamoter une dépense réelle en la prenant pour un
        // virement la fait disparaître du bilan sans laisser de trace, alors qu'un doublon
        // se voit et se corrige. Un seul jeton commun ne suffit donc jamais.
        Assert.Equal(TrLineKind.Flow, Classify("Boulangerie Libert"));
    }

    // ---------------------------------------------------------------- titres

    [Fact]
    public void Classify_Crypto_EstUnInvestissement()
    {
        // Bitcoin 200 € le 17/08/2026, compté en dépense variable du ménage.
        Assert.Equal(TrLineKind.Investment, Classify("Bitcoin"));
    }

    [Fact]
    public void Classify_PartCapitalisante_EstUnInvestissement()
    {
        // FTSE All-World 8,56 et 1,94 €, MSCI World ESG 30 € : 240,50 € d'achats de titres
        // rangés en dépenses courantes sur le seul mois d'août.
        Assert.Equal(TrLineKind.Investment, Classify("FTSE All-World USD (Acc)"));
    }

    [Fact]
    public void Classify_NomDInstrumentAussiPorteParUnCommercant_ResteUnFlux()
    {
        // Le piège Apple : l'action est au portefeuille et le remboursement App Store de 1,20 €
        // du 13/08/2026 n'est pas une vente d'action. Un nom sobre reste ambigu, donc hors
        // reconnaissance par libellé. Sans eventType, un vrai achat d'action Apple tombe en
        // Autres et se trie à la main : c'est le sens du compromis.
        Assert.Equal(TrLineKind.Flow, Classify("Apple"));
    }

    [Fact]
    public void Classify_ObligationAuNomSobre_ResteUnFlux()
    {
        // « Dec 2026 » est une obligation du portefeuille, et un libellé que n'importe quoi
        // pourrait porter. Même raisonnement que pour Apple.
        Assert.Equal(TrLineKind.Flow, Classify("Dec 2026"));
    }

    // ---------------------------------------------------------------- flux ordinaires

    [Fact]
    public void Classify_Commercant_EstUnFlux()
    {
        Assert.Equal(TrLineKind.Flow, Classify("STEONE"));
        Assert.Equal(TrLineKind.Flow, Classify("B&B Hotels"));
        Assert.Equal(TrLineKind.Flow, Classify("Anthropic"));
    }

    // ---------------------------------------------------------------- eventType

    [Fact]
    public void Classify_EventTypeCarte_PrimeSurLeLibelle()
    {
        // Quand TR dit que la ligne est un paiement carte, c'est un paiement carte, même si le
        // commerçant s'appelle comme un instrument du portefeuille. L'eventType est la source
        // la plus sûre, le libellé n'est qu'un repli.
        Assert.Equal(TrLineKind.Flow, Classify("Bitcoin", "CARD_TRANSACTION"));
    }

    [Fact]
    public void Classify_PaiementCarteRefuse_NEstPasImporte()
    {
        // Aucun euro n'a bougé. L'importer gonflerait les dépenses du montant refusé.
        Assert.Equal(TrLineKind.Ignore, Classify("STEONE", "card_failed_transaction"));
    }

    [Fact]
    public void Classify_EventTypeAlimentation_PrimeSurLeLibelle()
    {
        // Un virement entrant reste un virement entrant même si son libellé nomme un tiers.
        Assert.Equal(TrLineKind.InternalTransfer, Classify("Virement recu", "PAYMENT_INBOUND_SEPA_DIRECT_DEBIT"));
    }

    [Fact]
    public void Classify_EventTypeOrdreOuPlan_EstUnInvestissement()
    {
        Assert.Equal(TrLineKind.Investment, Classify("Apple", "SAVINGS_PLAN_EXECUTED"));
        Assert.Equal(TrLineKind.Investment, Classify("Apple", "trading_trade_executed"));
        Assert.Equal(TrLineKind.Investment, Classify("Apple", "benefits_saveback_execution"));
    }

    [Fact]
    public void Classify_InteretsEtDividendes_SontDesFlux()
    {
        // De l'argent qui entre pour de bon, contrairement à une alimentation de compte.
        Assert.Equal(TrLineKind.Flow, Classify("Interets", "INTEREST_PAYOUT"));
    }

    [Fact]
    public void Classify_EventTypeInconnu_RetombeSurLesReglesDeNom()
    {
        // TR fait évoluer son vocabulaire. Un type inconnu ne doit pas figer la ligne en flux
        // alors que son libellé la désigne clairement : le repli reste actif.
        Assert.Equal(TrLineKind.InternalTransfer, Classify("LIBERT - LAMBRECHT", "un_type_que_TR_inventera"));
        Assert.Equal(TrLineKind.Investment, Classify("Bitcoin", "un_type_que_TR_inventera"));
    }

    // ---------------------------------------------------------------- sélection des instruments

    [Fact]
    public void UnambiguousInstrumentNames_NeGardeQueCryptosEtPartsSuffixees()
    {
        var noms = TradeRepublicTimelineClassifier.UnambiguousInstrumentNames(Portefeuille());

        Assert.Contains("Bitcoin", noms);
        Assert.Contains("FTSE All-World USD (Acc)", noms);
        Assert.Contains("Physical Gold USD (Acc)", noms);
        Assert.DoesNotContain("Apple", noms);
        Assert.DoesNotContain("Dec 2026", noms);
    }
}
