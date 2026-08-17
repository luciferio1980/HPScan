using System.Net;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace CanonScanStudio.Scanning.Network;

/// <summary>
/// Localiza la IP del PIXMA TS5151 en Wi-Fi a partir de la cola de impresión,
/// la tabla ARP (p. ej. MAC 6C:F2:D8:…) y los archivos del Selector de red Canon.
/// WIA a menudo no publica el escáner de red aunque el Selector EX2 sí lo vea.
/// </summary>
public static class CanonNetworkLocator
{
    private static readonly Regex Ipv4 = new(
        @"(?:(?:25[0-5]|2[0-4]\d|[01]?\d\d?)\.){3}(?:25[0-5]|2[0-4]\d|[01]?\d\d?)",
        RegexOptions.Compiled);

    private static readonly Regex Mac = new(
        @"(?:[0-9A-Fa-f]{2}[:\-]){5}[0-9A-Fa-f]{2}",
        RegexOptions.Compiled);

    private static readonly string[] CanonOui =
    [
        "6CF2D8", // TS5151 del usuario
        "001E8F",
        "180CAC",
        "2C9EFC",
        "60128B",
        "84BA3B",
        "888717",
        "F48139",
        "6C3BE5",
        "74BFC0",
        "9C32CE",
        "A41437",
        "CCC3EA",
        "001B24"
    ];

    public static IReadOnlyList<NetworkScanTarget> Discover()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        var targets = new Dictionary<string, NetworkScanTarget>(StringComparer.OrdinalIgnoreCase);

        foreach (var printer in ReadCanonPrinters())
        {
            Add(targets, printer.Ip, printer.Name, "impresora Windows: " + printer.Port);
        }

        var selectorMacs = ReadSelectorMacs();
        foreach (var row in ReadArpTable())
        {
            var compact = CompactMac(row.Mac);
            var fromSelector = selectorMacs.Contains(compact);
            var canonOui = CanonOui.Any(oui => compact.StartsWith(oui, StringComparison.OrdinalIgnoreCase));
            if (fromSelector || canonOui)
            {
                Add(targets, row.Ip, fromSelector ? "Canon TS5100 series (Selector EX2)" : "Canon en red",
                    "ARP " + FormatMac(compact));
            }
        }

