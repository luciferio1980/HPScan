namespace CanonScanStudio.Models;

public sealed record ScanRequest
{
    public required string DeviceId { get; init; }
    public int Dpi { get; init; } = 300;
    public ColorMode ColorMode { get; init; } = ColorMode.Color;
    public PageSizeDefinition PageSize { get; init; } = PageSizeDefinition.A4;
    public ScanSourceKind Source { get; init; } = ScanSourceKind.Flatbed;
    public IProgress<ScanProgress>? Progress { get; init; }
    public CancellationToken CancellationToken { get; init; }
}

public sealed record ScanProgress(int Percent, string Message);

public sealed record ScanResult
{
    public required byte[] ImageBytes { get; init; }
    public required string FormatHint { get; init; }
    public int Dpi { get; init; }
    public ColorMode ColorMode { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public ScannerInterfaceKind Interface { get; init; }
    public string DeviceName { get; init; } = "";
}

public sealed class AppSettings
{
    public bool FirstRunCompleted { get; set; }
    public string? PreferredDeviceId { get; set; }
    public string? PreferredDeviceName { get; set; }
    public ScannerInterfaceKind Interface { get; set; } = ScannerInterfaceKind.Auto;
    public int DefaultDpi { get; set; } = 300;
    public ColorMode DefaultColorMode { get; set; } = ColorMode.Color;
    public string DefaultPageSizeId { get; set; } = "A4";
    public string DefaultSaveFolder { get; set; } = "";
    public OutputFormat DefaultFormat { get; set; } = OutputFormat.Pdf;
    public SendToDestination Destination { get; set; } = SendToDestination.LocalFolder;
    public bool RestoreLastSession { get; set; }
    public bool ConfirmPageDelete { get; set; } = true;
    public bool ShowDetailedErrors { get; set; }
    public bool OcrEnabled { get; set; }
    public string OcrLanguage { get; set; } = "spa";
    public bool AutoExposure { get; set; }
    public double CustomWidthInches { get; set; } = 8.27;
    public double CustomHeightInches { get; set; } = 11.69;
}

public sealed class DiagnosticReport
{
    public ScanDevice? Device { get; init; }
    public ScanCapabilities? Capabilities { get; init; }
    public string Interface { get; init; } = "Ninguna";
    public string Status { get; init; } = "No disponible";
    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}

public sealed class OcrWord
{
    public required string Text { get; init; }
    public double Left { get; init; }
    public double Top { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
    public float Confidence { get; init; }
}

public sealed record OcrPageResult
{
    public required Guid PageId { get; init; }
    public required string Text { get; init; }
    public IReadOnlyList<OcrWord> Words { get; init; } = Array.Empty<OcrWord>();
    public string Language { get; init; } = "spa";
}

public sealed class ExportOptions
{
    public required string DestinationFolder { get; init; }
    public required string FileNameWithoutExtension { get; init; }
    public OutputFormat Format { get; init; } = OutputFormat.Pdf;
    public bool SeparateImages { get; init; }
    public bool SearchablePdf { get; init; }
    public string OcrLanguage { get; init; } = "spa";
    public int JpegQuality { get; init; } = 92;
}
