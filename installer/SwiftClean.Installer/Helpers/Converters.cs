using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using SwiftClean.Installer.Models;

namespace SwiftClean.Installer.Helpers;

/// <summary><c>bool</c> → <see cref="Visibility"/> (Visible/Collapsed). Page + element toggling.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Visibility.Visible;
}

/// <summary>A path mini-language string → <see cref="Geometry"/> (for the animated progress ring).</summary>
public sealed class StringToGeometryConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var s = value as string;
        return string.IsNullOrWhiteSpace(s) ? Geometry.Empty : Geometry.Parse(s);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary><see cref="LogKind"/> → text brush for the install log console.</summary>
public sealed class LogKindToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Dim = Frozen("#38383f");
    private static readonly SolidColorBrush Info = Frozen("#45454f");
    private static readonly SolidColorBrush Highlight = Frozen("#5b7fff");
    private static readonly SolidColorBrush Error = Frozen("#e05c5c");

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            LogKind.Info => Info,
            LogKind.Highlight => Highlight,
            LogKind.Error => Error,
            _ => Dim,
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static SolidColorBrush Frozen(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }
}
