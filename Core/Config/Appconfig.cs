// AppConfig.cs
// Centralized path and configuration management for NarraVoice.
//
// Default data root: %LocalAppData%\NarraVoice
// Optional: config.json beside the executable overrides paths (e.g. move data to D:\).
//
// config.json example:
// {
//     "base_dir":           "D:\\NarraVoice",
//     "models_dir":         "D:\\NarraVoice\\models",
//     "voices_dir":         "D:\\NarraVoice\\voices",
//     "projects_dir":       "D:\\NarraVoice\\projects",
//     "kokoro_model_path":  "D:\\NarraVoice\\models\\kokoro\\Kokoro-v1.0.onnx",
//     "qwen_server_script": "D:\\NarraVoice\\models\\qwen\\qwen_server.py",
//     "espeak_path":        "D:\\NarraVoice\\espeak\\espeak-ng.exe",
//     "device":             "cpu"
// }

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NarraVoice.Core.Config
{
    public static class AppConfig
    {
        private static readonly string _exeDir =
            AppDomain.CurrentDomain.BaseDirectory.TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        private static readonly string _configPath =
            Path.Combine(_exeDir, "config.json");

        private static readonly Dictionary<string, string> _cfg;

        static AppConfig()
        {
            _cfg = LoadConfig();
        }

        // ── Default data root (Windows user AppData) ─────────────────────────

        /// <summary>
        /// Default root when no config.json override is present.
        /// %LocalAppData%\NarraVoice
        /// </summary>
        public static string DefaultDataRoot =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NarraVoice");

        /// <summary>
        /// Active data root: config base_dir if set, otherwise DefaultDataRoot.
        /// </summary>
        public static string BaseDir =>
            Get("base_dir", DefaultDataRoot);

        /// <summary>
        /// App-level files (narration_config, substitutions, voice_preferences).
        /// Defaults to BaseDir (same tree as projects/models).
        /// </summary>
        public static string AppDir =>
            Get("app_dir", BaseDir);

        public static string VoicesDir =>
            Get("voices_dir", Path.Combine(BaseDir, "voices"));

        public static string ModelsDir =>
            Get("models_dir", Path.Combine(BaseDir, "models"));

        public static string ProjectsDir =>
            Get("projects_dir", Path.Combine(BaseDir, "projects"));

        public static string BackupsDir =>
            Get("backups_dir", Path.Combine(BaseDir, "backups"));

        public static string NarrationConfigFile =>
            Path.Combine(AppDir, "narration_config.json");

        //public static string SubstitutionsFile =>
        //    Path.Combine(AppDir, "substitutions.json");

        /// <summary>Kokoro ONNX model. Default under ModelsDir/kokoro/.</summary>
        public static string KokoroModelPath =>
            Get("kokoro_model_path",
                Path.Combine(ModelsDir, "kokoro", "Kokoro-v1.0.onnx"));

        /// <summary>
        /// Qwen server script. Empty string means Qwen is not configured.
        /// Default under ModelsDir/qwen/ if that file exists later; path is still returned.
        /// </summary>
        public static string QwenServerScript =>
            Get("qwen_server_script",
                Path.Combine(ModelsDir, "qwen", "qwen_server.py"));

        /// <summary>
        /// espeak-ng.exe for Smart IPA fallback.
        /// Tries config, then data tree, then common install path.
        /// </summary>
        public static string EspeakPath
        {
            get
            {
                string configured = Get("espeak_path", "");
                if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
                    return configured;

                string underData = Path.Combine(BaseDir, "espeak", "espeak-ng.exe");
                if (File.Exists(underData))
                    return underData;

                string programFiles = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "eSpeak NG", "espeak-ng.exe");
                if (File.Exists(programFiles))
                    return programFiles;

                // Return preferred data path even if missing (caller can check File.Exists)
                return string.IsNullOrWhiteSpace(configured) ? underData : configured;
            }
        }

        public static string Device =>
            Get("device", "cpu");

        public static bool IsConfigured =>
            File.Exists(_configPath) || Directory.Exists(DefaultDataRoot);

        public static Dictionary<string, string> GetAll() =>
            new Dictionary<string, string>(_cfg);

        /// <summary>
        /// Default layout under a chosen root (e.g. D:\NarraVoice or LocalAppData).
        /// </summary>
        public static Dictionary<string, string> CreateDefaultConfig(string baseDir)
        {
            baseDir = Path.GetFullPath(baseDir);
            return new Dictionary<string, string>
            {
                ["base_dir"] = baseDir,
                ["app_dir"] = baseDir,
                ["models_dir"] = Path.Combine(baseDir, "models"),
                ["voices_dir"] = Path.Combine(baseDir, "voices"),
                ["projects_dir"] = Path.Combine(baseDir, "projects"),
                ["backups_dir"] = Path.Combine(baseDir, "backups"),
                ["kokoro_model_path"] = Path.Combine(baseDir, "models", "kokoro", "Kokoro-v1.0.onnx"),
                ["qwen_server_script"] = Path.Combine(baseDir, "models", "qwen", "qwen_server.py"),
                ["espeak_path"] = Path.Combine(baseDir, "espeak", "espeak-ng.exe"),
                ["device"] = "cpu",
            };
        }

        public static void SaveConfig(Dictionary<string, string> cfg)
        {
            string tmp = _configPath + ".tmp";
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(cfg, options);
                File.WriteAllText(tmp, json, System.Text.Encoding.UTF8);
                File.Move(tmp, _configPath, overwrite: true);
            }
            catch
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            }
        }

        public static void SetupDirectories()
        {
            foreach (var path in new[]
                     {
                         AppDir, ModelsDir, VoicesDir, ProjectsDir, BackupsDir,
                         Path.Combine(ModelsDir, "kokoro"),
                         Path.Combine(ModelsDir, "qwen"),
                     })
            {
                if (!string.IsNullOrWhiteSpace(path))
                    Directory.CreateDirectory(path);
            }
        }

        private static Dictionary<string, string> LoadConfig()
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(_configPath))
                return result;
            try
            {
                string json = File.ReadAllText(_configPath, System.Text.Encoding.UTF8);
                var node = JsonNode.Parse(json);
                if (node is JsonObject obj)
                {
                    foreach (var kvp in obj)
                    {
                        if (kvp.Value is not null)
                            result[kvp.Key] = kvp.Value.ToString() ?? "";
                    }
                }
            }
            catch
            {
                // Fall back to defaults
            }
            return result;
        }

        private static string Get(string key, string fallback)
        {
            if (_cfg.TryGetValue(key, out string? value) &&
                !string.IsNullOrWhiteSpace(value))
                return value;
            return fallback;
        }
    }
}