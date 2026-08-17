using System.Text;
using System.Windows;
using System.Windows.Input;
using CanonScanStudio.App.Services;
using CanonScanStudio.Infrastructure;
using CanonScanStudio.Services;

namespace CanonScanStudio.App.Views;

public partial class DiagnosticsWindow : Window
{
    private readonly IScannerService _scanner;
    private readonly IAppLog _log;
    private readonly ICommand _scanCommand;
    private readonly ICommand? _pickCommand;

    public DiagnosticsWindow(
        IScannerService scanner,
        IAppLog log,
        ICommand scanCommand,
        ICommand? pickCommand = null)
    {
        InitializeComponent();
        _scanner = scanner;
        _log = log;
        _scanCommand = scanCommand;
        _pickCommand = pickCommand;
        RenderReport();
    }

    private void RenderReport()
    {
        var report = _scanner.CreateDiagnosticReport();
        var builder = new StringBuilder();
        builder.AppendLine("Diagnóstico de Canon Scan Studio");
        builder.AppendLine($"Escáner detectado: {report.Device?.Name ?? "(ninguno)"}");
        builder.AppendLine($"Interfaz: {report.Interface}");
        builder.AppendLine($"Estado: {report.Status}");
        builder.AppendLine($"Conexión: {report.Device?.Connection}");
        if (report.Capabilities is { } caps)
        {
            builder.AppendLine($"Resoluciones: {(caps.ResolutionsDpi.Count == 0 ? "(el controlador no ha publicado una lista; se consultará al escanear)" : string.Join(", ", caps.ResolutionsDpi.Select(d => d + " DPI")))}");
            builder.AppendLine($"Modos: {string.Join(" / ", caps.ColorModes)}");
            builder.AppendLine($"Área máxima: {caps.MaxWidthInches:0.00} × {caps.MaxHeightInches:0.00} in");
            builder.AppendLine($"Brillo WIA: {(caps.SupportsBrightness ? "sí" : "no (se aplica en software)")}");
            builder.AppendLine($"Contraste WIA: {(caps.SupportsContrast ? "sí" : "no (se aplica en software)")}");
            builder.AppendLine($"Notas: {caps.Notes}");
        }
        builder.AppendLine();
        foreach (var note in report.Notes)
        {
            builder.AppendLine("- " + note);
        }
        builder.AppendLine();
        builder.AppendLine(CanonSetupHelper.BuildHint());
        builder.AppendLine();
        builder.AppendLine("Driver oficial TS5151: " + CanonSetupHelper.DriverPageUrl);
        builder.AppendLine("Registro: " + _log.LogDirectory);
        ReportBox.Text = builder.ToString();
    }

    private void OnRefresh(object sender, RoutedEventArgs e)
    {
        _scanner.RefreshDevices();
        RenderReport();
    }

    private void OnPick(object sender, RoutedEventArgs e)
    {
        if (_pickCommand?.CanExecute(null) == true)
        {
            _pickCommand.Execute(null);
        }

        RenderReport();
    }

    private void OnSelector(object sender, RoutedEventArgs e)
    {
        if (!CanonSetupHelper.TryOpenNetworkSelector())
        {
            if (MessageBox.Show(
                    "No está instalado el Selector de escáner de red de Canon (viene con el MP Driver).\n\n¿Abrir la página de descarga oficial?",
                    "Selector de red Canon",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                CanonSetupHelper.OpenDriverPage();
            }
        }
    }

    private void OnDriver(object sender, RoutedEventArgs e) => CanonSetupHelper.OpenDriverPage();

    private void OnTestScan(object sender, RoutedEventArgs e)
    {
        if (_scanCommand.CanExecute(null))
        {
            _scanCommand.Execute(null);
        }
        Close();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
