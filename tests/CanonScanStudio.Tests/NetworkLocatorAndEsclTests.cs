using CanonScanStudio.Scanning;
using CanonScanStudio.Scanning.Escl;
using CanonScanStudio.Scanning.Network;
using CanonScanStudio.Models;
using FluentAssertions;

namespace CanonScanStudio.Tests;

public class NetworkLocatorAndEsclTests
{
    [Theory]
    [InlineData("IP_192.168.1.45", "192.168.1.45")]
    [InlineData("CNBJNP_10.0.0.8", "10.0.0.8")]
    [InlineData("USB001", null)]
    [InlineData("WSD-abc", null)]
    public void Extracts_ipv4_from_port_names(string port, string? expected)
    {
        CanonNetworkLocator.ExtractIpv4(port).Should().Be(expected);
    }

    [Fact]
    public void Extracts_macs_from_selector_text()
    {
        var text = "TS5100 series 6C:F2:D8:C8:FA:E7 checked";
        CanonNetworkLocator.ExtractMacs(text).Should().Contain("6CF2D8C8FAE7");
    }

    [Fact]
    public void Compact_mac_strips_separators()
    {
        CanonNetworkLocator.CompactMac("6c:f2:d8:c8:fa:e7").Should().Be("6CF2D8C8FAE7");
    }

    [Fact]
    public void Reads_make_and_model_from_escl_capabilities()
    {
        var xml = """
            <?xml version="1.0"?>
            <scan:ScannerCapabilities xmlns:scan="http://schemas.hp.com/imaging/escl/2011/05/03" xmlns:pwg="http://www.pwg.org/schemas/2010/12/sm">
              <pwg:MakeAndModel>Canon TS5100 series</pwg:MakeAndModel>
            </scan:ScannerCapabilities>
            """;
        EsclProtocol.ReadMakeAndModel(xml).Should().Be("Canon TS5100 series");
    }

    [Fact]
    public void Scan_settings_include_resolution_and_platen()
    {
        var xml = EsclProtocol.BuildScanSettings(new ScanRequest
        {
            DeviceId = "escl:192.168.1.10",
            Dpi = 300,
            ColorMode = ColorMode.Color,
            PageSize = PageSizeDefinition.A4
        });
        xml.Should().Contain("300");
        xml.Should().Contain("RGB24");
        xml.Should().Contain("Platen");
        xml.Should().Contain("image/jpeg");
    }

    [Fact]
    public void Detects_network_product_name()
    {
        DeviceMatcher.IsCanonTs5100Family("Canon TS5100 series Network").Should().BeTrue();
        DeviceMatcher.InferConnection("Canon TS5100 series Network", null)
            .Should().Be(ScannerConnectionKind.Network);
        DeviceMatcher.LooksLikeMacAddress("TS5100 series - 6C:F2:D8:C8:FA:E7").Should().BeTrue();
    }
}
