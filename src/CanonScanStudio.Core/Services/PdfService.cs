using CanonScanStudio.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace CanonScanStudio.Services;

public interface IPdfService
{
    void Export(IReadOnlyList<ExportedPage> pages, string destinationPath, bool searchable);
}

public sealed record ExportedPage(byte[] ImageBytes, int Dpi, int Width, int Height, OcrPageResult? Ocr);

public sealed class PdfService : IPdfService
{
    public PdfService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public void Export(IReadOnlyList<ExportedPage> pages, string destinationPath, bool searchable)
    {
        if (pages.Count == 0)
        {
            throw new InvalidOperationException("No hay páginas para exportar.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        Document.Create(container =>
        {
            foreach (var page in pages)
            {
                var widthPts = page.Width * 72f / Math.Max(1, page.Dpi);
                var heightPts = page.Height * 72f / Math.Max(1, page.Dpi);
                container.Page(p =>
                {
                    p.Size(widthPts, heightPts);
                    p.Margin(0);
                    p.PageColor(Colors.White);
                    p.Content().Layers(layers =>
                    {
                        layers.PrimaryLayer().Image(page.ImageBytes).FitArea();
                        if (searchable && page.Ocr is { Words.Count: > 0 })
                        {
                            layers.Layer().Element(e => DrawInvisibleText(e, page));
                        }
                    });
                });
            }
        }).GeneratePdf(destinationPath);
    }

    private static void DrawInvisibleText(IContainer container, ExportedPage page)
    {
        container.Layers(layers =>
        {
            foreach (var word in page.Ocr!.Words.Where(w => !string.IsNullOrWhiteSpace(w.Text)))
            {
                var left = (float)word.Left;
                var top = (float)word.Top;
                layers.Layer()
                    .TranslateX(left)
                    .TranslateY(top)
                    .Width((float)Math.Max(1, word.Width))
                    .Height((float)Math.Max(1, word.Height))
                    .Text(word.Text)
                    .FontColor(Colors.Transparent)
                    .FontSize(Math.Max(4, (float)word.Height * 0.8f));
            }
        });
    }
}
