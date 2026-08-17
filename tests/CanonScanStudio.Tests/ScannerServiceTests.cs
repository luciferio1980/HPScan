using CanonScanStudio.Models;
using CanonScanStudio.Scanning;
using CanonScanStudio.Services;
using FluentAssertions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace CanonScanStudio.Tests;

public class ScannerServiceTests
{
    [Fact]
    public void Refresh_selects_canon_family_device()
    {
        var backend = new MockScannerBackend
        {
            Devices =
            [
                new ScanDevice { Id = "hp", Name = "Other Scanner", Interface = ScannerInterfaceKind.Wia, IsAvailable = true },
                new ScanDevice { Id = "wia-canon", Name = "Canon TS5100 series", Interface = ScannerInterfaceKind.Wia, IsCanonTs5100Family = true, IsAvailable = true }
            ]
        };
        var service = new ScannerService([backend], new TempSettingsService(), new InMemoryLog());
        service.RefreshDevices();
        service.SelectedDevice!.Id.Should().Be("wia-canon");
        service.Status.Should().Be(ScannerAvailability.Ready);
        service.Capabilities!.ResolutionsDpi.Should().Contain(300);
    }

    [Fact]
    public void Refresh_without_devices_is_not_found()
    {
        var service = new ScannerService([new MockScannerBackend()], new TempSettingsService(), new InMemoryLog());
        service.RefreshDevices();
        service.SelectedDevice.Should().BeNull();
        service.Status.Should().Be(ScannerAvailability.NotFound);
    }

    [Fact]
    public async Task Scan_uses_real_backend_result_not_a_placeholder()
    {
        var marker = "CANON-SCAN-MARKER"u8.ToArray();
        var backend = new MockScannerBackend
        {
            Devices = [new ScanDevice { Id = "wia-canon", Name = "Canon TS5100 series", Interface = ScannerInterfaceKind.Wia, IsAvailable = true, IsCanonTs5100Family = true }],
            ScanHandler = request =>
            {
                request.Dpi.Should().Be(300);
                request.ColorMode.Should().Be(ColorMode.Color);
                using var image = new Image<Rgba32>(32, 32, Color.White);
                using var ms = new MemoryStream();
                image.SaveAsPng(ms);
                return new ScanResult
                {
                    ImageBytes = ms.ToArray(),
                    FormatHint = "png",
                    Dpi = request.Dpi,
                    ColorMode = request.ColorMode,
                    Width = 32,
                    Height = 32,
                    Interface = ScannerInterfaceKind.Wia,
                    DeviceName = "Canon TS5100 series"
                };
            }
        };
        var service = new ScannerService([backend], new TempSettingsService(), new InMemoryLog());
        service.RefreshDevices();
        var result = await service.ScanAsync(new ScanRequest { DeviceId = "wia-canon", Dpi = 300, ColorMode = ColorMode.Color });
        result.ImageBytes.Length.Should().BeGreaterThan(0);
        result.DeviceName.Should().Contain("TS5100");
        result.Interface.Should().Be(ScannerInterfaceKind.Wia);
    }

    [Fact]
    public async Task Failed_scan_does_not_use_fake_image()
    {
        var backend = new MockScannerBackend
        {
            Devices = [new ScanDevice { Id = "wia-canon", Name = "Canon TS5100 series", Interface = ScannerInterfaceKind.Wia, IsAvailable = true }],
            ThrowOnScan = true
        };
        var service = new ScannerService([backend], new TempSettingsService(), new InMemoryLog());
        service.RefreshDevices();
        var act = async () => await service.ScanAsync(new ScanRequest { DeviceId = "wia-canon" });
        await act.Should().ThrowAsync<ScannerException>()
            .WithMessage("*escáner*");
    }
}
