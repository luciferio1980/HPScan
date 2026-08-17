namespace CanonScanStudio.Models;

public sealed record PageSizeDefinition(
    string Id,
    string DisplayName,
    double WidthInches,
    double HeightInches)
{
    public static PageSizeDefinition A4 { get; } = new("A4", "A4", 8.27, 11.69);
    public static PageSizeDefinition Letter { get; } = new("Letter", "Carta", 8.5, 11.0);
    public static PageSizeDefinition A5 { get; } = new("A5", "A5", 5.83, 8.27);
    public static PageSizeDefinition Photo10x15 { get; } = new("Photo10x15", "Foto 10 × 15 cm", 3.94, 5.91);
    public static PageSizeDefinition Custom { get; } = new("Custom", "Personalizado", 8.27, 11.69);

    public static IReadOnlyList<PageSizeDefinition> Presets { get; } =
    [
        A4,
        Letter,
        A5,
        Photo10x15,
        Custom
    ];

    public static PageSizeDefinition Find(string? id) =>
        Presets.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)) ?? A4;

    public PageSizeDefinition ClampTo(double maxWidthInches, double maxHeightInches)
    {
        return this with
        {
            WidthInches = Math.Min(WidthInches, maxWidthInches),
            HeightInches = Math.Min(HeightInches, maxHeightInches)
        };
    }
}
