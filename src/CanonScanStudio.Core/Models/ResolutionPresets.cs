namespace CanonScanStudio.Models;

/// <summary>
/// El PIXMA TS5151 (serie TS5100) tiene CIS óptico 1200×2400 dpi.
/// WIA en red a veces solo publica hasta 600; la UI igual ofrece 1200.
/// </summary>
public static class ResolutionPresets
{
    public static readonly int[] Standard = [75, 150, 200, 300, 600, 1200];

    public static IReadOnlyList<int> ForTs5151(IEnumerable<int>? advertised = null)
    {
        var set = new SortedSet<int>(Standard);
        if (advertised is not null)
        {
            foreach (var dpi in advertised)
            {
                if (dpi is >= 50 and <= 2400)
                {
                    set.Add(dpi);
                }
            }
        }

        return set.ToList();
    }
}
