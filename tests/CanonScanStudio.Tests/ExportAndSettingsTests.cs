using CanonScanStudio.Infrastructure;
using CanonScanStudio.Models;
using CanonScanStudio.Services;
using FluentAssertions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace CanonScanStudio.Tests;

public class ExportAndSettingsTests : IDisposable
{
    private readonly string _root;

    public ExportAndSettingsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "css-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { /* ignore */ }
    }

    [Fact]
    public void Exports_multipage_pdf_and_jpeg()
    {
        var pages = new List<ExportedPage>
        {
            Page(Color.White),
            Page(Color.LightGray)
        };
        var pdf = new PdfService();
        var export = new FileExportService(pdf);
        var pdfPath = export.Export(pages, new ExportOptions
        {
            DestinationFolder = _root,
            FileNameWithoutExtension = "Documento_2026-08-17",
            Format = OutputFormat.Pdf
        }).Single();
        File.Exists(pdfPath).Should().BeTrue();
        new FileInfo(pdfPath).Length.Should().BeGreaterThan(100);

        var jpeg = export.Export([pages[0]], new ExportOptions
        {
            DestinationFolder = _root,
            FileNameWithoutExtension = "pagina",
            Format = OutputFormat.Jpeg
        }).Single();
        File.Exists(jpeg).Should().BeTrue();
        Path.GetExtension(jpeg).Should().Be(".jpg");
    }

    [Fact]
    public void Settings_roundtrip()
    {
            Environment.SetEnvironmentVariable("CANON_SCAN_STUDIO_DATA", _root);
            var service = new SettingsService(new InMemoryLog());
            service.Current.DefaultDpi = 600;
            service.Current.DefaultColorMode = ColorMode.Grayscale;
            service.Current.Interface = ScannerInterfaceKind.Wia;
            service.Current.ConfirmPageDelete = false;
            service.Save();
            var loaded = new SettingsService(new InMemoryLog());
            loaded.Current.DefaultDpi.Should().Be(600);
            loaded.Current.DefaultColorMode.Should().Be(ColorMode.Grayscale);
            loaded.Current.Interface.Should().Be(ScannerInterfaceKind.Wia);
    }

    private static ExportedPage Page(Color color)
    {
        using var image = new Image<Rgba32>(40, 50, color);
        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        return new ExportedPage(ms.ToArray(), 75, 40, 50, null);
    }
}
