using System.Windows;
using CanonScanStudio.Models;

namespace CanonScanStudio.App.Views;

public partial class SaveDialogWindow : Window
{
    public SaveDialogWindow(string folder, string fileName, OutputFormat format, bool multiPage, bool searchable)
    {
        InitializeComponent();
        FolderBox.Text = folder;
        NameBox.Text = fileName;
        FormatBox.ItemsSource = new[] { OutputFormat.Pdf, OutputFormat.Jpeg, OutputFormat.Png, OutputFormat.Tiff };
        FormatBox.SelectedItem = multiPage ? OutputFormat.Pdf : format;
        SearchableBox.IsChecked = searchable;
        HintBox.Text = multiPage
            ? "Hay varias páginas. PDF es la opción recomendada. Las imágenes se pueden guardar página a página."
            : "Puedes guardar como PDF o como imagen.";
    }

    public string Folder => FolderBox.Text;
    public string FileName => NameBox.Text;
    public OutputFormat Format => FormatBox.SelectedItem is OutputFormat format ? format : OutputFormat.Pdf;
    public bool Searchable => SearchableBox.IsChecked == true && Format == OutputFormat.Pdf;

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Carpeta" };
        if (dialog.ShowDialog() == true)
        {
            FolderBox.Text = dialog.FolderName;
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
