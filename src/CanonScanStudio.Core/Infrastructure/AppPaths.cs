namespace CanonScanStudio.Infrastructure;

public static class AppPaths
{
    public static string Root => Path.Combine(
        Environment.GetEnvironmentVariable("CANON_SCAN_STUDIO_DATA")
        ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CanonScanStudio");

    public static string Logs => Path.Combine(Root, "logs");
    public static string Settings => Path.Combine(Root, "settings.json");
    public static string Sessions => Path.Combine(Root, "sessions");
    public static string Recovery => Path.Combine(Root, "recovery");
    public static string Thumbnails => Path.Combine(Root, "cache", "thumbnails");
    public static string Previews => Path.Combine(Root, "cache", "previews");
    public static string TessData => Path.Combine(Root, "tessdata");

    public static string DefaultDocuments
    {
        get
        {
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return Path.Combine(string.IsNullOrWhiteSpace(documents) ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) : documents, "Escaneos");
        }
    }

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Logs);
        Directory.CreateDirectory(Sessions);
        Directory.CreateDirectory(Recovery);
        Directory.CreateDirectory(Thumbnails);
        Directory.CreateDirectory(Previews);
        Directory.CreateDirectory(TessData);
        try
        {
            Directory.CreateDirectory(DefaultDocuments);
        }
        catch
        {
            // La carpeta de documentos puede no ser accesible; no debe impedir el arranque.
        }
    }
}
