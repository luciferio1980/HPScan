using System.Runtime.InteropServices;
using CanonScanStudio.Infrastructure;
using CanonScanStudio.Models;

namespace CanonScanStudio.Scanning.Twain;

/// <summary>
/// Backend TWAIN 1.9. El PIXMA TS5151 incluye ScanGear (TWAIN) en el MP Driver oficial.
/// Se usa como alternativa si WIA no publica el dispositivo. No simula escaneos:
/// si el DSM o la fuente Canon no están, la enumeración queda vacía.
/// </summary>
public sealed class TwainScannerBackend : IScannerBackend
{
    private readonly IAppLog _log;

    public TwainScannerBackend(IAppLog log)
    {
        _log = log;
    }

    public ScannerInterfaceKind Interface => ScannerInterfaceKind.Twain;
    public bool IsPlatformSupported => OperatingSystem.IsWindows();

    public IReadOnlyList<ScanDevice> ListDevices()
    {
        if (!IsPlatformSupported)
        {
            return Array.Empty<ScanDevice>();
        }

        try
        {
            return TwainNativeSession.ListSources(_log);
        }
        catch (DllNotFoundException ex)
        {
            _log.Warn("No se ha encontrado el Administrador de orígenes TWAIN (TWAINDSM.dll / twain_32.dll). " + ex.Message);
            return Array.Empty<ScanDevice>();
        }
        catch (Exception ex)
        {
            _log.Error("Error al enumerar orígenes TWAIN.", ex);
            return Array.Empty<ScanDevice>();
        }
    }

    public ScanCapabilities GetCapabilities(string deviceId)
    {
        EnsureWindows();
        return TwainNativeSession.GetCapabilities(deviceId, _log);
    }

    public bool CanConnect(string deviceId)
    {
        try
        {
            return ListDevices().Any(d => d.Id == deviceId);
        }
        catch
        {
            return false;
        }
    }

    public ScanResult Scan(ScanRequest request)
    {
        EnsureWindows();
        request.Progress?.Report(new ScanProgress(10, "Abriendo origen TWAIN..."));
        return TwainNativeSession.Scan(request, _log);
    }

    private void EnsureWindows()
    {
        if (!IsPlatformSupported)
        {
            throw new ScannerException(
                "TWAIN solo está disponible en Windows. Instala el MP Driver de Canon e inicia Canon Scan Studio en Windows 10 u 11.",
                RuntimeInformation.OSDescription,
                canRetry: false);
        }
    }
}
