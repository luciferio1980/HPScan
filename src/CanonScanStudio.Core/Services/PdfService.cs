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
                var widthPx = page.Width > 0 ? page.Width : 1;
                var heightPx = page.Height > 0 ? page.Height : 1;
                var (widthPts, heightPts) = ResolutionPresets.PdfPageSizePoints(widthPx, heightPx, page.Dpi);
                container.Page(p =>
                {
                    p.Size(widthPts, heightPts);
                    p.Margin(0);
                    p.PageColor(Colors.White);
                    if (searchable && page.Ocr is { Words.Count: > 0 })
                    {
                        p.Content().Layers(layers =>
                        {
                            layers.PrimaryLayer().Width(widthPts).Height(heightPts).Image(page.ImageBytes).FitArea();
                            layers.Layer().Element(e => DrawInvisibleText(e, page, widthPts, heightPts));
                        });
                    }
                    else
                    {
                        p.Content().Width(widthPts).Height(heightPts).Image(page.ImageBytes).FitArea();
                    }
                });
            }
        }).GeneratePdf(destinationPath);
    }

    private static void DrawInvisibleText(IContainer container, ExportedPage page, float pageWidthPts, float pageHeightPts)
    {
        var scaleX = page.Width <= 0 ? 1f : pageWidthPts / page.Width;
        var scaleY = page.Height <= 0 ? 1f : pageHeightPts / page.Height;
        container.Layers(layers =>
        {
            layers.PrimaryLayer();
            foreach (var word in page.Ocr!.Words.Where(w => !string.IsNullOrWhiteSpace(w.Text)))
            {
                var left = (float)(word.Left * scaleX);
                var top = (float)(word.Top * scaleY);
                var width = (float)Math.Max(1, word.Width * scaleX);
                var height = (float)Math.Max(1, word.Height * scaleY);
                if (left + width > pageWidthPts)
                {
                    width = Math.Max(1, pageWidthPts - left);
                }

                if (top + height > pageHeightPts)
                {
                    height = Math.Max(1, pageHeightPts - top);
                }

                if (width < 1 || height < 1 || left >= pageWidthPts || top >= pageHeightPts)
                {
                    continue;
                }

                layers.Layer()
                    .TranslateX(left)
                    .TranslateY(top)
                    .Width(width)
                    .Height(height)
                    .Text(word.Text)
                    .FontColor(Colors.Transparent)
                    .FontSize(Math.Max(4, height * 0.8f));
            }
        });
    }
}
