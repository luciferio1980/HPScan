using CanonScanStudio.Scanning;
using CanonScanStudio.Scanning.Escl;
using CanonScanStudio.Scanning.Twain;
using CanonScanStudio.Scanning.Wia;
using CanonScanStudio.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CanonScanStudio.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCanonScanStudio(this IServiceCollection services)
    {
        AppPaths.EnsureCreated();
        services.AddSingleton<IAppLog, AppLog>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IImageProcessingService, ImageProcessingService>();
        services.AddSingleton<IPdfService, PdfService>();
        services.AddSingleton<IFileExportService, FileExportService>();
        services.AddSingleton<IOcrService, OcrService>();
        services.AddSingleton<IImportService, ImportService>();
        services.AddSingleton<ISessionService, SessionService>();
        services.AddSingleton<IUndoService, UndoService>();
        services.AddSingleton<IScannerBackend, WiaScannerBackend>();
        services.AddSingleton<IScannerBackend, TwainScannerBackend>();
        services.AddSingleton<IScannerBackend, EsclScannerBackend>();
        services.AddSingleton<IScannerService, ScannerService>();
        return services;
    }
}
