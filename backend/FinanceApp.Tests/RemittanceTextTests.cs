using System.Text.Json;
using FinanceApp.API.Services;
using Xunit;

namespace FinanceApp.Tests;

/// <summary>
/// Le texte d'un champ de remittance GoCardless, en chaîne ou en tableau. Berlin Group laisse certaines
/// banques remplir le tableau structuré d'objets plutôt que de chaînes : un GetString() sur l'un d'eux
/// levait et faisait échouer l'import du compte à chaque cycle. Ici, les chaînes sont gardées, le reste ignoré.
/// </summary>
public class RemittanceTextTests
{
    private static JsonElement Payload(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void Chaine_RendueTelleQuelle()
    {
        var tx = Payload("""{ "remittanceInformationUnstructured": "Repas chauds septembre" }""");
        Assert.Equal("Repas chauds septembre", GoCardlessTransactionFields.RemittanceText(tx, "remittanceInformationUnstructured"));
    }

    [Fact]
    public void TableauDeChaines_JointParUnEspace()
    {
        var tx = Payload("""{ "remittanceInformationUnstructuredArray": ["Repas chauds", "septembre"] }""");
        Assert.Equal("Repas chauds septembre", GoCardlessTransactionFields.RemittanceText(tx, "remittanceInformationUnstructured"));
    }

    [Fact]
    public void TableauAvecUnObjetAuMilieu_GardeLesChaines_IgnoreLObjet()
    {
        var tx = Payload("""
        {
          "remittanceInformationStructuredArray": [
            "+++123/4567/89002+++",
            { "reference": "123456789002", "referenceType": "SCOR", "referenceIssuer": "Ecole" },
            "facture 2026-09"
          ]
        }
        """);
        Assert.Equal("+++123/4567/89002+++ facture 2026-09", GoCardlessTransactionFields.RemittanceText(tx, "remittanceInformationStructured"));
    }

    [Fact]
    public void TableauSansAucuneChaine_RendNull()
    {
        var tx = Payload("""{ "remittanceInformationStructuredArray": [ { "reference": "123456789002" }, 42, null ] }""");
        Assert.Null(GoCardlessTransactionFields.RemittanceText(tx, "remittanceInformationStructured"));
    }

    [Fact]
    public void ChampAbsent_RendNull()
    {
        var tx = Payload("""{ "creditorName": "ECOLE COMMUNALE" }""");
        Assert.Null(GoCardlessTransactionFields.RemittanceText(tx, "remittanceInformationUnstructured"));
        Assert.Null(GoCardlessTransactionFields.RemittanceText(tx, "remittanceInformationStructured"));
    }

    [Fact]
    public void ChaineVide_OuDUnAutreType_TombeSurLeTableau_PuisSurNull()
    {
        // Une chaîne vide ne vaut rien, le tableau est tenté ensuite ; un champ qui n'est ni chaîne ni tableau est ignoré.
        var tx = Payload("""{ "remittanceInformationUnstructured": "", "remittanceInformationUnstructuredArray": ["Repas"] }""");
        Assert.Equal("Repas", GoCardlessTransactionFields.RemittanceText(tx, "remittanceInformationUnstructured"));
        Assert.Null(GoCardlessTransactionFields.RemittanceText(Payload("""{ "remittanceInformationUnstructured": 12 }"""), "remittanceInformationUnstructured"));
        Assert.Null(GoCardlessTransactionFields.RemittanceText(Payload("""{ "remittanceInformationUnstructured": null }"""), "remittanceInformationUnstructured"));
    }
}
