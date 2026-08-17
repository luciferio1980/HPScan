using CanonScanStudio.Models;
using FluentAssertions;

namespace CanonScanStudio.Tests;

public class PageSizeTests
{
    [Fact]
    public void Clamps_custom_size_to_scanner_bed()
    {
        var custom = PageSizeDefinition.Custom with { WidthInches = 20, HeightInches = 20 };
        var clamped = custom.ClampTo(8.5, 11.7);
        clamped.WidthInches.Should().Be(8.5);
        clamped.HeightInches.Should().Be(11.7);
    }
}
