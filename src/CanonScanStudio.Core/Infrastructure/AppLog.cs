using System.Text;

namespace CanonScanStudio.Infrastructure;

public sealed class AppLog : IAppLog
{
    private readonly object _sync = new();
    private readonly string _filePath;

    public AppLog()
    {
        LogDirectory = AppPaths.Logs;
        Directory.CreateDirectory(LogDirectory);
        _filePath = Path.Combine(LogDirectory, $"canon-scan-studio-{DateTime.Now:yyyyMMdd}.log");
        Info("Registro iniciado.");
    }

    public string LogDirectory { get; }

    public void Info(string message) => Write("INFO", message, null);

    public void Warn(string message) => Write("WARN", message, null);

    public void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    public string ExportTo(string destinationFile)
    {
        lock (_sync)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
            File.Copy(_filePath, destinationFile, overwrite: true);
            return destinationFile;
        }
    }

    private void Write(string level, string message, Exception? exception)
    {
        var builder = new StringBuilder()
            .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
            .Append(" [")
            .Append(level)
            .Append("] ")
            .Append(message);
        if (exception is not null)
        {
            builder.AppendLine().Append(exception);
        }

        var line = builder.ToString();
        lock (_sync)
        {
            try
            {
                File.AppendAllText(_filePath, line + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // El registro nunca debe interrumpir el escaneo.
            }
        }
    }
}
