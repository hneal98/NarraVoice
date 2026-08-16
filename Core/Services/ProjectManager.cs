// ProjectManager.cs
// Project file management for NarraVoice.
// Handles loading/saving project.json, chunk_assignments.json,
// narration_config.json, and backup rotation.
//
// Equivalent to the project management functions in narration_gui.py:
//   load_project(), save_project(), get_project_dirs(),
//   load_chunk_assignments(), save_chunk_assignments(),
//   write_json_atomic(), rotate_backups()

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NarraVoice.Core.Config;
using NarraVoice.Core.Models;

namespace NarraVoice.Core.Services
{
    /// <summary>
    /// Represents the app-level config (narration_config.json).
    /// Stores the last open project and other app-level settings.
    /// </summary>
    public sealed class NarrationConfig
    {
        public string LastProject { get; set; } = string.Empty;
    }

    /// <summary>
    /// Represents a loaded NarraVoice project (project.json).
    /// </summary>
    public sealed class ProjectConfig
    {
        public string Name { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public string Voice { get; set; } = "af_heart";
        public string Rate { get; set; } = "+0%";
        public string Pitch { get; set; } = "+0st";
        public string Volume { get; set; } = "100%";
        public string Preset { get; set; } = string.Empty;
        public int LastChunk { get; set; } = 0;
        public string Created { get; set; } = string.Empty;
        public int PresetColorIndex { get; set; } = 0;
        public List<string> StoryFiles { get; set; } = new();  // All story files for project
        public int CurrentStoryIndex { get; set; } = 0;         // Which story is active

        /// <summary>Pronunciation substitutions — original → replacement.</summary>
        public Dictionary<string, string> Substitutions { get; set; } = new();

        /// <summary>Named voice presets — name → Preset.</summary>
        public Dictionary<string, Preset> Presets { get; set; } = new();

        /// <summary>Get the default VoiceProfile from project settings.</summary>
        public VoiceProfile ToVoiceProfile() => new(Voice, Rate, Pitch, Volume);

    }

    /// <summary>
    /// Project file management service for NarraVoice.
    /// All file I/O goes through this class.
    /// </summary>
    public static class ProjectManager
    {
        // ── JSON options ──────────────────────────────────────────────────────

        private static readonly JsonSerializerOptions _writeOptions = new()
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        // ── App-level config (narration_config.json) ──────────────────────────

        /// <summary>Load narration_config.json (last project etc.).</summary>
        public static NarrationConfig LoadNarrationConfig()
        {
            string path = AppConfig.NarrationConfigFile;
            if (!File.Exists(path))
                return new NarrationConfig();
            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                return JsonSerializer.Deserialize<NarrationConfig>(json)
                    ?? new NarrationConfig();
            }
            catch { return new NarrationConfig(); }
        }

        /// <summary>Save narration_config.json atomically.</summary>
        public static void SaveNarrationConfig(NarrationConfig cfg)
        {
            WriteJsonAtomic(AppConfig.NarrationConfigFile,
                JsonSerializer.Serialize(cfg, _writeOptions));
        }

        // ── Project config (project.json) ─────────────────────────────────────

        /// <summary>
        /// Load project.json from the given project directory.
        /// Returns a default ProjectConfig if the file doesn't exist.
        /// </summary>
        public static ProjectConfig LoadProject(string projectDir)
        {
            string path = Path.Combine(projectDir, "project.json");
            if (!File.Exists(path))
                return new ProjectConfig();
            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                var cfg = DeserializeProject(json);
                return cfg ?? new ProjectConfig();
            }
            catch { return new ProjectConfig(); }
        }

        /// <summary>Save project.json atomically.</summary>
        public static void SaveProject(string projectDir, ProjectConfig cfg)
        {
            string path = Path.Combine(projectDir, "project.json");
            WriteJsonAtomic(path, SerializeProject(cfg));
        }

        // ── Project discovery ─────────────────────────────────────────────────

        /// <summary>
        /// Return all valid project directories under ProjectsDir,
        /// sorted alphabetically. A valid project has a project.json file.
        /// </summary>
        public static List<string> GetProjectDirs()
        {
            string projectsDir = AppConfig.ProjectsDir;
            if (!Directory.Exists(projectsDir))
                return new List<string>();

            return Directory.GetDirectories(projectsDir)
                .Where(d => File.Exists(Path.Combine(d, "project.json")))
                .OrderBy(d => d)
                .ToList();
        }

