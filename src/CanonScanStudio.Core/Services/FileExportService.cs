using CanonScanStudio.Infrastructure;
using CanonScanStudio.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Tiff;
using SixLabors.ImageSharp.PixelFormats;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace CanonScanStudio.Services;

public interface IImportService
{
    IReadOnlyList<ImportedImage> Import(string path);
}

public sealed record ImportedImage(byte[] Bytes, string Extension, int Dpi);

public sealed class ImportService : IImportService
{
    private readonly IAppLog _log;
    private readonly IImageProcessingService _images;

    public ImportService(IAppLog log, IImageProcessingService images)
    {
        _log = log;
        _images = images;
    }

    public IReadOnlyList<ImportedImage> Import(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            throw new InvalidOperationException("No se ha encontrado el archivo. Elige una imagen del disco.");
        }

        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".pdf" => ImportPdf(path),
            ".jpg" or ".jpeg" or ".png" or ".tif" or ".tiff" or ".bmp" => [ImportImage(path)],
            _ => throw new InvalidOperationException("Formato no soportado. Usa JPG, PNG, TIFF, BMP o PDF.")
        };
    }

    private ImportedImage ImportImage(string path)
    {
        var bytes = _images.ApplyEdits(path, PageEditState.Identity());
        var info = _images.ReadInfo(bytes);
        return new ImportedImage(bytes, ".png", info.Dpi <= 0 ? 300 : info.Dpi);
    }

    private List<ImportedImage> ImportPdf(string path)
    {
        var pages = new List<ImportedImage>();
        using var document = PdfDocument.Open(path);
        foreach (var page in document.GetPages())
        {
            var images = page.GetImages().ToList();
            if (images.Count == 0)
            {
                _log.Warn($"La página {page.Number} del PDF no contiene imágenes incrustadas.");
                continue;
            }

            foreach (var image in images)
            {
                if (!TryGetImageBytes(image, out var bytes))
                {
                    continue;
                }

                try
                {
                    using var parsed = Image.Load<Rgba32>(bytes);
                    using var ms = new MemoryStream();
                    parsed.SaveAsPng(ms);
                    pages.Add(new ImportedImage(ms.ToArray(), ".png", 300));
                }
                catch (Exception ex)
                {
                    _log.Warn("No se ha podido decodificar una imagen del PDF: " + ex.Message);
                }
            }
        }

        if (pages.Count == 0)
        {
            throw new InvalidOperationException("El PDF no contiene páginas de imagen que se puedan importar. Exporta el documento como imágenes e inténtalo de nuevo.");
        }

        return pages;
    }

    private static bool TryGetImageBytes(IPdfImage image, out byte[] bytes)
    {
        try
        {
            if (image.TryGetPng(out var png) && png is { Length: > 0 })
            {
                bytes = png;
                return true;
            }
        }
        catch
        {
            // continue
        }

        try
        {
            bytes = image.RawBytes.ToArray();
            return bytes.Length > 0;
        }
        catch
        {
            bytes = [];
            return false;
        }
    }
}

public interface IFileExportService
{
    IReadOnlyList<string> Export(IReadOnlyList<ExportedPage> pages, ExportOptions options);
}

public sealed class FileExportService : IFileExportService
{
    private readonly IPdfService _pdf;

    public FileExportService(IPdfService pdf)
    {
        _pdf = pdf;
    }

    public IReadOnlyList<string> Export(IReadOnlyList<ExportedPage> pages, ExportOptions options)
    {
        Directory.CreateDirectory(options.DestinationFolder);
        if (options.Format == OutputFormat.Pdf)
        {
            var path = Path.Combine(options.DestinationFolder, options.FileNameWithoutExtension + ".pdf");
            _pdf.Export(pages, path, options.SearchablePdf);
            return [path];
        }

        if (!options.SeparateImages && pages.Count > 1 && options.Format == OutputFormat.Tiff)
        {
            var path = Path.Combine(options.DestinationFolder, options.FileNameWithoutExtension + ".tiff");
            SaveMultipageTiff(pages, path);
            return [path];
        }

        var written = new List<string>();
        for (var i = 0; i < pages.Count; i++)
        {
            var suffix = pages.Count == 1 ? "" : $"_{i + 1:00}";
            var ext = options.Format switch
            {
                OutputFormat.Jpeg => ".jpg",
                OutputFormat.Tiff => ".tiff",
                _ => ".png"
            };
            var path = Path.Combine(options.DestinationFolder, options.FileNameWithoutExtension + suffix + ext);
            SaveImage(pages[i].ImageBytes, path, options);
            written.Add(path);
        }

        return written;
    }

    private static void SaveImage(byte[] pngBytes, string path, ExportOptions options)
    {
        using var image = Image.Load<Rgba32>(pngBytes);
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".jpg":
            case ".jpeg":
                image.SaveAsJpeg(path, new JpegEncoder { Quality = options.JpegQuality });
                break;
            case ".tif":
            case ".tiff":
                image.SaveAsTiff(path, new TiffEncoder());
                break;
            default:
                image.SaveAsPng(path);
                break;
        }
    }

    private static void SaveMultipageTiff(IReadOnlyList<ExportedPage> pages, string path)
    {
        Image<Rgba32>? root = null;
        try
        {
            foreach (var page in pages)
            {
                var frame = Image.Load<Rgba32>(page.ImageBytes);
                if (root is null)
                {
                    root = frame;
                }
                else
                {
                    root.Frames.AddFrame(frame.Frames.RootFrame);
                    frame.Dispose();
                }
            }

            root!.SaveAsTiff(path, new TiffEncoder());
        }
        finally
        {
            root?.Dispose();
        }
    }
}
