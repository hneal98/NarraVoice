// VoiceManagerService.cs
// Voice management service for NarraVoice.
// Downloads all Kokoro voice .bin files from onnx-community/Kokoro-82M-v1.0-ONNX
// on first run, then manages which voices appear in the dropdown.
//
// Individual .bin files (~522KB each) are used instead of voices-v1.0.bin
// so each voice is self-contained and no index mapping is needed.
//
// Voice preferences (which voices show in dropdown) are saved to
// {AppDir}/voice_preferences.json

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using NarraVoice.Core.Config;

namespace NarraVoice.Core.Services
{
    /// <summary>
    /// Progress report for a voice download operation.
    /// </summary>
    public sealed class VoiceDownloadProgress
    {
        public int Current { get; init; }
        public int Total { get; init; }
        public string VoiceName { get; init; } = string.Empty;
        public bool Success { get; init; }
        public string? Error { get; init; }
    }

    /// <summary>
    /// Information about a single Kokoro voice.
    /// </summary>
    public sealed class VoiceInfo
    {
        public string Id { get; init; } = string.Empty;
        public string Label { get; init; } = string.Empty;
        public string Category { get; init; } = string.Empty;
        public string Language { get; init; } = string.Empty;
        public string Engine { get; init; } = "kokoro";   // "kokoro" | "qwen"
        public bool IsInstalled { get; set; }
        public bool IsVisible { get; set; } = true;
        public string BinFileName => Engine == "kokoro" ? $"{Id}.bin" : string.Empty;
    }

