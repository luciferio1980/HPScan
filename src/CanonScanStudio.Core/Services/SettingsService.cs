using System.Text.Json;
using CanonScanStudio.Infrastructure;
using CanonScanStudio.Models;

namespace CanonScanStudio.Services;

public interface ISettingsService
{
    AppSettings Current { get; }
    void Load();
    void Save();
}

public sealed class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly IAppLog _log;

    public SettingsService(IAppLog log)
    {
        _log = log;
        Load();
    }

    public AppSettings Current { get; private set; } = new();

    public void Load()
    {
        AppPaths.EnsureCreated();
        if (!File.Exists(AppPaths.Settings))
        {
            Current = CreateDefault();
            Save();
            return;
        }

        try
        {
            Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.Settings)) ?? CreateDefault();
            if (string.IsNullOrWhiteSpace(Current.DefaultSaveFolder))
            {
                Current.DefaultSaveFolder = AppPaths.DefaultDocuments;
            }

            if (Current.DefaultDpi <= 0)
            {
                Current.DefaultDpi = ScanSettingDefaults.Dpi;
            }

            if (!Enum.IsDefined(Current.DefaultColorMode))
            {
                Current.DefaultColorMode = ScanSettingDefaults.Color;
            }
        }
        catch (Exception ex)
        {
            _log.Warn("No se ha podido leer settings.json: " + ex.Message);
            Current = CreateDefault();
        }
    }

    public void Save()
    {
        AppPaths.EnsureCreated();
        if (string.IsNullOrWhiteSpace(Current.DefaultSaveFolder))
        {
            Current.DefaultSaveFolder = AppPaths.DefaultDocuments;
        }

        File.WriteAllText(AppPaths.Settings, JsonSerializer.Serialize(Current, Json));
    }

    private static AppSettings CreateDefault() => new()
    {
        DefaultSaveFolder = AppPaths.DefaultDocuments
    };
}