        // ── Path helpers ──────────────────────────────────────────────────────

        /// <summary>Path to the chunks subfolder for a project.</summary>
        public static string ChunksDir(string projectDir) =>
            Path.Combine(projectDir, "chunks");

        /// <summary>Path to the audio subfolder for a project.</summary>
        public static string AudioDir(string projectDir) =>
            Path.Combine(projectDir, "audio");

        /// <summary>Path to the temp_stories subfolder for a project.</summary>
        public static string TempStoriesDir(string projectDir) =>
            Path.Combine(projectDir, "temp_stories");

        /// <summary>Path to the audiobooks subfolder for a project.</summary>
        public static string AudiobooksDir(string projectDir) =>
            Path.Combine(projectDir, "audiobooks");

        /// <summary>Path to chunk_assignments.json for a project.</summary>
        public static string ChunkAssignmentsPath(string projectDir) =>
            Path.Combine(projectDir, "chunk_assignments.json");

        // ── Chunk assignments ─────────────────────────────────────────────────

        /// <summary>
        /// Load chunk_assignments.json for a project.
        /// Returns an empty ChunkAssignments if the file doesn't exist.
        /// </summary>
        public static ChunkAssignments LoadChunkAssignments(string projectDir)
        {
            string path = ChunkAssignmentsPath(projectDir);
            if (!File.Exists(path))
                return new ChunkAssignments();
            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                return JsonSerializer.Deserialize<ChunkAssignments>(json)
                    ?? new ChunkAssignments();
            }
            catch { return new ChunkAssignments(); }
        }

        /// <summary>Save chunk_assignments.json atomically.</summary>
        public static void SaveChunkAssignments(string projectDir, ChunkAssignments data)
        {
            string path = ChunkAssignmentsPath(projectDir);
            WriteJsonAtomic(path, JsonSerializer.Serialize(data, _writeOptions));
        }

        // ── Directory setup ───────────────────────────────────────────────────

        /// <summary>
        /// Create all required subdirectories for a new project.
        /// </summary>
        public static void SetupProjectDirectories(string projectDir)
        {
            Directory.CreateDirectory(projectDir);
            Directory.CreateDirectory(ChunksDir(projectDir));
            Directory.CreateDirectory(AudioDir(projectDir));
            Directory.CreateDirectory(TempStoriesDir(projectDir));
            Directory.CreateDirectory(AudiobooksDir(projectDir));
        }

        // ── Slugify ───────────────────────────────────────────────────────────

        /// <summary>
        /// Convert a project name to a filesystem-safe slug.
        /// e.g. "Sparky and the Storms" → "sparkyandthestorms"
        /// </summary>
        public static string Slugify(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "project";

            var sb = new StringBuilder();
            foreach (char c in name.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c))
                    sb.Append(c);
                // Skip spaces, punctuation, etc.
            }
            return sb.Length > 0 ? sb.ToString() : "project";
        }

        // ── Backup rotation ───────────────────────────────────────────────────

        /// <summary>
        /// Rotate backup snapshots of a file.
        /// .002.bak → .003.bak, .001.bak → .002.bak,
        /// then copy current live file to .001.bak.
        /// Keeps the newest <paramref name="count"/> snapshots.
        /// </summary>
        public static void RotateBackups(string path, int count = 3)
        {
            if (!File.Exists(path)) return;
            try
            {
                // Shift older backups backwards
                for (int i = count; i > 1; i--)
                {
                    string src = $"{path}.{i - 1:D3}.bak";
                    string dst = $"{path}.{i:D3}.bak";
                    if (File.Exists(src))
                        File.Move(src, dst, overwrite: true);
                }
                // Snapshot the current file as .001.bak
                File.Copy(path, $"{path}.001.bak", overwrite: true);
            }
            catch { /* Silently ignore backup failures */ }
        }

        // ── Atomic JSON write ─────────────────────────────────────────────────

        /// <summary>
        /// Write JSON to path via temp file + rename so an interrupted
        /// write can't corrupt the live file.
        /// </summary>
        public static void WriteJsonAtomic(string path, string json)
        {
            string tmp = path + ".tmp";
            try
            {
                // Ensure directory exists
                string? dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllText(tmp, json, Encoding.UTF8);
                File.Move(tmp, path, overwrite: true);
            }
            catch
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            }
        }

        // ── Private serialization helpers ─────────────────────────────────────

        /// <summary>
        /// Deserialize project.json — handles the nested Presets dictionary
        /// which has string keys mapping to Preset objects.
        /// </summary>
        private static ProjectConfig? DeserializeProject(string json)
        {
            var node = JsonNode.Parse(json);
            if (node is not JsonObject obj)
                return null;

            var cfg = new ProjectConfig
            {
                Name = obj["name"]?.GetValue<string>() ?? string.Empty,
                Slug = obj["slug"]?.GetValue<string>() ?? string.Empty,
                Voice = obj["voice"]?.GetValue<string>() ?? "af_heart",
                Rate = obj["rate"]?.GetValue<string>() ?? "+0%",
                Pitch = obj["pitch"]?.GetValue<string>() ?? "+0Hz",
                Volume = obj["volume"]?.GetValue<string>() ?? "100%",
                Preset = obj["preset"]?.GetValue<string>() ?? string.Empty,
                LastChunk = obj["last_chunk"]?.GetValue<int>() ?? 0,
                Created = obj["created"]?.GetValue<string>() ?? string.Empty,
                PresetColorIndex = obj["preset_color_index"]?.GetValue<int>() ?? 0,
                CurrentStoryIndex = obj["current_story_index"]?.GetValue<int>() ?? 0,
            };

            // Substitutions
            if (obj["substitutions"] is JsonObject subs)
                foreach (var kvp in subs)
                    cfg.Substitutions[kvp.Key] = kvp.Value?.GetValue<string>() ?? string.Empty;

            // Presets
            if (obj["presets"] is JsonObject presets)
            {
                foreach (var kvp in presets)
                {
                    if (kvp.Value is JsonObject p)
                    {
                        var preset = new Preset
                        {
                            Name = kvp.Key,
                            Voice = p["voice"]?.GetValue<string>() ?? "af_heart",
                            Rate = p["rate"]?.GetValue<string>() ?? "+0%",
                            Pitch = p["pitch"]?.GetValue<string>() ?? "+0st",
                            Volume = p["volume"]?.GetValue<string>() ?? "100%",
                            Color = p["color"]?.GetValue<string>() ?? "#808080",
                            Instruct = p["instruct"]?.GetValue<string>() ?? string.Empty,
                        };
                        cfg.Presets[kvp.Key] = preset;
                    }
                }
            }

            // Story files
            if (obj["story_files"] is JsonArray storyFiles)
            {
                foreach (var file in storyFiles)
                {
                    if (file?.GetValue<string>() is string f)
                        cfg.StoryFiles.Add(f);
                }
            }

            return cfg;
        }

        /// <summary>
        /// Serialize ProjectConfig to JSON matching the Python version's format.
        /// </summary>
        private static string SerializeProject(ProjectConfig cfg)
        {
            var obj = new JsonObject
            {
                ["name"] = cfg.Name,
                ["slug"] = cfg.Slug,
                ["voice"] = cfg.Voice,
                ["rate"] = cfg.Rate,
                ["pitch"] = cfg.Pitch,
                ["volume"] = cfg.Volume,
                ["preset"] = cfg.Preset,
                ["last_chunk"] = cfg.LastChunk,
                ["created"] = cfg.Created,
                ["preset_color_index"] = cfg.PresetColorIndex,
            };
            
            // ... and add story files array before the substitutions:
            var storyFiles = new JsonArray();
            foreach (var file in cfg.StoryFiles)
                storyFiles.Add(file);
            obj["story_files"] = storyFiles;

            // Substitutions
            var subs = new JsonObject();
            foreach (var kvp in cfg.Substitutions)
                subs[kvp.Key] = kvp.Value;
            obj["substitutions"] = subs;

            // Presets
            var presets = new JsonObject();
            foreach (var kvp in cfg.Presets)
            {
                presets[kvp.Key] = new JsonObject
                {
                    ["voice"] = kvp.Value.Voice,
                    ["rate"] = kvp.Value.Rate,
                    ["pitch"] = kvp.Value.Pitch,
                    ["volume"] = kvp.Value.Volume,
                    ["color"] = kvp.Value.Color,
                    ["instruct"] = kvp.Value.Instruct ?? string.Empty,

                };
            }
            obj["presets"] = presets;

            return obj.ToJsonString(_writeOptions);
        }
    }
}