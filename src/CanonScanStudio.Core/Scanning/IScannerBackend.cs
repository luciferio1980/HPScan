using CanonScanStudio.Models;

namespace CanonScanStudio.Scanning;

public interface IScannerBackend
{
    ScannerInterfaceKind Interface { get; }
    bool IsPlatformSupported { get; }
    IReadOnlyList<ScanDevice> ListDevices();
    ScanCapabilities GetCapabilities(string deviceId);
    ScanResult Scan(ScanRequest request);
    bool CanConnect(string deviceId);
    ScanDevice? PickInteractively();
}
