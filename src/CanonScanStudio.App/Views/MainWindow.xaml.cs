using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CanonScanStudio.App.ViewModels;

namespace CanonScanStudio.App.Views;

public partial class MainWindow : Window
{
    private Point _dragStart;
    private int _dragIndex = -1;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
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

    private void ThumbnailPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        if (ThumbnailList.SelectedIndex >= 0)
        {
            _dragIndex = ThumbnailList.SelectedIndex;
        }
    }

    private void ThumbnailDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.Move;
        e.Handled = true;
    }

    private void ThumbnailDrop(object sender, DragEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            foreach (var file in files)
            {
                vm.ImportPath(file);
            }
            return;
        }

        if (_dragIndex < 0) return;
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
                vm.Reorder(_dragIndex, to);
            }
        }
    }

    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        base.OnPreviewMouseMove(e);
        if (e.LeftButton != MouseButtonState.Pressed || _dragIndex < 0)
        {
            return;
        }

        var diff = _dragStart - e.GetPosition(null);
        if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            DragDrop.DoDragDrop(ThumbnailList, _dragIndex, DragDropEffects.Move);
        }
    }
}
