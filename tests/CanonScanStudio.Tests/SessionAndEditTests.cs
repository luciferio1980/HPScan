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
    public void Reset_edit_returns_to_identity()
    {
        var edit = new PageEditState { Brightness = 12, RotationDegrees = 180, Contrast = -5 };
        edit.HasChanges.Should().BeTrue();
        var reset = PageEditState.Identity();
        reset.HasChanges.Should().BeFalse();
    }

    private static ScanPage Page(string name) => new() { OriginalPath = name };
}
