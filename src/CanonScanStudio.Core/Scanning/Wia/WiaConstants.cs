using System.Runtime.InteropServices;
using CanonScanStudio.Models;

namespace CanonScanStudio.Scanning.Wia;

internal static class WiaConstants
{
    public const int ScannerDeviceType = 1;

    public const int DipDevId = 2;
    public const int DipVendDesc = 3;
    public const int DipDevDesc = 4;
    public const int DipDevType = 5;
    public const int DipPortName = 6;
    public const int DipDevName = 7;
    public const int DipServerName = 8;
    public const int DipRemoteDevId = 9;
    public const int DipHwConfig = 11;

    public const int DpsHorizontalBedSize = 3074;
    public const int DpsVerticalBedSize = 3075;
    public const int DpsDocumentHandlingCapabilities = 3086;
    public const int DpsDocumentHandlingStatus = 3087;
    public const int DpsDocumentHandlingSelect = 3088;
    public const int DpsOpticalXRes = 3090;
    public const int DpsOpticalYRes = 3091;
    public const int DpsPages = 3096;
    public const int DpsPageSize = 3097;
    public const int DpsPageWidth = 3098;
    public const int DpsPageHeight = 3099;

    public const int IpaItemName = 4098;
    public const int IpaDatatype = 4103;
    public const int IpaDepth = 4104;
    public const int IpaFormat = 4106;
    public const int IpaTymed = 4108;

    public const int IpsCurIntent = 6146;
    public const int IpsXRes = 6147;
    public const int IpsYRes = 6148;
    public const int IpsXPos = 6149;
    public const int IpsYPos = 6150;
    public const int IpsXExtent = 6151;
    public const int IpsYExtent = 6152;
    public const int IpsBrightness = 6154;
    public const int IpsContrast = 6155;
    public const int IpsThreshold = 6159;

    public const int IntentColor = 1;
    public const int IntentGrayscale = 2;
    public const int IntentText = 4;
    public const int IntentMaximizeQuality = 0x00020000;

    public const int DataThreshold = 0;
    public const int DataGrayscale = 2;
    public const int DataColor = 3;

    public const int HandlingFlatbed = 0x02;
    public const int HandlingFeeder = 0x01;

    public const int PropRange = 0x10;
    public const int PropList = 0x20;

    public const string FormatBmp = "{B96B3CAB-0728-11D3-9D7B-0000F81EF32E}";
    public const string FormatJpeg = "{B96B3CAE-0728-11D3-9D7B-0000F81EF32E}";
    public const string FormatPng = "{B96B3CAF-0728-11D3-9D7B-0000F81EF32E}";
    public const string FormatTiff = "{B96B3CB1-0728-11D3-9D7B-0000F81EF32E}";

    public static readonly int[] PreferredResolutions = [75, 150, 200, 300, 600, 1200];
}

internal static class WiaErrorMapper
{
    // Códigos documentados de WIA. Nunca se muestran en crudo al usuario.
    private const int NoDeviceAvailable = unchecked((int)0x80210015);
    private const int Offline = unchecked((int)0x80210005);
    private const int Busy = unchecked((int)0x80210006);
    private const int WarmingUp = unchecked((int)0x80210007);
    private const int UserIntervention = unchecked((int)0x80210008);
    private const int ItemDeleted = unchecked((int)0x80210009);
    private const int DeviceCommunication = unchecked((int)0x8021000A);
    private const int InvalidCommand = unchecked((int)0x8021000B);
    private const int Locked = unchecked((int)0x8021000D);
    private const int ExceptionInDriver = unchecked((int)0x8021000E);
    private const int CoverOpen = unchecked((int)0x80210010);
    private const int LampOff = unchecked((int)0x80210011);
    private const int PaperEmpty = unchecked((int)0x80210003);
    private const int PaperJam = unchecked((int)0x80210002);

    public static ScannerException Map(Exception exception, string? deviceName = null)
    {
        var label = string.IsNullOrWhiteSpace(deviceName) ? "Canon PIXMA TS5151" : deviceName;
        if (exception is ScannerException scanner)
        {
            return scanner;
        }

        if (exception is COMException com)
        {
            return com.ErrorCode switch
            {
                NoDeviceAvailable => NotDetected(label, com),
                Offline => AccessFailed(label, com, "El escáner está apagado o fuera de línea."),
                Busy or Locked => AccessFailed(label, com, "El escáner está ocupado. Cierra otras aplicaciones de escaneo (por ejemplo, Fax y Escáner de Windows o IJ Scan Utility) e inténtalo de nuevo."),
                WarmingUp => AccessFailed(label, com, "El escáner se está preparando. Espera unos segundos y pulsa Reintentar."),
                DeviceCommunication => AccessFailed(label, com, "Se ha perdido la comunicación con el escáner."),
                CoverOpen => AccessFailed(label, com, "La tapa del escáner está abierta. Ciérrala e inténtalo de nuevo."),
                LampOff or UserIntervention => AccessFailed(label, com, "El escáner requiere atención. Comprueba la pantalla del Canon y vuelve a intentarlo."),
                PaperEmpty => AccessFailed(label, com, "No hay original en el cristal. Coloca el documento y reinténtalo."),
                PaperJam => AccessFailed(label, com, "Hay un atasco o un original mal colocado. Revisa el cristal e inténtalo de nuevo."),
                ExceptionInDriver or InvalidCommand or ItemDeleted => AccessFailed(label, com, "El controlador del escáner ha rechazado la operación."),
                _ => AccessFailed(label, com, "No se puede completar el escaneo.")
            };
        }

        return AccessFailed(label, exception, "No se puede completar el escaneo.");
    }

    public static ScannerException NotDetected(string deviceName, Exception? inner = null) =>
        new(
            $"{deviceName} no detectado. Comprueba que el escáner esté encendido y conectado mediante USB o Wi-Fi y que el controlador de Canon esté instalado.",
            inner?.ToString(),
            canRetry: true,
            inner: inner);

    public static ScannerException AccessFailed(string deviceName, Exception inner, string? extra = null)
    {
        var details = extra is null ? string.Empty : extra + Environment.NewLine + Environment.NewLine;
        return new ScannerException(
            $"""
             No se puede acceder al escáner.

             {details}Comprueba:
             1. Que el Canon esté encendido.
             2. Que el cable USB esté conectado o que esté conectado a la misma red Wi-Fi.
             3. Que el controlador del escáner esté instalado.
             4. Que ninguna otra aplicación esté utilizando el escáner.
             """,
            inner.ToString(),
            canRetry: true,
            inner: inner);
    }
}
