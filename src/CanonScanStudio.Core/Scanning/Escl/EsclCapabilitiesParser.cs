using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CanonScanStudio.Models;

namespace CanonScanStudio.Scanning.Escl;

public sealed record EsclParsedCapabilities(
    IReadOnlyList<int> ResolutionsDpi,
    IReadOnlyList<string> DocumentFormats,
    bool HasAdf,
    int MaxWidth300ths,
    int MaxHeight300ths);

/// <summary>
/// Reads the DPI list the scanner actually advertises in ScannerCapabilities.
/// Does not invent 1200 DPI when the XML only lists 600.
/// </summary>
public static class EsclCapabilitiesParser
{
    /// <summary>eSCL MaxWidth/MaxHeight are in 1/300 inch units.</summary>
    public const int UnitsPerInch = 300;

    public static EsclParsedCapabilities Parse(string xml)
    {
        var resolutions = new SortedSet<int>();
        var formats = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hasAdf = false;
        var maxW = 0;
        var maxH = 0;

        if (string.IsNullOrWhiteSpace(xml))
        {
            return Empty();
        }

        try
        {
            var doc = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
            foreach (var el in doc.Descendants())
            {
                var local = el.Name.LocalName;
                if (local is "XResolution" or "YResolution" && TryDpi(el.Value, out var dpi))
                {
                    resolutions.Add(dpi);
                }
                else if (local is "Min" or "MinValue" && IsResolutionRangeParent(el.Parent)
                         && TryDpi(el.Value, out var minDpi))
                {
                    resolutions.Add(minDpi);
                }
                else if (local is "Max" or "MaxValue" && IsResolutionRangeParent(el.Parent)
                         && TryDpi(el.Value, out var maxDpi))
                {
                    resolutions.Add(maxDpi);
                    ExpandRange(resolutions, maxDpi);
                }
                else if (local is "DocumentFormat" or "DocumentFormatExt")
                {
                    var fmt = el.Value.Trim();
                    if (fmt.Length > 0)
                    {
                        formats.Add(fmt);
                    }
                }
                else if (local is "MaxWidth" && TryInt(el.Value, out var w))
                {
                    maxW = Math.Max(maxW, w);
                }
                else if (local is "MaxHeight" && TryInt(el.Value, out var h))
                {
                    maxH = Math.Max(maxH, h);
                }
                else if (local is "Adf" or "AdfSimplexInputCaps" or "AdfDuplexInputCaps")
                {
                    hasAdf = true;
                }
            }
        }
        catch (Exception)
        {
            foreach (Match match in Regex.Matches(xml, @"<(?:\w+:)?XResolution>(\d{2,4})</"))
            {
                if (int.TryParse(match.Groups[1].Value, out var dpi) && dpi is >= 50 and <= 9600)
                {
                    resolutions.Add(dpi);
                }
            }
        }

        if (resolutions.Count == 0)
        {
            return Empty();
        }

        return new EsclParsedCapabilities(
            resolutions.ToArray(),
            formats.Count == 0 ? ["image/jpeg"] : formats.ToArray(),
            hasAdf,
            maxW,
            maxH);
    }

    public static ScanCapabilities ToScanCapabilities(
        EsclParsedCapabilities parsed,
        string deviceId,
        string deviceName,
        string notes)
    {
        var maxW = parsed.MaxWidth300ths > 0 ? parsed.MaxWidth300ths / (double)UnitsPerInch : 8.5;
        var maxH = parsed.MaxHeight300ths > 0 ? parsed.MaxHeight300ths / (double)UnitsPerInch : 11.7;
        return new ScanCapabilities
        {
            DeviceId = deviceId,
            DeviceName = deviceName,
            Interface = ScannerInterfaceKind.Escl,
            ResolutionsDpi = parsed.ResolutionsDpi.Count == 0
                ? ResolutionPresets.UntilDeviceReady
                : parsed.ResolutionsDpi,
            ColorModes = [ColorMode.Color, ColorMode.Grayscale, ColorMode.BlackAndWhite],
            Sources = [ScanSourceKind.Flatbed],
            MaxWidthInches = maxW,
            MaxHeightInches = maxH,
            HasAutomaticDocumentFeeder = parsed.HasAdf,
            Notes = notes
        };
    }

    private static EsclParsedCapabilities Empty() =>
        new(ResolutionPresets.UntilDeviceReady, ["image/jpeg"], false, 0, 0);

    private static bool IsResolutionRangeParent(XElement? parent)
    {
        var name = parent?.Name.LocalName ?? "";
        return name.Contains("Resolution", StringComparison.OrdinalIgnoreCase);
    }

    private static void ExpandRange(SortedSet<int> resolutions, int maxDpi)
    {
        foreach (var step in ResolutionPresets.UntilDeviceReady)
        {
            if (step <= maxDpi)
            {
                resolutions.Add(step);
            }
        }

        if (maxDpi >= 1200)
        {
            resolutions.Add(1200);
        }
    }

    private static bool TryDpi(string? text, out int dpi)
    {
        dpi = 0;
        if (!TryInt(text, out var value) || value is < 50 or > 9600)
        {
            return false;
        }

        dpi = value;
        return true;
    }

    private static bool TryInt(string? text, out int value) =>
        int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
}
