using System.Windows;
using CanonScanStudio.App.Services;
using CanonScanStudio.App.ViewModels;
using CanonScanStudio.Models;

namespace CanonScanStudio.App.Views;

public partial class WelcomeWindow : Window
{
    private readonly MainViewModel _main;

    public WelcomeWindow(MainViewModel main)
    {
        InitializeComponent();
        _main = main;
        Loaded += async (_, _) => await SearchAsync();
    }

    private async Task SearchAsync()
    {
        try
        {
            StatusLabel.Text = "Buscando escáneres...";
            await _main.RefreshDevicesAsync();
            DeviceList.ItemsSource = _main.Devices;
            if (_main.Devices.Count == 0)
            {
                StatusLabel.Text = "No hemos encontrado ningún escáner.";
            }
            else
            {
                StatusLabel.Text = "Escáner encontrado";
                DeviceList.SelectedItem = _main.SelectedDevice ?? _main.Devices.FirstOrDefault();
            }
        }
        catch (Exception)
        {
            StatusLabel.Text = "No se ha podido buscar escáneres. Puedes continuar y reintentarlo después.";
            DeviceList.ItemsSource = _main.Devices;
        }
    }

    private async void OnSearch(object sender, RoutedEventArgs e) => await SearchAsync();

    private void OnDriver(object sender, RoutedEventArgs e) => CanonSetupHelper.OpenDriverPage();

    private void OnPick(object sender, RoutedEventArgs e)
    {
        if (_main.PickWindowsScannerCommand.CanExecute(null))
        {
            _main.PickWindowsScannerCommand.Execute(null);
        }

        DeviceList.ItemsSource = _main.Devices;
        DeviceList.SelectedItem = _main.SelectedDevice ?? _main.Devices.FirstOrDefault();
        StatusLabel.Text = _main.SelectedDevice is null
            ? "No hemos encontrado ningún escáner."
            : "Escáner encontrado";
    }

    private void OnSelector(object sender, RoutedEventArgs e)
    {
        if (_main.OpenNetworkSelectorCommand.CanExecute(null))
        {
            _main.OpenNetworkSelectorCommand.Execute(null);
        }
    }

    private void OnSkip(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnSelect(object sender, RoutedEventArgs e)
    {
        if (DeviceList.SelectedItem is ScanDevice device)
        {
            _main.SelectedDevice = device;
        }

        DialogResult = true;
        Close();
    }
}
