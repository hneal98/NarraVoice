// NarraPlayback.cs
// Custom audio playback class for NarraVoice.
// Based on KokoroSharp's KokoroPlayback but with configurable sample rate
// to allow pitch control by playing samples at a different rate than 24000Hz.
//
// Pitch control: play at higher sample rate = higher pitch
//                play at lower sample rate  = lower pitch
// Formula: pitchSampleRate = (int)(24000 * MathF.Pow(2f, semitones / 12f))

using KokoroSharp;
using KokoroSharp.Core;
using KokoroSharp.Utilities;
using NAudio.Wave;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NarraVoice.Core.Engine
{

    /// <summary>
    /// Playback states for KokoroPlayback.
    /// </summary>

    public enum AudioPlaybackState
    {
        Idle,
        Playing,
        Paused,
        Stopped
    }

    public sealed class NarraPlayback : IDisposable
    {
        // ── Audio format ──────────────────────────────────────────────────────

        public WaveFormat WaveFormat { get; }

        // ── Internal state ────────────────────────────────────────────────────

        private readonly WaveOutEvent _waveOut = new WaveOutEvent();
        private readonly ConcurrentQueue<PlaybackHandle> _queuedPackets = new();
        private volatile bool _hasExited;
        public AudioPlaybackState State { get; private set; } = AudioPlaybackState.Idle;
        public event Action<AudioPlaybackState>? OnStateChanged;
        public event Action? OnPlaybackCompleted;

        private volatile bool _isPaused;

        public bool NicifySamples { get; set; } = true;

        // ── Constructor ───────────────────────────────────────────────────────

        /// <summary>
        /// Create a NarraPlayback instance with a configurable sample rate.
        /// Use a higher sample rate than 24000 to raise pitch, lower to reduce pitch.
        /// </summary>
        /// <param name="sampleRate">Playback sample rate. Default 24000Hz (no pitch shift).</param>
        public NarraPlayback(int sampleRate = 24000)
        {
            WaveFormat = new WaveFormat(sampleRate, 16, 1);

            var thread = new Thread((ThreadStart)async delegate
            {
                while (!_hasExited)
                {
                    await Task.Delay(100);
                    PlaybackHandle packet;
                    while (!_hasExited && _queuedPackets.TryDequeue(out packet))
                    {
                        if (!packet.Aborted)
                        {
                            float[] samples = packet.Samples;
                            DateTime startTime = DateTime.Now;
                            float[] array = samples;

                            packet.OnStarted?.Invoke();
                            SetState(AudioPlaybackState.Playing);

                            if (NicifySamples)
                                array = KokoroPlayback.PostProcessSamples(array);

                            var stream = new RawSourceWaveStream(
                                GetBytes(array), 0, array.Length * 2, WaveFormat);

                            _waveOut.Init(stream);
                            _waveOut.Play();

                            while (!_hasExited && !packet.Aborted &&
                                   _waveOut.PlaybackState == PlaybackState.Playing)
                            {
                                if (_isPaused)
                                {
                                    _waveOut.Pause();
                                    while (_isPaused && !_hasExited)
                                        await Task.Delay(50);
                                    _waveOut.Play();
                                }
                                await Task.Delay(10);
                            }

                            if (!_hasExited && packet.Aborted)
                                _waveOut.Stop();

                            if (stream.Position == stream.Length)
                            {
                                packet.OnSpoken?.Invoke();
                                packet.State = KokoroPlaybackHandleState.Completed;
                                SetState(AudioPlaybackState.Idle);
                                OnPlaybackCompleted?.Invoke();
                            }
                            else
                            {
                                packet.OnCanceled?.Invoke((
                                    (float)(DateTime.Now - startTime).TotalSeconds,
                                    (float)stream.Position / (float)stream.Length));
                            }

                            stream.Dispose();
                        }
                    }
                }
            });

            thread.IsBackground = true;
            thread.Start();
        }

        // ── Public API ────────────────────────────────────────────────────────

        public void Enqueue(float[] samples)
        {
            Enqueue(samples, null, null, null);
        }

        public PlaybackHandle Enqueue(
            float[] samples,
            Action? OnStarted = null,
            Action? OnSpoken = null,
            Action<(float time, float percentage)>? OnCanceled = null)
        {
            ObjectDisposedException.ThrowIf(_hasExited, this);

            var handle = new PlaybackHandle(samples, OnStarted, OnSpoken, OnCanceled);

            _queuedPackets.Enqueue(handle);
            return handle;
        }

        public void StopPlayback(bool clearQueue = false)
        {
            _waveOut.Stop();

            if (!clearQueue) return;

            foreach (var packet in _queuedPackets)
                packet.Abort();

            _queuedPackets.Clear();
        }

        public void Pause()
        {
            _isPaused = true;
            SetState(AudioPlaybackState.Paused);
        }

        public void Resume()
        {
            _isPaused = false;
            SetState(AudioPlaybackState.Playing);
        }

        public void SetVolume(float volume)
        {
            _waveOut.Volume = Math.Clamp(volume, 0f, 1f);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Calculate the sample rate needed to achieve a given pitch shift in semitones.
        /// </summary>
        public static int PitchToSampleRate(float semitones, int baseSampleRate = 24000)
        {
            return (int)(baseSampleRate * MathF.Pow(2f, semitones / 12f));
        }

        /// <summary>
        /// Calculate the speed compensation needed to maintain normal tempo
        /// when playing at a pitch-adjusted sample rate.
        /// </summary>
        public static float PitchToSpeedCompensation(float semitones, float overcompensate = 0.92f)
        {
            return (1f / MathF.Pow(2f, semitones / 12f)) * overcompensate;
        }

        public static byte[] GetBytes(float[] samples)
        {
            return samples
                .Select(f => (short)(f * 32767f))
                .SelectMany(BitConverter.GetBytes)
                .ToArray();
        }

        private void SetState(AudioPlaybackState state)
        {
            if (State == state) return;
            State = state;
            OnStateChanged?.Invoke(state);
        }

        // ── Disposal ──────────────────────────────────────────────────────────

        public void Dispose()
        {
            _hasExited = true;
            _waveOut.Stop();
            _waveOut.Dispose();

            foreach (var packet in _queuedPackets)
                packet.Abort();

            _queuedPackets.Clear();
        }
    }
}