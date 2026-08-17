using System.Globalization;
using System.Net;
using System.Text;
using System.Xml.Linq;
using CanonScanStudio.Models;

namespace CanonScanStudio.Scanning.Escl;

internal static class EsclProtocol
{
    public const string CapabilitiesPath = "/eSCL/ScannerCapabilities";
    public const string ScanJobsPath = "/eSCL/ScanJobs";

    private static readonly XNamespace ScanNs = "http://schemas.hp.com/imaging/escl/2011/05/03";
    private static readonly XNamespace PwgNs = "http://www.pwg.org/schemas/2010/12/sm";

    public static string? ReadMakeAndModel(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return null;
        }

        try
        {
            var doc = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
            var node = doc.Descendants().FirstOrDefault(e =>
                e.Name.LocalName.Equals("MakeAndModel", StringComparison.OrdinalIgnoreCase));
            var value = node?.Value?.Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch
        {
            return null;
        }
    }

    public static string BuildScanSettings(ScanRequest request)
    {
        var dpi = request.Dpi <= 0 ? 300 : request.Dpi;
        var color = request.ColorMode switch
        {
            ColorMode.Grayscale => "Grayscale8",
            ColorMode.BlackAndWhite => "BlackAndWhite1",
            _ => "RGB24"
        };
        var width = (int)Math.Round(request.PageSize.WidthInches * 300);
        var height = (int)Math.Round(request.PageSize.HeightInches * 300);

        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <scan:ScanSettings xmlns:scan="{ScanNs}" xmlns:pwg="{PwgNs}">
              <pwg:Version>2.0</pwg:Version>
              <pwg:ScanRegions>
                <pwg:ScanRegion>
                  <pwg:ContentRegionUnits>escl:ThreeHundredthsOfInches</pwg:ContentRegionUnits>
                  <pwg:Width>{width.ToString(CultureInfo.InvariantCulture)}</pwg:Width>
                  <pwg:Height>{height.ToString(CultureInfo.InvariantCulture)}</pwg:Height>
                  <pwg:XOffset>0</pwg:XOffset>
                  <pwg:YOffset>0</pwg:YOffset>
                </pwg:ScanRegion>
              </pwg:ScanRegions>
              <pwg:InputSource>Platen</pwg:InputSource>
              <scan:Intent>Document</scan:Intent>
              <pwg:DocumentFormat>image/jpeg</pwg:DocumentFormat>
              <scan:ColorMode>{color}</scan:ColorMode>
              <scan:XResolution>{dpi.ToString(CultureInfo.InvariantCulture)}</scan:XResolution>
              <scan:YResolution>{dpi.ToString(CultureInfo.InvariantCulture)}</scan:YResolution>
            </scan:ScanSettings>
            """;
    }

    public static bool IsImageBytes(byte[] bytes)
    {
        if (bytes.Length < 4)
        {
            return false;
        }

        return (bytes[0] == 0xFF && bytes[1] == 0xD8) ||
               (bytes[0] == 0x89 && bytes[1] == 0x50) ||
               (bytes[0] == (byte)'B' && bytes[1] == (byte)'M') ||
               (bytes[0] == (byte)'I' && bytes[1] == (byte)'I') ||
               (bytes[0] == (byte)'M' && bytes[1] == (byte)'M');
    }

    public static bool IsXmlOrHtml(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return false;
        }

        var take = Math.Min(bytes.Length, 80);
        var text = Encoding.UTF8.GetString(bytes, 0, take).TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
        return text.StartsWith('<') || text.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase);
    }

    public static string FormatHint(byte[] bytes) =>
        bytes.Length >= 2 && bytes[0] == 0x89 && bytes[1] == 0x50 ? "png"
        : bytes.Length >= 2 && bytes[0] == (byte)'I' && bytes[1] == (byte)'I' ? "tiff"
        : bytes.Length >= 2 && bytes[0] == (byte)'M' && bytes[1] == (byte)'M' ? "tiff"
        : "jpeg";

    public static Uri? ResolveJobUri(string baseUrl, Uri? location, IEnumerable<string>? extraLocations)
    {
        if (location is not null)
        {
            return location.IsAbsoluteUri ? location : new Uri(new Uri(baseUrl.TrimEnd('/') + "/"), location);
        }

        var raw = extraLocations?.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        if (raw is null)
        {
            return null;
        }

        if (Uri.TryCreate(raw, UriKind.Absolute, out var abs))
        {
            return abs;
        }

        return Uri.TryCreate(new Uri(baseUrl.TrimEnd('/') + "/"), raw, out var rel) ? rel : null;
    }

    public static bool ShouldRetryNextDocument(HttpStatusCode status) =>
        status is HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.RequestTimeout
            or HttpStatusCode.NotFound
            or HttpStatusCode.Conflict
            or (HttpStatusCode)425;
}
