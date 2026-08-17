using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CanonScanStudio.App.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CanonScanStudio.App.Views;

public sealed partial class OrganizePageItem : ObservableObject
{
    public OrganizePageItem(Guid id, BitmapImage? preview, int position)
    {
        Id = id;
        Preview = preview;
        Position = position;
    }

    public Guid Id { get; }
    public BitmapImage? Preview { get; }
    [ObservableProperty] private int position;
    public string Title => $"Página {Position}";

    partial void OnPositionChanged(int value) => OnPropertyChanged(nameof(Title));
}

public partial class OrganizePagesWindow : Window
{
    private readonly ObservableCollection<OrganizePageItem> _pages;
    private Point _dragStart;
    private int _dragIndex = -1;
    private bool _dragging;

    public OrganizePagesWindow(IReadOnlyList<PageItemViewModel> pages)
    {
        InitializeComponent();
        _pages = new ObservableCollection<OrganizePageItem>(
            pages.Select((p, i) => new OrganizePageItem(p.Page.Id, p.Preview ?? p.Thumbnail, i + 1)));
        PageList.ItemsSource = _pages;
        if (_pages.Count > 0)
        {
            PageList.SelectedIndex = 0;
        }
    }

    public IReadOnlyList<Guid> OrderedIds => _pages.Select(p => p.Id).ToList();

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _dragIndex = IndexFromSource(e.OriginalSource);
        _dragging = false;
        if (_dragIndex >= 0)
        {
            PageList.SelectedIndex = _dragIndex;
        }
    }

    private void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragIndex < 0 || _dragging)
        {
            return;
        }

        var diff = _dragStart - e.GetPosition(null);
        if (Math.Abs(diff.X) <= SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(diff.Y) <= SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _dragging = true;
        var from = _dragIndex;
        _dragIndex = -1;
        var data = new DataObject(typeof(int), from);
        DragDrop.DoDragDrop(PageList, data, DragDropEffects.Move);
        _dragging = false;
    }

    private void OnPreviewMouseUp(object sender, MouseButtonEventArgs e) => _dragIndex = -1;

    private void OnGiveFeedback(object sender, GiveFeedbackEventArgs e)
    {
        e.UseDefaultCursors = false;
        Mouse.SetCursor(Cursors.Hand);
        e.Handled = true;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(int)) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(int)))
        {
            return;
        }

        var from = (int)e.Data.GetData(typeof(int))!;
        if (from < 0 || from >= _pages.Count)
        {
            return;
        }

        var targetIndex = IndexFromSource(e.OriginalSource);
        if (targetIndex < 0)
        {
            targetIndex = IndexFromPoint(e.GetPosition(PageList));
        }

        if (targetIndex < 0)
        {
            targetIndex = _pages.Count - 1;
        }
        else
        {
            var container = PageList.ItemContainerGenerator.ContainerFromIndex(targetIndex) as FrameworkElement;
            if (container is not null)
            {
                var pos = e.GetPosition(container);
                if (pos.X > container.ActualWidth / 2)
                {
                    targetIndex++;
                }
            }
        }

        if (targetIndex > from)
        {
            targetIndex--;
        }

        targetIndex = Math.Clamp(targetIndex, 0, _pages.Count - 1);
        if (targetIndex != from)
        {
            _pages.Move(from, targetIndex);
            Renumber();
            PageList.SelectedIndex = targetIndex;
        }

        e.Handled = true;
    }

    private void Renumber()
    {
        for (var i = 0; i < _pages.Count; i++)
        {
            _pages[i].Position = i + 1;
        }
    }

    private int IndexFromSource(object source)
    {
        var current = source as DependencyObject;
        while (current is not null && current is not ListBoxItem)
        {
            current = VisualTreeHelper.GetParent(current);
        }

        return current is ListBoxItem item
            ? PageList.ItemContainerGenerator.IndexFromContainer(item)
            : -1;
    }

    private int IndexFromPoint(Point point)
    {
        var hit = PageList.InputHitTest(point) as DependencyObject;
        return hit is null ? -1 : IndexFromSource(hit);
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

    private void OnMoveLeft(object sender, RoutedEventArgs e) => MoveSelected(-1);

    private void OnMoveRight(object sender, RoutedEventArgs e) => MoveSelected(1);

    private void MoveSelected(int delta)
    {
        var from = PageList.SelectedIndex;
        if (from < 0)
        {
            from = 0;
        }

        var to = Math.Clamp(from + delta, 0, _pages.Count - 1);
        if (to == from || _pages.Count < 2)
        {
            return;
        }

        _pages.Move(from, to);
        Renumber();
        PageList.SelectedIndex = to;
    }
}
