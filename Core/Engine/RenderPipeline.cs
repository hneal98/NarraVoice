// RenderPipeline.cs (Refactored)
// Main render coordinator for NarraVoice.
// Uses KokoroSharp's KokoroTTS (SpeakFast) exclusively, with a custom
// punctuation-aware segmenter so pauses fire correctly regardless of
// segment length.
//
// Flow:
//   1. Apply substitutions to chunk text
//   2. Split by preset changes (if any gutter markers exist)
//   3. For each segment: Synthesize via KokoroTTS.SpeakFast()
//   4. Apply pitch shift and volume adjustments
//   5. Concatenate all segment audio (no crossfade)
//   6. Save to MP3 via AudioPlayerService.SaveToMp3()
//   7. Merge all chunks into final audiobook (batch render complete)

using DocumentFormat.OpenXml.InkML;
using KokoroSharp;
using KokoroSharp.Core;
using KokoroSharp.Processing;
using KokoroSharp.Utilities;
using Microsoft.ML.OnnxRuntime;
using NarraVoice.Core.Models;
using NarraVoice.Core.Services;
using NarraVoice.Core.Config;
using NAudio.Wave;
using SoundTouch;
using System.IO;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace NarraVoice.Core.Engine
{
    /// <summary>
    /// Result of a render operation.
    /// </summary>
    public sealed class RenderResult
    {
        /// <summary>Full path to the rendered WAV file.</summary>
        public string Mp3Path { get; init; } = string.Empty;

        /// <summary>File size in bytes.</summary>
        public long FileSize { get; init; }

        /// <summary>True if the render succeeded.</summary>
        public bool Success => !string.IsNullOrEmpty(Mp3Path) && FileSize > 0;

        /// <summary>
        /// Start time (in seconds) of each synthesized segment within the audio.
        /// Only populated for preview renders (chunkIndex == -1).
        /// </summary>
        public List<(double Time, string Text)> SegmentBoundaryTimes { get; init; } = new();
        public List<SegmentTiming> SegmentTimings { get; init; } = new();
    }

    /// <summary>
    /// Coordinates the full render pipeline for a single chunk or preview.
    /// Uses KokoroSharp's KokoroTTS (SpeakFast) for all TTS rendering.
    /// </summary>
    public sealed class RenderPipeline : IDisposable
    {
        // ── Dependencies ──────────────────────────────────────────────────────

        private readonly SubstitutionService _substitutions;
               

        // ── Constants ─────────────────────────────────────────────────────────

        
        private const int KokoroSampleRate = 24000;
       

        // How long to wait for a single SpeakFast() call to finish before
        // giving up. This is a safety net, not a real limit — a full
        // ~2500-character chunk shouldn't take more than ~3 minutes to
        // render, so 180s gives some headroom above that before treating
        // it as hung.
        private static readonly TimeSpan SpeakTimeout = TimeSpan.FromSeconds(180);

        // ── Lazy, shared KokoroTTS instance ──────────────────────────────────
        // Loaded once on first use and kept alive for the life of this
        // RenderPipeline instance, instead of reloading the ONNX model on
        // every single render call.

        private readonly object _ttsLock = new();
        private KokoroTTS? _tts;
        private KokoroJob? _currentJob;
        private NarraPlayback? _currentPlayer;
        private string? _lastPreviewPath;

        
        private KokoroTTS GetTts()
        {
            if (_tts != null) return _tts;

            lock (_ttsLock)
            {
                if (_tts == null)
                {
                    var sessionOptions = new SessionOptions();
                    sessionOptions.AppendExecutionProvider_CPU();

                    _tts = new KokoroTTS(AppConfig.KokoroModelPath, sessionOptions)
                    {
                        NicifyAudio = true
                    };
                }
            }

            return _tts;
        }

        private KokoroWavSynthesizer? _synthesizer;

        private KokoroWavSynthesizer GetSynthesizer()
        {
            if (_synthesizer != null) return _synthesizer;

            lock (_ttsLock)
            {
                if (_synthesizer == null)
                {
                    var sessionOptions = new SessionOptions();
                    sessionOptions.AppendExecutionProvider_CPU();
                    _synthesizer = new KokoroWavSynthesizer(AppConfig.KokoroModelPath, sessionOptions);
                }
            }

            return _synthesizer;
        }

        // ── Constructor ───────────────────────────────────────────────────────

        public RenderPipeline(SubstitutionService substitutions)
        {
            _substitutions = substitutions;
        }

        public void Dispose()
        {
            _tts?.Dispose();
            _tts = null;
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Provides access to the shared KokoroTTS instance for playback use.
        /// Do not dispose — lifetime is managed by RenderPipeline.
        /// </summary>
        public KokoroTTS GetSharedTts() => GetTts();

        /// <summary>
        /// Render a chunk of text to an MP3 file.
        /// </summary>
        public async Task<RenderResult> RenderChunkAsync(
            string chunkText,
            VoiceProfile defaultProfile,
            string outputDir,
            int chunkIndex,
            string prefix,
            List<PresetChange>? presetChanges = null,
            Dictionary<string, Preset>? presetsLibrary = null,
            Action<string>? log = null,
            CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(outputDir);

            string filename = chunkIndex < 0
                ? $"{prefix}_preview.wav"
                : $"{prefix}_{chunkIndex:D4}.wav";
            string outPath = Path.Combine(outputDir, filename);

            TryDeleteFile(outPath, log);
            log?.Invoke($"Generating {filename} ...");

            string renderText = chunkText;

            float[] samples;
            int sampleRate;
            var segmentBoundaryTimes = new List<(double Time, string Text)>();
            var segmentTimings = new List<SegmentTiming>();
            

            if (presetChanges != null && presetChanges.Count > 0)
            {
                (samples, sampleRate) = await RenderWithPresetsAsync(
                    renderText, defaultProfile, presetChanges,
                    presetsLibrary ?? new Dictionary<string, Preset>(),
                    log, cancellationToken);
            }
            else
            {
                (samples, sampleRate, segmentBoundaryTimes, segmentTimings) = await RenderTextAsync(
                        renderText, defaultProfile, log);

                // Apply volume if needed
                if (defaultProfile.NeedsVolumeAdjust)
                {
                    log?.Invoke($"Applying volume: {defaultProfile.Volume}");
                    float multiplier = defaultProfile.VolumeMultiplier;
                    for (int vi = 0; vi < samples.Length; vi++)
                        samples[vi] *= multiplier;
                }

                // Play via NarraPlayback at pitch-adjusted sample rate
                _currentPlayer = new NarraPlayback(sampleRate);
                _currentPlayer.NicifySamples = false;
                _currentPlayer.Enqueue(samples, null, null, null);
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (chunkIndex < 0)
                _lastPreviewPath = outPath;

            log?.Invoke($"DEBUG SaveToWav: samples.Length={samples.Length}, sampleRate={sampleRate}");

            SaveToWav(samples, sampleRate, outPath);

            long fileSize = new FileInfo(outPath).Length;
            log?.Invoke($"  OK -- {fileSize:N0} bytes -> {filename}");

            return new RenderResult
            {
                Mp3Path = outPath,
                FileSize = fileSize,
                SegmentBoundaryTimes = chunkIndex < 0 ? segmentBoundaryTimes : new List<(double Time, string Text)>(),
                SegmentTimings = chunkIndex < 0 ? segmentTimings : new List<SegmentTiming>()
            };
        }

        public void TogglePause()
        {
            if (_currentPlayer == null) return;

            if (_currentPlayer.State == AudioPlaybackState.Paused)
                _currentPlayer.Resume();
            else
                _currentPlayer.Pause();
        }

        public bool IsCurrentlyPaused()
        {
            return _currentPlayer?.State == AudioPlaybackState.Paused;
        }

        public void PlayLastPreview()
        {
            if (string.IsNullOrEmpty(_lastPreviewPath) || !File.Exists(_lastPreviewPath))
                return;

            // Stop any current playback first
            _currentPlayer?.StopPlayback(true);
            //_currentPlayer?.Dispose();

            using var reader = new NAudio.Wave.AudioFileReader(_lastPreviewPath);
            int segRate = reader.WaveFormat.SampleRate;
            var buffer = new float[reader.Length / 4];
            reader.Read(buffer, 0, buffer.Length);

            _currentPlayer = new NarraPlayback(segRate);
            _currentPlayer.NicifySamples = false;
            _currentPlayer.Enqueue(buffer, null, null, null);
        }

        public void StopPlayback()
        {
            _currentJob?.Cancel();
            _currentPlayer?.StopPlayback(true);
        }

        /// <summary>
        /// Merge all rendered chunk MP3s into a single audiobook file.
        /// </summary>
        public async Task<string> MergeChunksAsync(
            List<string> wavFiles,
            string outputDir,
            string outputFilename = "audiobook.mp3",
            Action<string>? log = null)
        {
            if (wavFiles.Count == 0)
            {
                log?.Invoke("No files to merge.");
                return string.Empty;
            }

            string outPath = Path.Combine(outputDir, outputFilename);
            log?.Invoke($"Merging {wavFiles.Count} chunk(s) into {outputFilename}...");

            await Task.Run(() =>
            {
                var allSamples = new List<float>();
                int sampleRate = KokoroSampleRate;

                // Read all chunk WAV files and concatenate samples
                foreach (string wav in wavFiles)
                {
                    if (!File.Exists(wav)) continue;
                    using var reader = new NAudio.Wave.AudioFileReader(wav);
                    sampleRate = reader.WaveFormat.SampleRate;
                    var buffer = new float[reader.Length / 4];
                    reader.Read(buffer, 0, buffer.Length);
                    allSamples.AddRange(buffer);
                }



                // Save merged audio as WAV first
                string mergedWavPath = Path.ChangeExtension(outPath, ".wav");
                SaveToWav(allSamples.ToArray(), sampleRate, mergedWavPath);
                log?.Invoke($"  Merged WAV: {new FileInfo(mergedWavPath).Length:N0} bytes");

                // Convert final WAV to MP3 once
                AudioPlayerService.SaveToMp3(allSamples.ToArray(), sampleRate, outPath);

                // Clean up intermediate merged WAV
                TryDeleteFile(mergedWavPath, log);
            });

            long size = new FileInfo(outPath).Length;
            log?.Invoke($"  Merged: {size:N0} bytes -> {outputFilename}");
            return outPath;
        }

        // ── Private helpers ───────────────────────────────────────────────────

        /// <summary>
        /// Render text using KokoroTTS.SpeakFast(), with a custom
        /// punctuation-aware segmenter so pauses fire correctly regardless
        /// of segment length (KokoroSharp's default segmenter requires
        /// ~100+ characters before it will split, which breaks on
        /// dialogue-length lines).
        ///
        /// If SpeakFast() produces multiple internal segments (because the
        /// text contains multiple punctuation breaks), all of their audio
        /// is concatenated, in order, into a single continuous result —
        /// matching how this method's callers already expect one
        /// contiguous float[] per chunk/segment.
        /// </summary>
        /// 

        private async Task<(float[] samples, int sampleRate, List<(double Time, string Text)> boundaryTimes, List<SegmentTiming> segmentTimings)> RenderTextAsync(
            string text,
            VoiceProfile profile,
            Action<string>? log = null)
        {
            try
            {

                if (string.Equals(profile.Engine, "qwen", StringComparison.OrdinalIgnoreCase))
                {
                    var (qwenSamples, qwenRate) = await SynthQwenAsync(text, profile, log);
                    return (qwenSamples, qwenRate, new List<(double, string)>(), new List<SegmentTiming>());
                }

                var synthesizer = GetSynthesizer();
                var voice = KokoroVoiceManager.GetVoice(profile.Voice);

                // Strip quote characters that cause phantom sounds
                text = text.Replace('"', ' ')
                           .Replace('\u201C', ' ')
                           .Replace('\u201D', ' ');

                // Calculate pitch adjustment
                float pitchSemitones = profile.PitchSemitones;
                int pitchSampleRate = NarraPlayback.PitchToSampleRate(pitchSemitones);
                float speedCompensation = NarraPlayback.PitchToSpeedCompensation(pitchSemitones, 0.92f) * profile.SpeedMultiplier;

                var config = new KokoroTTSPipelineConfig(new DefaultSegmentationConfig())
                {
                    Speed = speedCompensation,
                    SecondsOfPauseBetweenProperSegments = new PauseAfterSegmentStrategy(
                        CommaPause: 0.20f,
                        PeriodPause: 0.45f,
                        QuestionMarkPause: 0.35f,
                        ExclamationMarkPause: 0.6f,
                        NewLinePause: 0.75f,
                        OthersPause: 0.15f
                    )
                };

                log?.Invoke($"Synthesizing with pitch: {pitchSemitones:+0.#;-0.#;0}st at {pitchSampleRate}Hz");

                log?.Invoke($"Text to synthesize: {text}");

                // Capture the FULL original text before any splitting
                string fullOriginalText = text.Trim();

                List<string> textSegments = SplitTextOnSentenceBoundaries(fullOriginalText);

                // Fallback marker detection on the full raw text
                bool hasRiseMarker = text.Contains("?+");
                bool hasFallMarker = text.Contains(".-");

                log?.Invoke($"Global marker fallback: Rise={hasRiseMarker}, Fall={hasFallMarker}");

                var filteredTextSegments = new List<string>();
                var tokenSegments = new List<int[]?>();
                var originalSegments = new List<string>();

                foreach (var s in textSegments)
                {
                    if (s == "\n\n")
                    {
                        filteredTextSegments.Add(s);
                        tokenSegments.Add(null);
                        originalSegments.Add(s);
                        continue;
                    }

                    // Use the raw segment from splitter, but also check full text if needed
                    string originalForRamp = s.TrimEnd(); // keep trailing punctuation

                    string cleanTextForSynthesis = _substitutions.Apply(
                        s.Replace("?+", "?")
                         .Replace(".-", ".")
                         .Replace("+", "")
                         .Replace("?", "? ")   // temporary space if needed
                    );

                    var tokens = Tokenizer.Tokenize(cleanTextForSynthesis, voice.GetLangCode(), true);

                    if (tokens.Length > 0)
                    {
                        filteredTextSegments.Add(cleanTextForSynthesis);
                        tokenSegments.Add(tokens);
                        originalSegments.Add(originalForRamp);

                        log?.Invoke($"DEBUG: OriginalForRamp='{originalForRamp}', Clean='{cleanTextForSynthesis}'");
                    }
                }


                log?.Invoke($"Token segments: {tokenSegments.Count} total, {tokenSegments.Count(t => t == null)} nulls");

                var allSamples = new List<float>();
                var boundaryTimes = new List<(double Time, string Text)>();
                int runningSamples = 0;
                var cleanSegments = new List<string>();
                var segmentTimings = new List<SegmentTiming>();

                var nodes = Parser.Parse(text);
                log?.Invoke($"Parser produced {nodes.Count} nodes");
                foreach (var n in nodes)
                    log?.Invoke($"  node: {n}");


                foreach (var node in nodes)
                {
                    // inside the silence branch:
                    if (node.IsSilence)
                    {
                        int silenceSamples = (int)(node.SilenceMs / 1000.0 * KokoroSampleRate);
                        log?.Invoke($"Inserted silence: {node.SilenceMs}ms → {silenceSamples} samples");
                        allSamples.AddRange(new float[silenceSamples]);
                        runningSamples += silenceSamples;
                        continue;
                    }

                    // ----- Text node: run the existing synthesis logic on node.Text -----
                    string originalForRamp = node.Text.TrimEnd();
                    string cleanTextForSynthesis = _substitutions.Apply(
                        node.Text
                            .Replace("?+", "?")
                            .Replace(".-", ".")
                            .Replace("+", "")
                    );

                    var tokens = Tokenizer.Tokenize(cleanTextForSynthesis, voice.GetLangCode(), true);
                    if (tokens.Length == 0) continue;

                    boundaryTimes.Add((runningSamples / (double)KokoroSampleRate, node.Text));

                    float[] segSamples = await SynthesizeSegmentAsync(tokens, voice, speedCompensation);

                    // Trim leading/trailing near-silence
                    int segStart = 0, segEnd = segSamples.Length - 1;
                    while (segStart < segSamples.Length && Math.Abs(segSamples[segStart]) < 0.02f) segStart++;
                    while (segEnd > segStart && Math.Abs(segSamples[segEnd]) < 0.02f) segEnd--;
                    if (segEnd > segStart)
                        segSamples = segSamples[segStart..(segEnd + 1)];

                    double segStartTime = runningSamples / (double)KokoroSampleRate;
                    double segDuration = segSamples.Length / (double)KokoroSampleRate;

                    segmentTimings.Add(new SegmentTiming
                    {
                        Text = node.Text,
                        StartTime = segStartTime,
                        Duration = segDuration,
                        Tokens = tokens
                    });

                    allSamples.AddRange(segSamples);
                    runningSamples += segSamples.Length;
                    log?.Invoke($"Text node added: {segSamples.Length} samples | '{node.Text.Trim()}'");
                }

                float[] samples = allSamples.ToArray();
                log?.Invoke($"allSamples before NaN cleanup: {samples.Length}");
                // NaN cleanup + clamp
                for (int i = 0; i < samples.Length; i++)
                {
                    if (float.IsNaN(samples[i]) || float.IsInfinity(samples[i]))
                        samples[i] = 0.0f;
                    else
                        samples[i] = Math.Clamp(samples[i], -1.0f, 1.0f);
                }

                log?.Invoke($"RenderTextAsync returned {samples.Length} samples at {pitchSampleRate}Hz");


                return (samples, pitchSampleRate, boundaryTimes, segmentTimings);
            }
            catch (Exception ex)
            {
                log?.Invoke($"ERROR in RenderTextAsync: {ex.Message}");
                throw;
            }
        }

        private async Task<(float[] qwenSamples, int QwenRate)> SynthQwenAsync(
            string text,
            VoiceProfile profile,
            Action<string>? log = null)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            log?.Invoke($"Qwen synthesis: voice={profile.Voice}");

            string speaker = profile.Voice;
            if (speaker.StartsWith("qwen_", StringComparison.OrdinalIgnoreCase))
                speaker = speaker.Substring(5);
            if (speaker.Length > 0)
                speaker = char.ToUpperInvariant(speaker[0]) + speaker.Substring(1).ToLowerInvariant();

            await QwenServerManager.Instance.EnsureRunningAsync(log);
            var (wavBytes, genSeconds) = await QwenServerManager.Instance.GenerateAsync(text, speaker, profile.Instruct, log);

            float[] samples;
            int sampleRate;
            using (var ms = new MemoryStream(wavBytes))
            using (var reader = new NAudio.Wave.WaveFileReader(ms))
            {
                sampleRate = reader.WaveFormat.SampleRate;
                var sampleProvider = reader.ToSampleProvider();
                var buffer = new List<float>();
                float[] chunk = new float[4096];
                int read;
                while ((read = sampleProvider.Read(chunk, 0, chunk.Length)) > 0)
                    buffer.AddRange(chunk.Take(read));
                samples = buffer.ToArray();
            }

            sw.Stop();
            log?.Invoke($"Qwen done: {samples.Length} samples @ {sampleRate}Hz in {sw.Elapsed.TotalSeconds:F1}s (gen: {genSeconds:F1}s)");

            return (samples, sampleRate);
        }

        /// <summary>
        /// Splits text into sentences based on major boundaries (. ? ! and newlines).
        /// Keeps commas/semicolons/colons inside the sentence so Kokoro sees full context.
        /// </summary>
        private static List<string> SplitTextOnSentenceBoundaries(string text)
        {
            var segments = new List<string>();
            var current = new System.Text.StringBuilder();

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                current.Append(c);

                // Special case for our markers
                if (i + 1 < text.Length && c == '?' && text[i + 1] == '+')
                {
                    current.Append('+'); // keep the marker
                    i++; // skip the +
                }
                else if (i + 1 < text.Length && c == '.' && text[i + 1] == '-')
                {
                    current.Append('-');
                    i++;
                }

                if (c == '.' || c == '?' || c == '!' || c == '\n')
                {
                    segments.Add(current.ToString().Trim());
                    current.Clear();
                }
            }

            if (current.Length > 0)
                segments.Add(current.ToString().Trim());

            return segments.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        }

        private async Task<float[]> SynthesizeSegmentAsync(
            int[] tokens,
            KokoroVoice voice,
            float speed = 1.0f)
        {
            var synthesizer = GetSynthesizer();
            var segments = new List<int[]> { tokens };
            var job = synthesizer.EnqueueJob(KokoroJob.Create(segments, voice, speed, null));
            _currentJob = job;

            float[]? result = null;
            job.Steps[0].OnStepComplete = (float[] samples) =>
            {
                result = samples;
            };
            
            while (!job.isDone)
                await Task.Delay(10);

            return result ?? Array.Empty<float>();
        }

        /// <summary>
        /// Split chunk text by preset changes and render each segment.
        /// </summary>
        private async Task<(float[] samples, int sampleRate)> RenderWithPresetsAsync(
            string text,
            VoiceProfile defaultProfile,
            List<PresetChange> presetChanges,
            Dictionary<string, Preset> presetsLibrary,
            Action<string>? log,
            CancellationToken cancellationToken)
        {
            var segments = SplitByPresetChanges(
                text, presetChanges, defaultProfile, presetsLibrary, log);

            log?.Invoke($"Splitter: {segments.Count} preset segment(s)");

            var tempFiles = new List<string>();
            var allSamples = new List<float>();
            string tmpDir = Path.Combine(Path.GetTempPath(), "NarraVoice");
            Directory.CreateDirectory(tmpDir);

            for (int i = 0; i < segments.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var (segText, segProfile) = segments[i];
                log?.Invoke($"  segment {i + 1}/{segments.Count} voice='{segProfile.Voice}'");

                // Render this segment — returns samples at pitchSampleRate
                var (segSamples, pitchSampleRate, _, _) = await RenderTextAsync(segText, segProfile, log);

                // Apply volume if needed
                if (segProfile.NeedsVolumeAdjust)
                {
                    log?.Invoke($"Applying volume: {segProfile.Volume}");
                    float multiplier = segProfile.VolumeMultiplier;
                    for (int vi = 0; vi < segSamples.Length; vi++)
                        segSamples[vi] *= multiplier;
                }

                // Save segment to temp WAV at pitch-adjusted sample rate
                string tempPath = Path.Combine(tmpDir, $"seg_{i}_{DateTime.Now.Ticks}.wav");
                SaveToWav(segSamples, pitchSampleRate, tempPath);
                tempFiles.Add(tempPath);
            }

            // Read all temp WAVs, resample to KokoroSampleRate when needed, then concatenate
            foreach (var file in tempFiles)
            {
                using var reader = new NAudio.Wave.AudioFileReader(file);
                int segRate = reader.WaveFormat.SampleRate;

                float[] buffer;

                if (segRate == KokoroSampleRate && reader.WaveFormat.Channels == 1)
                {
                    buffer = new float[(int)(reader.Length / 4)];
                    int read = reader.Read(buffer, 0, buffer.Length);
                    if (read < buffer.Length)
                        Array.Resize(ref buffer, read);
                }
                else
                {
                    // Resample to 24000 Hz (float samples)
                    var resampler = new NAudio.Wave.SampleProviders.WdlResamplingSampleProvider(reader, KokoroSampleRate);

                    var samples = new List<float>();
                    var temp = new float[KokoroSampleRate / 10]; // ~100 ms
                    int n;
                    while ((n = resampler.Read(temp, 0, temp.Length)) > 0)
                    {
                        for (int i = 0; i < n; i++)
                            samples.Add(temp[i]);
                    }
                    buffer = samples.ToArray();
                }

                allSamples.AddRange(buffer);

                // Live preview at the *target* rate so it matches the saved mix
                _currentPlayer = new NarraPlayback(KokoroSampleRate);
                _currentPlayer.NicifySamples = false;
                _currentPlayer.Enqueue(buffer, null, null, null);
                int durationMs = (int)(buffer.Length / (float)KokoroSampleRate * 1000) + 200;
                await Task.Delay(durationMs);
            }

            // Clean up temp files
            foreach (var file in tempFiles)
                TryDeleteFile(file, log);

            return (allSamples.ToArray(), KokoroSampleRate);
        }

        /// <summary>
        /// Split chunk text into segments based on gutter preset change markers.
        /// </summary>
        private static List<(string text, VoiceProfile profile)> SplitByPresetChanges(
            string text,
            List<PresetChange> presetChanges,
            VoiceProfile defaultProfile,
            Dictionary<string, Preset> presetsLibrary,
            Action<string>? log = null)
        {
            if (presetChanges.Count == 0)
                return new List<(string, VoiceProfile)> { (text, defaultProfile) };

            var changes = presetChanges.OrderBy(c => c.Line).ToList();
            string[] lines = text.Split('\n');
            int total = lines.Length;

            changes = changes
                .Where(c => presetsLibrary.ContainsKey(c.Preset))
                .ToList();

            if (changes.Count == 0)
                return new List<(string, VoiceProfile)> { (text, defaultProfile) };

            VoiceProfile ProfileFor(string presetName)
            {
                if (presetsLibrary.TryGetValue(presetName, out var preset))
                {
                    var p = preset.ToVoiceProfile();
                    if (string.IsNullOrWhiteSpace(p.Instruct))
                        p.Instruct = defaultProfile.Instruct;
                    return p;
                }
                return defaultProfile;
            }

            var segments = new List<(string text, VoiceProfile profile)>();

            // Text before the first marker uses default profile
            int firstLine = changes[0].Line;
            if (firstLine > 1)
            {
                string head = string.Join("\n", lines[..(firstLine - 1)]);
                //string segText = string.Join(" ", lines[startIdx..endIdx]);
                if (!string.IsNullOrWhiteSpace(head))
                    segments.Add((head, defaultProfile));
            }

            // Each marker segment
            for (int i = 0; i < changes.Count; i++)
            {
                int startLine = changes[i].Line;
                int endLine = i + 1 < changes.Count ? changes[i + 1].Line : total + 1;

                int startIdx = Math.Max(0, startLine - 1);
                int endIdx = Math.Min(total, endLine - 1);

                if (startIdx >= endIdx) continue;

                //string segText = string.Join("\n", lines[startIdx..endIdx]);
                string segText = string.Join(" ", lines[startIdx..endIdx]);
                if (string.IsNullOrWhiteSpace(segText)) continue;

                segments.Add((segText, ProfileFor(changes[i].Preset)));
            }
            
            foreach (var (segText, segProfile) in segments)
                log?.Invoke($"  Segment: '{segText}' → voice='{segProfile.Voice}'");

            return segments.Count > 0
                ? segments
                : new List<(string, VoiceProfile)> { (text, defaultProfile) };
        }

        /// <summary>
        /// Try to delete a file.
        /// </summary>
        private static void TryDeleteFile(string path, Action<string>? log)
        {
            if (!File.Exists(path)) return;
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                System.Threading.Thread.Sleep(500);
                try { File.Delete(path); }
                catch (Exception ex)
                {
                    log?.Invoke($"Warning: Could not remove old file {path}: {ex.Message}");
                }
            }
        }

        private static void SaveToWav(float[] samples, int sampleRate, string path)
        {
            int byteCount = samples.Length * 2;
            using var bw = new BinaryWriter(File.Create(path));

            bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            bw.Write(36 + byteCount);
            bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
            bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
            bw.Write(16);
            bw.Write((ushort)1);   // PCM
            bw.Write((ushort)1);   // mono
            bw.Write(sampleRate);
            bw.Write(sampleRate * 2);
            bw.Write((ushort)2);   // block align
            bw.Write((ushort)16);  // bits per sample
            bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            bw.Write(byteCount);

            foreach (var s in samples)
            {
                short pcm = (short)Math.Clamp(s * 32768f, short.MinValue, short.MaxValue);
                bw.Write(pcm);
            }
        }
    }
}