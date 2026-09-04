using System.Security.Cryptography;
using FinanceApp.API.Services;
using Xunit;

namespace FinanceApp.Tests;

/// <summary>Le disque des documents : réception en deux temps, empreinte, racine verrouillée, absence tolérée.</summary>
public class DocumentStorageTests : IDisposable
{
    private readonly DocumentStorage _storage;
    private readonly string _root;

    public DocumentStorageTests()
    {
        (_storage, _, _root) = TestHousehold.TempStorage(maxFileBytes: 1024);
    }

    public void Dispose() => TestHousehold.RemoveTemp(_root);

    [Fact]
    public async Task Reception_PasseParIncoming_PuisSeRangeSousAnneeEtId()
    {
        var bytes = TestHousehold.PdfBytes("range");
        var result = await _storage.StageAsync(new MemoryStream(bytes));

        Assert.Equal(StageOutcome.Ok, result.Outcome);
        var staged = result.File!;
        Assert.StartsWith(Path.Combine(_root, ".incoming"), staged.PartPath);
        Assert.EndsWith(".part", staged.PartPath);
        Assert.True(File.Exists(staged.PartPath));
        Assert.Equal(bytes.Length, staged.SizeBytes);
        Assert.Equal(FileKind.Pdf, staged.Kind);

        var stored = _storage.Commit(staged, documentId: 17, year: 2026);

        Assert.Equal("2026/17.pdf", stored);
        Assert.False(File.Exists(staged.PartPath));
        var final = Path.Combine(_root, "2026", "17.pdf");
        Assert.True(File.Exists(final));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(final));
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, ".incoming")));
    }

    [Fact]
    public async Task LEmpreinte_EstLeSha256DuContenu_EnHexaMinuscule()
    {
        var bytes = TestHousehold.PdfBytes("sha");
        var result = await _storage.StageAsync(new MemoryStream(bytes));

        var expected = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        Assert.Equal(expected, result.File!.Sha256);
        Assert.Equal(64, result.File.Sha256.Length);
        _storage.Discard(result.File);
    }

    [Fact]
    public async Task TypeInconnu_EstRefuse_EtNeLaisseRien()
    {
        var mz = new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00, 0x04, 0x00 };
        var result = await _storage.StageAsync(new MemoryStream(mz));

        Assert.Equal(StageOutcome.UnknownType, result.Outcome);
        Assert.Null(result.File);
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, ".incoming")));
    }

    [Fact]
    public async Task FichierVide_EstRefuse()
    {
        var result = await _storage.StageAsync(new MemoryStream(Array.Empty<byte>()));
        Assert.Equal(StageOutcome.Empty, result.Outcome);
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, ".incoming")));
    }

    [Fact]
    public async Task AuDelaDeLaLimite_EstRefuse_EtNeLaisseRien()
    {
        // Limite posée à 1 Kio dans le constructeur du test.
        var big = "%PDF-1.4\n"u8.ToArray().Concat(new byte[2048]).ToArray();
        var result = await _storage.StageAsync(new MemoryStream(big));

        Assert.Equal(StageOutcome.TooLarge, result.Outcome);
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, ".incoming")));
    }

    [Fact]
    public void FichierAbsent_RendNull_PasUneException()
    {
        Assert.Null(_storage.Open("2026/999.pdf"));
        _storage.Delete("2026/999.pdf"); // ne lève pas
    }

    [Theory]
    [InlineData("../finance.db")]
    [InlineData("2026/../../x.pdf")]
    [InlineData("/etc/passwd")]
    [InlineData("")]
    [InlineData("2026//1.pdf")]
    [InlineData("2026\\1.pdf")]
    [InlineData("2026/1.exe")]
    [InlineData("2026/1.pdf.html")]
    [InlineData("data/1.pdf")]
    public void UnCheminQuiSortDeLaRacine_OuHorsForme_EstUneErreur(string storedPath)
    {
        Assert.Throws<InvalidOperationException>(() => _storage.Open(storedPath));
    }

    [Fact]
    public void RacineSousWwwroot_EstRefusee()
    {
        var content = Path.Combine(Path.GetTempPath(), "app");
        var ex = Assert.Throws<InvalidOperationException>(() =>
            DocumentStorage.ResolveRoot("wwwroot/docs", content, Path.Combine(content, "wwwroot"), content, isProduction: false));
        Assert.Contains("wwwroot", ex.Message);

        // Même quand WebRootPath n'est pas renseigné (dossier wwwroot absent au démarrage).
        Assert.Throws<InvalidOperationException>(() => DocumentStorage.ResolveRoot("wwwroot", content, null, content, isProduction: false));
    }

    [Fact]
    public void RacineAbsente_EstRefusee()
    {
        var content = Path.Combine(Path.GetTempPath(), "app");
        Assert.Throws<InvalidOperationException>(() => DocumentStorage.ResolveRoot(null, content, null, content, isProduction: false));
        Assert.Throws<InvalidOperationException>(() => DocumentStorage.ResolveRoot("  ", content, null, content, isProduction: true));
    }

    [Fact]
    public void RacineEgaleAuDossierDeLApplication_EstRefusee()
    {
        var content = Path.Combine(Path.GetTempPath(), "app");
        Assert.Throws<InvalidOperationException>(() => DocumentStorage.ResolveRoot(".", content, null, content, isProduction: false));
        Assert.Throws<InvalidOperationException>(() => DocumentStorage.ResolveRoot(content, content, null, Path.Combine(content, "publish"), isProduction: false));
    }

    [Fact]
    public void EnDeveloppement_RacineRelative_SeResoutDepuisLeContentRoot()
    {
        var content = Path.Combine(Path.GetTempPath(), "app");
        var root = DocumentStorage.ResolveRoot("data/documents", content, null, content, isProduction: false);
        Assert.Equal(Path.GetFullPath(Path.Combine(content, "data", "documents")), root);
    }

    [Fact]
    public void EnProduction_RacineRelative_EstRefusee()
    {
        // Le défaut de appsettings.json : sur le Pi, le dossier de l'app est remplacé à chaque livraison.
        var content = Path.Combine(Path.GetTempPath(), "finance-app", "app");
        var ex = Assert.Throws<InvalidOperationException>(() =>
            DocumentStorage.ResolveRoot("data/documents", content, null, content, isProduction: true));
        Assert.Contains("relatif", ex.Message);
    }

    [Fact]
    public void EnProduction_RacineAbsolueSousLeDossierDeLApp_EstRefusee()
    {
        var content = Path.Combine(Path.GetTempPath(), "finance-app", "app");
        var publish = Path.Combine(Path.GetTempPath(), "finance-app", "publish");
        Assert.Throws<InvalidOperationException>(() =>
            DocumentStorage.ResolveRoot(Path.Combine(content, "data", "documents"), content, null, publish, isProduction: true));
        Assert.Throws<InvalidOperationException>(() =>
            DocumentStorage.ResolveRoot(Path.Combine(publish, "documents"), content, null, publish, isProduction: true));
    }

    [Fact]
    public void EnProduction_RacineAbsolueACoteDeLaBase_EstAcceptee()
    {
        // /home/admin/finance-app/data/documents à côté de finance.db, l'app dans /home/admin/finance-app/app.
        var home = Path.Combine(Path.GetTempPath(), "finance-app-" + Guid.NewGuid().ToString("N"));
        var content = Path.Combine(home, "app");
        var data = Path.Combine(home, "data", "documents");
        var root = DocumentStorage.ResolveRoot(data, content, Path.Combine(content, "wwwroot"), content, isProduction: true);
        Assert.Equal(Path.GetFullPath(data), root);
    }

    [Fact]
    public async Task IncomingNettoye_DesRestesDePlusDe24h_GardeLesRecents()
    {
        var incoming = Path.Combine(_root, ".incoming");
        var old = Path.Combine(incoming, "old.part");
        var fresh = Path.Combine(incoming, "fresh.part");
        await File.WriteAllBytesAsync(old, new byte[] { 1 });
        await File.WriteAllBytesAsync(fresh, new byte[] { 1 });
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddHours(-30));

        var removed = _storage.CleanIncoming(DateTime.UtcNow);

        Assert.Equal(1, removed);
        Assert.False(File.Exists(old));
        Assert.True(File.Exists(fresh));
    }
}
