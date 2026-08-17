namespace CanonScanStudio.Models;

public sealed record AppThemeOption(string Id, string DisplayName);

public static class AppThemes
{
    public const string DefaultId = "claro";

    public static IReadOnlyList<AppThemeOption> All { get; } =
    [
        new("claro", "Claro"),
        new("oscuro", "Oscuro"),
        new("medianoche", "Medianoche"),
        new("bosque", "Bosque"),
        new("atardecer", "Atardecer"),
        new("oceano", "Océano"),
        new("lavanda", "Lavanda")
    ];

    public static string Normalize(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return DefaultId;
        }

        var match = All.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));
        return match?.Id ?? DefaultId;
    }
}
