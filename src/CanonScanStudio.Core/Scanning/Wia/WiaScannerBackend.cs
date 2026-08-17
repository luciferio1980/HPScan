using System.Runtime.InteropServices;
using CanonScanStudio.Infrastructure;
using CanonScanStudio.Models;

namespace CanonScanStudio.Scanning.Wia;

/// <summary>
/// Backend WIA real. No genera imágenes de prueba: si no hay dispositivo WIA, falla con un mensaje claro.
/// El PIXMA TS5151 se publica habitualmente como "Canon TS5100 series" (USB)
/// o "TS5100 series_&lt;MAC&gt;" (red).
/// </summary>
public sealed class WiaScannerBackend : IScannerBackend, IDisposable
{
    private readonly WiaStaDispatcher _sta = new();
    private readonly IAppLog _log;

    public WiaScannerBackend(IAppLog log)
    {
        _log = log;
    }

    public ScannerInterfaceKind Interface => ScannerInterfaceKind.Wia;

    public bool IsPlatformSupported => OperatingSystem.IsWindows();

    public IReadOnlyList<ScanDevice> ListDevices()
    {
        if (!IsPlatformSupported)
        {
            _log.Info("WIA solo está disponible en Windows. No se enumeran dispositivos en esta plataforma.");
            return Array.Empty<ScanDevice>();
        }

        try
        {
            return _sta.Invoke(ListDevicesCore);
        }
        catch (Exception ex)
        {
            _log.Error("Error al enumerar dispositivos WIA.", ex);
            throw WiaErrorMapper.Map(ex);
        }
    }

    public ScanCapabilities GetCapabilities(string deviceId)
    {
        EnsureWindows();
        try
        {
            return _sta.Invoke(() => GetCapabilitiesCore(deviceId));
        }
        catch (Exception ex)
        {
            _log.Error($"Error al leer capacidades WIA de {deviceId}.", ex);
            throw WiaErrorMapper.Map(ex);
        }
    }

    public bool CanConnect(string deviceId)
    {
        if (!IsPlatformSupported)
        {
            return false;
        }

        try
        {
            return _sta.Invoke(() => CanConnectCore(deviceId));
        }
        catch (Exception ex)
        {
            _log.Warn($"No se ha podido conectar con el dispositivo WIA {deviceId}: {ex.Message}");
            return false;
        }
    }

