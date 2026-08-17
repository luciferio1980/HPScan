using CanonScanStudio.Scanning.Wia;
using FluentAssertions;

namespace CanonScanStudio.Tests;

public class WiaBackendPlatformTests
{
    [Fact]
    public void Wia_does_not_invent_devices_when_windows_wia_is_unavailable()
    {
        var backend = new WiaScannerBackend(new InMemoryLog());
        var devices = backend.ListDevices();
        if (!OperatingSystem.IsWindows())
        {
            devices.Should().BeEmpty();
            backend.IsPlatformSupported.Should().BeFalse();
        }
    }
}
