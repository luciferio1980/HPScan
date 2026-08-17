using CanonScanStudio.Models;
using FluentAssertions;

namespace CanonScanStudio.Tests;

public class PageSizeTests
{
    [Fact]
    public void Size_label_shows_pixels_and_dpi()
    {
        var page = new ScanPage
        {
            OriginalPath = "x.jpg",
            OriginalWidth = 4961,
            OriginalHeight = 7016,
            Dpi = 600
        };
        page.SizeLabel.Should().Be("4961 × 7016 px · 600 DPI");
    }

    [Fact]
    public void Clamps_custom_size_to_scanner_bed()
    {
        var custom = PageSizeDefinition.Custom with { WidthInches = 20, HeightInches = 20 };
        var clamped = custom.ClampTo(8.5, 11.7);
        clamped.WidthInches.Should().Be(8.5);
        clamped.HeightInches.Should().Be(11.7);
    }

    [Fact]
    public void Sanitize_dpi_fixes_imported_photo_metadata()
    {
        ResolutionPresets.SanitizeDpi(1220, 1549, 1).Should().Be(300);
        ResolutionPresets.SanitizeDpi(372, 628, 3780).Should().Be(300);
        ResolutionPresets.SanitizeDpi(2480, 3508, 300).Should().Be(300);
        ResolutionPresets.SanitizeDpi(4961, 7016, 600).Should().Be(600);

        var huge = ResolutionPresets.PdfPageSizePoints(1220, 1549, 1);
        huge.WidthPts.Should().BeInRange(72, 14400);
        huge.HeightPts.Should().BeInRange(72, 14400);
        var tiny = ResolutionPresets.PdfPageSizePoints(372, 628, 3780);
        tiny.WidthPts.Should().BeInRange(72, 14400);
        tiny.HeightPts.Should().BeInRange(72, 14400);
    }
}