    /// <summary>
    /// Manages Kokoro voice files and user preferences.
    /// Downloads all voices on first run, then lets users choose
    /// which appear in the dropdown via Voice Manager.
    /// </summary>
    public sealed class VoiceManagerService : IDisposable
    {
        // Constants
        private const string OnnxRepoBase =
            "https://huggingface.co/onnx-community/Kokoro-82M-v1.0-ONNX/resolve/main/voices";
        private const string PrefsFileName = "voice_preferences.json";
        // All known Kokoro voices
        public static readonly List<VoiceInfo> AllVoices = new()
        {
            // American English
            new() { Id="af_alloy",    Label="Alloy",     Category="American English", Language="en-us" },
            new() { Id="af_aoede",    Label="Aoede",     Category="American English", Language="en-us" },
            new() { Id="af_bella",    Label="Bella",     Category="American English", Language="en-us" },
            new() { Id="af_heart",    Label="Heart",     Category="American English", Language="en-us" },
            new() { Id="af_jessica",  Label="Jessica",   Category="American English", Language="en-us" },
            new() { Id="af_kore",     Label="Kore",      Category="American English", Language="en-us" },
            new() { Id="af_nicole",   Label="Nicole",    Category="American English", Language="en-us" },
            new() { Id="af_nova",     Label="Nova",      Category="American English", Language="en-us" },
            new() { Id="af_river",    Label="River",     Category="American English", Language="en-us" },
            new() { Id="af_sarah",    Label="Sarah",     Category="American English", Language="en-us" },
            new() { Id="af_sky",      Label="Sky",       Category="American English", Language="en-us" },
            new() { Id="am_adam",     Label="Adam",      Category="American English", Language="en-us" },
            new() { Id="am_echo",     Label="Echo",      Category="American English", Language="en-us" },
            new() { Id="am_eric",     Label="Eric",      Category="American English", Language="en-us" },
            new() { Id="am_fenrir",   Label="Fenrir",    Category="American English", Language="en-us" },
            new() { Id="am_liam",     Label="Liam",      Category="American English", Language="en-us" },
            new() { Id="am_michael",  Label="Michael",   Category="American English", Language="en-us" },
            new() { Id="am_onyx",     Label="Onyx",      Category="American English", Language="en-us" },
            new() { Id="am_puck",     Label="Puck",      Category="American English", Language="en-us" },
            new() { Id="am_santa",    Label="Santa",     Category="American English", Language="en-us" },
            // British English
            new() { Id="bf_alice",    Label="Alice",     Category="British English",  Language="en-gb" },
            new() { Id="bf_emma",     Label="Emma",      Category="British English",  Language="en-gb" },
            new() { Id="bf_isabella", Label="Isabella",  Category="British English",  Language="en-gb" },
            new() { Id="bf_lily",     Label="Lily",      Category="British English",  Language="en-gb" },
            new() { Id="bm_daniel",   Label="Daniel",    Category="British English",  Language="en-gb" },
            new() { Id="bm_fable",    Label="Fable",     Category="British English",  Language="en-gb" },
            new() { Id="bm_george",   Label="George",    Category="British English",  Language="en-gb" },
            new() { Id="bm_lewis",    Label="Lewis",     Category="British English",  Language="en-gb" },
            // Japanese
            new() { Id="jf_alpha",    Label="Alpha",     Category="Japanese",         Language="ja" },
            new() { Id="jf_gongitsune",Label="Gongitsune",Category="Japanese",        Language="ja" },
            new() { Id="jf_nezuko",   Label="Nezuko",    Category="Japanese",         Language="ja" },
            new() { Id="jf_tebukuro", Label="Tebukuro",  Category="Japanese",         Language="ja" },
            new() { Id="jm_kumo",     Label="Kumo",      Category="Japanese",         Language="ja" },
            // Mandarin Chinese
            new() { Id="zf_xiaobei",  Label="Xiaobei",   Category="Mandarin Chinese", Language="zh" },
            new() { Id="zf_xiaoni",   Label="Xiaoni",    Category="Mandarin Chinese", Language="zh" },
            new() { Id="zf_xiaoxiao", Label="Xiaoxiao",  Category="Mandarin Chinese", Language="zh" },
            new() { Id="zf_xiaoyi",   Label="Xiaoyi",    Category="Mandarin Chinese", Language="zh" },
            new() { Id="zm_yunjian",  Label="Yunjian",   Category="Mandarin Chinese", Language="zh" },
            new() { Id="zm_yunxi",    Label="Yunxi",     Category="Mandarin Chinese", Language="zh" },
            new() { Id="zm_yunxia",   Label="Yunxia",    Category="Mandarin Chinese", Language="zh" },
            new() { Id="zm_yunyang",  Label="Yunyang",   Category="Mandarin Chinese", Language="zh" },
            // Spanish
            new() { Id="ef_dora",     Label="Dora",      Category="Spanish",          Language="es" },
            new() { Id="em_alex",     Label="Alex",      Category="Spanish",          Language="es" },
            new() { Id="em_santa",    Label="Santa",     Category="Spanish",          Language="es" },
            // French
            new() { Id="ff_siwis",    Label="Siwis",     Category="French",           Language="fr" },
            // Hindi
            new() { Id="hf_alpha",    Label="Alpha",     Category="Hindi",            Language="hi" },
            new() { Id="hf_beta",     Label="Beta",      Category="Hindi",            Language="hi" },
            new() { Id="hm_omega",    Label="Omega",     Category="Hindi",            Language="hi" },
            new() { Id="hm_psi",      Label="Psi",       Category="Hindi",            Language="hi" },
            // Italian
            new() { Id="if_sara",     Label="Sara",      Category="Italian",          Language="it" },
            new() { Id="im_nicola",   Label="Nicola",    Category="Italian",          Language="it" },
            // Brazilian Portuguese
            new() { Id="pf_dora",     Label="Dora",      Category="Brazilian Portuguese", Language="pt-br" },
            new() { Id="pm_alex",     Label="Alex",      Category="Brazilian Portuguese", Language="pt-br" },
            new() { Id="pm_santa",    Label="Santa",     Category="Brazilian Portuguese", Language="pt-br" },
            // Qwen3-TTS CustomVoice
            new() { Id="qwen_aiden",    Label="Aiden",    Category="Qwen3-TTS", Language="en", Engine="qwen" },
            new() { Id="qwen_dylan",    Label="Dylan",    Category="Qwen3-TTS", Language="en", Engine="qwen" },
            new() { Id="qwen_eric",     Label="Eric",     Category="Qwen3-TTS", Language="en", Engine="qwen" },
            new() { Id="qwen_ono_anna", Label="Ono Anna", Category="Qwen3-TTS", Language="en", Engine="qwen" },
            new() { Id="qwen_ryan",     Label="Ryan",     Category="Qwen3-TTS", Language="en", Engine="qwen" },
            new() { Id="qwen_serena",   Label="Serena",   Category="Qwen3-TTS", Language="en", Engine="qwen" },
            new() { Id="qwen_sohee",    Label="Sohee",    Category="Qwen3-TTS", Language="en", Engine="qwen" },
            new() { Id="qwen_uncle_fu", Label="Uncle Fu", Category="Qwen3-TTS", Language="en", Engine="qwen" },
            new() { Id="qwen_vivian",   Label="Vivian",   Category="Qwen3-TTS", Language="en", Engine="qwen" },
        };

        private readonly HttpClient _http;
        private HashSet<string> _visibleVoices = new();
        private bool _disposed;

        public VoiceManagerService()
        {
            _http = new HttpClient();
            _http.Timeout = TimeSpan.FromMinutes(10);
            _http.DefaultRequestHeaders.Add("User-Agent", "NarraVoice/1.0");
            LoadPreferences();
        }

        private string PrefsPath =>
            Path.Combine(AppConfig.AppDir, "source", PrefsFileName);

