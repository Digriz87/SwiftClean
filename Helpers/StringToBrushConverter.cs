using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace SwiftClean.Helpers
{
    /// <summary>
    /// Converts a hex color string (e.g. <c>#5b4fff</c>) into a frozen <see cref="SolidColorBrush"/>.
    /// Lets view models carry plain color strings while the UI binds them as brushes.
    /// </summary>
    public class StringToBrushConverter : IValueConverter
    {
        private static readonly Dictionary<string, Brush> Cache = new();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not string hex || string.IsNullOrWhiteSpace(hex))
                return Brushes.Transparent;

            if (Cache.TryGetValue(hex, out var cached))
                return cached;

            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            Cache[hex] = brush;
            return brush;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
