// AudioPlayerService.cs
// Audio playback service for NarraVoice using NAudio.
// Equivalent to audio.py's AudioPlayer class in the Python version.
//
// In WPF the UI buttons (Play, Pause) live in XAML — this service
// handles only the audio engine underneath them.
//
// Supports:
//   - Loading and playing MP3 files
//   - Autoplay on load
//   - Unloading to release file locks before re-rendering
//   - Playback state tracking

using System.IO;
using NAudio.Wave;
using NarraVoice.Core.Engine;

namespace NarraVoice.Core.Services
{
    /// <summary>
    /// Playback state of the audio player.
    /// </summary>
    //public enum AudioPlaybackState
    //{
    //    Stopped,
    //    Playing,
    //    Paused,
    //}

    /// <summary>
    /// Audio playback service using NAudio.
    /// Handles loading, playing, pausing, and unloading MP3 audio files.
    /// </summary>
    public sealed class AudioPlayerService : IDisposable
    {
        // ── State ─────────────────────────────────────────────────────────────

        private IWavePlayer? _wavePlayer;
        private AudioFileReader? _audioReader;
        private bool _disposed;

        // ── Events ────────────────────────────────────────────────────────────

        /// <summary>Raised when playback state changes.</summary>
        public event EventHandler<AudioPlaybackState>? PlaybackStateChanged;

        /// <summary>Raised when playback reaches the end of the file.</summary>
        public event EventHandler? PlaybackStopped;

        // ── Properties ────────────────────────────────────────────────────────

        /// <summary>Current playback state.</summary>
        public AudioPlaybackState State { get; private set; } = AudioPlaybackState.Stopped;

        /// <summary>True if a file is currently loaded.</summary>
        public bool IsLoaded => _audioReader != null;

        /// <summary>Current playback position in seconds.</summary>
        public double PositionSeconds =>
            _audioReader?.CurrentTime.TotalSeconds ?? 0;

        /// <summary>Total duration in seconds.</summary>
        public double DurationSeconds =>
            _audioReader?.TotalTime.TotalSeconds ?? 0;

        /// <summary>Volume 0.0 to 1.0.</summary>
        public float Volume
        {
            get => _wavePlayer?.Volume ?? 1.0f;
            set { if (_wavePlayer != null) _wavePlayer.Volume = value; }
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Load an MP3 file for playback.
        /// Unloads any currently loaded file first.
        /// </summary>
        /// <param name="path">Full path to the MP3 file.</param>
        /// <param name="autoplay">If true, start playing immediately after loading.</param>
        public void Load(string path, bool autoplay = false)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return;

            // Unload current file first
            Unload();

            try
            {
                _audioReader = new AudioFileReader(path);
                _wavePlayer = new WaveOutEvent();
                _wavePlayer.Init(_audioReader);
                _wavePlayer.PlaybackStopped += OnPlaybackStopped;

                if (autoplay)
                    Play();
            }
            catch (Exception)
            {
                // Clean up if loading fails
                Unload();
                throw;
            }
        }

        /// <summary>
        /// Unload the current file and release the file lock.
        /// Call this before re-rendering to avoid "file in use" errors.
        /// </summary>
        public void Unload()
        {
            Stop();

            // Remove event handler first to prevent double-firing
            if (_wavePlayer != null)
            {
                _wavePlayer.PlaybackStopped -= OnPlaybackStopped;
            }

            // Dispose in correct order
            if (_audioReader != null)
            {
                _audioReader.Dispose();
                _audioReader = null;
            }

            if (_wavePlayer != null)
            {
                _wavePlayer.Dispose();
                _wavePlayer = null;
            }

            SetState(AudioPlaybackState.Stopped);

            // Extra safety: clear any cached source/path if your class has one
            // CurrentFilePath = null;   // Uncomment if you have this field
        }

        /// <summary>Start or resume playback.</summary>
        public void Play()
        {
            if (_wavePlayer == null || _audioReader == null)
                return;

            _wavePlayer.Play();
            SetState(AudioPlaybackState.Playing);
        }

        /// <summary>Pause playback.</summary>
        public void Pause()
        {
            if (_wavePlayer == null)
                return;

            _wavePlayer.Pause();
            SetState(AudioPlaybackState.Paused);
        }

        /// <summary>Stop playback and reset position to beginning.</summary>
        public void Stop()
        {
            if (_wavePlayer == null)
                return;

            _wavePlayer.Stop();
            if (_audioReader != null)
                _audioReader.Position = 0;

            SetState(AudioPlaybackState.Stopped);
        }

        /// <summary>
        /// Save raw float32 audio samples to an MP3 file using NAudio.Lame.
        /// Used by the render pipeline to write chunk audio to disk.
        /// </summary>
        /// <param name="samples">Float32 audio samples.</param>
        /// <param name="sampleRate">Sample rate in Hz.</param>
        /// <param name="outputPath">Full path to write the MP3 file.</param>
        public static void SaveToMp3(float[] samples, int sampleRate, string outputPath)
        {
            // Convert float32 samples to 16-bit PCM
            var pcm = new short[samples.Length];
            for (int i = 0; i < samples.Length; i++)
            {
                float clamped = Math.Clamp(samples[i], -1.0f, 1.0f);
                pcm[i] = (short)(clamped * short.MaxValue);
            }

            var waveFormat = new WaveFormat(sampleRate, 16, 1); // Mono, 16-bit

            using var memStream = new MemoryStream();
            using (var writer = new NAudio.Wave.RawSourceWaveStream(
                new MemoryStream(PcmToBytes(pcm)), waveFormat))
            using (var mp3Writer = new NAudio.Lame.LameMP3FileWriter(
                outputPath, waveFormat, NAudio.Lame.LAMEPreset.STANDARD))
            {
                writer.CopyTo(mp3Writer);
            }
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
        {
            SetState(AudioPlaybackState.Stopped);
            PlaybackStopped?.Invoke(this, EventArgs.Empty);
        }

        private void SetState(AudioPlaybackState state)
        {
            if (State == state) return;
            State = state;
            PlaybackStateChanged?.Invoke(this, state);
        }

        private static byte[] PcmToBytes(short[] pcm)
        {
            var bytes = new byte[pcm.Length * 2];
            Buffer.BlockCopy(pcm, 0, bytes, 0, bytes.Length);
            return bytes;
        }

        // ── Disposal ──────────────────────────────────────────────────────────

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Unload();
        }
    }
}

