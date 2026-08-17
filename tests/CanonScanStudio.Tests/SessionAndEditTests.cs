using CanonScanStudio.Models;
using CanonScanStudio.Services;
using FluentAssertions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace CanonScanStudio.Tests;

public class SessionAndEditTests : IDisposable
{
    private readonly string _root;

    public SessionAndEditTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "css-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        Environment.SetEnvironmentVariable("CANON_SCAN_STUDIO_DATA", _root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { /* ignore */ }
    }

    [Fact]
    public void Session_reorders_and_removes_pages()
    {
        var session = new ScanSession();
        session.Pages.Add(Page("a"));
        session.Pages.Add(Page("b"));
        session.Pages.Add(Page("c"));
        session.MovePage(2, 0);
        session.Pages.Select(p => p.OriginalPath).Should().Equal("c", "a", "b");
        session.Pages.RemoveAt(1);
        session.Renumber();
        session.Pages.Select(p => p.Order).Should().Equal(0, 1);
    }

    [Fact]
    public void Apply_order_resequences_pages()
    {
        var session = new ScanSession();
        var a = Page("a");
        var b = Page("b");
        var c = Page("c");
        session.Pages.AddRange([a, b, c]);
        session.ApplyOrder([c.Id, a.Id, b.Id]);
        session.Pages.Select(p => p.OriginalPath).Should().Equal("c", "a", "b");
        session.Pages.Select(p => p.Order).Should().Equal(0, 1, 2);
    }

    [Fact]
    public void Undo_redo_restores_edit_state()
    {
        var undo = new UndoService();
        var page = new ScanPage { OriginalPath = "x.png", Edit = PageEditState.Identity() };
        var previous = page.Edit.Clone();
        undo.Execute(new DelegateCommand("rotar",
            () => page.Edit.RotationDegrees = 90,
            () => page.Edit = previous.Clone()));
        page.Edit.RotationDegrees.Should().Be(90);
        undo.Undo();
        page.Edit.RotationDegrees.Should().Be(0);
        undo.Redo();
        page.Edit.RotationDegrees.Should().Be(90);
    }

    [Fact]
    public void Image_processing_rotates_and_adjusts_brightness()
    {
        var path = Path.Combine(_root, "orig.png");
        using (var image = new Image<Rgba32>(20, 10, new Rgba32(120, 80, 80)))
        {
            image.SaveAsPng(path);
        }

        var service = new ImageProcessingService();
        var rotated = service.ApplyEdits(path, new PageEditState { RotationDegrees = 90 });
        using var rotatedImage = Image.Load<Rgba32>(rotated);
        rotatedImage.Width.Should().Be(10);
        rotatedImage.Height.Should().Be(20);

        var bright = service.ApplyEdits(path, new PageEditState { Brightness = 40, Contrast = 20 });
        bright.Length.Should().BeGreaterThan(0);

        var crop = service.ApplyEdits(path, new PageEditState { Crop = new CropRegion(2, 2, 8, 6) });
        using var cropped = Image.Load<Rgba32>(crop);
        cropped.Width.Should().Be(8);
        cropped.Height.Should().Be(6);

        var baked = service.CropBytes(File.ReadAllBytes(path), new CropRegion(2, 2, 8, 6));
        using var bakedImage = Image.Load<Rgba32>(baked);
        bakedImage.Width.Should().Be(8);
        bakedImage.Height.Should().Be(6);
        var bakedInfo = service.ReadInfo(baked);
        bakedInfo.Width.Should().Be(8);
    }

    [Fact]
    public void Preview_resize_happens_after_crop_not_before_decode()
    {
        var path = Path.Combine(_root, "orig.png");
        using (var image = new Image<Rgba32>(20, 10, new Rgba32(120, 80, 80)))
        {
            image.SaveAsPng(path);
        }

        var preview = new ImageProcessingService().ApplyEdits(
            path,
            new PageEditState { Crop = new CropRegion(2, 2, 8, 6) },
            4);
        using var result = Image.Load<Rgba32>(preview);
        result.Width.Should().Be(4);
        result.Height.Should().Be(3);
        preview[0].Should().Be(0x89);
        preview[1].Should().Be(0x50);
    }

    [Fact]
    public void Jpeg_scan_preview_is_png_without_decoder_target_size()
    {
        var path = Path.Combine(_root, "scan.jpg");
        using (var image = new Image<Rgba32>(40, 30, new Rgba32(10, 20, 200)))
        {
            image.SaveAsJpeg(path);
        }

        var service = new ImageProcessingService();
        var preview = service.ApplyEdits(path, PageEditState.Identity(), 20);
        preview[0].Should().Be(0x89);
        preview[1].Should().Be(0x50);
        using var result = Image.Load<Rgba32>(preview);
        result.Width.Should().Be(20);
        result.Height.Should().Be(15);

        var thumb = service.CreateThumbnail(path, PageEditState.Identity(), 10);
        using var thumbImage = Image.Load<Rgba32>(thumb);
        thumbImage.Width.Should().BeLessThanOrEqualTo(10);
        thumb[0].Should().Be(0x89);
    }

    [Fact]
    public void Rotate_left_and_right_step_by_90_degrees()
    {
        var edit = PageEditState.Identity();
        edit.RotateRight();
        edit.RotationDegrees.Should().Be(90);
        edit.RotateRight();
        edit.RotationDegrees.Should().Be(180);
        edit.RotateLeft();
        edit.RotationDegrees.Should().Be(90);
        edit.RotateLeft();
        edit.RotationDegrees.Should().Be(0);
        edit.RotateLeft();
        edit.RotationDegrees.Should().Be(270);
        edit.RotateRight();
        edit.RotationDegrees.Should().Be(0);
    }

    [Fact]
    public void Import_jpeg_returns_png_bytes()
    {
        var path = Path.Combine(_root, "photo.jpg");
        using (var image = new Image<Rgba32>(16, 12, new Rgba32(40, 80, 120)))
        {
            image.SaveAsJpeg(path);
        }

        var imported = new ImportService(new InMemoryLog(), new ImageProcessingService()).Import(path);
        imported.Should().HaveCount(1);
        imported[0].Extension.Should().Be(".png");
        imported[0].Bytes[0].Should().Be(0x89);
        imported[0].Bytes[1].Should().Be(0x50);
        using var png = Image.Load<Rgba32>(imported[0].Bytes);
        png.Width.Should().Be(16);
        png.Height.Should().Be(12);
    }

    [Fact]
    public void Duplicate_and_remove_pages_copy_files_on_disk()
    {
        var png = Path.Combine(_root, "page.png");
        using (var image = new Image<Rgba32>(8, 8, new Rgba32(1, 2, 3)))
        {
            image.SaveAsPng(png);
        }

        var bytes = File.ReadAllBytes(png);
        var session = new SessionService(new InMemoryLog());
        var first = session.AddScannedPage(new ScanResult
        {
            ImageBytes = bytes,
            FormatHint = "png",
            Dpi = 300,
            Width = 8,
            Height = 8
        }, bytes, png);
        var copy = session.DuplicatePage(first.Id);

        session.Current.Pages.Should().HaveCount(2);
        copy.OriginalPath.Should().NotBe(first.OriginalPath);
        File.Exists(copy.OriginalPath).Should().BeTrue();
        File.ReadAllBytes(copy.OriginalPath).Should().Equal(bytes);

        session.RemovePages([first.Id]);
        session.Current.Pages.Should().HaveCount(1);
        session.Current.Pages[0].Id.Should().Be(copy.Id);
        session.Current.Pages[0].Order.Should().Be(0);
    }

    [Fact]
    public void Reset_edit_returns_to_identity()
    {
        var edit = new PageEditState { Brightness = 12, RotationDegrees = 180, Contrast = -5 };
        edit.HasChanges.Should().BeTrue();
        var reset = PageEditState.Identity();
        reset.HasChanges.Should().BeFalse();
    }

    private static ScanPage Page(string name) => new() { OriginalPath = name };
}
