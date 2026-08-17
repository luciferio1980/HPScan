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
    public void Exports_imported_photos_with_junk_dpi()
    {
        var pages = new List<ExportedPage>
        {
            PageAt(1220, 1549, 1, Color.White),
            PageAt(372, 628, 3780, Color.LightGray)
        };
        var path = Path.Combine(_root, "importados.pdf");
        new PdfService().Export(pages, path, searchable: false);
        File.Exists(path).Should().BeTrue();
        new FileInfo(path).Length.Should().BeGreaterThan(200);
    }

    [Fact]
    public void Exports_searchable_pdf_when_ocr_boxes_are_in_pixels()
    {
        var image = PageAt(372, 628, 3780, Color.White);
        var ocr = new OcrPageResult
        {
            PageId = Guid.NewGuid(),
            Text = "hola",
            Words =
            [
                new OcrWord
                {
                    Text = "hola",
                    Left = 40,
                    Top = 80,
                    Width = 90,
                    Height = 24,
                    Confidence = 90
                }
            ]
        };
        var path = Path.Combine(_root, "ocr.pdf");
        new PdfService().Export([image with { Ocr = ocr }], path, searchable: true);
        File.Exists(path).Should().BeTrue();
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
            service.Current.ThemeId = "oscuro";
            service.Save();
            var loaded = new SettingsService(new InMemoryLog());
            loaded.Current.DefaultDpi.Should().Be(600);
            loaded.Current.DefaultColorMode.Should().Be(ColorMode.Grayscale);
            loaded.Current.Interface.Should().Be(ScannerInterfaceKind.Wia);
            loaded.Current.ThemeId.Should().Be("oscuro");
    }

    [Fact]
    public void Settings_zero_dpi_becomes_300_color()
    {
        Environment.SetEnvironmentVariable("CANON_SCAN_STUDIO_DATA", _root);
        AppPaths.EnsureCreated();
        File.WriteAllText(
            AppPaths.Settings,
            """{"DefaultDpi":0,"DefaultColorMode":99,"DefaultSaveFolder":""}""");
        var loaded = new SettingsService(new InMemoryLog());
        loaded.Current.DefaultDpi.Should().Be(300);
        loaded.Current.DefaultColorMode.Should().Be(ColorMode.Color);
    }

    private static ExportedPage Page(Color color) => PageAt(40, 50, 75, color);

    private static ExportedPage PageAt(int width, int height, int dpi, Color color)
    {
        using var image = new Image<Rgba32>(width, height, color);
        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        return new ExportedPage(ms.ToArray(), dpi, width, height, null);
    }
}
