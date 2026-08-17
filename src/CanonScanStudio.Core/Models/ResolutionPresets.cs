namespace CanonScanStudio.Models;

public static class ResolutionPresets
{
    /// <summary>
    /// Shown before a scanner reports its own list. Intentionally excludes 1200:
    /// Wi-Fi eSCL on TS5100 is typically max 600, and offering 1200 caused failed scans.
    /// </summary>
    public static readonly int[] UntilDeviceReady = [75, 150, 300, 600];

    /// <summary>USB WIA on TS5151 often advertises these when the MP Driver is healthy.</summary>
    public static readonly int[] UsbTs5151Typical = [75, 100, 150, 200, 300, 600, 1200];

    public static IReadOnlyList<int> MergeAdvertised(IEnumerable<int>? advertised)
    {
        var set = new SortedSet<int>();
        if (advertised is not null)
        {
            foreach (var dpi in advertised)
            {
                if (dpi is >= 50 and <= 9600)
                {
                    set.Add(dpi);
                }
            }
        }

        if (set.Count == 0)
        {
            return UntilDeviceReady;
        }

        return set.ToArray();
    }

    /// <summary>
    /// Pixel width vs paper width in inches → nearest common DPI so the UI matches the file.
    /// </summary>
    public static int InferFromPixels(int pixelWidth, double pageWidthInches)
    {
        if (pixelWidth < 8 || pageWidthInches < 0.4)
        {
            return 0;
        }

        var raw = pixelWidth / pageWidthInches;
        var candidates = new[] { 75, 100, 150, 200, 300, 400, 600, 1200, 2400 };
        var best = candidates[0];
        var bestDelta = double.MaxValue;
        foreach (var c in candidates)
        {
            var d = Math.Abs(c - raw);
            if (d < bestDelta)
            {
                bestDelta = d;
                best = c;
            }
        }

        return bestDelta / best > 0.18 ? (int)Math.Round(raw) : best;
    }
}
