using CanonScanStudio.Models;
using CanonScanStudio.Services;
using FluentAssertions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace CanonScanStudio.Tests;

public class JpegSanitizerTests
{
    [Fact]
    public void Strips_exif_app1_and_keeps_a_loadable_jpeg()
    {
        using var image = new Image<Rgba32>(12, 8, new Rgba32(30, 80, 200));
        using var ms = new MemoryStream();
        image.SaveAsJpeg(ms);
        var jpeg = ms.ToArray();

        var poisoned = InsertApp1(jpeg, "Exif\0\0broken");
        poisoned.Length.Should().BeGreaterThan(jpeg.Length);

        var cleaned = JpegSanitizer.StripProblematicSegments(poisoned);
        cleaned[0].Should().Be(0xFF);
        cleaned[1].Should().Be(0xD8);
        FindMarker(cleaned, 0xE1).Should().BeFalse();

        using var loaded = Image.Load<Rgba32>(cleaned);
        loaded.Width.Should().Be(12);
        loaded.Height.Should().Be(8);
    }

    [Fact]
    public void SaveOriginal_converts_jpeg_to_png()
    {
        var root = Path.Combine(Path.GetTempPath(), "css-jpeg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var image = new Image<Rgba32>(16, 10, new Rgba32(10, 20, 200));
            using var ms = new MemoryStream();
            image.SaveAsJpeg(ms);
            var dest = Path.Combine(root, "page.png");
            var written = new ImageProcessingService().SaveOriginal(InsertApp1(ms.ToArray(), "Exif\0\0x"), dest, 300);
            written[0].Should().Be(0x89);
            written[1].Should().Be(0x50);
            File.ReadAllBytes(dest)[0].Should().Be(0x89);
            var info = new ImageProcessingService().ReadInfo(dest);
            info.Width.Should().Be(16);
            info.Height.Should().Be(10);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    private static bool FindMarker(byte[] jpeg, byte marker)
    {
        for (var i = 0; i < jpeg.Length - 1; i++)
        {
            if (jpeg[i] == 0xFF && jpeg[i + 1] == marker)
            {
                return true;
            }
        }

        return false;
    }

    private static byte[] InsertApp1(byte[] jpeg, string payload)
    {
        var data = System.Text.Encoding.ASCII.GetBytes(payload);
        var length = data.Length + 2;
        var segment = new byte[2 + 2 + data.Length];
        segment[0] = 0xFF;
        segment[1] = 0xE1;
        segment[2] = (byte)(length >> 8);
        segment[3] = (byte)length;
        Buffer.BlockCopy(data, 0, segment, 4, data.Length);
        var result = new byte[jpeg.Length + segment.Length];
        result[0] = jpeg[0];
        result[1] = jpeg[1];
        Buffer.BlockCopy(segment, 0, result, 2, segment.Length);
        Buffer.BlockCopy(jpeg, 2, result, 2 + segment.Length, jpeg.Length - 2);
        return result;
    }
}
