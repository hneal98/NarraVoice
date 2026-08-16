// AudioAnalysisService.cs
// Audio analysis service for NarraVoice.
// Uses NAudio for waveform data and pure C# for pitch and RMS analysis.

using System;
using System.Collections.Generic;
using NAudio.Wave;

namespace NarraVoice.Core.Services
{
    public sealed class SilenceRegion
    {
        public double StartTime { get; init; }
        public double EndTime { get; init; }
        public double Duration => EndTime - StartTime;
    }

    public sealed class AudioAnalysisResult
    {
        public float[] Waveform { get; init; } = Array.Empty<float>();
        public double SampleRate { get; init; }
        public float[] PitchContour { get; init; } = Array.Empty<float>();
        public float[] PitchTimes { get; init; } = Array.Empty<float>();
        public float[] PitchConfidence { get; init; } = Array.Empty<float>();
        public float[] RmsEnergy { get; init; } = Array.Empty<float>();
        public float[] RmsTimes { get; init; } = Array.Empty<float>();
        public List<SilenceRegion> SilenceRegions { get; init; } = new();
        public double TotalDuration { get; init; }
    }

    public static class AudioAnalysisService
    {
        // RMS below this threshold is considered silence
        private const float SilenceThreshold = 0.08f;

        // Minimum silence duration to record as a region (seconds)
        private const double MinSilenceDuration = 0.05;

        public static AudioAnalysisResult Analyze(string audioPath)
        {
            // ── Waveform via NAudio ───────────────────────────────────────────
            float[] waveform;
            double sampleRate;

            using (var reader = new AudioFileReader(audioPath))
            {
                sampleRate = reader.WaveFormat.SampleRate;
                var buffer = new float[reader.Length / 4];
                reader.Read(buffer, 0, buffer.Length);
                waveform = buffer;
            }

            // Trim leading silence
            int firstNonSilence = 0;
            for (int i = 0; i < waveform.Length; i++)
            {
                if (Math.Abs(waveform[i]) > 0.001f)
                {
                    firstNonSilence = i;
                    break;
                }
            }
            if (firstNonSilence > 0)
                waveform = waveform[firstNonSilence..];

            int srInt = (int)sampleRate;
            double totalDuration = waveform.Length / sampleRate;

            // ── RMS energy ────────────────────────────────────────────────────
            int rmsWindow = 512;
            int rmsCount = waveform.Length / rmsWindow;
            var rmsEnergy = new List<float>();
            var rmsTimes = new List<float>();

            for (int i = 0; i < rmsCount; i++)
            {
                float sum = 0;
                for (int j = 0; j < rmsWindow; j++)
                    sum += waveform[i * rmsWindow + j] * waveform[i * rmsWindow + j];
                rmsEnergy.Add((float)Math.Sqrt(sum / rmsWindow));
                rmsTimes.Add((float)(i * rmsWindow) / srInt);
            }

            // ── Silence regions from RMS ──────────────────────────────────────
            var silenceRegions = DetectSilenceRegions(
                rmsEnergy.ToArray(), rmsTimes.ToArray(), SilenceThreshold, MinSilenceDuration);

            // ── Pitch via YIN algorithm ───────────────────────────────────────
            int pitchWindow = 2048;
            int pitchHop = 512;
            int pitchCount = (waveform.Length - pitchWindow) / pitchHop;
            var pitchContour = new List<float>();
            var pitchTimes = new List<float>();
            var pitchConfidence = new List<float>();

            for (int i = 0; i < pitchCount; i++)
            {
                int start = i * pitchHop;
                var (pitch, confidence) = YinPitchWithConfidence(waveform, start, pitchWindow, srInt);
                pitchContour.Add(pitch);
                pitchTimes.Add((float)start / srInt);
                pitchConfidence.Add(confidence);
            }

            return new AudioAnalysisResult
            {
                Waveform = waveform,
                SampleRate = sampleRate,
                PitchContour = pitchContour.ToArray(),
                PitchTimes = pitchTimes.ToArray(),
                PitchConfidence = pitchConfidence.ToArray(),
                RmsEnergy = rmsEnergy.ToArray(),
                RmsTimes = rmsTimes.ToArray(),
                SilenceRegions = silenceRegions,
                TotalDuration = totalDuration,
            };
        }

        private static List<SilenceRegion> DetectSilenceRegions(
            float[] rms, float[] times, float threshold, double minDuration)
        {
            var regions = new List<SilenceRegion>();
            bool inSilence = false;
            double silenceStart = 0;

            for (int i = 0; i < rms.Length; i++)
            {
                bool isSilent = rms[i] < threshold;

                if (isSilent && !inSilence)
                {
                    inSilence = true;
                    silenceStart = times[i];
                }
                else if (!isSilent && inSilence)
                {
                    inSilence = false;
                    double duration = times[i] - silenceStart;
                    if (duration >= minDuration)
                    {
                        regions.Add(new SilenceRegion
                        {
                            StartTime = silenceStart,
                            EndTime = times[i]
                        });
                    }
                }
            }

            // Handle trailing silence
            if (inSilence && times.Length > 0)
            {
                double duration = times[^1] - silenceStart;
                if (duration >= minDuration)
                {
                    regions.Add(new SilenceRegion
                    {
                        StartTime = silenceStart,
                        EndTime = times[^1]
                    });
                }
            }

            return regions;
        }

        private static (float pitch, float confidence) YinPitchWithConfidence(
            float[] samples, int start, int windowSize, int sampleRate)
        {
            int halfWindow = windowSize / 2;
            var diff = new float[halfWindow];

            // Step 1: Difference function
            for (int tau = 0; tau < halfWindow; tau++)
            {
                for (int j = 0; j < halfWindow; j++)
                {
                    float delta = samples[start + j] - samples[start + j + tau];
                    diff[tau] += delta * delta;
                }
            }

            // Step 2: Cumulative mean normalized difference
            var cmnd = new float[halfWindow];
            cmnd[0] = 1f;
            float runningSum = 0;
            for (int tau = 1; tau < halfWindow; tau++)
            {
                runningSum += diff[tau];
                cmnd[tau] = diff[tau] * tau / runningSum;
            }

            // Step 3: Find first dip below threshold
            float threshold = 0.15f;
            int minPeriod = sampleRate / 400;
            int maxPeriod = sampleRate / 75;

            int bestTau = -1;
            float bestCmnd = 1f;

            for (int tau = minPeriod; tau < Math.Min(maxPeriod, halfWindow); tau++)
            {
                if (cmnd[tau] < threshold)
                {
                    bestCmnd = cmnd[tau];
                    if (tau > 0 && tau < halfWindow - 1)
                    {
                        float better = tau + (cmnd[tau + 1] - cmnd[tau - 1]) /
                            (2f * (2f * cmnd[tau] - cmnd[tau - 1] - cmnd[tau + 1]));
                        float confidence = 1f - bestCmnd;
                        return better > 0
                            ? (sampleRate / better, confidence)
                            : (float.NaN, 0f);
                    }
                    bestTau = tau;
                    break;
                }
            }

            if (bestTau > 0)
                return ((float)sampleRate / bestTau, 1f - bestCmnd);

            // Step 5: Global minimum
            float minVal = float.MaxValue;
            int minIdx = minPeriod;
            for (int tau = minPeriod; tau < Math.Min(maxPeriod, halfWindow); tau++)
            {
                if (cmnd[tau] < minVal)
                {
                    minVal = cmnd[tau];
                    minIdx = tau;
                }
            }

            return minVal < 0.5f
                ? ((float)sampleRate / minIdx, 1f - minVal)
                : (float.NaN, 0f);
        }
    }
}