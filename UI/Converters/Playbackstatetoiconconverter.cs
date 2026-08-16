// PlaybackStateToIconConverter.cs
// Converts AudioPlaybackState to UI values for the playback controls.
// Used to update Play/Pause button appearance based on playback state.
//
// Usage in XAML:
//   <Button Content="{Binding PlaybackState,
//           Converter={StaticResource PlaybackStateToIconConverter}}"/>

using System;
using System.Globalization;
using System.Windows.Data;
using NarraVoice.Core.Services;
using NarraVoice.Core.Engine;

namespace NarraVoice.UI.Converters
{
    /// <summary>
    /// Converts AudioPlaybackState → button icon/label string.
    /// Playing  → "⏸ Pause"
    /// Stopped  → "▶ Play"
    /// Paused   → "▶ Resume"
    /// </summary>
    [ValueConversion(typeof(AudioPlaybackState), typeof(string))]
    public sealed class PlaybackStateToIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType,
            object parameter, CultureInfo culture)
        {
            if (value is AudioPlaybackState state)
            {
                return state switch
                {
                    AudioPlaybackState.Playing => "⏸ Pause",
                    AudioPlaybackState.Paused => "▶ Resume",
                    _ => "▶ Play",
                };
            }
            return "▶ Play";
        }

        public object ConvertBack(object value, Type targetType,
            object parameter, CultureInfo culture) =>
            AudioPlaybackState.Stopped;
    }

    /// <summary>
    /// Converts AudioPlaybackState → bool indicating whether audio is playing.
    /// Used to enable/disable controls during playback.
    /// Playing → true
    /// Other   → false
    /// </summary>
    [ValueConversion(typeof(AudioPlaybackState), typeof(bool))]
    public sealed class IsPlayingConverter : IValueConverter
    {
        public object Convert(object value, Type targetType,
            object parameter, CultureInfo culture) =>
            value is AudioPlaybackState s && s == AudioPlaybackState.Playing;

        public object ConvertBack(object value, Type targetType,
            object parameter, CultureInfo culture) =>
            AudioPlaybackState.Stopped;
    }
}
