using System.Windows.Media.Imaging;
using CanonScanStudio.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CanonScanStudio.App.ViewModels;

public sealed partial class PageItemViewModel : ObservableObject
{
    public PageItemViewModel(ScanPage page)
    {
        Page = page;
    }

    public ScanPage Page { get; }

    [ObservableProperty] private BitmapImage? thumbnail;
    [ObservableProperty] private BitmapImage? preview;
    [ObservableProperty] private bool isSelected;

    public string Title => $"Página {Page.Order + 1}";
    public string SizeLabel => Page.SizeLabel;

    public void NotifyLabels()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(SizeLabel));
    }
}
