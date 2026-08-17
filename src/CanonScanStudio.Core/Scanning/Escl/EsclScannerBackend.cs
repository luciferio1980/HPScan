using System.Net.Http;
using CanonScanStudio.Infrastructure;
using CanonScanStudio.Models;
using CanonScanStudio.Scanning.Network;
using SixLabors.ImageSharp;

namespace CanonScanStudio.Scanning.Escl;

/// <summary>
/// Escaneo por eSCL (AirPrint) en Wi-Fi. El PIXMA TS5151 lo expone aunque WIA
/// no liste el dispositivo de red que sí ve el Selector EX2.
/// </summary>
public sealed class EsclScannerBackend : IScannerBackend
{
    private static readonly HttpClient Http = CreateClient();
    private readonly IAppLog _log;

    public EsclScannerBackend(IAppLog log)
    {
        _log = log;
    }

    public ScannerInterfaceKind Interface => ScannerInterfaceKind.Escl;
    public bool IsPlatformSupported => true;

    public IReadOnlyList<ScanDevice> ListDevices()
    {
        var devices = new List<ScanDevice>();
        IReadOnlyList<NetworkScanTarget> targets;
        try
        {
            targets = CanonNetworkLocator.Discover();
        }
        catch (Exception ex)
        {
            _log.Warn("No se han podido localizar IPs de Canon: " + ex.Message);
            return devices;
        }

        _log.Info(targets.Count == 0
            ? "eSCL: no hay IPs candidatas (impresora/ARP/Selector)."
            : "eSCL candidatos: " + string.Join(", ", targets.Select(t => t.Ip + " [" + t.Source + "]")));

        foreach (var target in targets.Take(12))
        {
            try
            {
                var probed = Probe(target);
                if (probed is null)
                {
                    _log.Info($"eSCL no responde en {target.Ip} ({target.Source}).");
                    continue;
                }

                devices.Add(probed);
                _log.Info($"eSCL dispositivo: '{probed.Name}' ip={target.Ip}");
            }
            catch (Exception ex)
            {
                _log.Warn($"eSCL {target.Ip}: {ex.Message}");
            }
        }

        return devices;
    }

    public ScanCapabilities GetCapabilities(string deviceId)
    {
        var ip = Unwrap(deviceId);
        return new ScanCapabilities
        {
            DeviceId = deviceId,
            DeviceName = "Canon TS5100 series",
            Interface = ScannerInterfaceKind.Escl,
            ResolutionsDpi = ResolutionPresets.ForTs5151(),
            ColorModes = [ColorMode.Color, ColorMode.Grayscale, ColorMode.BlackAndWhite],
            Sources = [ScanSourceKind.Flatbed],
            MaxWidthInches = 8.5,
            MaxHeightInches = 11.7,
            SupportsBrightness = false,
            SupportsContrast = false,
            Notes = $"Escáner de red eSCL en {ip}. No depende de WIA; el PIXMA TS5151 usa la platina."
        };
    }

    public bool CanConnect(string deviceId)
    {
        try
        {
            return Probe(new NetworkScanTarget(Unwrap(deviceId), "Canon", "probe")) is not null;
        }
        catch
        {
            return false;
        }
    }

    public ScanDevice? PickInteractively() => null;

