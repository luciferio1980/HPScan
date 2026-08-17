using System.Windows;
using CanonScanStudio.Models;

namespace CanonScanStudio.App.Services;

public static class ThemeService
{
    public static void Apply(string? themeId)
    {
        if (Application.Current is null)
        {
            return;
        }

        var id = AppThemes.Normalize(themeId);
        var uri = new Uri($"pack://application:,,,/Themes/Theme.{id}.xaml", UriKind.Absolute);
        var theme = new ResourceDictionary { Source = uri };
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        if (dictionaries.Count == 0)
        {
            dictionaries.Add(theme);
            return;
        }

        dictionaries[0] = theme;
    }
}
