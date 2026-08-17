using CanonScanStudio.Models;
using CanonScanStudio.Scanning;

namespace CanonScanStudio.Services;

public interface IScannerService
{
    ScannerAvailability Status { get; }
    ScanDevice? SelectedDevice { get; }
    ScanCapabilities? Capabilities { get; }
    IReadOnlyList<ScanDevice> Devices { get; }
    event EventHandler? Changed;

    IReadOnlyList<ScanDevice> RefreshDevices();
    void SelectDevice(string? deviceId);
    ScanCapabilities? RefreshCapabilities();
    Task<ScanResult> ScanAsync(ScanRequest request);
    DiagnosticReport CreateDiagnosticReport();
}
