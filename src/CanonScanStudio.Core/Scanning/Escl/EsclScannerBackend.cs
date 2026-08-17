using System.Collections.Concurrent;
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
    private readonly ConcurrentDictionary<string, string> _capabilitiesXml = new(StringComparer.OrdinalIgnoreCase);
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
        var xml = GetOrFetchCapabilitiesXml(ip);
        var parsed = EsclCapabilitiesParser.Parse(xml ?? "");
        var name = string.IsNullOrWhiteSpace(xml) ? "Canon TS5100 series" : EsclProtocol.ReadMakeAndModel(xml) ?? "Canon TS5100 series";
        var max = parsed.ResolutionsDpi.Count == 0 ? 600 : parsed.ResolutionsDpi.Max();
        _log.Info($"eSCL capacidades {ip}: {string.Join(", ", parsed.ResolutionsDpi)} DPI (máx. {max}).");
        return EsclCapabilitiesParser.ToScanCapabilities(
            parsed,
            deviceId,
            name,
            $"Escáner de red eSCL en {ip}. Resolución máxima anunciada: {max} DPI.");
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
        var advertised = GetCapabilities(request.DeviceId).ResolutionsDpi;
        var maxAdvertised = advertised.Count == 0 ? 600 : advertised.Max();
        if (advertised.Count > 0 && !advertised.Contains(request.Dpi))
        {
            throw new ScannerException(
                $"Esta conexión no admite {request.Dpi} DPI (máximo {maxAdvertised} DPI). Elige {maxAdvertised} DPI e inténtalo de nuevo.",
                canRetry: false);
        }

        request.Progress?.Report(new ScanProgress(15, $"Conectando… {request.Dpi} DPI"));
        using var post = PostJob(ip, request);
        if ((int)post.StatusCode is < 200 or >= 300)
        {
            var detail = $"{(int)post.StatusCode} {post.ReasonPhrase}";
            if (request.Dpi > maxAdvertised || (int)post.StatusCode == 400 && request.Dpi >= 1200)
            {
                throw new ScannerException(
                    $"El escáner ha rechazado {request.Dpi} DPI. En Wi-Fi el máximo suele ser {maxAdvertised} DPI. Elige esa resolución e inténtalo de nuevo.",
                    detail,
                    canRetry: false);
            }

            throw new ScannerException(
                "El Canon ha rechazado el escaneo por red. Cierra IJ Scan Utility y las ventanas extra del Selector EX2, y reintenta.",
                detail,
                canRetry: true);
        }

        post.Headers.TryGetValues("Location", out var extra);
        var jobUrl = EsclProtocol.ResolveJobUri(Base(ip), post.Headers.Location, extra);
        if (jobUrl is null)
        {
            throw new ScannerException("El escáner de red no ha devuelto el trabajo de escaneo.", canRetry: true);
        }

        request.Progress?.Report(new ScanProgress(45, "Escaneando… espera a que termine el paso de la lámpara."));
        var bytes = WaitForDocument(jobUrl, request);
        try
        {
            Http.DeleteAsync(jobUrl).GetAwaiter().GetResult();
        }
        catch
        {
            // El trabajo puede caducar solo.
        }

        if (!EsclProtocol.IsImageBytes(bytes))
        {
            throw new ScannerException(
                "El escáner ha terminado pero no ha enviado la imagen. Cierra el Selector EX2 extra y reintenta.",
                canRetry: true);
        }

        int width = 0, height = 0;
        try
        {
            using var ms = new MemoryStream(bytes);
            var info = Image.Identify(ms);
            if (info is not null)
            {
                width = info.Width;
                height = info.Height;
            }
        }
        catch
        {
            // La vista previa leerá el JPEG igualmente.
        }

        var inferred = ResolutionPresets.InferFromPixels(width, request.PageSize.WidthInches);
        var dpi = inferred > 0 ? inferred : request.Dpi;
        if (inferred > 0 && inferred != request.Dpi)
        {
            _log.Warn($"eSCL: se pidieron {request.Dpi} DPI y la imagen es {width}×{height} ({inferred} DPI).");
        }

        var format = EsclProtocol.FormatHint(bytes);
        request.Progress?.Report(new ScanProgress(95, $"Escaneo de red completado ({dpi} DPI)."));
        _log.Info($"Escaneo eSCL {ip}: {bytes.Length} bytes {width}x{height} {format} {dpi} DPI (pedido {request.Dpi}).");
        return new ScanResult
        {
            ImageBytes = bytes,
            FormatHint = format,
            Dpi = dpi,
            ColorMode = request.ColorMode,
            Width = width,
            Height = height,
            Interface = ScannerInterfaceKind.Escl,
            DeviceName = "Canon TS5100 series"
        };
    }

    private HttpResponseMessage PostJob(string ip, ScanRequest request)
    {
        var xml = EsclProtocol.BuildScanSettings(request);
        var url = Base(ip) + EsclProtocol.ScanJobsPath;
        HttpResponseMessage? last = null;
        foreach (var media in new[] { "application/xml", "text/xml" })
        {
            last?.Dispose();
            using var content = new StringContent(xml, System.Text.Encoding.UTF8, media);
            last = Http.PostAsync(url, content, request.CancellationToken).GetAwaiter().GetResult();
            if ((int)last.StatusCode != 415)
            {
                return last;
            }
        }

        return last!;
    }

    private byte[] WaitForDocument(Uri jobUrl, ScanRequest request)
    {
        var next = jobUrl.ToString().TrimEnd('/') + "/NextDocument";
        var deadline = DateTime.UtcNow.AddMinutes(4);
        Exception? last = null;
        while (DateTime.UtcNow < deadline)
        {
            request.CancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var get = Http.GetAsync(next, request.CancellationToken).GetAwaiter().GetResult();
                if (EsclProtocol.ShouldRetryNextDocument(get.StatusCode))
                {
                    var wait = get.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(2);
                    if (wait > TimeSpan.FromSeconds(8))
                    {
                        wait = TimeSpan.FromSeconds(8);
                    }

                    request.Progress?.Report(new ScanProgress(60, "El escáner sigue trabajando…"));
                    Thread.Sleep(wait);
                    continue;
                }

                var bytes = get.Content.ReadAsByteArrayAsync(request.CancellationToken).GetAwaiter().GetResult();
                if (!get.IsSuccessStatusCode)
                {
                    last = new ScannerException(
                        "No se ha podido recoger la imagen del escáner de red.",
                        get.StatusCode + " " + get.ReasonPhrase,
                        canRetry: true);
                    Thread.Sleep(1500);
                    continue;
                }

                if (EsclProtocol.IsXmlOrHtml(bytes) || bytes.Length < 32)
                {
                    request.Progress?.Report(new ScanProgress(70, "Recibiendo la imagen…"));
                    Thread.Sleep(1500);
                    continue;
                }

                if (EsclProtocol.IsImageBytes(bytes))
                {
                    return bytes;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                last = ex;
                Thread.Sleep(1500);
            }
        }

        throw last as ScannerException ?? new ScannerException(
            "El escáner ha terminado pero no ha enviado la imagen a tiempo. Cierra el Selector EX2 extra y reintenta.",
            last?.ToString(),
            canRetry: true,
            inner: last);
    }

    private ScanDevice? Probe(NetworkScanTarget target)
    {
        var xml = FetchCapabilitiesXml(target.Ip, TimeSpan.FromSeconds(3));
        if (xml is null)
        {
            return null;
        }

        _capabilitiesXml[target.Ip] = xml;
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

    private string? GetOrFetchCapabilitiesXml(string ip)
    {
        if (_capabilitiesXml.TryGetValue(ip, out var cached) && !string.IsNullOrWhiteSpace(cached))
        {
            return cached;
        }

        var xml = FetchCapabilitiesXml(ip, TimeSpan.FromSeconds(2));
        if (!string.IsNullOrWhiteSpace(xml))
        {
            _capabilitiesXml[ip] = xml;
        }

        return xml;
    }

    private static string? FetchCapabilitiesXml(string ip, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            using var response = Http.GetAsync(Base(ip) + EsclProtocol.CapabilitiesPath, cts.Token)
                .GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var xml = response.Content.ReadAsStringAsync(cts.Token).GetAwaiter().GetResult();
            return string.IsNullOrWhiteSpace(xml) ? null : xml;
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
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "text/xml, application/xml, image/jpeg, */*");
        return client;
    }
}
