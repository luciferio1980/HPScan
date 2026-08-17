using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace CanonScanStudio.App.Services;

/// <summary>
/// Ayuda a instalar y localizar el MP Driver de la serie TS5100.
/// No redistribuye software de Canon: abre la página oficial y, si está instalado,
/// el Selector de escáner de red (imprescindible en Wi-Fi).
/// </summary>
public static class CanonSetupHelper
{
    /// <summary>
    /// Página oficial de controladores del PIXMA TS5151 (serie TS5100) en Canon España.
    /// Hay que instalar el paquete «MP Drivers», no solo añadir la impresora en Windows.
    /// </summary>
    public const string DriverPageUrl =
        "https://www.canon.es/support/consumer/products/printers/pixma/ts-series/pixma-ts5151.html?type=drivers&detailId=tcm:86-1604954&productTcmUri=tcm:86-1604881";

    public static void OpenDriverPage() => OpenUrl(DriverPageUrl);

    public static void OpenWindowsPrinters() => OpenUrl("ms-settings:printers");

    public static bool TryOpenNetworkSelector()
    {
        var path = FindNetworkSelector();
        if (path is null)
        {
            return false;
        }

        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        return true;
    }

    public static string? FindNetworkSelector()
    {
        var roots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Canon"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Canon")
        };

        string[] folders =
        [
            "IJ Network Scanner Selector EX",
            "IJ Network Scanner Selector EX2",
            "Canon IJ Network Scanner Selector EX2",
            "IJNetworkScannerSelectorEX"
        ];

        string[] exes = ["CNMNSST.exe", "IJNetworkScannerSelectorEX.exe", "NSE.exe"];

        foreach (var root in roots.Where(Directory.Exists))
        {
            foreach (var folder in folders)
            {
                var dir = Path.Combine(root, folder);
                if (!Directory.Exists(dir))
                {
                    continue;
                }

                foreach (var exe in exes)
                {
                    var candidate = Path.Combine(dir, exe);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }

                var found = Directory.EnumerateFiles(dir, "*.exe", SearchOption.TopDirectoryOnly).FirstOrDefault();
                if (found is not null)
                {
                    return found;
                }
            }

            try
            {
                var deep = Directory.EnumerateFiles(root, "CNMNSST.exe", SearchOption.AllDirectories).FirstOrDefault();
                if (deep is not null)
                {
                    return deep;
                }
            }
            catch
            {
                // Algunos directorios de Canon pueden no ser accesibles.
            }
        }

        return null;
    }

    public static string BuildHint()
    {
        var lines = new List<string>();
        var mp = IsMpDriverInstalled();
        var selector = FindNetworkSelector();
        var printers = ListCanonPrinters().ToList();

        lines.Add(mp
            ? "MP Driver de Canon: parece instalado."
            : "MP Driver de Canon: no se ha encontrado. Descárgalo desde la web oficial (serie TS5100) e instálalo con la impresora encendida.");

        if (selector is null)
        {
            lines.Add("En Wi-Fi hace falta el «IJ Network Scanner Selector EX» (viene con el MP Driver). Ábrelo, marca el TS5100 y pulsa OK.");
        }
        else
        {
            lines.Add("Selector de red Canon encontrado. Ábrelo, selecciona el TS5100 series y pulsa OK; luego Actualizar dispositivos.");
        }

        if (printers.Count > 0)
        {
            lines.Add("Windows sí ve estas colas de impresión: " + string.Join(", ", printers) + ". Eso no implica que el escáner esté instalado.");
        }

        return string.Join(Environment.NewLine, lines);
    }

    public static bool IsMpDriverInstalled()
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall is null)
                {
                    continue;
                }

                foreach (var name in uninstall.GetSubKeyNames())
                {
                    using var key = uninstall.OpenSubKey(name);
                    var display = key?.GetValue("DisplayName") as string;
                    if (string.IsNullOrWhiteSpace(display))
                    {
                        continue;
                    }

                    var n = display.ToLowerInvariant();
                    if (n.Contains("canon", StringComparison.Ordinal) &&
                        (n.Contains("ts5100", StringComparison.Ordinal) ||
                         n.Contains("ts5151", StringComparison.Ordinal) ||
                         n.Contains("mp driver", StringComparison.Ordinal) ||
                         n.Contains("scangear", StringComparison.Ordinal)))
                    {
                        return true;
                    }
                }
            }
            catch
            {
                // El registro puede no ser accesible en algunos equipos.
            }
        }

        return FindNetworkSelector() is not null;
    }

    public static IReadOnlyList<string> ListCanonPrinters()
    {
        var names = new List<string>();
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Print\Printers");
            if (key is null)
            {
                return names;
            }

            foreach (var name in key.GetSubKeyNames())
            {
                if (DeviceLooksCanon(name))
                {
                    names.Add(name);
                }
            }
        }
        catch
        {
            // ignore
        }

        return names;
    }

    private static bool DeviceLooksCanon(string name)
    {
        var n = name.ToLowerInvariant();
        return n.Contains("canon", StringComparison.Ordinal) ||
               n.Contains("ts5100", StringComparison.Ordinal) ||
               n.Contains("ts5151", StringComparison.Ordinal) ||
               n.Contains("pixma", StringComparison.Ordinal);
    }

    private static void OpenUrl(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
