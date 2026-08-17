namespace CanonScanStudio.Scanning;

public sealed class ScannerException : Exception
{
    public ScannerException(string userMessage, string? technicalDetails = null, bool canRetry = true, Exception? inner = null)
        : base(userMessage, inner)
    {
        UserMessage = userMessage;
        TechnicalDetails = technicalDetails;
        CanRetry = canRetry;
    }

    public string UserMessage { get; }
    public string? TechnicalDetails { get; }
    public bool CanRetry { get; }
}
