namespace CanonScanStudio.Models;

public sealed class PageEditState
{
    public int RotationDegrees { get; set; }
    public bool FlipHorizontal { get; set; }
    public bool FlipVertical { get; set; }
    public CropRegion? Crop { get; set; }
    public double DeskewAngle { get; set; }
    public int Brightness { get; set; }
    public int Contrast { get; set; }
    public int Gamma { get; set; }
    public int Saturation { get; set; }
    public DocumentFilter Filter { get; set; }
    public bool EnhanceDocument { get; set; }
    public bool RemoveBorders { get; set; }

    public bool HasChanges =>
        RotationDegrees != 0 ||
        FlipHorizontal ||
        FlipVertical ||
        Crop is not null ||
        Math.Abs(DeskewAngle) > 0.01 ||
        Brightness != 0 ||
        Contrast != 0 ||
        Gamma != 0 ||
        Saturation != 0 ||
        Filter != DocumentFilter.None ||
        EnhanceDocument ||
        RemoveBorders;

    public PageEditState Clone() => new()
    {
        RotationDegrees = RotationDegrees,
        FlipHorizontal = FlipHorizontal,
        FlipVertical = FlipVertical,
        Crop = Crop,
        DeskewAngle = DeskewAngle,
        Brightness = Brightness,
        Contrast = Contrast,
        Gamma = Gamma,
        Saturation = Saturation,
        Filter = Filter,
        EnhanceDocument = EnhanceDocument,
        RemoveBorders = RemoveBorders
    };

    public static PageEditState Identity() => new();
}

public sealed record CropRegion(double X, double Y, double Width, double Height)
{
    public CropRegion Clamp(double imageWidth, double imageHeight)
    {
        var x = Math.Clamp(X, 0, Math.Max(0, imageWidth - 1));
        var y = Math.Clamp(Y, 0, Math.Max(0, imageHeight - 1));
        var w = Math.Clamp(Width, 1, imageWidth - x);
        var h = Math.Clamp(Height, 1, imageHeight - y);
        return new CropRegion(x, y, w, h);
    }
}
