using System.Windows;
using CanonScanStudio.App.Services;
using CanonScanStudio.App.ViewModels;
using CanonScanStudio.App.Views;
using CanonScanStudio.Infrastructure;
using CanonScanStudio.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CanonScanStudio.App;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var collection = new ServiceCollection();
        collection.AddCanonScanStudio();
        collection.AddSingleton<IUiDialogService, UiDialogService>();
        collection.AddSingleton<MainViewModel>();
        collection.AddSingleton<MainWindow>();
        Services = collection.BuildServiceProvider();

        var settings = Services.GetRequiredService<ISettingsService>();
        var session = Services.GetRequiredService<ISessionService>();
        if (settings.Current.RestoreLastSession)
        {
            session.TryRestoreRecovery();
        }

        var main = Services.GetRequiredService<MainWindow>();
        MainWindow = main;
        main.Show();

        if (!settings.Current.FirstRunCompleted)
        {
            var wizard = new WelcomeWindow(Services.GetRequiredService<MainViewModel>());
            wizard.Owner = main;
            wizard.ShowDialog();
            settings.Current.FirstRunCompleted = true;
            settings.Save();
        }
        else
        {
            _ = Services.GetRequiredService<MainViewModel>().InitializeAsync();
        }
    }
}