        private void LoadPreferences()
         {
            _visibleVoices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (!File.Exists(PrefsPath))
            {
                // Default: show all English voices
                foreach (var v in AllVoices.Where(v =>
                    v.Language == "en-us" || v.Language == "en-gb"))
                    _visibleVoices.Add(v.Id);
                return;
            }

            try
            {
                string json = File.ReadAllText(PrefsPath, Encoding.UTF8);
                var node = JsonNode.Parse(json);
                if (node?["visible_voices"] is JsonArray arr)
                    foreach (var item in arr)
                        if (item?.GetValue<string>() is string id)
                            _visibleVoices.Add(id);
            }
            catch
            {
                foreach (var v in AllVoices.Where(v =>
                    v.Language == "en-us" || v.Language == "en-gb"))
                    _visibleVoices.Add(v.Id);
            }
        }

        public void SavePreferences(IEnumerable<string> visibleVoiceIds)
        {
            _visibleVoices = new HashSet<string>(
                visibleVoiceIds, StringComparer.OrdinalIgnoreCase);

            var arr = new JsonArray();
            foreach (var id in _visibleVoices)
                arr.Add(id);

            var obj = new JsonObject { ["visible_voices"] = arr };
            var options = new JsonSerializerOptions { WriteIndented = true };
            ProjectManager.WriteJsonAtomic(PrefsPath, obj.ToJsonString());
        }

        public List<VoiceInfo> GetAllVoices()
        {
            var installed = GetInstalledVoiceIds();
            return AllVoices.Select(v => new VoiceInfo
            {
                Id = v.Id,
                Label = v.Label,
                Category = v.Category,
                Language = v.Language,
                Engine = v.Engine,
                IsInstalled = installed.Contains(v.Id),
                IsVisible = _visibleVoices.Contains(v.Id),
            }).ToList();
        }

        public List<(string Id, string Label)> GetAvailableVoices()
        {
            var installed = GetInstalledVoiceIds();
            return AllVoices
                .Where(v => installed.Contains(v.Id) && _visibleVoices.Contains(v.Id))
                .Select(v =>
                {
                    string label = v.Engine == "qwen"
                        ? $"{v.Category} — {v.Label}"
                        : $"{v.Category} — {(v.Id.Length > 1 && v.Id[1] == 'f' ? "Female" : "Male")} — {v.Label}";
                    return (v.Id, label);
                })
                .ToList();
        }

        public HashSet<string> GetInstalledVoiceIds()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string dir = AppConfig.VoicesDir;
            if (Directory.Exists(dir))
            {
                foreach (var f in Directory.GetFiles(dir, "*.bin"))
                    set.Add(Path.GetFileNameWithoutExtension(f));
            }

            // Qwen voices: treat as installed (model presence can be checked later)
            foreach (var v in AllVoices.Where(v => v.Engine == "qwen"))
                set.Add(v.Id);


            return set;
        }

        public bool AllVoicesInstalled =>
            AllVoices
                .Where(v => v.Engine == "kokoro")
                .All(v => GetInstalledVoiceIds().Contains(v.Id));

        public async Task<int> DownloadAllVoicesAsync(
            IProgress<VoiceDownloadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            string voicesDir = AppConfig.VoicesDir;
            Directory.CreateDirectory(voicesDir);

            var installed = GetInstalledVoiceIds();
            var toDownload = AllVoices
                .Where(v => v.Engine == "kokoro" && !installed.Contains(v.Id))
                .ToList();

            int downloaded = 0;
            int total = toDownload.Count;

            for (int i = 0; i < toDownload.Count; i++)
            {
                if (cancellationToken.IsCancellationRequested) break;

                var voice = toDownload[i];
                string outPath = Path.Combine(voicesDir, voice.BinFileName);

                progress?.Report(new VoiceDownloadProgress
                {
                    Current = i + 1,
                    Total = total,
                    VoiceName = voice.Id,
                    Success = false,
                });

                try
                {
                    string url = $"{OnnxRepoBase}/{voice.BinFileName}";
                    string tmpPath = outPath + ".tmp";

                    using var response = await _http.GetAsync(
                        url, HttpCompletionOption.ResponseHeadersRead,
                        cancellationToken);
                    response.EnsureSuccessStatusCode();

                    await using var stream = await response.Content
                        .ReadAsStreamAsync(cancellationToken);
                    await using var file = File.Create(tmpPath);
                    await stream.CopyToAsync(file, cancellationToken);

                    File.Move(tmpPath, outPath, overwrite: true);
                    downloaded++;

                    progress?.Report(new VoiceDownloadProgress
                    {
                        Current = i + 1,
                        Total = total,
                        VoiceName = voice.Id,
                        Success = true,
                    });
                }
                catch (Exception ex)
                {
                    progress?.Report(new VoiceDownloadProgress
                    {
                        Current = i + 1,
                        Total = total,
                        VoiceName = voice.Id,
                        Success = false,
                        Error = ex.Message,
                    });
                    string tmpPath = outPath + ".tmp";
                    try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }
                }
            }

            return downloaded;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _http.Dispose();
        }
    }
}