    public ScanResult Scan(ScanRequest request)
    {
        EnsureWindows();
        request.Progress?.Report(new ScanProgress(5, "Conectando con el escáner..."));
        try
        {
            return _sta.Invoke(() => ScanCore(request), request.CancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Error("El escaneo WIA ha fallado.", ex);
            throw WiaErrorMapper.Map(ex, request.DeviceId);
        }
    }

    public void Dispose() => _sta.Dispose();

    private List<ScanDevice> ListDevicesCore()
    {
        object? manager = null;
        var devices = new List<ScanDevice>();
        try
        {
            manager = WiaCom.CreateDeviceManager();
            var infos = WiaCom.Get(manager, "DeviceInfos")
                        ?? throw new ScannerException("WIA no ha devuelto la lista de dispositivos.", canRetry: true);
            var count = WiaCom.Count(infos);
            _log.Info($"WIA ha encontrado {count} dispositivo(s) en DeviceManager.");

            for (var i = 1; i <= count; i++)
            {
                object? info = null;
                try
                {
                    info = WiaCom.Item(infos, i);
                    var type = Convert.ToInt32(WiaCom.Get(info, "Type") ?? 0);
                    if (type != WiaConstants.ScannerDeviceType)
                    {
                        continue;
                    }

                    var properties = WiaCom.Get(info, "Properties")!;
                    var id = WiaCom.ReadString(properties, WiaConstants.DipDevId)
                             ?? WiaCom.ReadString(info, "DeviceID")
                             ?? $"wia-{i}";
                    var name = WiaCom.ReadString(properties, WiaConstants.DipDevName)
                               ?? WiaCom.ReadString(properties, WiaConstants.DipDevDesc)
                               ?? "Escáner WIA";
                    var manufacturer = WiaCom.ReadString(properties, WiaConstants.DipVendDesc);
                    var port = WiaCom.ReadString(properties, WiaConstants.DipPortName);
                    var connection = DeviceMatcher.InferConnection(name, port);
                    var family = DeviceMatcher.IsCanonTs5100Family(name);

                    devices.Add(new ScanDevice
                    {
                        Id = id,
                        Name = name,
                        Interface = ScannerInterfaceKind.Wia,
                        Connection = connection,
                        IsCanonTs5100Family = family,
                        Manufacturer = manufacturer,
                        Port = port,
                        StatusText = "Detectado",
                        IsAvailable = true
                    });

                    _log.Info($"WIA dispositivo: '{name}' id={id} puerto={port} familiaTS5100={family}");
                }
                catch (Exception ex)
                {
                    _log.Warn($"No se ha podido leer un DeviceInfo WIA: {ex.Message}");
                }
                finally
                {
                    WiaCom.Release(info);
                }
            }
        }
        finally
        {
            WiaCom.Release(manager);
        }

        return devices;
    }

    private ScanCapabilities GetCapabilitiesCore(string deviceId)
    {
        object? manager = null;
        object? info = null;
        object? device = null;
        object? item = null;
        try
        {
            (manager, info, device, item) = Connect(deviceId);
            var deviceProps = WiaCom.Get(device, "Properties")!;
            var itemProps = WiaCom.Get(item, "Properties")!;
            var name = WiaCom.ReadString(deviceProps, WiaConstants.DipDevName)
                       ?? WiaCom.ReadString(deviceProps, WiaConstants.DipDevDesc)
                       ?? deviceId;

            var resolutions = WiaCom.ReadNumericSubtypes(itemProps, WiaConstants.IpsXRes);
            if (resolutions.Count == 0)
            {
                resolutions = WiaCom.ReadNumericSubtypes(itemProps, WiaConstants.IpsYRes);
            }

            // Canon documenta 50-600 dpi en WIA. No ofrecemos 1200 salvo que el driver lo liste.
            if (resolutions.Count == 0)
            {
                _log.Warn($"El controlador WIA de '{name}' no ha publicado resoluciones. Se consultará al escanear.");
            }

            var colorModes = ReadColorModes(itemProps);
            var bedWidth = ReadBedInches(deviceProps, WiaConstants.DpsHorizontalBedSize, 8.5);
            var bedHeight = ReadBedInches(deviceProps, WiaConstants.DpsVerticalBedSize, 11.7);
            var handling = WiaCom.ReadInt(deviceProps, WiaConstants.DpsDocumentHandlingCapabilities) ?? WiaConstants.HandlingFlatbed;
            var hasFeeder = (handling & WiaConstants.HandlingFeeder) != 0;
            var brightness = WiaCom.TryGetProperty(itemProps, WiaConstants.IpsBrightness) is not null;
            var contrast = WiaCom.TryGetProperty(itemProps, WiaConstants.IpsContrast) is not null;
            var notes = hasFeeder
                ? "El controlador declara alimentador. El PIXMA TS5151 es de platina; se usará el cristal."
                : "Fuente: platina (flatbed).";

            return new ScanCapabilities
            {
                DeviceId = deviceId,
                DeviceName = name,
                Interface = ScannerInterfaceKind.Wia,
                ResolutionsDpi = resolutions,
                ColorModes = colorModes,
                Sources = [ScanSourceKind.Flatbed],
                MaxWidthInches = bedWidth,
                MaxHeightInches = bedHeight,
                SupportsBrightness = brightness,
                SupportsContrast = contrast,
                HasAutomaticDocumentFeeder = hasFeeder,
                Notes = notes
            };
        }
        finally
        {
            WiaCom.Release(item);
            WiaCom.Release(device);
            WiaCom.Release(info);
            WiaCom.Release(manager);
        }
    }

    private bool CanConnectCore(string deviceId)
    {
        object? manager = null;
        object? info = null;
        object? device = null;
        try
        {
            (manager, info, device, _) = Connect(deviceId, connectItem: false);
            return device is not null;
        }
        finally
        {
            WiaCom.Release(device);
            WiaCom.Release(info);
            WiaCom.Release(manager);
        }
    }

    private ScanResult ScanCore(ScanRequest request)
    {
        request.CancellationToken.ThrowIfCancellationRequested();
        object? manager = null;
        object? info = null;
        object? device = null;
        object? item = null;
        object? imageFile = null;
        try
        {
            request.Progress?.Report(new ScanProgress(15, "Abriendo el dispositivo WIA..."));
            (manager, info, device, item) = Connect(request.DeviceId);
            var deviceProps = WiaCom.Get(device, "Properties")!;
            var itemProps = WiaCom.Get(item, "Properties")!;
            var deviceName = WiaCom.ReadString(deviceProps, WiaConstants.DipDevName)
                             ?? WiaCom.ReadString(deviceProps, WiaConstants.DipDevDesc)
                             ?? request.DeviceId;

            request.Progress?.Report(new ScanProgress(30, "Configurando resolución y color..."));
            ApplyScanSettings(itemProps, deviceProps, request);

            request.Progress?.Report(new ScanProgress(45, "Escaneando..."));
            imageFile = TransferBestFormat(item);
            var bytes = WiaCom.ReadImageBytes(imageFile);
            if (bytes.Length == 0)
            {
                throw new ScannerException("El escáner ha completado la operación pero no ha devuelto una imagen.", canRetry: true);
            }

            request.Progress?.Report(new ScanProgress(90, "Recibiendo la imagen..."));
            var format = WiaCom.Get(imageFile, "FileExtension")?.ToString() ?? "png";
            var width = WiaCom.ReadInt(imageFile, "Width") ?? 0;
            var height = WiaCom.ReadInt(imageFile, "Height") ?? 0;

            _log.Info($"Escaneo WIA completado: {deviceName}, {bytes.Length} bytes, {width}x{height}, formato {format}, {request.Dpi} dpi.");

            return new ScanResult
            {
                ImageBytes = bytes,
                FormatHint = format,
                Dpi = request.Dpi,
                ColorMode = request.ColorMode,
                Width = width,
                Height = height,
                Interface = ScannerInterfaceKind.Wia,
                DeviceName = deviceName
            };
        }
        finally
        {
            WiaCom.Release(imageFile);
            WiaCom.Release(item);
            WiaCom.Release(device);
            WiaCom.Release(info);
            WiaCom.Release(manager);
        }
    }

    private (object manager, object info, object device, object? item) Connect(string deviceId, bool connectItem = true)
    {
        var manager = WiaCom.CreateDeviceManager();
        var infos = WiaCom.Get(manager, "DeviceInfos")
                    ?? throw WiaErrorMapper.NotDetected("Canon PIXMA TS5151");
        var count = WiaCom.Count(infos);
        object? info = null;
        for (var i = 1; i <= count; i++)
        {
            var candidate = WiaCom.Item(infos, i);
            var type = Convert.ToInt32(WiaCom.Get(candidate, "Type") ?? 0);
            if (type != WiaConstants.ScannerDeviceType)
            {
                WiaCom.Release(candidate);
                continue;
            }

            var properties = WiaCom.Get(candidate, "Properties")!;
            var id = WiaCom.ReadString(properties, WiaConstants.DipDevId)
                     ?? WiaCom.ReadString(candidate, "DeviceID");
            if (string.Equals(id, deviceId, StringComparison.OrdinalIgnoreCase))
            {
                info = candidate;
                break;
            }

            WiaCom.Release(candidate);
        }

        if (info is null)
        {
            WiaCom.Release(manager);
            throw WiaErrorMapper.NotDetected("Canon PIXMA TS5151");
        }

        object device;
        try
        {
            device = WiaCom.Call(info, "Connect")
                     ?? throw WiaErrorMapper.AccessFailed("Canon PIXMA TS5151", new InvalidOperationException("Connect devolvió null."));
        }
        catch (Exception ex)
        {
            WiaCom.Release(info);
            WiaCom.Release(manager);
            throw WiaErrorMapper.Map(ex, "Canon PIXMA TS5151");
        }

        object? item = null;
        if (connectItem)
        {
            var items = WiaCom.Get(device, "Items")
                        ?? throw new ScannerException("El controlador WIA no ha publicado ningún origen de escaneo (platina).", canRetry: true);
            if (WiaCom.Count(items) < 1)
            {
                throw new ScannerException("El controlador WIA no ha publicado ningún origen de escaneo (platina).", canRetry: true);
            }

            // WIA es 1-based. El primer ítem es la platina en el TS5100.
            item = WiaCom.Item(items, 1);
        }

        return (manager, info, device, item);
    }

    private void ApplyScanSettings(object itemProps, object deviceProps, ScanRequest request)
    {
        var intent = request.ColorMode switch
        {
            ColorMode.Grayscale => WiaConstants.IntentGrayscale,
            ColorMode.BlackAndWhite => WiaConstants.IntentText,
            _ => WiaConstants.IntentColor
        };
        WiaCom.TryWriteValue(itemProps, WiaConstants.IpsCurIntent, intent | WiaConstants.IntentMaximizeQuality);

        var dataType = request.ColorMode switch
        {
            ColorMode.Grayscale => WiaConstants.DataGrayscale,
            ColorMode.BlackAndWhite => WiaConstants.DataThreshold,
            _ => WiaConstants.DataColor
        };
        WiaCom.TryWriteValue(itemProps, WiaConstants.IpaDatatype, dataType);

        if (!WiaCom.TryWriteValue(itemProps, WiaConstants.IpsXRes, request.Dpi) ||
            !WiaCom.TryWriteValue(itemProps, WiaConstants.IpsYRes, request.Dpi))
        {
            _log.Warn($"El controlador no ha aceptado {request.Dpi} dpi. Se usará la resolución activa del driver.");
        }

        var handling = WiaCom.ReadInt(deviceProps, WiaConstants.DpsDocumentHandlingCapabilities);
        if (handling is not null && (handling.Value & WiaConstants.HandlingFlatbed) != 0)
        {
            WiaCom.TryWriteValue(deviceProps, WiaConstants.DpsDocumentHandlingSelect, WiaConstants.HandlingFlatbed);
        }

        var bedWidth = ReadBedInches(deviceProps, WiaConstants.DpsHorizontalBedSize, 8.5);
        var bedHeight = ReadBedInches(deviceProps, WiaConstants.DpsVerticalBedSize, 11.7);
        var size = request.PageSize.ClampTo(bedWidth, bedHeight);
        var widthPx = Math.Max(1, (int)Math.Round(size.WidthInches * request.Dpi));
        var heightPx = Math.Max(1, (int)Math.Round(size.HeightInches * request.Dpi));

        var maxWidthPx = WiaCom.ReadInt(itemProps, WiaConstants.IpsXExtent);
        var maxHeightPx = WiaCom.ReadInt(itemProps, WiaConstants.IpsYExtent);
        // Tras cambiar la resolución, XExtent/YExtent suelen representar el área máxima en píxeles.
        if (maxWidthPx is > 0)
        {
            widthPx = Math.Min(widthPx, maxWidthPx.Value);
        }

        if (maxHeightPx is > 0)
        {
            heightPx = Math.Min(heightPx, maxHeightPx.Value);
        }

        WiaCom.TryWriteValue(itemProps, WiaConstants.IpsXPos, 0);
        WiaCom.TryWriteValue(itemProps, WiaConstants.IpsYPos, 0);
        WiaCom.TryWriteValue(itemProps, WiaConstants.IpsXExtent, widthPx);
        WiaCom.TryWriteValue(itemProps, WiaConstants.IpsYExtent, heightPx);
        WiaCom.TryWriteValue(deviceProps, WiaConstants.DpsPages, 1);

        _log.Info($"WIA configurado: {request.Dpi} dpi, {request.ColorMode}, área {widthPx}x{heightPx} px ({size.WidthInches:0.00}x{size.HeightInches:0.00} in).");
    }

    private object TransferBestFormat(object item)
    {
        Exception? last = null;
        foreach (var format in new[] { WiaConstants.FormatPng, WiaConstants.FormatBmp, WiaConstants.FormatTiff, WiaConstants.FormatJpeg })
        {
            try
            {
                var image = WiaCom.Call(item, "Transfer", format);
                if (image is not null)
                {
                    return image;
                }
            }
            catch (Exception ex)
            {
                last = ex;
                _log.Warn($"Transfer WIA en formato {format} no soportado: {ex.Message}");
            }
        }

        throw last ?? new ScannerException("El controlador WIA no ha podido transferir la imagen.", canRetry: true);
    }

    private static IReadOnlyList<ColorMode> ReadColorModes(object itemProps)
    {
        var types = WiaCom.ReadNumericSubtypes(itemProps, WiaConstants.IpaDatatype);
        var modes = new List<ColorMode>();
        if (types.Count == 0)
        {
            return [ColorMode.Color, ColorMode.Grayscale, ColorMode.BlackAndWhite];
        }

        if (types.Contains(WiaConstants.DataColor)) modes.Add(ColorMode.Color);
        if (types.Contains(WiaConstants.DataGrayscale)) modes.Add(ColorMode.Grayscale);
        if (types.Contains(WiaConstants.DataThreshold)) modes.Add(ColorMode.BlackAndWhite);
        return modes.Count == 0 ? [ColorMode.Color] : modes;
    }

    private static double ReadBedInches(object properties, int propertyId, double fallback)
    {
        var thousandths = WiaCom.ReadInt(properties, propertyId);
        if (thousandths is null or <= 0)
        {
            return fallback;
        }

        return thousandths.Value / 1000.0;
    }

    private void EnsureWindows()
    {
        if (!IsPlatformSupported)
        {
            throw new ScannerException(
                "Canon Scan Studio debe ejecutarse en Windows 10 u 11 para comunicarse con el PIXMA TS5151.",
                RuntimeInformation.OSDescription,
                canRetry: false);
        }
    }
}
