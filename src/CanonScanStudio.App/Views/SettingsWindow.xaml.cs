using System.Windows;
using CanonScanStudio.App.Services;
using CanonScanStudio.Infrastructure;
using CanonScanStudio.Models;
using CanonScanStudio.Services;

namespace CanonScanStudio.App.Views;

public partial class SettingsWindow : Window
{
    private readonly ISettingsService _settings;
    private readonly IScannerService _scanner;
    private readonly IAppLog _log;

    public SettingsWindow(ISettingsService settings, IScannerService scanner, IAppLog log)
    {
        InitializeComponent();
        _settings = settings;
        _scanner = scanner;
        _log = log;
        DeviceBox.ItemsSource = _scanner.Devices;
        DeviceBox.SelectedItem = _scanner.SelectedDevice;
        IfAuto.IsChecked = settings.Current.Interface == ScannerInterfaceKind.Auto;
        IfWia.IsChecked = settings.Current.Interface == ScannerInterfaceKind.Wia;
        IfTwain.IsChecked = settings.Current.Interface == ScannerInterfaceKind.Twain;
        DpiBox.ItemsSource = new[] { 75, 150, 300, 600, 1200 };
        DpiBox.SelectedItem = settings.Current.DefaultDpi > 0 ? settings.Current.DefaultDpi : ScanSettingDefaults.Dpi;
        if (DpiBox.SelectedItem is null)
        {
            DpiBox.SelectedItem = ScanSettingDefaults.Dpi;
        }

        ColorBox.ItemsSource = new[] { ColorMode.Color, ColorMode.Grayscale, ColorMode.BlackAndWhite };
        ColorBox.SelectedItem = settings.Current.DefaultColorMode;
        if (ColorBox.SelectedItem is null)
        {
            ColorBox.SelectedItem = ScanSettingDefaults.Color;
        }
        SizeBox.ItemsSource = PageSizeDefinition.Presets;
        SizeBox.SelectedItem = PageSizeDefinition.Find(settings.Current.DefaultPageSizeId);
        FolderBox.Text = settings.Current.DefaultSaveFolder;
        FormatBox.ItemsSource = new[] { OutputFormat.Pdf, OutputFormat.Jpeg, OutputFormat.Png, OutputFormat.Tiff };
        FormatBox.SelectedItem = settings.Current.DefaultFormat;
        RestoreBox.IsChecked = settings.Current.RestoreLastSession;
        ConfirmBox.IsChecked = settings.Current.ConfirmPageDelete;
        DetailsBox.IsChecked = settings.Current.ShowDetailedErrors;
        ThemeBox.ItemsSource = AppThemes.All;
        ThemeBox.SelectedItem = AppThemes.All.First(t => t.Id == AppThemes.Normalize(settings.Current.ThemeId));
    }

    private void OnThemeChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ThemeBox.SelectedItem is AppThemeOption theme)
        {
            ThemeService.Apply(theme.Id);
        }
    }

    private void OnDriver(object sender, RoutedEventArgs e) => CanonSetupHelper.OpenDriverPage();

    private void OnPrinters(object sender, RoutedEventArgs e) => CanonSetupHelper.OpenWindowsPrinters();

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

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Carpeta de guardado" };
        if (dialog.ShowDialog() == true)
        {
            FolderBox.Text = dialog.FolderName;
        }
    }

    private void OnExportLog(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Registro|*.log",
            FileName = $"CanonScanStudio-{DateTime.Now:yyyyMMdd}.log"
        };
        if (dialog.ShowDialog() == true)
        {
            _log.ExportTo(dialog.FileName);
            MessageBox.Show("Registro exportado.", "Diagnóstico");
        }
    }

    private void OnClose(object sender, RoutedEventArgs e)
    {
        _settings.Current.Interface = IfWia.IsChecked == true ? ScannerInterfaceKind.Wia
            : IfTwain.IsChecked == true ? ScannerInterfaceKind.Twain
            : ScannerInterfaceKind.Auto;
        if (DpiBox.SelectedItem is int dpi) _settings.Current.DefaultDpi = dpi;
        if (ColorBox.SelectedItem is ColorMode color) _settings.Current.DefaultColorMode = color;
        if (SizeBox.SelectedItem is PageSizeDefinition size) _settings.Current.DefaultPageSizeId = size.Id;
        _settings.Current.DefaultSaveFolder = FolderBox.Text;
        if (FormatBox.SelectedItem is OutputFormat format) _settings.Current.DefaultFormat = format;
        _settings.Current.RestoreLastSession = RestoreBox.IsChecked == true;
        _settings.Current.ConfirmPageDelete = ConfirmBox.IsChecked == true;
        _settings.Current.ShowDetailedErrors = DetailsBox.IsChecked == true;
        if (ThemeBox.SelectedItem is AppThemeOption theme)
        {
            _settings.Current.ThemeId = theme.Id;
            ThemeService.Apply(theme.Id);
        }
        if (DeviceBox.SelectedItem is ScanDevice device)
        {
            _settings.Current.PreferredDeviceId = device.Id;
            _settings.Current.PreferredDeviceName = device.Name;
            _scanner.SelectDevice(device.Id);
        }
        _settings.Save();
        DialogResult = true;
        Close();
    }
}
