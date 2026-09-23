using FinanceApp.API.Services.Mail;
using Xunit;
using static FinanceApp.Tests.MailIngestTestSupport;

namespace FinanceApp.Tests;

/// <summary>Les trois verrous du tri, un par un, et l'année fiscale d'après la date du mail. Zéro I/O.</summary>
public class MailIngestRulesTests
{
    private static readonly MailIngestOptions Opts = Options(dashboardId: 2);

    [Fact]
    public void ExpediteurHorsListe_EstRefuse()
    {
        var decision = MailIngestRules.Decide(Mail("1", from: "quelqu-un@ailleurs.invalid"), Opts);
        Assert.False(decision.Accepted);
        Assert.Equal(MailRejectReason.ExpediteurInconnu, decision.Reason);
    }

    [Fact]
    public void FromAvecNomAffiche_EstAccepte_SurLAdresseEntreChevrons()
    {
        Assert.True(MailIngestRules.Decide(Mail("1", from: "École <ind2@proecoles.be>"), Opts).Accepted);
        Assert.True(MailIngestRules.Decide(Mail("2", from: "IND2@PROECOLES.BE"), Opts).Accepted);
        Assert.True(MailIngestRules.Decide(Mail("3", from: "  ind2@elmarche.be  "), Opts).Accepted);
        // Le nom affiché ne fait pas foi : l'adresse réelle est ailleurs.
        Assert.False(MailIngestRules.Decide(Mail("4", from: "ind2@proecoles.be <usurpateur@ailleurs.invalid>"), Opts).Accepted);
    }

    [Fact]
    public void SansXForwardedFor_EstRefuse()
    {
        var decision = MailIngestRules.Decide(Mail("1", forwardedFor: null), Opts);
        Assert.False(decision.Accepted);
        Assert.Equal(MailRejectReason.PasTransfere, decision.Reason);

        var autre = MailIngestRules.Decide(Mail("2", forwardedFor: "quelqu-un@ailleurs.invalid " + Mailbox), Opts);
        Assert.Equal(MailRejectReason.PasTransfere, autre.Reason);
    }

    [Fact]
    public void XForwardedFor_AvecLaBonneAdresseAuMilieu_EstAccepte()
    {
        var mail = Mail("1", forwardedFor: "premier@ailleurs.invalid SB.Dupont@gmail.com " + Mailbox);
        Assert.True(MailIngestRules.Decide(mail, Opts).Accepted);
    }

    [Fact]
    public void ObjetSansMot_EstRefuse()
    {
        var decision = MailIngestRules.Decide(Mail("1", subject: "Semaine de la mobilité"), Opts);
        Assert.False(decision.Accepted);
        Assert.Equal(MailRejectReason.ObjetHorsListe, decision.Reason);
        Assert.False(MailIngestRules.Decide(Mail("2", subject: "Information poux"), Opts).Accepted);
    }

    [Theory]
    [InlineData("Décompte septembre")]
    [InlineData("DECOMPTE SEPTEMBRE")]
    [InlineData("decompte")]
    [InlineData("Rappel de paiement")]
    [InlineData("Votre facture est disponible")]
    public void ObjetAvecUnMot_SansCasseNiAccents_EstAccepte(string subject)
    {
        Assert.True(MailIngestRules.Decide(Mail("1", subject: subject), Opts).Accepted);
    }

    [Fact]
    public void LesVerrous_SontDansLOrdre_ExpediteurPuisTransfertPuisObjet()
    {
        // Tout faux : c'est l'expéditeur qui est nommé, pas l'objet.
        var tout = MailIngestRules.Decide(Mail("1", from: "x@ailleurs.invalid", forwardedFor: null, subject: "poux"), Opts);
        Assert.Equal(MailRejectReason.ExpediteurInconnu, tout.Reason);
        var deux = MailIngestRules.Decide(Mail("2", forwardedFor: null, subject: "poux"), Opts);
        Assert.Equal(MailRejectReason.PasTransfere, deux.Reason);
    }

    [Fact]
    public void FiscalYear_Janvier_RangeEnNMoins1()
    {
        var zone = AgendaTestSupport.Brussels;
        Assert.Equal(2026, MailIngestRules.FiscalYearFor(new DateTimeOffset(2027, 1, 15, 10, 0, 0, TimeSpan.FromHours(1)), zone));
        Assert.Equal(2026, MailIngestRules.FiscalYearFor(new DateTimeOffset(2026, 9, 4, 9, 0, 0, TimeSpan.FromHours(2)), zone));
        // 31 décembre 23h30 UTC, c'est déjà le 1er janvier à Bruxelles : janvier, donc l'année du 31 décembre.
        Assert.Equal(2026, MailIngestRules.FiscalYearFor(new DateTimeOffset(2026, 12, 31, 23, 30, 0, TimeSpan.Zero), zone));
        // Et le 1er janvier 23h30 UTC en heure locale reste en janvier.
        Assert.Equal(2026, MailIngestRules.FiscalYearFor(new DateTimeOffset(2027, 1, 1, 23, 30, 0, TimeSpan.Zero), zone));
        // Février reprend l'année en cours.
        Assert.Equal(2027, MailIngestRules.FiscalYearFor(new DateTimeOffset(2027, 2, 1, 0, 30, 0, TimeSpan.Zero), zone));
    }

    [Fact]
    public void Fold_RetireCasseEtAccents()
    {
        Assert.Equal("decompte", MailIngestRules.Fold("DÉCOMPTE"));
        Assert.Equal("ecole elmarche", MailIngestRules.Fold("École Elmarche"));
        Assert.Equal(string.Empty, MailIngestRules.Fold(null));
    }

    [Fact]
    public void AddressOf_PrendLaPartieEntreChevrons_OuToutLeChamp()
    {
        Assert.Equal("ind2@proecoles.be", MailIngestRules.AddressOf("\"École\" <ind2@proecoles.be>"));
        Assert.Equal("ind2@proecoles.be", MailIngestRules.AddressOf(" ind2@proecoles.be "));
        Assert.Equal("a@b.c", MailIngestRules.AddressOf("Un <a@b.c>, Deux <d@e.f>"));
        Assert.Equal(string.Empty, MailIngestRules.AddressOf(""));
    }
}
