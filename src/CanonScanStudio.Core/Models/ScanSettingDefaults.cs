namespace CanonScanStudio.Models;

public static class ScanSettingDefaults
{
    public const int Dpi = 300;
    public const ColorMode Color = ColorMode.Color;
    public const double Zoom = 1;

    public static int ChooseDpi(IReadOnlyList<int>? advertised, int current)
    {
        var list = ResolutionPresets.MergeAdvertised(advertised);
        var requested = current > 0 ? current : Dpi;
        if (list.Contains(requested))
        {
            return requested;
        }

        if (list.Contains(Dpi))
        {
            return Dpi;
        }

        return list.OrderBy(d => Math.Abs(d - requested)).First();
    }

    public static ColorMode ChooseColor(IReadOnlyList<ColorMode>? advertised, ColorMode current)
    {
        IReadOnlyList<ColorMode> modes = advertised is { Count: > 0 }
            ? advertised
            : [ColorMode.Color, ColorMode.Grayscale, ColorMode.BlackAndWhite];
        if (modes.Contains(current))
        {
            return current;
        }

        if (modes.Contains(ColorMode.Color))
        {
            return ColorMode.Color;
        }

        return modes[0];
    }
}
