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

    /// <summary>
    /// Phone photos and screenshots often store 1 DPI or thousands of DPI.
    /// That makes a PDF page the size of a billboard or a stamp, and QuestPDF fails.
    /// </summary>
    public static int SanitizeDpi(int widthPx, int heightPx, int metadataDpi)
    {
        widthPx = Math.Max(1, widthPx);
        heightPx = Math.Max(1, heightPx);

        var dpi = metadataDpi;
        if (dpi < 36 || dpi > 2400)
        {
            dpi = ScanSettingDefaults.Dpi;
        }

        var longPx = Math.Max(widthPx, heightPx);
        var shortPx = Math.Min(widthPx, heightPx);
        var longInches = longPx / (double)dpi;
        var shortInches = shortPx / (double)dpi;
        if (longInches > 20 || shortInches > 17 || (longInches < 0.75 && longPx >= 80))
        {
            dpi = (int)Math.Clamp(
                Math.Round(longPx / PageSizeDefinition.A4.HeightInches),
                72,
                1200);
        }

        return dpi;
    }

    public static (float WidthPts, float HeightPts) PdfPageSizePoints(int widthPx, int heightPx, int dpi)
    {
        var safeDpi = SanitizeDpi(widthPx, heightPx, dpi);
        var widthPts = widthPx * 72f / safeDpi;
        var heightPts = heightPx * 72f / safeDpi;
        const float minPts = 72f;
        const float maxPts = 14400f;
        var scale = 1f;
        var longest = Math.Max(widthPts, heightPts);
        var shortest = Math.Min(widthPts, heightPts);
        if (longest > maxPts)
        {
            scale = maxPts / longest;
        }

        if (shortest * scale < minPts)
        {
            scale = minPts / shortest;
        }

        return (widthPts * scale, heightPts * scale);
    }
}
