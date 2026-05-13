using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace ServiceBusExplorer.App;

/// Converts a "#AARRGGBB" or "#RRGGBB" hex string into a SolidColorBrush.
public sealed class HexColorConverter : IValueConverter
{
    public static readonly HexColorConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrEmpty(hex))
        {
            try { return new SolidColorBrush(Color.Parse(hex)); }
            catch { /* fall through */ }
        }
        return Brushes.Transparent;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// Shows a human-readable namespace name instead of the raw connection string.
public sealed class ConnectionStringLabelConverter : IValueConverter
{
    public static readonly ConnectionStringLabelConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string cs ? SettingsService.GetDisplayLabel(cs) : value?.ToString() ?? "";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
