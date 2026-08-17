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

    [Fact]
    public void App_themes_normalize_unknown_ids_to_claro()
    {
        AppThemes.All.Should().HaveCount(7);
        AppThemes.All.Select(t => t.Id).Should().Contain(["claro", "oscuro", "medianoche", "bosque", "atardecer", "oceano", "lavanda"]);
        AppThemes.Normalize(null).Should().Be("claro");
        AppThemes.Normalize("").Should().Be("claro");
        AppThemes.Normalize("OSCURO").Should().Be("oscuro");
        AppThemes.Normalize("no-existe").Should().Be("claro");
        new AppSettings().ThemeId.Should().Be("claro");
    }

    [Fact]
    public void Dark_theme_files_do_not_use_claro_body_text_color()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CanonScanStudio.sln")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("el test debe ejecutarse dentro del repositorio");
        foreach (var id in new[] { "oscuro", "medianoche" })
        {
            var path = Path.Combine(dir!.FullName, "src", "CanonScanStudio.App", "Themes", $"Theme.{id}.xaml");
            File.Exists(path).Should().BeTrue(path);
            var xaml = File.ReadAllText(path);
            xaml.Should().Contain("x:Key=\"TextPrimary\"");
            xaml.Should().Contain("x:Key=\"ControlBg\"");
            xaml.Should().NotContain("Color=\"#2B2B2B\"");
        }
    }
}
