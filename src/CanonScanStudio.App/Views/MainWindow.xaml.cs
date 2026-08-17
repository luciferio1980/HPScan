using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CanonScanStudio.App.ViewModels;
using CanonScanStudio.Models;

namespace CanonScanStudio.App.Views;

public partial class MainWindow : Window
{
    private Point _dragStart;
    private int _dragIndex = -1;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.Pages.CollectionChanged += (_, _) =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                PageStripScroll.ScrollToRightEnd();
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        };
    }

    public void FitPagesInView()
    {
        if (DataContext is not MainViewModel vm || PageStripScroll.ActualHeight < 80)
        {
            return;
        }

        const double cardHeight = 348;
        vm.Zoom = Math.Clamp((PageStripScroll.ActualHeight - 20) / cardHeight, 0.35, 2.8);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.Zoom = ScanSettingDefaults.Zoom;
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            foreach (var file in files)
            {
                vm.ImportPath(file);
            }
        }
    }

    private void OnPageStripWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer viewer)
        {
            return;
        }

        viewer.ScrollToHorizontalOffset(viewer.HorizontalOffset - e.Delta);
        e.Handled = true;
    }

    private void OnThumbnailDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.OpenCropPage(vm.SelectedPage);
        }

        e.Handled = true;
    }

    private void ThumbnailPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 2)
        {
            return;
        }

        _dragStart = e.GetPosition(null);
        _dragIndex = ThumbnailList.SelectedIndex;
    }

    private void ThumbnailPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragIndex < 0)
        {
            return;
        }

        var diff = _dragStart - e.GetPosition(null);
        if (Math.Abs(diff.X) <= SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(diff.Y) <= SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var from = _dragIndex;
        _dragIndex = -1;
        DragDrop.DoDragDrop(ThumbnailList, from, DragDropEffects.Move);
    }

    private void ThumbnailPreviewMouseUp(object sender, MouseButtonEventArgs e) => _dragIndex = -1;

    private void ThumbnailDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.Move;
        e.Handled = true;
    }

    private void ThumbnailDrop(object sender, DragEventArgs e)
    {
        _dragIndex = -1;
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            foreach (var file in files)
            {
                vm.ImportPath(file);
            }

            return;
        }

        if (!e.Data.GetDataPresent(typeof(int)))
        {
            return;
        }

        var from = (int)e.Data.GetData(typeof(int))!;
        var target = e.OriginalSource as DependencyObject;
        while (target is not null && target is not ListBoxItem)
        {
            target = System.Windows.Media.VisualTreeHelper.GetParent(target);
        }

        if (target is ListBoxItem item)
        {
            var to = ThumbnailList.ItemContainerGenerator.IndexFromContainer(item);
            if (to >= 0)
            {
                vm.Reorder(from, to);
            }
        }
    }
}