        return targets.Values.ToList();
    }

    public static string BuildSummary()
    {
        var targets = Discover();
        if (targets.Count == 0)
        {
            return "No se ha encontrado una IP de Canon en impresoras ni en ARP. Si el Selector EX2 muestra un MAC, pulsa Aceptar y Reintentar.";
        }

        return "Candidatos de red: " + string.Join("; ", targets.Select(t => $"{t.Ip} ({t.Source})"));
    }

    public static string? ExtractIpv4(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = Ipv4.Match(text);
        return match.Success ? match.Value : null;
    }

    public static string CompactMac(string mac) =>
        mac.Replace(":", "", StringComparison.Ordinal)
            .Replace("-", "", StringComparison.Ordinal)
            .Replace(" ", "", StringComparison.Ordinal)
            .ToUpperInvariant();

    public static IReadOnlyList<string> ExtractMacs(string text) =>
        Mac.Matches(text).Select(m => CompactMac(m.Value)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static void Add(Dictionary<string, NetworkScanTarget> targets, string? ip, string name, string source)
    {
        if (string.IsNullOrWhiteSpace(ip) || ip.StartsWith("127.", StringComparison.Ordinal) ||
            ip.StartsWith("169.254.", StringComparison.Ordinal))
        {
            return;
        }

        if (!IPAddress.TryParse(ip, out var parsed) || parsed.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return;
        }

        if (targets.ContainsKey(ip))
        {
            return;
        }

        targets[ip] = new NetworkScanTarget(ip, name, source);
    }

    [SupportedOSPlatform("windows")]
    private static IReadOnlyList<(string Name, string Port, string? Ip)> ReadCanonPrinters()
    {
        var list = new List<(string, string, string?)>();
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Print\Printers");
            if (key is null)
            {
                return list;
            }

            foreach (var name in key.GetSubKeyNames())
            {
                if (!LooksCanon(name))
                {
                    continue;
                }

                using var printer = key.OpenSubKey(name);
                var port = printer?.GetValue("Port") as string ?? "";
                var ip = ExtractIpv4(port) ?? ReadMonitorPortAddress(port);
                list.Add((name, port, ip));
            }
        }
        catch
        {
            // El registro de impresoras puede no ser accesible.
        }

        return list;
    }

    [SupportedOSPlatform("windows")]
    private static string? ReadMonitorPortAddress(string portName)
    {
        if (string.IsNullOrWhiteSpace(portName))
        {
            return null;
        }

        try
        {
            using var monitors = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Print\Monitors");
            if (monitors is null)
            {
                return null;
            }

            foreach (var monitor in monitors.GetSubKeyNames())
            {
                using var ports = monitors.OpenSubKey(monitor + @"\Ports\" + portName)
                                  ?? monitors.OpenSubKey(monitor + @"\Ports");
                if (ports is null)
                {
                    continue;
                }

                if (ports.GetSubKeyNames().Length > 0 && monitors.OpenSubKey(monitor + @"\Ports\" + portName) is { } named)
                {
                    using (named)
                    {
                        var ip = ExtractIpv4(named.GetValue("HostAddress") as string)
                                 ?? ExtractIpv4(named.GetValue("IPAddress") as string)
                                 ?? ExtractIpv4(named.GetValue("HostName") as string)
                                 ?? ExtractIpv4(named.GetValue("IP") as string);
                        if (ip is not null)
                        {
                            return ip;
                        }
                    }
                }

                foreach (var valueName in new[] { "HostAddress", "IPAddress", "HostName", "IP" })
                {
                    var ip = ExtractIpv4(ports.GetValue(valueName) as string);
                    if (ip is not null)
                    {
                        return ip;
                    }
                }
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    [SupportedOSPlatform("windows")]
    private static HashSet<string> ReadSelectorMacs()
    {
        var macs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var roots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Canon"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Canon"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Canon")
        };

        foreach (var root in roots.Where(Directory.Exists))
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                             .Where(f => f.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)
                                         || f.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
                                         || f.EndsWith(".dat", StringComparison.OrdinalIgnoreCase)
                                         || f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
                                         || f.EndsWith(".cfg", StringComparison.OrdinalIgnoreCase)))
                {
                    if (new FileInfo(file).Length > 512_000)
                    {
                        continue;
                    }

                    string text;
                    try
                    {
                        text = File.ReadAllText(file);
                    }
                    catch
                    {
                        continue;
                    }

                    foreach (var mac in ExtractMacs(text))
                    {
                        macs.Add(mac);
                    }
                }
            }
            catch
            {
                // Algunos directorios de Canon no son accesibles.
            }
        }

        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view);
                using var canon = baseKey.OpenSubKey(@"Software\Canon");
                if (canon is null)
                {
                    continue;
                }

                ReadRegistryMacs(canon, macs, depth: 0);
            }
            catch
            {
                // ignore
            }
        }

        return macs;
    }

    [SupportedOSPlatform("windows")]
    private static void ReadRegistryMacs(RegistryKey key, HashSet<string> macs, int depth)
    {
        if (depth > 6)
        {
            return;
        }

        foreach (var name in key.GetValueNames())
        {
            if (key.GetValue(name) is string text)
            {
                foreach (var mac in ExtractMacs(text))
                {
                    macs.Add(mac);
                }
            }
        }

        foreach (var subName in key.GetSubKeyNames())
        {
            using var sub = key.OpenSubKey(subName);
            if (sub is not null)
            {
                ReadRegistryMacs(sub, macs, depth + 1);
            }
        }
    }

    private static IReadOnlyList<(string Mac, string Ip)> ReadArpTable()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        var size = 0;
        _ = GetIpNetTable(IntPtr.Zero, ref size, false);
        if (size <= 0)
        {
            return [];
        }

        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            var status = GetIpNetTable(buffer, ref size, false);
            if (status != 0)
            {
                return [];
            }

            var count = Marshal.ReadInt32(buffer);
            var list = new List<(string, string)>(count);
            var offset = 4;
            const int rowSize = 24;
            for (var i = 0; i < count && offset + rowSize <= size; i++)
            {
                var physLen = Marshal.ReadInt32(buffer, offset + 4);
                physLen = Math.Clamp(physLen, 0, 8);
                var macBytes = new byte[physLen];
                if (physLen > 0)
                {
                    Marshal.Copy(IntPtr.Add(buffer, offset + 8), macBytes, 0, physLen);
                }

                var addr = unchecked((uint)Marshal.ReadInt32(buffer, offset + 16));
                var ipBytes = BitConverter.GetBytes(addr);
                var ip = new IPAddress(ipBytes).ToString();
                if (physLen >= 6)
                {
                    var mac = string.Concat(macBytes.Take(6).Select(b => b.ToString("X2")));
                    list.Add((mac, ip));
                }

                offset += rowSize;
            }

            return list;
        }
        catch
        {
            return [];
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static bool LooksCanon(string name)
    {
        var n = name.ToLowerInvariant();
        return n.Contains("canon", StringComparison.Ordinal) ||
               n.Contains("ts5100", StringComparison.Ordinal) ||
               n.Contains("ts5151", StringComparison.Ordinal) ||
               n.Contains("pixma", StringComparison.Ordinal);
    }

    private static string FormatMac(string compact)
    {
        if (compact.Length < 12)
        {
            return compact;
        }

        return string.Join(":", Enumerable.Range(0, 6).Select(i => compact.Substring(i * 2, 2)));
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern int GetIpNetTable(IntPtr pIpNetTable, ref int pdwSize, bool bOrder);
}

public sealed record NetworkScanTarget(string Ip, string Name, string Source);
