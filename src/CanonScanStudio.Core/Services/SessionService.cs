using System.Text.Json;
using CanonScanStudio.Infrastructure;
using CanonScanStudio.Models;

namespace CanonScanStudio.Services;

public interface ISessionService
{
    ScanSession Current { get; }
    string SessionFolder { get; }
    ScanPage AddScannedPage(ScanResult result, byte[] pngBytes, string? existingPath = null);
    ScanPage AddImportedPage(string originalPath, int dpi, int width, int height);
    void RemovePages(IEnumerable<Guid> ids);
    ScanPage DuplicatePage(Guid id);
    void MovePage(int from, int to);
    void ApplyOrder(IReadOnlyList<Guid> orderedIds);
    void NewSession();
    void SaveRecovery();
    bool TryRestoreRecovery();
    void ClearRecovery();
}

public sealed class SessionService : ISessionService
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly IAppLog _log;
    private ScanSession _current = new();
    private string _folder;

    public SessionService(IAppLog log)
    {
        _log = log;
        AppPaths.EnsureCreated();
        _folder = CreateFolder();
    }

    public ScanSession Current => _current;
    public string SessionFolder => _folder;

    public ScanPage AddScannedPage(ScanResult result, byte[] pngBytes, string? existingPath = null)
    {
        var path = existingPath ?? Path.Combine(_folder, $"{Guid.NewGuid():N}.png");
        if (existingPath is null)
        {
            File.WriteAllBytes(path, pngBytes);
        }

        var page = new ScanPage
        {
            Order = _current.Pages.Count,
            OriginalPath = path,
            Dpi = result.Dpi,
            OriginalWidth = result.Width,
            OriginalHeight = result.Height,
            Source = PageSourceKind.Scanned,
            CapturedWith = result.Interface,
            DeviceName = result.DeviceName,
            ColorMode = result.ColorMode
        };
        _current.Pages.Add(page);
        _current.IsDirty = true;
        _current.ModifiedAt = DateTimeOffset.Now;
        SaveRecovery();
        return page;
    }

    public ScanPage AddImportedPage(string originalPath, int dpi, int width, int height)
    {
        var dest = Path.Combine(_folder, $"{Guid.NewGuid():N}{Path.GetExtension(originalPath)}");
        File.Copy(originalPath, dest, overwrite: true);
        var page = new ScanPage
        {
            Order = _current.Pages.Count,
            OriginalPath = dest,
            Dpi = dpi,
            OriginalWidth = width,
            OriginalHeight = height,
            Source = PageSourceKind.Imported
        };
        _current.Pages.Add(page);
        _current.IsDirty = true;
        _current.ModifiedAt = DateTimeOffset.Now;
        SaveRecovery();
        return page;
    }

    public void RemovePages(IEnumerable<Guid> ids)
    {
        var set = ids.ToHashSet();
        _current.Pages.RemoveAll(p => set.Contains(p.Id));
        _current.Renumber();
        _current.IsDirty = true;
        SaveRecovery();
    }

    public ScanPage DuplicatePage(Guid id)
    {
        var source = _current.GetPage(id) ?? throw new InvalidOperationException("Página no encontrada.");
        var copy = source.CloneMetadata();
        var dest = Path.Combine(_folder, $"{copy.Id:N}{Path.GetExtension(source.OriginalPath)}");
        File.Copy(source.OriginalPath, dest, overwrite: true);
        copy.OriginalPath = dest;
        copy.Order = source.Order + 1;
        _current.Pages.Insert(Math.Min(copy.Order, _current.Pages.Count), copy);
        _current.Renumber();
        _current.IsDirty = true;
        SaveRecovery();
        return copy;
    }

    public void MovePage(int from, int to)
    {
        _current.MovePage(from, to);
        SaveRecovery();
    }

    public void ApplyOrder(IReadOnlyList<Guid> orderedIds)
    {
        _current.ApplyOrder(orderedIds);
        SaveRecovery();
    }

    public void NewSession()
    {
        _current = new ScanSession();
        _folder = CreateFolder();
        SaveRecovery();
    }

    public void SaveRecovery()
    {
        try
        {
            AppPaths.EnsureCreated();
            var recoveryFile = Path.Combine(AppPaths.Recovery, "session.json");
            var snapshot = new RecoverySnapshot
            {
                Folder = _folder,
                Session = _current
            };
            File.WriteAllText(recoveryFile, JsonSerializer.Serialize(snapshot, Json));
        }
        catch (Exception ex)
        {
            _log.Warn("No se ha podido guardar la recuperación: " + ex.Message);
        }
    }

    public bool TryRestoreRecovery()
    {
        var recoveryFile = Path.Combine(AppPaths.Recovery, "session.json");
        if (!File.Exists(recoveryFile))
        {
            return false;
        }

        try
        {
            var snapshot = JsonSerializer.Deserialize<RecoverySnapshot>(File.ReadAllText(recoveryFile));
            if (snapshot?.Session?.Pages.Count > 0 && Directory.Exists(snapshot.Folder))
            {
                _current = snapshot.Session;
                _folder = snapshot.Folder;
                _current.Pages.RemoveAll(p => !File.Exists(p.OriginalPath));
                _current.Renumber();
                return _current.Pages.Count > 0;
            }
        }
        catch (Exception ex)
        {
            _log.Warn("No se ha podido restaurar la sesión: " + ex.Message);
        }

        return false;
    }

    public void ClearRecovery()
    {
        try
        {
            var recoveryFile = Path.Combine(AppPaths.Recovery, "session.json");
            if (File.Exists(recoveryFile))
            {
                File.Delete(recoveryFile);
            }
        }
        catch
        {
            // ignore
        }
    }

    private static string CreateFolder()
    {
        var folder = Path.Combine(AppPaths.Sessions, DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        return folder;
    }

    private sealed class RecoverySnapshot
    {
        public string Folder { get; set; } = "";
        public ScanSession? Session { get; set; }
    }
}
