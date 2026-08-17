namespace CanonScanStudio.Models;

public sealed class ScanCapabilities
{
    public string DeviceId { get; init; } = "";
    public string DeviceName { get; init; } = "";
    public ScannerInterfaceKind Interface { get; init; }
    public IReadOnlyList<int> ResolutionsDpi { get; init; } = Array.Empty<int>();
    public IReadOnlyList<ColorMode> ColorModes { get; init; } = Array.Empty<ColorMode>();
    public IReadOnlyList<ScanSourceKind> Sources { get; init; } = [ScanSourceKind.Flatbed];
    public double MaxWidthInches { get; init; } = 8.5;
    public double MaxHeightInches { get; init; } = 11.7;
    public bool SupportsBrightness { get; init; }
    public bool SupportsContrast { get; init; }
    public bool HasAutomaticDocumentFeeder { get; init; }
    public string? Notes { get; init; }

    public bool SupportsDpi(int dpi) => ResolutionsDpi.Contains(dpi);

    public int ClosestDpi(int requested)
    {
        if (ResolutionsDpi.Count == 0)
        {
            return requested;
        }

        return ResolutionsDpi.OrderBy(d => Math.Abs(d - requested)).First();
    }
}
