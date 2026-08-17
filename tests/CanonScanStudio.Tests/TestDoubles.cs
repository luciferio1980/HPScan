using CanonScanStudio.Infrastructure;
using CanonScanStudio.Models;
using CanonScanStudio.Scanning;
using CanonScanStudio.Services;

namespace CanonScanStudio.Tests;

internal sealed class InMemoryLog : IAppLog
{
    public List<string> Lines { get; } = [];
    public string LogDirectory => Path.GetTempPath();
    public void Info(string message) => Lines.Add(message);
    public void Warn(string message) => Lines.Add(message);
    public void Error(string message, Exception? exception = null) => Lines.Add(message + exception);
    public string ExportTo(string destinationFile)
    {
        File.WriteAllLines(destinationFile, Lines);
        return destinationFile;
    }
}

internal sealed class MockScannerBackend : IScannerBackend
{
    public ScannerInterfaceKind Interface { get; init; } = ScannerInterfaceKind.Wia;
    public bool IsPlatformSupported { get; init; } = true;
    public List<ScanDevice> Devices { get; init; } = [];
    public ScanCapabilities? Capabilities { get; init; }
    public Func<ScanRequest, ScanResult>? ScanHandler { get; init; }
    public bool ThrowOnScan { get; init; }

    public IReadOnlyList<ScanDevice> ListDevices() => Devices;
    public ScanCapabilities GetCapabilities(string deviceId) =>
        Capabilities ?? new ScanCapabilities
        {
            DeviceId = deviceId,
            DeviceName = Devices.FirstOrDefault()?.Name ?? deviceId,
            Interface = Interface,
            ResolutionsDpi = [75, 150, 300, 600],
            ColorModes = [ColorMode.Color, ColorMode.Grayscale, ColorMode.BlackAndWhite],
            MaxWidthInches = 8.5,
            MaxHeightInches = 11.7
        };

    public ScanResult Scan(ScanRequest request)
    {
        if (ThrowOnScan)
        {
            throw new ScannerException("No se puede acceder al escáner.", canRetry: true);
        }

        return ScanHandler?.Invoke(request) ?? throw new InvalidOperationException("Configure ScanHandler in tests.");
    }

    public bool CanConnect(string deviceId) => Devices.Any(d => d.Id == deviceId);
}

internal sealed class TempSettingsService : ISettingsService
{
    public AppSettings Current { get; } = new();
    public void Load() { }
    public void Save() { }
}
