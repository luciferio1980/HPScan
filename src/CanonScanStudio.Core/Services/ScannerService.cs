using CanonScanStudio.Infrastructure;
using CanonScanStudio.Models;
using CanonScanStudio.Scanning;

namespace CanonScanStudio.Services;

public sealed class ScannerService : IScannerService
{
    private readonly IReadOnlyList<IScannerBackend> _backends;
    private readonly ISettingsService _settings;
    private readonly IAppLog _log;
    private readonly object _sync = new();
    private List<ScanDevice> _devices = [];
    private ScanDevice? _selected;
    private ScanCapabilities? _capabilities;
    private ScannerAvailability _status = ScannerAvailability.Unknown;

    public ScannerService(IEnumerable<IScannerBackend> backends, ISettingsService settings, IAppLog log)
    {
        _backends = backends.ToList();
        _settings = settings;
        _log = log;
    }

    public ScannerAvailability Status
    {
        get { lock (_sync) return _status; }
        private set { lock (_sync) _status = value; }
    }

    public ScanDevice? SelectedDevice
    {
        get { lock (_sync) return _selected; }
    }

    public ScanCapabilities? Capabilities
    {
        get { lock (_sync) return _capabilities; }
    }

    public IReadOnlyList<ScanDevice> Devices
    {
        get { lock (_sync) return _devices.ToList(); }
    }

    public event EventHandler? Changed;

    public IReadOnlyList<ScanDevice> RefreshDevices()
    {
        var found = new List<ScanDevice>();
        var preference = _settings.Current.Interface;

        foreach (var backend in _backends.Where(b => Matches(b, preference)))
        {
            if (!backend.IsPlatformSupported)
            {
                continue;
            }

            try
            {
                found.AddRange(backend.ListDevices());
            }
            catch (Exception ex)
            {
                _log.Warn($"El backend {backend.Interface} no ha podido enumerar dispositivos: {ex.Message}");
            }
        }

        var distinct = found
            .GroupBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(DeviceMatcher.Score).First())
            .OrderByDescending(DeviceMatcher.Score)
            .ToList();

        lock (_sync)
        {
            _devices = distinct;
            _selected = DeviceMatcher.SelectPreferred(distinct, _settings.Current.PreferredDeviceId, _settings.Current.PreferredDeviceName);
            _status = _selected is null ? ScannerAvailability.NotFound : ScannerAvailability.Ready;
            _capabilities = null;
        }

        if (_selected is not null)
        {
            _settings.Current.PreferredDeviceId = _selected.Id;
            _settings.Current.PreferredDeviceName = _selected.Name;
            _settings.Save();
            try
            {
                RefreshCapabilities();
            }
            catch (Exception ex)
            {
                _log.Warn("No se han podido leer las capacidades tras detectar el escáner: " + ex.Message);
            }
        }

        _log.Info(distinct.Count == 0
            ? "No se han detectado escáneres WIA/TWAIN."
            : $"Dispositivos detectados: {string.Join(", ", distinct.Select(d => $"{d.Name} ({d.InterfaceLabel})"))}");

        Changed?.Invoke(this, EventArgs.Empty);
        return Devices;
    }

    public void SelectDevice(string? deviceId)
    {
        lock (_sync)
        {
            _selected = _devices.FirstOrDefault(d => d.Id == deviceId);
            _status = _selected is null ? ScannerAvailability.NotFound : ScannerAvailability.Ready;
            _capabilities = null;
        }

        if (_selected is not null)
        {
            _settings.Current.PreferredDeviceId = _selected.Id;
            _settings.Current.PreferredDeviceName = _selected.Name;
            _settings.Save();
            RefreshCapabilities();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public ScanCapabilities? RefreshCapabilities()
    {
        var device = SelectedDevice;
        if (device is null)
        {
            return null;
        }

        var backend = BackendFor(device);
        var caps = backend.GetCapabilities(device.Id);
        lock (_sync)
        {
            _capabilities = caps;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return caps;
    }

    public async Task<ScanResult> ScanAsync(ScanRequest request)
    {
        var device = SelectedDevice ?? throw new ScannerException(
            "Canon PIXMA TS5151 no detectado. Comprueba que el escáner esté encendido y conectado mediante USB o Wi-Fi y que el controlador de Canon esté instalado.");

        var backend = BackendFor(device);
        Status = ScannerAvailability.Scanning;
        Changed?.Invoke(this, EventArgs.Empty);
        try
        {
            var result = await Task.Run(() => backend.Scan(request with { DeviceId = device.Id }), request.CancellationToken)
                .ConfigureAwait(false);
            Status = ScannerAvailability.Ready;
            Changed?.Invoke(this, EventArgs.Empty);
            return result;
        }
        catch (Exception ex)
        {
            Status = ScannerAvailability.Ready;
            Changed?.Invoke(this, EventArgs.Empty);
            if (ex is ScannerException)
            {
                throw;
            }

            throw new ScannerException(
                """
                No se puede acceder al escáner.

                Comprueba:
                1. Que el Canon esté encendido.
                2. Que el cable USB esté conectado o que esté conectado a la misma red Wi-Fi.
                3. Que el controlador del escáner esté instalado.
                4. Que ninguna otra aplicación esté utilizando el escáner.
                """,
                ex.ToString(),
                canRetry: true,
                inner: ex);
        }
    }

    public DiagnosticReport CreateDiagnosticReport()
    {
        var devices = Devices;
        var selected = SelectedDevice;
        var caps = Capabilities;
        var notes = new List<string>
        {
            $"Backends activos: {string.Join(", ", _backends.Where(b => b.IsPlatformSupported).Select(b => b.Interface))}",
            $"Dispositivos visibles: {devices.Count}"
        };
        if (devices.Count == 0)
        {
            notes.Add("No hay dispositivos WIA/TWAIN. Instala el MP Driver de la serie TS5100 desde el sitio de Canon.");
        }

        if (caps is { HasAutomaticDocumentFeeder: true })
        {
            notes.Add("El controlador declara ADF; esta aplicación usará solo la platina del TS5151.");
        }

        if (caps is { SupportsBrightness: false } or { SupportsContrast: false })
        {
            notes.Add("Brillo y contraste se aplican en software tras el escaneo (el WIA de red de Canon no expone exposición).");
        }

        return new DiagnosticReport
        {
            Device = selected,
            Capabilities = caps,
            Interface = selected?.InterfaceLabel ?? "Ninguna",
            Status = Status switch
            {
                ScannerAvailability.Ready => "Disponible",
                ScannerAvailability.Scanning => "Escaneando",
                ScannerAvailability.Busy => "Ocupado",
                ScannerAvailability.Offline => "Desconectado",
                ScannerAvailability.NotFound => "No disponible",
                _ => "Desconocido"
            },
            Notes = notes
        };
    }

    private IScannerBackend BackendFor(ScanDevice device) =>
        _backends.FirstOrDefault(b => b.Interface == device.Interface)
        ?? throw new ScannerException($"No hay backend para {device.InterfaceLabel}.");

    private static bool Matches(IScannerBackend backend, ScannerInterfaceKind preference) =>
        preference == ScannerInterfaceKind.Auto || backend.Interface == preference;
}
