// BoolToVisibilityConverter.cs
// Converts bool to WPF Visibility for show/hide bindings.
// Supports both normal and inverse conversion.
//
// Usage in XAML:
//   <Button Visibility="{Binding IsRendering,
//           Converter={StaticResource BoolToVisibilityConverter}}"/>
//
//   <!-- Inverse: hide when true -->
//   <Button Visibility="{Binding IsRendering,
//           Converter={StaticResource InverseBoolToVisibilityConverter}}"/>

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace NarraVoice.UI.Converters
{
    /// <summary>
    /// Converts bool → Visibility.
    /// true  → Visible
    /// false → Collapsed
    /// </summary>
    [ValueConversion(typeof(bool), typeof(Visibility))]
    public sealed class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType,
            object parameter, CultureInfo culture)
        {
            bool flag = value is bool b && b;
            return flag ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType,
            object parameter, CultureInfo culture)
        {
            return value is Visibility v && v == Visibility.Visible;
        }
    }

    /// <summary>
    /// Converts bool → Visibility (inverted).
    /// true  → Collapsed
    /// false → Visible
    /// </summary>
    [ValueConversion(typeof(bool), typeof(Visibility))]
    public sealed class InverseBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType,
            object parameter, CultureInfo culture)
        {
            bool flag = value is bool b && b;
            return flag ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType,
            object parameter, CultureInfo culture)
        {
            return value is Visibility v && v == Visibility.Collapsed;
        }
    }
}

