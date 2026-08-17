using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using CanonScanStudio.App.Scanning;
using CanonScanStudio.App.Services;
using CanonScanStudio.App.ViewModels;
using CanonScanStudio.App.Views;
using CanonScanStudio.Infrastructure;
using CanonScanStudio.Scanning;
using CanonScanStudio.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CanonScanStudio.App;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        RegisterExceptionHandlers();

        try
        {
            StartApplication();
        }
        catch (Exception ex)
        {
            ReportFatalError("No se ha podido iniciar Canon Scan Studio.", ex);
            Shutdown(1);
        }
    }

    private void StartApplication()
    {
        var collection = new ServiceCollection();
        collection.AddCanonScanStudio();
        collection.AddSingleton<IScannerBackend, WinRtScannerBackend>();
        collection.AddSingleton<IUiDialogService, UiDialogService>();
        collection.AddSingleton<MainViewModel>();
        collection.AddSingleton<MainWindow>();
        Services = collection.BuildServiceProvider();

        var settings = Services.GetRequiredService<ISettingsService>();
        var session = Services.GetRequiredService<ISessionService>();
        if (settings.Current.RestoreLastSession)
        {
            try
            {
                session.TryRestoreRecovery();
            }
            catch (Exception ex)
            {
                Services.GetRequiredService<IAppLog>().Warn("No se ha podido restaurar la sesión anterior: " + ex.Message);
            }
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

    private void RegisterExceptionHandlers()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ReportFatalError("Canon Scan Studio ha encontrado un error inesperado.", e.Exception);
        e.Handled = true;
        Shutdown(1);
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            ReportFatalError("Canon Scan Studio se ha cerrado por un error grave.", ex);
        }
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        try
        {
            WriteCrashLog(e.Exception);
        }
        catch
        {
            // Evitar bucles si el registro también falla.
        }

        e.SetObserved();
    }

    private static void ReportFatalError(string title, Exception ex)
    {
        try
        {
            WriteCrashLog(ex);
        }
        catch
        {
            // Continuar mostrando el diálogo aunque falle el log.
        }

        var message = new StringBuilder()
            .AppendLine(title)
            .AppendLine()
            .AppendLine(ex.GetBaseException().Message)
            .AppendLine()
            .Append("Si el problema continúa, revisa el registro en:")
            .AppendLine()
            .Append(GetCrashLogHint())
            .ToString();

        try
        {
            MessageBox.Show(message, "Canon Scan Studio", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch
        {
            // Sin UI disponible (arranque muy temprano).
        }
    }

    private static void WriteCrashLog(Exception ex)
    {
        try
        {
            AppPaths.EnsureCreated();
            var path = Path.Combine(AppPaths.Logs, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            File.WriteAllText(path, ex.ToString());
        }
        catch
        {
            var fallback = Path.Combine(
                Path.GetTempPath(),
                $"CanonScanStudio-crash-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            File.WriteAllText(fallback, ex.ToString());
        }
    }

    private static string GetCrashLogHint()
    {
        try
        {
            return AppPaths.Logs;
        }
        catch
        {
            return Path.GetTempPath();
        }
    }
}