    public ScanResult Scan(ScanRequest request)
    {
        var ip = Unwrap(request.DeviceId);
        request.Progress?.Report(new ScanProgress(15, "Conectando con el escáner de red..."));
        var xml = EsclProtocol.BuildScanSettings(request);
        using var content = new StringContent(xml, System.Text.Encoding.UTF8, "text/xml");
        request.Progress?.Report(new ScanProgress(35, "Escaneando por Wi-Fi..."));
        using var post = Http.PostAsync(Base(ip) + EsclProtocol.ScanJobsPath, content, request.CancellationToken)
            .GetAwaiter().GetResult();
        if ((int)post.StatusCode is < 200 or >= 300)
        {
            throw new ScannerException(
                "El Canon ha rechazado el escaneo por red. Comprueba que esté encendido, en la misma Wi-Fi, y que ninguna otra app lo esté usando.",
                post.StatusCode + " " + post.ReasonPhrase,
                canRetry: true);
        }

        var location = post.Headers.Location ?? (post.Headers.TryGetValues("Location", out var values)
            ? values.Select(v => Uri.TryCreate(v, UriKind.RelativeOrAbsolute, out var u) ? u : null).FirstOrDefault()
            : null);
        if (location is null)
        {
            throw new ScannerException("El escáner de red no ha devuelto el trabajo de escaneo.", canRetry: true);
        }

        var jobUrl = location.IsAbsoluteUri ? location : new Uri(new Uri(Base(ip) + "/"), location);
        var next = jobUrl.ToString().TrimEnd('/') + "/NextDocument";
        request.Progress?.Report(new ScanProgress(70, "Recibiendo la imagen..."));
        using var get = Http.GetAsync(next, request.CancellationToken).GetAwaiter().GetResult();
        get.EnsureSuccessStatusCode();
        var bytes = get.Content.ReadAsByteArrayAsync(request.CancellationToken).GetAwaiter().GetResult();
        if (bytes.Length == 0)
        {
            throw new ScannerException("El escáner de red ha devuelto una imagen vacía.", canRetry: true);
        }

        try
        {
            Http.DeleteAsync(jobUrl).GetAwaiter().GetResult();
        }
        catch
        {
            // El trabajo puede caducar solo.
        }

        int width = 0, height = 0;
        try
        {
            using var image = Image.Load(bytes);
            width = image.Width;
            height = image.Height;
        }
        catch
        {
            // JPEG válido para exportar aunque no se lean dimensiones aquí.
        }

        var format = get.Content.Headers.ContentType?.MediaType?.Contains("png", StringComparison.OrdinalIgnoreCase) == true
            ? "png"
            : "jpeg";
        request.Progress?.Report(new ScanProgress(95, "Escaneo de red completado."));
        _log.Info($"Escaneo eSCL {ip}: {bytes.Length} bytes {width}x{height}.");
        return new ScanResult
        {
            ImageBytes = bytes,
            FormatHint = format,
            Dpi = request.Dpi,
            ColorMode = request.ColorMode,
            Width = width,
            Height = height,
            Interface = ScannerInterfaceKind.Escl,
            DeviceName = "Canon TS5100 series"
        };
    }

    private ScanDevice? Probe(NetworkScanTarget target)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            using var response = Http.GetAsync(Base(target.Ip) + EsclProtocol.CapabilitiesPath, cts.Token)
                .GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var xml = response.Content.ReadAsStringAsync(cts.Token).GetAwaiter().GetResult();
            var name = EsclProtocol.ReadMakeAndModel(xml);
            if (string.IsNullOrWhiteSpace(name))
            {
                name = string.IsNullOrWhiteSpace(target.Name) ? "Canon TS5100 series" : target.Name;
            }

            if (!DeviceMatcher.IsCanonTs5100Family(name) &&
                !name.Contains("canon", StringComparison.OrdinalIgnoreCase) &&
                !DeviceMatcher.IsCanonTs5100Family(target.Name))
            {
                _log.Info($"eSCL en {target.Ip} no parece Canon ({name}).");
            }

            return new ScanDevice
            {
                Id = "escl:" + target.Ip,
                Name = name + " (Wi-Fi)",
                Interface = ScannerInterfaceKind.Escl,
                Connection = ScannerConnectionKind.Network,
                IsCanonTs5100Family = DeviceMatcher.IsCanonTs5100Family(name) || DeviceMatcher.IsCanonTs5100Family(target.Name),
                Manufacturer = "Canon",
                Port = target.Ip,
                StatusText = "Detectado",
                IsAvailable = true
            };
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    private static string Unwrap(string deviceId) =>
        deviceId.StartsWith("escl:", StringComparison.OrdinalIgnoreCase) ? deviceId["escl:".Length..] : deviceId;

    private static string Base(string ip) => "http://" + ip;

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "text/xml, application/xml, image/jpeg, */*");
        return client;
    }
}
