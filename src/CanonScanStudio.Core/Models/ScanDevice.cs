namespace CanonScanStudio.Models;

public sealed class ScanDevice
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required ScannerInterfaceKind Interface { get; init; }
    public ScannerConnectionKind Connection { get; init; }
    public bool IsCanonTs5100Family { get; init; }
    public string? Manufacturer { get; init; }
    public string? Port { get; init; }
    public string StatusText { get; init; } = "Desconocido";
    public bool IsAvailable { get; init; }

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "Escáner" : Name;

    public string InterfaceLabel => Interface switch
    {
        ScannerInterfaceKind.Wia => "WIA",
        ScannerInterfaceKind.Twain => "TWAIN",
        _ => "Automático"
    };
}
