using CanonScanStudio.Models;
using FluentAssertions;

namespace CanonScanStudio.Tests;

public class ScanSettingDefaultsTests
{
    [Fact]
    public void Defaults_are_300_dpi_color_and_100_percent_zoom()
    {
        ScanSettingDefaults.Dpi.Should().Be(300);
        ScanSettingDefaults.Color.Should().Be(ColorMode.Color);
        ScanSettingDefaults.Zoom.Should().Be(1);
        new AppSettings().DefaultDpi.Should().Be(300);
        new AppSettings().DefaultColorMode.Should().Be(ColorMode.Color);
    }

    [Fact]
    public void ChooseDpi_keeps_configured_value_and_falls_back_to_300()
    {
        ScanSettingDefaults.ChooseDpi([75, 150, 300, 600], 300).Should().Be(300);
        ScanSettingDefaults.ChooseDpi([75, 150, 300, 600], 0).Should().Be(300);
        ScanSettingDefaults.ChooseDpi([75, 150, 300, 600], 600).Should().Be(600);
        ScanSettingDefaults.ChooseDpi([75, 150, 600], 300).Should().Be(150);
        ScanSettingDefaults.ChooseDpi(null, 0).Should().Be(300);
    }

    [Fact]
    public void ChooseColor_prefers_color_when_selection_is_missing()
    {
        var modes = new[] { ColorMode.Color, ColorMode.Grayscale, ColorMode.BlackAndWhite };
        ScanSettingDefaults.ChooseColor(modes, ColorMode.Color).Should().Be(ColorMode.Color);
        ScanSettingDefaults.ChooseColor(modes, ColorMode.Grayscale).Should().Be(ColorMode.Grayscale);
        ScanSettingDefaults.ChooseColor([ColorMode.Grayscale, ColorMode.BlackAndWhite], ColorMode.Color)
            .Should().Be(ColorMode.Grayscale);
        ScanSettingDefaults.ChooseColor([], ColorMode.BlackAndWhite).Should().Be(ColorMode.BlackAndWhite);
        ScanSettingDefaults.ChooseColor(null, (ColorMode)99).Should().Be(ColorMode.Color);
    }
}
