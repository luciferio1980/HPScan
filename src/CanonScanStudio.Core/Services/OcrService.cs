using CanonScanStudio.Infrastructure;
using CanonScanStudio.Models;
using Tesseract;

namespace CanonScanStudio.Services;

public interface IOcrService
{
    bool IsAvailable { get; }
    IReadOnlyList<OcrLanguage> Languages { get; }
    OcrPageResult Recognize(string imagePath, string language);
}

public sealed record OcrLanguage(string Code, string DisplayName);

public sealed class OcrService : IOcrService
{
    private readonly IAppLog _log;

    public OcrService(IAppLog log)
    {
        _log = log;
        Directory.CreateDirectory(AppPaths.TessData);
        TryCopyBundledTessData();
    }

    public IReadOnlyList<OcrLanguage> Languages { get; } =
    [
        new("spa", "Español"),
        new("eng", "Inglés"),
        new("fra", "Francés"),
        new("deu", "Alemán"),
        new("ita", "Italiano"),
        new("por", "Portugués")
    ];

    public bool IsAvailable
    {
        get
        {
            try
            {
                return Directory.Exists(ResolveTessData()) &&
                       File.Exists(Path.Combine(ResolveTessData(), "eng.traineddata"));
            }
            catch
            {
                return false;
            }
        }
    }

    public OcrPageResult Recognize(string imagePath, string language)
    {
        var tess = ResolveTessData();
        var lang = File.Exists(Path.Combine(tess, language + ".traineddata")) ? language : "eng";
        if (!File.Exists(Path.Combine(tess, lang + ".traineddata")))
        {
            throw new InvalidOperationException(
                "El reconocimiento de texto no está listo. Descarga los archivos tessdata (spa/eng) en la carpeta de datos de la aplicación. El escaneo funciona sin OCR.");
        }

        try
        {
            using var engine = new TesseractEngine(tess, lang, EngineMode.Default);
            using var pix = Pix.LoadFromFile(imagePath);
            using var page = engine.Process(pix);
            var words = new List<OcrWord>();
            using var iter = page.GetIterator();
            iter.Begin();
            do
            {
                if (!iter.TryGetBoundingBox(PageIteratorLevel.Word, out var box))
                {
                    continue;
                }

                var text = iter.GetText(PageIteratorLevel.Word)?.Trim();
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                words.Add(new OcrWord
                {
                    Text = text,
                    Left = box.X1,
                    Top = box.Y1,
                    Width = box.X2 - box.X1,
                    Height = box.Y2 - box.Y1,
                    Confidence = iter.GetConfidence(PageIteratorLevel.Word)
                });
            } while (iter.Next(PageIteratorLevel.Word));

            return new OcrPageResult
            {
                PageId = Guid.Empty,
                Text = page.GetText() ?? string.Empty,
                Words = words,
                Language = lang
            };
        }
        catch (DllNotFoundException ex)
        {
            _log.Error("Tesseract nativo no encontrado.", ex);
            throw new InvalidOperationException("OCR no disponible en este equipo. El resto de la aplicación sigue funcionando.");
        }
    }

    private static string ResolveTessData()
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "tessdata");
        if (Directory.Exists(bundled) && Directory.EnumerateFiles(bundled, "*.traineddata").Any())
        {
            return bundled;
        }

        return AppPaths.TessData;
    }

    private void TryCopyBundledTessData()
    {
        try
        {
            var bundled = Path.Combine(AppContext.BaseDirectory, "tessdata");
            if (!Directory.Exists(bundled))
            {
                return;
            }

            foreach (var file in Directory.EnumerateFiles(bundled, "*.traineddata"))
            {
                var dest = Path.Combine(AppPaths.TessData, Path.GetFileName(file));
                if (!File.Exists(dest))
                {
                    File.Copy(file, dest);
                }
            }
        }
        catch (Exception ex)
        {
            _log.Warn("No se han copiado los datos OCR: " + ex.Message);
        }
    }
}
