using System.IO;
using CanonScanStudio.Infrastructure;
using CanonScanStudio.Models;
using CanonScanStudio.Scanning;
using Windows.Devices.Enumeration;
using Windows.Devices.Scanners;
using Windows.Storage;
using Windows.Storage.Streams;

namespace CanonScanStudio.App.Scanning;

/// <summary>
/// Descubre escáneres con la API de Windows (WSD/eSCL/WIA). Cubre el PIXMA en Wi-Fi
/// cuando Windows lo ha añadido como impresora pero WIA clásico no lo lista.
/// </summary>
public sealed class WinRtScannerBackend : IScannerBackend
{
    private readonly IAppLog _log;

    public WinRtScannerBackend(IAppLog log)
    {
        _log = log;
    }

    public ScannerInterfaceKind Interface => ScannerInterfaceKind.WindowsScan;

    public bool IsPlatformSupported => OperatingSystem.IsWindowsVersionAtLeast(10);

    public IReadOnlyList<ScanDevice> ListDevices()
    {
        if (!IsPlatformSupported)
        {
            return Array.Empty<ScanDevice>();
        }

        try
        {
            var selector = ImageScanner.GetDeviceSelector();
            var found = DeviceInformation.FindAllAsync(selector).AsTask().GetAwaiter().GetResult();
            var devices = new List<ScanDevice>(found.Count);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var info in found)
            {
                AddWinRtDevice(devices, seen, info);
            }

            try
            {
                var stillImage = DeviceInformation.FindAllAsync(
                        @"System.Devices.InterfaceClassGuid:=""{6BDD1FC6-810F-11D0-BEC7-08002BE2092F}""")
                    .AsTask().GetAwaiter().GetResult();
                foreach (var info in stillImage)
                {
                    if (!DeviceMatcher.IsCanonTs5100Family(info.Name) &&
                        !info.Name.Contains("canon", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    AddWinRtDevice(devices, seen, info);
                }
            }
            catch (Exception ex)
            {
                _log.Warn("No se han podido enumerar dispositivos Still Image de Windows: " + ex.Message);
            }

            _log.Info($"Windows Scan ha encontrado {devices.Count} dispositivo(s).");
            return devices;
        }
        catch (Exception ex)
        {
            _log.Warn("No se han podido enumerar escáneres de Windows: " + ex.Message);
            return Array.Empty<ScanDevice>();
        }
    }

    private void AddWinRtDevice(List<ScanDevice> devices, HashSet<string> seen, DeviceInformation info)
    {
        if (!seen.Add(info.Id))
        {
            return;
        }

        var name = string.IsNullOrWhiteSpace(info.Name) ? "Escáner Windows" : info.Name;
        devices.Add(new ScanDevice
        {
            Id = "winrt:" + info.Id,
            Name = name,
            Interface = ScannerInterfaceKind.WindowsScan,
            Connection = DeviceMatcher.InferConnection(name, info.Id),
            IsCanonTs5100Family = DeviceMatcher.IsCanonTs5100Family(name),
            StatusText = info.IsEnabled ? "Detectado" : "Deshabilitado",
            IsAvailable = info.IsEnabled
        });
        _log.Info($"Windows Scan: '{name}' id={info.Id} enabled={info.IsEnabled}");
    }

    public ScanCapabilities GetCapabilities(string deviceId)
    {
        var scanner = Open(deviceId);
        var name = Unwrap(deviceId);
        var resolutions = new List<int>();
        var colorModes = new List<ColorMode>();
        var source = PreferredSource(scanner);
        try
        {
            if (source == ImageScannerScanSource.Flatbed)
            {
                var cfg = scanner.FlatbedConfiguration;
                AddResolution(resolutions, cfg.MinResolution, cfg.MaxResolution, cfg.OpticalResolution);
                AddColorModes(colorModes, cfg);
            }
        }
        catch (Exception ex)
        {
            _log.Warn("No se han podido leer capacidades de Windows Scan: " + ex.Message);
        }

        if (resolutions.Count == 0)
        {
            resolutions.AddRange([150, 300, 600]);
        }

        if (colorModes.Count == 0)
        {
            colorModes.AddRange([ColorMode.Color, ColorMode.Grayscale, ColorMode.BlackAndWhite]);
        }

        return new ScanCapabilities
        {
            DeviceId = deviceId,
            DeviceName = name,
            Interface = ScannerInterfaceKind.WindowsScan,
            ResolutionsDpi = resolutions.Distinct().OrderBy(v => v).ToArray(),
            ColorModes = colorModes,
            Sources = [ScanSourceKind.Flatbed],
            MaxWidthInches = 8.5,
            MaxHeightInches = 11.7,
            SupportsBrightness = false,
            SupportsContrast = false,
            Notes = "Escáner detectado por Windows (WSD/eSCL/WIA). El PIXMA TS5151 usa la platina."
        };
    }

    public bool CanConnect(string deviceId)
    {
        try
        {
            _ = Open(deviceId);
            return true;
        }
        catch (Exception ex)
        {
            _log.Warn($"Windows Scan no puede abrir {deviceId}: {ex.Message}");
            return false;
        }
    }

    public ScanDevice? PickInteractively() => null;

    public ScanResult Scan(ScanRequest request)
    {
        request.Progress?.Report(new ScanProgress(10, "Abriendo el escáner de Windows..."));
        var scanner = Open(request.DeviceId);
        var source = PreferredSource(scanner);
        request.Progress?.Report(new ScanProgress(25, "Configurando el escaneo..."));
        ApplySettings(scanner, source, request);

        StorageFolder folder;
        try
        {
            folder = ApplicationData.Current.TemporaryFolder;
        }
        catch
        {
            folder = StorageFolder.GetFolderFromPathAsync(Path.GetTempPath()).AsTask().GetAwaiter().GetResult();
        }
        request.Progress?.Report(new ScanProgress(45, "Escaneando..."));
        var operation = scanner.ScanFilesToFolderAsync(source, folder);
        using (request.CancellationToken.Register(() =>
        {
            try { operation.Cancel(); } catch { /* ignore */ }
        }))
        {
            var result = operation.AsTask().GetAwaiter().GetResult();
            var file = result.ScannedFiles.FirstOrDefault()
                       ?? throw new ScannerException("El escáner ha completado la operación pero no ha devuelto una imagen.", canRetry: true);
            request.Progress?.Report(new ScanProgress(85, "Recibiendo la imagen..."));
            var buffer = FileIO.ReadBufferAsync(file).AsTask().GetAwaiter().GetResult();
            var bytes = new byte[buffer.Length];
            using (var reader = DataReader.FromBuffer(buffer))
            {
                reader.ReadBytes(bytes);
            }

            try
            {
                file.DeleteAsync(StorageDeleteOption.PermanentDelete).AsTask().GetAwaiter().GetResult();
            }
            catch
            {
                // El temporal de Windows se limpia solo.
            }

            return new ScanResult
            {
                ImageBytes = bytes,
                FormatHint = file.FileType?.Trim('.') ?? "jpg",
                Dpi = request.Dpi,
                ColorMode = request.ColorMode,
                Width = 0,
                Height = 0,
                Interface = ScannerInterfaceKind.WindowsScan,
                DeviceName = Unwrap(request.DeviceId)
            };
        }
    }

    private ImageScanner Open(string deviceId)
    {
        var id = Unwrap(deviceId);
        return ImageScanner.FromIdAsync(id).AsTask().GetAwaiter().GetResult();
    }

    private static string Unwrap(string deviceId) =>
        deviceId.StartsWith("winrt:", StringComparison.OrdinalIgnoreCase) ? deviceId["winrt:".Length..] : deviceId;

    private static ImageScannerScanSource PreferredSource(ImageScanner scanner)
    {
        if (scanner.IsScanSourceSupported(ImageScannerScanSource.Flatbed))
        {
            return ImageScannerScanSource.Flatbed;
        }

        if (scanner.IsScanSourceSupported(ImageScannerScanSource.AutoConfigured))
        {
            return ImageScannerScanSource.AutoConfigured;
        }

        if (scanner.IsScanSourceSupported(ImageScannerScanSource.Feeder))
        {
            return ImageScannerScanSource.Feeder;
        }

        throw new ScannerException("Windows no ha publicado ninguna fuente de escaneo (platina) para este dispositivo.", canRetry: true);
    }

    private void ApplySettings(ImageScanner scanner, ImageScannerScanSource source, ScanRequest request)
    {
        try
        {
            if (source != ImageScannerScanSource.Flatbed)
            {
                return;
            }

            var cfg = scanner.FlatbedConfiguration;
            var dpi = Math.Clamp(request.Dpi, (int)Math.Max(50, cfg.MinResolution.DpiX), (int)Math.Max(cfg.MinResolution.DpiX, cfg.MaxResolution.DpiX));
            cfg.DesiredResolution = new ImageScannerResolution { DpiX = dpi, DpiY = dpi };
            var desired = request.ColorMode switch
            {
                ColorMode.Grayscale => ImageScannerColorMode.Grayscale,
                ColorMode.BlackAndWhite => ImageScannerColorMode.Monochrome,
                _ => ImageScannerColorMode.Color
            };
            if (cfg.IsColorModeSupported(desired))
            {
                cfg.ColorMode = desired;
            }
        }
        catch (Exception ex)
        {
            _log.Warn("Windows Scan no ha aceptado todos los ajustes; se usarán los del controlador. " + ex.Message);
        }
    }

    private static void AddResolution(List<int> resolutions, ImageScannerResolution min, ImageScannerResolution max, ImageScannerResolution optical)
    {
        foreach (var candidate in new[] { 75, 150, 200, 300, 600, 1200, (int)optical.DpiX })
        {
            if (candidate >= min.DpiX && candidate <= max.DpiX && candidate > 0)
            {
                resolutions.Add(candidate);
            }
        }
    }

    private static void AddColorModes(List<ColorMode> modes, ImageScannerFlatbedConfiguration cfg)
    {
        if (cfg.IsColorModeSupported(ImageScannerColorMode.Color)) modes.Add(ColorMode.Color);
        if (cfg.IsColorModeSupported(ImageScannerColorMode.Grayscale)) modes.Add(ColorMode.Grayscale);
        if (cfg.IsColorModeSupported(ImageScannerColorMode.Monochrome)) modes.Add(ColorMode.BlackAndWhite);
    }
}
