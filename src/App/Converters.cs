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

/// Shows the label from non-secret connection profile metadata.
public sealed class ConnectionStringLabelConverter : IValueConverter
{
    public static readonly ConnectionStringLabelConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is ConnectionProfile profile ? profile.Label : value?.ToString() ?? "";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
