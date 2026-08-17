using CanonScanStudio.Models;
using CanonScanStudio.Scanning;
using FluentAssertions;

namespace CanonScanStudio.Tests;

public class DeviceMatcherTests
{
    [Theory]
    [InlineData("Canon TS5100 series", true)]
    [InlineData("Canon PIXMA TS5151", true)]
    [InlineData("TS5151", true)]
    [InlineData("TS5100 series_AABBCCDDEEFF", true)]
    [InlineData("Canon TS5150 series", true)]
    [InlineData("Escáner Canon TS5100 series", true)]
    [InlineData("Canon Inkjet TS5100 series", true)]
    [InlineData("HP DeskJet 4100 series", false)]
    [InlineData("", false)]
    public void Detects_ts5100_family_names(string name, bool expected)
    {
        DeviceMatcher.IsCanonTs5100Family(name).Should().Be(expected);
    }

    [Theory]
    [InlineData("WIA-Canon TS5100 series", true)]
    [InlineData("eSCL TS5151", true)]
    [InlineData("Generic Camera", false)]
    public void Looks_like_scanner_names(string name, bool expected)
    {
        DeviceMatcher.LooksLikeScanner(name).Should().Be(expected);
    }

    [Fact]
    public void Prefers_ts5151_over_generic_scanner()
    {
        var devices = new[]
        {
            new ScanDevice { Id = "other", Name = "Generic Scanner", Interface = ScannerInterfaceKind.Wia, IsAvailable = true },
            new ScanDevice { Id = "canon", Name = "Canon TS5100 series", Interface = ScannerInterfaceKind.Wia, IsCanonTs5100Family = true, IsAvailable = true }
        };

        var selected = DeviceMatcher.SelectPreferred(devices);
        selected!.Id.Should().Be("canon");
    }

    [Fact]
    public void Infers_network_from_mac_suffix()
    {
        DeviceMatcher.InferConnection("TS5100 series_AABBCCDDEEFF", null)
            .Should().Be(ScannerConnectionKind.Network);
    }

    [Fact]
    public void Infers_usb_from_port_name()
    {
        DeviceMatcher.InferConnection("Canon TS5100 series", "USB")
            .Should().Be(ScannerConnectionKind.Usb);
    }
}
