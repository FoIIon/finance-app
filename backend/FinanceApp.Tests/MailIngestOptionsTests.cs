using FinanceApp.API.Services.Mail;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FinanceApp.Tests;

/// <summary>
/// Section MailIngest absente : IsConfigured faux et validateur muet, c'est le poste de développement et
/// la suite E2E. Section présente : chaque clé manquante est nommée dans le refus, aucune valeur n'y figure.
/// </summary>
public class MailIngestOptionsTests
{
    private static MailIngestOptions Bind(params (string Key, string Value)[] entries)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(entries.ToDictionary(e => e.Key, e => (string?)e.Value))
            .Build();
        var options = new MailIngestOptions();
        config.GetSection(MailIngestOptions.SectionName).Bind(options);
        return options;
    }

    [Fact]
    public void SectionAbsente_NEstPasConfiguree_EtLeValidateurEstMuet()
    {
        var options = Bind(("Calendar:MaxBytes", "1000"));
        Assert.False(options.IsConfigured);
        Assert.True(new MailIngestOptionsValidator().Validate(null, options).Succeeded);

        var vide = Bind(("MailIngest:Host", ""), ("MailIngest:Port", "993"));
        Assert.False(vide.IsConfigured);
        Assert.True(new MailIngestOptionsValidator().Validate(null, vide).Succeeded);
    }

    [Fact]
    public void HostPose_SansMotDePasse_EstRefuse_AvecLeNomDeLaCle_SansLesValeurs()
    {
        var options = Bind(
            ("MailIngest:Host", "imap.test.invalid"),
            ("MailIngest:Port", "993"),
            ("MailIngest:User", "boite-factures@test.invalid"),
            ("MailIngest:ForwardedFrom", "sb.dupont@gmail.com"),
            ("MailIngest:AllowedSenders:0", "ind2@proecoles.be"),
            ("MailIngest:DashboardId", "2"));
        Assert.True(options.IsConfigured);

        var result = new MailIngestOptionsValidator().Validate(null, options);
        Assert.True(result.Failed);
        var failures = string.Join(" | ", result.Failures!);
        Assert.Contains("MailIngest:Password", failures);
        Assert.DoesNotContain("imap.test.invalid", failures);
        Assert.DoesNotContain("boite-factures", failures);
        Assert.DoesNotContain("sb.dupont", failures);
        Assert.DoesNotContain("proecoles", failures);
    }

    [Fact]
    public void ChaqueCleManquante_EstNommee()
    {
        var options = Bind(("MailIngest:Host", "imap.test.invalid"), ("MailIngest:Port", "70000"), ("MailIngest:AllowedSenders:0", ""));
        var result = new MailIngestOptionsValidator().Validate(null, options);
        Assert.True(result.Failed);
        var failures = result.Failures!.ToList();
        Assert.Contains(failures, f => f.StartsWith("MailIngest:Port"));
        Assert.Contains(failures, f => f.StartsWith("MailIngest:User"));
        Assert.Contains(failures, f => f.StartsWith("MailIngest:Password"));
        Assert.Contains(failures, f => f.StartsWith("MailIngest:ForwardedFrom"));
        Assert.Contains(failures, f => f.StartsWith("MailIngest:AllowedSenders"));
        Assert.Contains(failures, f => f.StartsWith("MailIngest:DashboardId"));
    }

    [Fact]
    public void SectionComplete_Passe_EtLaListeConfigureeEstLaListeEffective()
    {
        var options = Bind(
            ("MailIngest:Host", "imap.test.invalid"),
            ("MailIngest:Port", "993"),
            ("MailIngest:User", "boite-factures@test.invalid"),
            ("MailIngest:Password", "un-mot-de-passe-d-application"),
            ("MailIngest:ForwardedFrom", "sb.dupont@gmail.com"),
            ("MailIngest:AllowedSenders:0", "ind2@proecoles.be"),
            ("MailIngest:AllowedSenders:1", "ind2@elmarche.be"),
            ("MailIngest:DashboardId", "2"));
        Assert.True(new MailIngestOptionsValidator().Validate(null, options).Succeeded);
        Assert.Equal(new[] { "ind2@proecoles.be", "ind2@elmarche.be" }, options.AllowedSenders);
        Assert.Equal(2, options.DashboardId);
    }
}
