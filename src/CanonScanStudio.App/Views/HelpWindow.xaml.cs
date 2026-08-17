using System.Windows;
using CanonScanStudio.App.Services;

namespace CanonScanStudio.App.Views;

public partial class HelpWindow : Window
{
    public HelpWindow()
    {
        InitializeComponent();
    }

    private void OnDriver(object sender, RoutedEventArgs e) => CanonSetupHelper.OpenDriverPage();

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
