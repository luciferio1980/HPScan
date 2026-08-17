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
        EsclProtocol.BuildScanSettings(new ScanRequest
        {
            DeviceId = "escl:192.168.1.10",
            Dpi = 1200,
            ColorMode = ColorMode.Color
        }).Should().Contain("1200");
    }

    [Fact]
    public void Parses_discrete_resolutions_without_inventing_1200()
    {
        var xml = """
            <?xml version="1.0"?>
            <scan:ScannerCapabilities xmlns:scan="http://schemas.hp.com/imaging/escl/2011/05/03" xmlns:pwg="http://www.pwg.org/schemas/2010/12/sm">
              <pwg:MakeAndModel>Canon TS5100 series</pwg:MakeAndModel>
              <scan:Platen>
                <scan:PlatenInputCaps>
                  <scan:MaxWidth>2550</scan:MaxWidth>
                  <scan:MaxHeight>3508</scan:MaxHeight>
                  <scan:SettingProfiles>
                    <scan:SettingProfile>
                      <scan:SupportedResolutions>
                        <scan:DiscreteResolutions>
                          <scan:DiscreteResolution>
                            <scan:XResolution>75</scan:XResolution>
                            <scan:YResolution>75</scan:YResolution>
                          </scan:DiscreteResolution>
                          <scan:DiscreteResolution>
                            <scan:XResolution>300</scan:XResolution>
                            <scan:YResolution>300</scan:YResolution>
                          </scan:DiscreteResolution>
                          <scan:DiscreteResolution>
                            <scan:XResolution>600</scan:XResolution>
                            <scan:YResolution>600</scan:YResolution>
                          </scan:DiscreteResolution>
                        </scan:DiscreteResolutions>
                      </scan:SupportedResolutions>
                    </scan:SettingProfile>
                  </scan:SettingProfiles>
                </scan:PlatenInputCaps>
              </scan:Platen>
            </scan:ScannerCapabilities>
            """;
        var parsed = EsclCapabilitiesParser.Parse(xml);
        parsed.ResolutionsDpi.Should().Equal(75, 300, 600);
        parsed.ResolutionsDpi.Should().NotContain(1200);
    }

    [Fact]
    public void Range_max_1200_includes_1200()
    {
        var xml = """
            <scan:ScannerCapabilities xmlns:scan="http://schemas.hp.com/imaging/escl/2011/05/03">
              <scan:XResolutionRange>
                <scan:Min>75</scan:Min>
                <scan:Max>1200</scan:Max>
              </scan:XResolutionRange>
            </scan:ScannerCapabilities>
            """;
        EsclCapabilitiesParser.Parse(xml).ResolutionsDpi.Should().Contain(1200);
        EsclCapabilitiesParser.Parse(xml).ResolutionsDpi.Should().Contain(600);
    }

    [Fact]
    public void Advertised_resolutions_are_not_padded_with_1200()
    {
        ResolutionPresets.UntilDeviceReady.Should().Equal(75, 150, 300, 600);
        ResolutionPresets.MergeAdvertised([150, 300, 600]).Should().Equal(150, 300, 600);
        ResolutionPresets.MergeAdvertised([150, 300, 600]).Should().NotContain(1200);
        ResolutionPresets.MergeAdvertised([75, 300, 600, 1200]).Should().Contain(1200);
        ResolutionPresets.MergeAdvertised(null).Should().Equal(ResolutionPresets.UntilDeviceReady);
    }

    [Theory]
    [InlineData(2480, 8.27, 300)]
    [InlineData(4961, 8.27, 600)]
    [InlineData(9924, 8.27, 1200)]
    public void Infers_dpi_from_pixel_width(int pixels, double inches, int expected)
    {
        ResolutionPresets.InferFromPixels(pixels, inches).Should().Be(expected);
    }

    [Fact]
    public void Detects_jpeg_magic()
    {
        EsclProtocol.IsImageBytes([0xFF, 0xD8, 0xFF, 0xE0]).Should().BeTrue();
        EsclProtocol.IsImageBytes("<html>"u8.ToArray()).Should().BeFalse();
        EsclProtocol.FormatHint([0xFF, 0xD8]).Should().Be("jpeg");
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
