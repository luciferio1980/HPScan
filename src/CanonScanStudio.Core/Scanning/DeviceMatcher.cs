using CanonScanStudio.Models;

namespace CanonScanStudio.Scanning;

/// <summary>
/// Identifica el PIXMA TS5151 aunque Windows lo publique como familia TS5100.
/// El controlador oficial no garantiza el nombre exacto "Canon PIXMA TS5151".
/// </summary>
public static class DeviceMatcher
{
    private static readonly string[] Ts5100Tokens =
    [
        "ts5151",
        "ts5150",
        "ts5100",
        "pixma ts51",
        "canon ts51",
        "ts51",
        "5151"
    ];

    private static readonly string[] ScannerTokens =
    [
        "scan",
        "escáner",
        "escaner",
        "wia",
        "escl",
        "wsd",
        "scangear",
        "pixma",
        "flatbed",
        "platina",
        "ts5100",
        "network"
    ];

    public static bool IsCanonTs5100Family(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var normalized = Normalize(name);
        return Ts5100Tokens.Any(token => normalized.Contains(token, StringComparison.Ordinal));
    }

    public static bool IsScannerName(string? name) => !string.IsNullOrWhiteSpace(name);

    public static bool LooksLikeScanner(string? name)
    {
        if (IsCanonTs5100Family(name))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var normalized = Normalize(name);
        return ScannerTokens.Any(token => normalized.Contains(token, StringComparison.Ordinal));
    }

    public static int Score(ScanDevice device)
    {
        var score = 0;
        var name = Normalize(device.Name);

        if (name.Contains("ts5151", StringComparison.Ordinal)) score += 100;
        else if (name.Contains("ts5150", StringComparison.Ordinal)) score += 90;
        else if (name.Contains("ts5100", StringComparison.Ordinal)) score += 80;
        else if (name.Contains("pixma", StringComparison.Ordinal)) score += 40;

        if (name.Contains("canon", StringComparison.Ordinal)) score += 20;
        if (device.Interface == ScannerInterfaceKind.Wia) score += 10;
        if (device.Interface == ScannerInterfaceKind.WindowsScan) score += 8;
        if (device.Interface == ScannerInterfaceKind.Escl) score += 12;
        if (device.IsAvailable) score += 5;
        if (device.Connection == ScannerConnectionKind.Usb) score += 2;
        return score;
    }

    public static ScanDevice? SelectPreferred(IEnumerable<ScanDevice> devices, string? preferredId = null, string? preferredName = null)
    {
        var list = devices.ToList();
        if (list.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(preferredId))
        {
            var exact = list.FirstOrDefault(d => string.Equals(d.Id, preferredId, StringComparison.OrdinalIgnoreCase));
            if (exact is not null)
            {
                return exact;
            }
        }

        if (!string.IsNullOrWhiteSpace(preferredName))
        {
            var byName = list.FirstOrDefault(d => string.Equals(d.Name, preferredName, StringComparison.OrdinalIgnoreCase));
            if (byName is not null)
            {
                return byName;
            }
        }

        return list.OrderByDescending(Score).First();
    }

    public static ScannerConnectionKind InferConnection(string? name, string? port)
    {
        var haystack = $"{name} {port}".ToLowerInvariant();
        if (haystack.Contains("usb", StringComparison.Ordinal))
        {
            return ScannerConnectionKind.Usb;
        }

        if (haystack.Contains("wpd") || haystack.Contains("network") || haystack.Contains("wlan") ||
            haystack.Contains("wifi") || haystack.Contains("wi-fi") || haystack.Contains("escl") ||
            haystack.Contains("red") || LooksLikeMacSuffix(name) || LooksLikeMacAddress(haystack))
        {
            return ScannerConnectionKind.Network;
        }

        return ScannerConnectionKind.Unknown;
    }

    public static string Normalize(string value) =>
        value.Replace('_', ' ').Trim().ToLowerInvariant();

    private static bool LooksLikeMacSuffix(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        // Canon WIA en red: "TS5100 series_AABBCCDDEEFF"
        var parts = name.Split('_', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && parts[^1].Length is >= 8 and <= 16 &&
               parts[^1].All(c => Uri.IsHexDigit(c));
    }

    public static bool LooksLikeMacAddress(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return System.Text.RegularExpressions.Regex.IsMatch(
            value,
            @"(?:[0-9A-Fa-f]{2}[:\-]){5}[0-9A-Fa-f]{2}");
    }
}
