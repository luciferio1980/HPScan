namespace CanonScanStudio.Infrastructure;

public interface IAppLog
{
    string LogDirectory { get; }
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? exception = null);
    string ExportTo(string destinationFile);
}
