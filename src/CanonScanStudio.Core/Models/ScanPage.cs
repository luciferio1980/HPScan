namespace CanonScanStudio.Models;

public sealed class ScanPage
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public int Order { get; set; }
    public required string OriginalPath { get; set; }
    public int Dpi { get; set; } = 300;
    public int OriginalWidth { get; set; }
    public int OriginalHeight { get; set; }
    public PageEditState Edit { get; set; } = PageEditState.Identity();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public PageSourceKind Source { get; set; } = PageSourceKind.Scanned;
    public ScannerInterfaceKind? CapturedWith { get; set; }
    public string? DeviceName { get; set; }
    public ColorMode ColorMode { get; set; } = ColorMode.Color;

    public double WidthInches => Dpi <= 0 ? 0 : OriginalWidth / (double)Dpi;
    public double HeightInches => Dpi <= 0 ? 0 : OriginalHeight / (double)Dpi;

    public string SizeLabel
    {
        get
        {
            if (OriginalWidth <= 0 || OriginalHeight <= 0)
            {
                return string.Empty;
            }

            return $"{OriginalWidth} × {OriginalHeight} px · {Dpi} DPI";
        }
    }

    public ScanPage CloneMetadata() => new()
    {
        Id = Guid.NewGuid(),
        Order = Order,
        OriginalPath = OriginalPath,
        Dpi = Dpi,
        OriginalWidth = OriginalWidth,
        OriginalHeight = OriginalHeight,
        Edit = Edit.Clone(),
        CreatedAt = DateTimeOffset.Now,
        Source = Source,
        CapturedWith = CapturedWith,
        DeviceName = DeviceName,
        ColorMode = ColorMode
    };
}
