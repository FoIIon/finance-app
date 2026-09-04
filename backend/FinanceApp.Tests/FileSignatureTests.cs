using System.Text;
using FinanceApp.API.Services;
using Xunit;

namespace FinanceApp.Tests;

/// <summary>Le type d'un fichier se lit dans ses octets, jamais dans son nom ni dans ce que le client annonce.</summary>
public class FileSignatureTests
{
    [Fact]
    public void Pdf_ParSonEnTete() =>
        Assert.Equal(FileKind.Pdf, FileSignature.Detect("%PDF-1.7 blabla"u8));

    [Fact]
    public void Jpeg_ParSesTroisOctets() =>
        Assert.Equal(FileKind.Jpeg, FileSignature.Detect(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46 }));

    [Fact]
    public void Png_ParSesHuitOctets() =>
        Assert.Equal(FileKind.Png, FileSignature.Detect(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00 }));

    [Fact]
    public void Executable_RenommeEnPdf_EstRefuse()
    {
        // En-tête MZ d'un exécutable Windows. Le nom « facture.pdf » n'est pas consulté : il n'existe pas ici.
        var mz = new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00 };
        Assert.Null(FileSignature.Detect(mz));
    }

    [Fact]
    public void FichierVide_EstRefuse() =>
        Assert.Null(FileSignature.Detect(ReadOnlySpan<byte>.Empty));

    [Fact]
    public void Svg_EstRefuse() =>
        Assert.Null(FileSignature.Detect(Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>")));

    [Fact]
    public void Html_EstRefuse() =>
        Assert.Null(FileSignature.Detect(Encoding.UTF8.GetBytes("<!DOCTYPE html><html><body>x</body></html>")));

    [Fact]
    public void PdfTronque_AvantLeTiret_EstRefuse() =>
        Assert.Null(FileSignature.Detect("%PDF"u8));

    [Theory]
    [InlineData(FileKind.Pdf, "pdf", "application/pdf")]
    [InlineData(FileKind.Jpeg, "jpg", "image/jpeg")]
    [InlineData(FileKind.Png, "png", "image/png")]
    public void ExtensionEtContentType_ViennentDuType(FileKind kind, string extension, string contentType)
    {
        Assert.Equal(extension, FileSignature.Extension(kind));
        Assert.Equal(contentType, FileSignature.ContentType(kind));
    }
}
