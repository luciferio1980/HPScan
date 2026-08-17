using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using CanonScanStudio.Models;

namespace CanonScanStudio.App.Converters;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var flag = value is true;
        if (Invert) flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is null) return Visibility.Collapsed;
        if (value is string text && string.IsNullOrWhiteSpace(text)) return Visibility.Collapsed;
        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class EnumDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        ColorMode.Color => "Color",
        ColorMode.Grayscale => "Escala de grises",
        ColorMode.BlackAndWhite => "Blanco y negro",
        OutputFormat.Pdf => "PDF",
        OutputFormat.Jpeg => "JPEG",
        OutputFormat.Png => "PNG",
        OutputFormat.Tiff => "TIFF",
        SendToDestination.LocalFolder => "Carpeta local",
        SendToDestination.Desktop => "Escritorio",
        SendToDestination.Documents => "Documentos",
        SendToDestination.EmailPlaceholder => "Correo electrónico (próximamente)",
        ScannerInterfaceKind.Auto => "Automático",
        ScannerInterfaceKind.Wia => "WIA",
        ScannerInterfaceKind.Twain => "TWAIN",
        ScannerInterfaceKind.WindowsScan => "Windows",
        int dpi => $"{dpi} DPI",
        _ => value?.ToString() ?? ""
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class InvertBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is not true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is not true;
}

public sealed class StatusBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var text = value?.ToString() ?? "";
        if (text.Contains("Escaneando", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Buscando", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Ocupado", StringComparison.OrdinalIgnoreCase))
        {
            return new SolidColorBrush(Color.FromRgb(0xEF, 0x6C, 0x00));
        }

        if (text.Contains("No disponible", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Desconectado", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Desconocido", StringComparison.OrdinalIgnoreCase))
        {
            return new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28));
        }

        if (text.Contains("Listo", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Conectado", StringComparison.OrdinalIgnoreCase))
        {
            return new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32));
        }

        return new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
