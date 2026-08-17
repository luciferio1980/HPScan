using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CanonScanStudio.App.ViewModels;

namespace CanonScanStudio.App.Views;

public partial class OrganizePagesWindow : Window
{
    private readonly ObservableCollection<PageItemViewModel> _pages;
    private Point _dragStart;
    private int _dragIndex = -1;

    public OrganizePagesWindow(IReadOnlyList<PageItemViewModel> pages)
    {
        InitializeComponent();
        _pages = new ObservableCollection<PageItemViewModel>(pages);
        PageList.ItemsSource = _pages;
        if (_pages.Count > 0)
        {
            PageList.SelectedIndex = 0;
        }
    }

    public IReadOnlyList<Guid> OrderedIds => _pages.Select(p => p.Page.Id).ToList();

    private void OnMoveUp(object sender, RoutedEventArgs e) => MoveSelected(-1);

    private void OnMoveDown(object sender, RoutedEventArgs e) => MoveSelected(1);

    private void MoveSelected(int delta)
    {
        var index = PageList.SelectedIndex;
        var target = index + delta;
        if (index < 0 || target < 0 || target >= _pages.Count)
        {
            return;
        }

        _pages.Move(index, target);
        PageList.SelectedIndex = target;
        PageList.ScrollIntoView(PageList.SelectedItem);
    }

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _dragIndex = IndexFromPoint(e.GetPosition(PageList));
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragIndex < 0)
        {
            return;
        }

        var diff = _dragStart - e.GetPosition(null);
        if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            DragDrop.DoDragDrop(PageList, _dragIndex, DragDropEffects.Move);
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (_dragIndex < 0)
        {
            return;
        }

        var target = IndexFromPoint(e.GetPosition(PageList));
        if (target < 0)
        {
            target = _pages.Count - 1;
        }

        if (target != _dragIndex && target >= 0 && target < _pages.Count)
        {
            _pages.Move(_dragIndex, target);
            PageList.SelectedIndex = target;
        }

        _dragIndex = -1;
    }

    private int IndexFromPoint(Point point)
    {
        var element = PageList.InputHitTest(point) as DependencyObject;
        while (element is not null && element is not ListBoxItem)
        {
            element = VisualTreeHelper.GetParent(element);
        }

        return element is ListBoxItem item
            ? PageList.ItemContainerGenerator.IndexFromContainer(item)
            : -1;
    }

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
