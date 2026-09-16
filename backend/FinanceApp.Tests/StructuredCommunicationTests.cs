using FinanceApp.API.Services;
using Xunit;

namespace FinanceApp.Tests;

/// <summary>
/// La communication structurée belge : douze chiffres dont les deux derniers valent les dix premiers
/// modulo 97 (97 quand le reste est nul). Extraction d'un libellé bancaire et normalisation d'une saisie.
/// </summary>
public class StructuredCommunicationTests
{
    /// <summary>Douze chiffres construits depuis les dix premiers : le test ne dépend pas d'un calcul de tête.</summary>
    internal static string Sc(long body)
    {
        var r = (int)(body % 97);
        if (r == 0) r = 97;
        return $"{body:D10}{r:D2}";
    }

    // 1234567890 mod 97 = 2 : …89002 est l'exemple de l'écran, …89012 la saisie fausse des tests.
    private static readonly string Valide = Sc(1234567890);          // 123456789002
    private static readonly string Autre = Sc(9876543210);           // 9876543210 mod 97 = 9
    private static readonly string ResteNul = Sc(1234567888);        // 1234567888 = 97 × 12727504

    private static string Formatee(string d, string delim = "+++") => $"{delim}{d[..3]}/{d[3..7]}/{d[7..]}{delim}";

    [Fact]
    public void Extract_FormePlus_RendLesDouzeChiffres() =>
        Assert.Equal(Valide, StructuredCommunication.Extract($"Virement {Formatee(Valide)} école"));

    [Fact]
    public void Extract_FormeEtoile_RendLesDouzeChiffres() =>
        Assert.Equal(Valide, StructuredCommunication.Extract($"PAIEMENT {Formatee(Valide, "***")}"));

    [Fact]
    public void Extract_SansBarres_RendLesDouzeChiffres() =>
        Assert.Equal(Valide, StructuredCommunication.Extract($"+++{Valide}+++"));

    [Fact]
    public void Extract_AvecEspacesAutourDesBarres_RendLesDouzeChiffres() =>
        Assert.Equal(Valide, StructuredCommunication.Extract($"+++ {Valide[..3]} / {Valide[3..7]} / {Valide[7..]} +++"));

    [Fact]
    public void Extract_ControleJuste_Rapproche()
    {
        Assert.Equal("123456789002", Valide);
        Assert.Equal(Valide, StructuredCommunication.Extract(Formatee(Valide)));
    }

    [Fact]
    public void Extract_ControleFaux_RendNull()
    {
        // Une seule erreur de frappe sur l'exemple : 12 au lieu de 02.
        Assert.Null(StructuredCommunication.Extract("+++123/4567/89012+++"));
    }

    [Fact]
    public void Extract_ResteNul_Attend97()
    {
        Assert.EndsWith("97", ResteNul);
        Assert.Equal(ResteNul, StructuredCommunication.Extract(Formatee(ResteNul)));
        Assert.Null(StructuredCommunication.Extract("+++123/4567/88800+++"));
    }

    [Fact]
    public void Extract_DeuxCommunications_LaPremiereGagne() =>
        Assert.Equal(Valide, StructuredCommunication.Extract($"{Formatee(Valide)} puis {Formatee(Autre)}"));

    [Fact]
    public void Extract_PremiereFausse_SecondeJuste_RendLaJuste() =>
        Assert.Equal(Autre, StructuredCommunication.Extract($"+++123/4567/89012+++ {Formatee(Autre)}"));

    [Fact]
    public void Extract_LibelleSansRien_RendNull() =>
        Assert.Null(StructuredCommunication.Extract("COLRUYT DEMOVILLE 12/09"));

    [Fact]
    public void Extract_ChiffresSansSeparateurs_RendNull() =>
        Assert.Null(StructuredCommunication.Extract($"Facture {Valide}"));

    [Fact]
    public void Extract_NullEtVide_RendentNull()
    {
        Assert.Null(StructuredCommunication.Extract(null));
        Assert.Null(StructuredCommunication.Extract(""));
        Assert.Null(StructuredCommunication.Extract("   "));
    }

    [Fact]
    public void Normalize_SaisieFormatee_RendLesDouzeChiffres()
    {
        Assert.Equal(Valide, StructuredCommunication.Normalize(Formatee(Valide)));
        Assert.Equal(Valide, StructuredCommunication.Normalize(Formatee(Valide, "***")));
        Assert.Equal(Valide, StructuredCommunication.Normalize($" {Valide[..3]} {Valide[3..7]} {Valide[7..]} "));
        Assert.Equal(Valide, StructuredCommunication.Normalize(Valide));
    }

    [Fact]
    public void Normalize_PasDouzeChiffres_OuControleFaux_RendNull()
    {
        Assert.Null(StructuredCommunication.Normalize("+++123/4567/8901+++"));
        Assert.Null(StructuredCommunication.Normalize("+++123/4567/89012+++"));
        Assert.Null(StructuredCommunication.Normalize("abc"));
        Assert.Null(StructuredCommunication.Normalize(""));
    }

    [Fact]
    public void IsValid_ExigeDouzeChiffres()
    {
        Assert.True(StructuredCommunication.IsValid(Valide));
        Assert.True(StructuredCommunication.IsValid(ResteNul));
        Assert.False(StructuredCommunication.IsValid("12345678900"));
        Assert.False(StructuredCommunication.IsValid("1234567890023"));
        Assert.False(StructuredCommunication.IsValid("12345678900a"));
    }

    [Fact]
    public void Format_EcritLaFormeAffichee() =>
        Assert.Equal("+++123/4567/89002+++", StructuredCommunication.Format(Valide));
}
