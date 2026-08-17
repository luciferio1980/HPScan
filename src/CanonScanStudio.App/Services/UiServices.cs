using System.IO;
using System.Windows.Media.Imaging;

namespace CanonScanStudio.App.Services;

public static class ImageSourceFactory
{
    public static BitmapImage FromBytes(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreImageCache | BitmapCreateOptions.IgnoreColorProfile;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }
}

public interface IUiDialogService
{
    void Info(string title, string message);
    bool Confirm(string title, string message);
    bool ConfirmRetry(string title, string message);
    string? PickOpenFiles(string filter);
    string? PickFolder(string? initial);
    string? PickSaveFile(string filter, string defaultName);
}

public sealed class UiDialogService : IUiDialogService
{
    public void Info(string title, string message) =>
        System.Windows.MessageBox.Show(message, title, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);

    public bool Confirm(string title, string message) =>
        System.Windows.MessageBox.Show(message, title, System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question) ==
        System.Windows.MessageBoxResult.Yes;

    public bool ConfirmRetry(string title, string message) =>
        System.Windows.MessageBox.Show(message, title, System.Windows.MessageBoxButton.OKCancel, System.Windows.MessageBoxImage.Warning) ==
        System.Windows.MessageBoxResult.OK;

    public string? PickOpenFiles(string filter)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = filter,
            Multiselect = true
        };
        return dialog.ShowDialog() == true ? string.Join("|", dialog.FileNames) : null;
    }

    public string? PickFolder(string? initial)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Seleccionar carpeta",
            InitialDirectory = initial ?? ""
        };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    public string? PickSaveFile(string filter, string defaultName)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = filter,
            FileName = defaultName
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
