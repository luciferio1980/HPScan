using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using CanonScanStudio.App.Services;
using CanonScanStudio.App.ViewModels;
using CanonScanStudio.Models;
using CanonScanStudio.Services;

namespace CanonScanStudio.App.Views;

public partial class CropPageWindow : Window
{
    private const double MinCrop = 24;
    private readonly double _imageWidth;
    private readonly double _imageHeight;
    private double _viewWidth;
    private double _viewHeight;
    private double _cropX;
    private double _cropY;
    private double _cropW;
    private double _cropH;
    private Point _dragStart;
    private double _startX;
    private double _startY;
    private double _startW;
    private double _startH;
    private string? _handle;
    private bool _moving;

    public CropPageWindow(PageItemViewModel page, IImageProcessingService images)
    {
        InitializeComponent();
        var edit = page.Page.Edit.Clone();
        edit.Crop = null;
        var preview = images.ApplyEdits(page.Page.OriginalPath, edit, 1800);
        var info = images.ReadInfo(preview);
        _imageWidth = info.Width;
        _imageHeight = info.Height;
        Photo.Source = ImageSourceFactory.FromBytes(preview);
        Title = "Recortar · " + page.Title;

        _cropX = _imageWidth * 0.08;
        _cropY = _imageHeight * 0.08;
        _cropW = _imageWidth * 0.84;
        _cropH = _imageHeight * 0.84;
    }

    public CropRegion? NormalizedCrop { get; private set; }

    private void OnStageSizeChanged(object sender, SizeChangedEventArgs e) => LayoutImage();

    private void LayoutImage()
    {
        var availW = Math.Max(40, Stage.ActualWidth - 32);
        var availH = Math.Max(40, Stage.ActualHeight - 32);
        var scale = Math.Min(availW / _imageWidth, availH / _imageHeight);
        _viewWidth = _imageWidth * scale;
        _viewHeight = _imageHeight * scale;
        Work.Width = _viewWidth;
        Work.Height = _viewHeight;
        Photo.Width = _viewWidth;
        Photo.Height = _viewHeight;
        Canvas.SetLeft(Photo, 0);
        Canvas.SetTop(Photo, 0);
        UpdateOverlay();
    }

    private void UpdateOverlay()
    {
        var x = ToView(_cropX, _imageWidth, _viewWidth);
        var y = ToView(_cropY, _imageHeight, _viewHeight);
        var w = ToView(_cropW, _imageWidth, _viewWidth);
        var h = ToView(_cropH, _imageHeight, _viewHeight);

        var group = new GeometryGroup { FillRule = FillRule.EvenOdd };
        group.Children.Add(new RectangleGeometry(new Rect(0, 0, _viewWidth, _viewHeight)));
        group.Children.Add(new RectangleGeometry(new Rect(x, y, w, h)));
        DimOverlay.Data = group;

        Canvas.SetLeft(CropBorder, x);
        Canvas.SetTop(CropBorder, y);
        CropBorder.Width = w;
        CropBorder.Height = h;

        PlaceHandle(HandleNw, x - 5, y - 5);
        PlaceHandle(HandleNe, x + w - 5, y - 5);
        PlaceHandle(HandleSw, x - 5, y + h - 5);
        PlaceHandle(HandleSe, x + w - 5, y + h - 5);
        PlaceHandle(HandleN, x + w / 2 - 5, y - 5);
        PlaceHandle(HandleS, x + w / 2 - 5, y + h - 5);
        PlaceHandle(HandleW, x - 5, y + h / 2 - 5);
        PlaceHandle(HandleE, x + w - 5, y + h / 2 - 5);
    }

    private static void PlaceHandle(Rectangle handle, double left, double top)
    {
        Canvas.SetLeft(handle, left);
        Canvas.SetTop(handle, top);
    }

    private static double ToView(double value, double image, double view) =>
        image <= 0 ? 0 : value * view / image;

    private static double ToImage(double value, double image, double view) =>
        view <= 0 ? 0 : value * image / view;

    private void OnCropMouseDown(object sender, MouseButtonEventArgs e)
    {
        _moving = true;
        _handle = null;
        _dragStart = e.GetPosition(Work);
        _startX = _cropX;
        _startY = _cropY;
        CropBorder.CaptureMouse();
        e.Handled = true;
    }

    private void OnCropMouseMove(object sender, MouseEventArgs e)
    {
        if (!_moving || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var pos = e.GetPosition(Work);
        var dx = ToImage(pos.X - _dragStart.X, _imageWidth, _viewWidth);
        var dy = ToImage(pos.Y - _dragStart.Y, _imageHeight, _viewHeight);
        _cropX = _startX + dx;
        _cropY = _startY + dy;
        ClampCrop();
        UpdateOverlay();
    }

    private void OnCropMouseUp(object sender, MouseButtonEventArgs e)
    {
        _moving = false;
        CropBorder.ReleaseMouseCapture();
    }

    private void OnHandleDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement handle)
        {
            return;
        }

        _handle = handle.Tag as string;
        _moving = false;
        _dragStart = e.GetPosition(Work);
        _startX = _cropX;
        _startY = _cropY;
        _startW = _cropW;
        _startH = _cropH;
        handle.CaptureMouse();
        handle.MouseMove += OnHandleMove;
        handle.MouseLeftButtonUp += OnHandleUp;
        e.Handled = true;
    }

    private void OnHandleMove(object sender, MouseEventArgs e)
    {
        if (_handle is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var pos = e.GetPosition(Work);
        var dx = ToImage(pos.X - _dragStart.X, _imageWidth, _viewWidth);
        var dy = ToImage(pos.Y - _dragStart.Y, _imageHeight, _viewHeight);
        var x = _startX;
        var y = _startY;
        var w = _startW;
        var h = _startH;

        if (_handle.Contains('w'))
        {
            x += dx;
            w -= dx;
        }

        if (_handle.Contains('e'))
        {
            w += dx;
        }

        if (_handle.Contains('n'))
        {
            y += dy;
            h -= dy;
        }

        if (_handle.Contains('s'))
        {
            h += dy;
        }

        if (w < MinCrop)
        {
            if (_handle.Contains('w'))
            {
                x = _startX + _startW - MinCrop;
            }

            w = MinCrop;
        }

        if (h < MinCrop)
        {
            if (_handle.Contains('n'))
            {
                y = _startY + _startH - MinCrop;
            }

            h = MinCrop;
        }

        _cropX = x;
        _cropY = y;
        _cropW = w;
        _cropH = h;
        ClampCrop();
        UpdateOverlay();
    }

    private void OnHandleUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement handle)
        {
            handle.ReleaseMouseCapture();
            handle.MouseMove -= OnHandleMove;
            handle.MouseLeftButtonUp -= OnHandleUp;
        }

        _handle = null;
    }

    private void ClampCrop()
    {
        _cropW = Math.Clamp(_cropW, MinCrop, _imageWidth);
        _cropH = Math.Clamp(_cropH, MinCrop, _imageHeight);
        _cropX = Math.Clamp(_cropX, 0, Math.Max(0, _imageWidth - _cropW));
        _cropY = Math.Clamp(_cropY, 0, Math.Max(0, _imageHeight - _cropH));
    }

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        if (_imageWidth < 1 || _imageHeight < 1)
        {
            DialogResult = false;
            return;
        }

        NormalizedCrop = new CropRegion(
            _cropX / _imageWidth,
            _cropY / _imageHeight,
            _cropW / _imageWidth,
            _cropH / _imageHeight);
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        NormalizedCrop = null;
        DialogResult = false;
        Close();
    }
}
