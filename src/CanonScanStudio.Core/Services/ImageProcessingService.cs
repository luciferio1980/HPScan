using CanonScanStudio.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace CanonScanStudio.Services;

public interface IImageProcessingService
{
    ProcessedImage LoadOriginal(string path);
    byte[] SaveOriginal(byte[] sourceBytes, string destinationPath, int dpi);
    byte[] ApplyEdits(string originalPath, PageEditState edit, int maxEdge = 0);
    byte[] CreateThumbnail(string originalPath, PageEditState edit, int width = 140);
    CropRegion DetectDocument(string originalPath);
    double DetectSkew(string originalPath);
    ImageInfo ReadInfo(string path);
    ImageInfo ReadInfo(byte[] bytes);
    byte[] CropBytes(byte[] sourceBytes, CropRegion region);
}

public sealed record ImageInfo(int Width, int Height, int Dpi);

public sealed class ProcessedImage : IDisposable
{
    public ProcessedImage(Image<Rgba32> image)
    {
        Image = image;
    }

    public Image<Rgba32> Image { get; }

    public void Dispose() => Image.Dispose();
}

public sealed class ImageProcessingService : IImageProcessingService
{
    public ProcessedImage LoadOriginal(string path) => new(LoadRgba32(path));

    public byte[] SaveOriginal(byte[] sourceBytes, string destinationPath, int dpi)
    {
        if (sourceBytes.Length < 32 || LooksLikeXml(sourceBytes))
        {
            throw new InvalidOperationException(
                "El escáner ha devuelto un error en lugar de la imagen. Cierra IJ Scan Utility y el Selector EX2 extra, y vuelve a escanear.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        using var image = LoadRgba32(sourceBytes);
        image.Metadata.HorizontalResolution = dpi;
        image.Metadata.VerticalResolution = dpi;
        ClearProfiles(image);
        image.SaveAsPng(destinationPath);
        return File.ReadAllBytes(destinationPath);
    }

    public ImageInfo ReadInfo(string path)
    {
        var info = Image.Identify(path) ?? throw new InvalidOperationException("No se ha podido leer la imagen escaneada.");
        var dpi = info.Metadata.HorizontalResolution > 0 ? (int)Math.Round(info.Metadata.HorizontalResolution) : 300;
        return new ImageInfo(info.Width, info.Height, dpi);
    }

    public ImageInfo ReadInfo(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        var info = Image.Identify(ms) ?? throw new InvalidOperationException("No se ha podido leer la imagen recortada.");
        var dpi = info.Metadata.HorizontalResolution > 0 ? (int)Math.Round(info.Metadata.HorizontalResolution) : 300;
        return new ImageInfo(info.Width, info.Height, dpi);
    }

    public byte[] CropBytes(byte[] sourceBytes, CropRegion region)
    {
        using var image = LoadRgba32(sourceBytes);
        var rect = new Rectangle(
            (int)Math.Round(region.X),
            (int)Math.Round(region.Y),
            (int)Math.Round(region.Width),
            (int)Math.Round(region.Height));
        rect.Intersect(new Rectangle(0, 0, image.Width, image.Height));
        if (rect.Width < 2 || rect.Height < 2)
        {
            throw new InvalidOperationException("El recorte es demasiado pequeño.");
        }

        image.Mutate(x => x.Crop(rect));
        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        return ms.ToArray();
    }

    public byte[] ApplyEdits(string originalPath, PageEditState edit, int maxEdge = 0)
    {
        using var image = LoadForEdit(originalPath);
        Mutate(image, edit);
        ResizeToMaxEdge(image, maxEdge);

        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        return ms.ToArray();
    }

    public byte[] CreateThumbnail(string originalPath, PageEditState edit, int width = 140)
    {
        using var image = LoadForEdit(originalPath);
        Mutate(image, edit);
        image.Mutate(x => x.Resize(new ResizeOptions
        {
            Mode = ResizeMode.Max,
            Size = new Size(width, (int)(width * 1.5))
        }));
        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        return ms.ToArray();
    }

    public CropRegion DetectDocument(string originalPath)
    {
        using var image = LoadRgba32(originalPath);
        using var work = image.Clone();
        work.Mutate(x =>
        {
            x.Resize(new ResizeOptions { Mode = ResizeMode.Max, Size = new Size(800, 800) });
            x.Grayscale();
        });

        var scaleX = image.Width / (double)work.Width;
        var scaleY = image.Height / (double)work.Height;
        var minX = work.Width;
        var minY = work.Height;
        var maxX = 0;
        var maxY = 0;
        var count = 0;

        work.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    // Cristal oscuro + hoja clara: consideramos papel los píxeles luminosos.
                    if (row[x].R > 70)
                    {
                        minX = Math.Min(minX, x);
                        minY = Math.Min(minY, y);
                        maxX = Math.Max(maxX, x);
                        maxY = Math.Max(maxY, y);
                        count++;
                    }
                }
            }
        });

        if (count < work.Width * work.Height * 0.02)
        {
            return new CropRegion(0, 0, image.Width, image.Height);
        }

        var padX = Math.Max(4, (maxX - minX) * 0.01);
        var padY = Math.Max(4, (maxY - minY) * 0.01);
        var x0 = Math.Max(0, (minX - padX) * scaleX);
        var y0 = Math.Max(0, (minY - padY) * scaleY);
        var x1 = Math.Min(image.Width, (maxX + padX) * scaleX);
        var y1 = Math.Min(image.Height, (maxY + padY) * scaleY);
        return new CropRegion(x0, y0, Math.Max(1, x1 - x0), Math.Max(1, y1 - y0));
    }

    public double DetectSkew(string originalPath)
    {
        using var image = LoadRgba32(originalPath);
        using var work = image.Clone();
        work.Mutate(x =>
        {
            x.Resize(new ResizeOptions { Mode = ResizeMode.Max, Size = new Size(900, 900) });
            x.Grayscale();
            x.BinaryThreshold(0.55f);
        });

        double bestAngle = 0;
        double bestScore = double.MinValue;
        for (var angle = -10.0; angle <= 10.0; angle += 0.5)
        {
            using var rotated = work.Clone();
            rotated.Mutate(x => x.Rotate((float)angle));
            var projection = new int[rotated.Height];
            rotated.ProcessPixelRows(accessor =>
            {
                for (var y = 0; y < accessor.Height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    var sum = 0;
                    for (var x = 0; x < row.Length; x++)
                    {
                        if (row[x].R < 128)
                        {
                            sum++;
                        }
                    }

                    projection[y] = sum;
                }
            });

            var mean = projection.Average();
            var variance = projection.Select(v => (v - mean) * (v - mean)).Average();
            if (variance > bestScore)
            {
                bestScore = variance;
                bestAngle = angle;
            }
        }

        return Math.Abs(bestAngle) < 0.25 ? 0 : -bestAngle;
    }

    private static Image<Rgba32> LoadForEdit(string originalPath) => LoadRgba32(originalPath);

    private static Image<Rgba32> LoadRgba32(string originalPath) =>
        LoadRgba32(File.ReadAllBytes(originalPath));

    private static Image<Rgba32> LoadRgba32(byte[] sourceBytes)
    {
        var bytes = IsJpeg(sourceBytes) ? JpegSanitizer.StripProblematicSegments(sourceBytes) : sourceBytes;
        var options = new DecoderOptions { SkipMetadata = true };
        try
        {
            using var stream = new MemoryStream(bytes);
            var image = Image.Load<Rgba32>(options, stream);
            ClearProfiles(image);
            return image;
        }
        catch (Exception first)
        {
            try
            {
                using var stream = new MemoryStream(bytes);
                using var loaded = Image.Load(options, stream);
                var rgba = loaded.CloneAs<Rgba32>();
                ClearProfiles(rgba);
                return rgba;
            }
            catch (Exception second)
            {
                throw new InvalidOperationException(
                    "No se ha podido leer la imagen escaneada.",
                    second.InnerException ?? first);
            }
        }
    }

    private static void ClearProfiles(Image image)
    {
        image.Metadata.ExifProfile = null;
        image.Metadata.IccProfile = null;
        image.Metadata.IptcProfile = null;
        image.Metadata.XmpProfile = null;
    }

    private static void ResizeToMaxEdge(Image<Rgba32> image, int maxEdge)
    {
        if (maxEdge <= 0)
        {
            return;
        }

        var longest = Math.Max(image.Width, image.Height);
        if (longest <= maxEdge)
        {
            return;
        }

        image.Mutate(x => x.Resize(new ResizeOptions
        {
            Mode = ResizeMode.Max,
            Size = new Size(maxEdge, maxEdge)
        }));
    }

    private static bool IsJpeg(byte[] bytes) => bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xD8;
    private static bool IsPng(byte[] bytes) => bytes.Length >= 2 && bytes[0] == 0x89 && bytes[1] == 0x50;
    private static bool IsBmp(byte[] bytes) => bytes.Length >= 2 && bytes[0] == (byte)'B' && bytes[1] == (byte)'M';
    private static bool IsTiff(byte[] bytes) =>
        bytes.Length >= 2 &&
        ((bytes[0] == (byte)'I' && bytes[1] == (byte)'I') || (bytes[0] == (byte)'M' && bytes[1] == (byte)'M'));

    private static bool LooksLikeXml(byte[] bytes)
    {
        var take = Math.Min(bytes.Length, 80);
        var text = System.Text.Encoding.UTF8.GetString(bytes, 0, take).TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
        return text.StartsWith('<');
    }

    private static void Mutate(Image<Rgba32> image, PageEditState edit)
    {
        image.Mutate(ctx =>
        {
            if (edit.Crop is { } crop)
            {
                var rect = new Rectangle(
                    (int)Math.Round(crop.X),
                    (int)Math.Round(crop.Y),
                    (int)Math.Round(crop.Width),
                    (int)Math.Round(crop.Height));
                rect.Intersect(new Rectangle(0, 0, image.Width, image.Height));
                if (rect.Width > 1 && rect.Height > 1)
                {
                    ctx.Crop(rect);
                }
            }

            if (Math.Abs(edit.DeskewAngle) > 0.05)
            {
                ctx.BackgroundColor(Color.White);
                ctx.Rotate((float)edit.DeskewAngle);
            }

            var rotation = ((edit.RotationDegrees % 360) + 360) % 360;
            if (rotation == 90) ctx.Rotate(RotateMode.Rotate90);
            else if (rotation == 180) ctx.Rotate(RotateMode.Rotate180);
            else if (rotation == 270) ctx.Rotate(RotateMode.Rotate270);

            if (edit.FlipHorizontal) ctx.Flip(FlipMode.Horizontal);
            if (edit.FlipVertical) ctx.Flip(FlipMode.Vertical);

            if (edit.Brightness != 0)
            {
                ctx.Brightness(1f + edit.Brightness / 100f);
            }

            if (edit.Contrast != 0)
            {
                ctx.Contrast(1f + edit.Contrast / 100f);
            }

            if (edit.Saturation != 0)
            {
                ctx.Saturate(1f + edit.Saturation / 100f);
            }

            if (edit.EnhanceDocument)
            {
                ctx.Contrast(1.12f);
                ctx.GaussianSharpen(0.8f);
            }

            switch (edit.Filter)
            {
                case DocumentFilter.Grayscale:
                    ctx.Grayscale();
                    break;
                case DocumentFilter.BlackAndWhite:
                    ctx.Grayscale();
                    ctx.BinaryThreshold(0.5f);
                    break;
                case DocumentFilter.Invert:
                    ctx.Invert();
                    break;
            }
        });

        if (edit.Gamma != 0)
        {
            ApplyGamma(image, Math.Pow(2.2, edit.Gamma / 100.0));
        }

        if (edit.RemoveBorders)
        {
            var bounds = DetectWhiteContent(image);
            var rect = new Rectangle((int)bounds.X, (int)bounds.Y, (int)bounds.Width, (int)bounds.Height);
            rect.Intersect(new Rectangle(0, 0, image.Width, image.Height));
            if (rect.Width > 10 && rect.Height > 10)
            {
                image.Mutate(x => x.Crop(rect));
            }
        }
    }

    private static CropRegion DetectWhiteContent(Image<Rgba32> image)
    {
        var minX = image.Width;
        var minY = image.Height;
        var maxX = 0;
        var maxY = 0;
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    if (row[x].R < 245 || row[x].G < 245 || row[x].B < 245)
                    {
                        minX = Math.Min(minX, x);
                        minY = Math.Min(minY, y);
                        maxX = Math.Max(maxX, x);
                        maxY = Math.Max(maxY, y);
                    }
                }
            }
        });

        if (maxX <= minX)
        {
            return new CropRegion(0, 0, image.Width, image.Height);
        }

        return new CropRegion(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    private static void ApplyGamma(Image<Rgba32> image, double gamma)
    {
        gamma = Math.Clamp(gamma, 0.2, 5);
        var table = new byte[256];
        for (var i = 0; i < 256; i++)
        {
            table[i] = (byte)Math.Clamp(Math.Round(255 * Math.Pow(i / 255d, 1d / gamma)), 0, 255);
        }

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    row[x].R = table[row[x].R];
                    row[x].G = table[row[x].G];
                    row[x].B = table[row[x].B];
                }
            }
        });
    }
}
