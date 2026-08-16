// ColorStringToBrushConverter.cs
// Converts a hex color string to a WPF SolidColorBrush.
// Used for gutter marker colors defined in presets.
//
// Usage in XAML:
//   <Rectangle Fill="{Binding Color,
//              Converter={StaticResource ColorStringToBrushConverter}}"/>
//
// Input format: "#E27B4A" or "E27B4A" (with or without hash)
// Falls back to gray (#808080) if the string is invalid.

using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace NarraVoice.UI.Converters
{
    /// <summary>
    /// Converts a hex color string → SolidColorBrush.
    /// </summary>
    [ValueConversion(typeof(string), typeof(SolidColorBrush))]
    public sealed class ColorStringToBrushConverter : IValueConverter
    {
        private static readonly SolidColorBrush _fallback =
            new(Color.FromRgb(0x80, 0x80, 0x80));

        public object Convert(object value, Type targetType,
            object parameter, CultureInfo culture)
        {
            if (value is not string colorStr || string.IsNullOrWhiteSpace(colorStr))
                return _fallback;

            try
            {
                // Ensure leading hash
                string hex = colorStr.StartsWith('#') ? colorStr : "#" + colorStr;
                var color = (Color)ColorConverter.ConvertFromString(hex);
                var brush = new SolidColorBrush(color);
                brush.Freeze(); // Freeze for performance — brushes are immutable
                return brush;
            }
            catch
            {
                return _fallback;
            }
        }

        public object ConvertBack(object value, Type targetType,
            object parameter, CultureInfo culture)
        {
            if (value is SolidColorBrush brush)
            {
                var c = brush.Color;
                return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            }
            return "#808080";
        }
    }
}
