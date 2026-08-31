using System.Text.Json;
using FinanceApp.API.Services;
using Xunit;

namespace FinanceApp.Tests;

/// <summary>
/// L'IBAN du bénéficiaire, lu du payload GoCardless puis utilisable en règle. Ajouté le 31/08/2026 :
/// les paiements à la commune de Marche arrivent sous deux libellés, avec un libellé de virement vide,
/// et rien ne permettait de séparer la crèche de Léonie du reste.
/// </summary>
public class CounterpartyIbanTests
{
    private static JsonElement Payload(string json) => JsonDocument.Parse(json).RootElement.Clone();

    // ---------------------------------------------------------------- extraction

    [Fact]
    public void Depense_LitLeCompteDuCrediteur()
    {
        var tx = Payload("""
        {
          "creditorName": "ADMINISTRATION COMMUNALE DE MARCHE-",
          "creditorAccount": { "iban": "BE38 0910 0055 4030" }
        }
        """);
        // Normalisé : sans espaces, en majuscules, pour se comparer à l'identique.
        Assert.Equal("BE38091000554030", GoCardlessTransactionFields.CounterpartyIban(tx));
    }

    [Fact]
    public void Revenu_LitLeCompteDuDebiteur()
    {
        var tx = Payload("""{ "debtorName": "AUDREY LAMBRECHT", "debtorAccount": { "iban": "be42732050362754" } }""");
        Assert.Equal("BE42732050362754", GoCardlessTransactionFields.CounterpartyIban(tx));
    }

    [Fact]
    public void ANumeroNational_FauteDIban()
    {
        var tx = Payload("""{ "creditorAccount": { "bban": "091-0005540-30" } }""");
        Assert.Equal("091-0005540-30", GoCardlessTransactionFields.CounterpartyIban(tx));
    }

    [Fact]
    public void PaiementCarte_PasDeCompte()
    {
        // Un achat en magasin n'a pas de compte bénéficiaire dans le payload : c'est attendu, pas un bug.
        var tx = Payload("""{ "creditorName": "DECATHLON MARCHE EN FA MARCHE EN FA" }""");
        Assert.Null(GoCardlessTransactionFields.CounterpartyIban(tx));
    }

    [Fact]
    public void ChampVideOuNul_TraiteCommeAbsent()
    {
        Assert.Null(GoCardlessTransactionFields.CounterpartyIban(Payload("""{ "creditorAccount": { "iban": "" } }""")));
        Assert.Null(GoCardlessTransactionFields.CounterpartyIban(Payload("""{ "creditorAccount": null }""")));
        Assert.Null(GoCardlessTransactionFields.CounterpartyIban(Payload("""{ "creditorAccount": { "iban": null } }""")));
    }

    // ---------------------------------------------------------------- règles

    [Fact]
    public void UneRegleIban_RafleLesDeuxLibellesDuMemeBeneficiaire()
    {
        // Le cas qui a motivé le champ : même IBAN, deux orthographes selon le mois.
        Assert.True(CategoryRuleMatcher.Matches("BE38091000554030", "", "Ville de Marche-en-Famenne", "BE38091000554030"));
        Assert.True(CategoryRuleMatcher.Matches("BE38 0910 0055 4030", "", "ADMINISTRATION COMMUNALE DE MARCHE-", "BE38091000554030"));
    }

    [Fact]
    public void UneRegleCourte_NeTouchePasALIban()
    {
        // « DVV » (assurances) ou « CNS » (santé) ne doivent pas matcher le code banque d'un IBAN
        // étranger. L'IBAN ne se compare qu'à un mot-clé qui ressemble à un IBAN.
        Assert.False(CategoryRuleMatcher.Matches("ABNA", "", null, "NL91ABNA0417164300"));
        Assert.False(CategoryRuleMatcher.Matches("BE", "", null, "BE38091000554030"));
        Assert.False(CategoryRuleMatcher.Matches("091000", "", null, "BE38091000554030"));
    }

    [Fact]
    public void LibelleEtContrepartie_MatchentCommeAvant()
    {
        Assert.True(CategoryRuleMatcher.Matches("Crèche", "Crèche communale août", null, null));
        Assert.True(CategoryRuleMatcher.Matches("amnesty", "", "AMNESTY INTERNATIONAL", null));
        Assert.False(CategoryRuleMatcher.Matches("Loyer", "Courses Delhaize", "DELHAIZE", "BE38091000554030"));
    }

    [Fact]
    public void MotCleVide_NeMatcheRien()
    {
        Assert.False(CategoryRuleMatcher.Matches("", "n'importe quoi", "n'importe qui", "BE38091000554030"));
        Assert.False(CategoryRuleMatcher.Matches("   ", "n'importe quoi", null, null));
    }
}
