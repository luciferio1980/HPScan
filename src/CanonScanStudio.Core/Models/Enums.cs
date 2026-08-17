namespace CanonScanStudio.Models;

public enum ScannerInterfaceKind
{
    Auto = 0,
    Wia = 1,
    Twain = 2,
    WindowsScan = 3
}

public enum ScannerConnectionKind
{
    Unknown = 0,
    Usb = 1,
    Network = 2
}

public enum ScannerAvailability
{
    Unknown = 0,
    Ready = 1,
    Scanning = 2,
    Offline = 3,
    Busy = 4,
    NotFound = 5
}

public enum ColorMode
{
    Color = 0,
    Grayscale = 1,
    BlackAndWhite = 2
}

public enum OutputFormat
{
    Pdf = 0,
    Jpeg = 1,
    Png = 2,
    Tiff = 3
}

public enum SendToDestination
{
    LocalFolder = 0,
    Desktop = 1,
    Documents = 2,
    EmailPlaceholder = 3
}

public enum ScanSourceKind
{
    Flatbed = 0
}

public enum PageSourceKind
{
    Scanned = 0,
    Imported = 1
}

public enum DocumentFilter
{
    None = 0,
    Grayscale = 1,
    BlackAndWhite = 2,
    Invert = 3
}
