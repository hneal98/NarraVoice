// SubstitutionService.cs
// Global pronunciation substitution management for NarraVoice.
//
// Substitutions are stored in a single global file:
//   {AppDir}/substitutions.json
//
// They apply to ALL projects — no project-level substitutions.
// This makes sense because substitutions fix Kokoro's pronunciation
// quirks which are engine-wide, not story-specific.
//
// Format:
// {
//   "exhaust pipe": "exhaustpipe",
//   "lightning Rod Ranch": "lightningrodrannch"
// }
//
// Applied before rendering: original text → substituted text → Kokoro

using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NarraVoice.Core.Config;

namespace NarraVoice.Core.Services
{
    /// <summary>
    /// Manages global pronunciation substitutions for NarraVoice.
    /// Substitutions are applied to all projects before rendering.
    /// </summary>
    public sealed class SubstitutionService
    {
        // ── State ─────────────────────────────────────────────────────────────

        private Dictionary<string, string> _substitutions = new();
        private readonly string _filePath;

        // ── Constructor ───────────────────────────────────────────────────────

        public SubstitutionService()
        {
            _filePath = Path.Combine(AppConfig.AppDir, "substitutions.json");
            Load();
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// All current substitutions as a read-only view.
        /// Key = original text, Value = replacement text.
        /// </summary>
        public IReadOnlyDictionary<string, string> Substitutions =>
            _substitutions;

        /// <summary>
        /// Apply all substitutions to the given text.
        /// Substitutions are applied in order of descending key length
        /// so longer phrases are matched before shorter ones.
        /// </summary>
        public string Apply(string text)
        {
            if (string.IsNullOrEmpty(text) || _substitutions.Count == 0)
                return text;

            // Sort by key length descending so "exhaust pipe" matches
            // before "pipe" if both exist
            var sorted = new List<KeyValuePair<string, string>>(_substitutions);
            sorted.Sort((a, b) => b.Key.Length.CompareTo(a.Key.Length));

            foreach (var kvp in sorted)
            {
                if (!string.IsNullOrEmpty(kvp.Key))
                    text = System.Text.RegularExpressions.Regex.Replace(
                        text,
                        $@"\b{System.Text.RegularExpressions.Regex.Escape(kvp.Key)}\b",
                        kvp.Value);
            }

            return text;
        }

        /// <summary>
        /// Add or update a substitution.
        /// </summary>
        public void Set(string original, string replacement)
        {
            if (string.IsNullOrWhiteSpace(original))
                return;
            _substitutions[original] = replacement ?? string.Empty;
        }

        /// <summary>
        /// Remove a substitution by its original text.
        /// </summary>
        public void Remove(string original)
        {
            _substitutions.Remove(original);
        }

        /// <summary>
        /// Replace all substitutions with a new set.
        /// </summary>
        public void SetAll(Dictionary<string, string> substitutions)
        {
            _substitutions = new Dictionary<string, string>(substitutions);
        }

        /// <summary>
        /// Save substitutions to disk atomically.
        /// </summary>
        public void Save()
        {
            var obj = new JsonObject();
            foreach (var kvp in _substitutions)
                obj[kvp.Key] = kvp.Value;

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder
                    .UnsafeRelaxedJsonEscaping,
            };

            string json = obj.ToJsonString(options);
            ProjectManager.WriteJsonAtomic(_filePath, json);
        }

        /// <summary>
        /// Reload substitutions from disk.
        /// </summary>
        public void Load()
        {
            _substitutions = new Dictionary<string, string>();

            if (!File.Exists(_filePath))
                return;

            try
            {
                string json = File.ReadAllText(_filePath, Encoding.UTF8);
                var node = JsonNode.Parse(json);
                if (node is JsonObject obj)
                {
                    foreach (var kvp in obj)
                    {
                        if (!string.IsNullOrWhiteSpace(kvp.Key))
                            _substitutions[kvp.Key] =
                                kvp.Value?.GetValue<string>() ?? string.Empty;
                    }
                }
            }
            catch { /* Silently fall back to empty substitutions */ }
        }

        /// <summary>
        /// Import substitutions from a project.json's substitutions dict.
        /// Useful for migrating existing Python version projects.
        /// Merges with existing substitutions — does not replace them.
        /// </summary>
        public void ImportFromProject(Dictionary<string, string> projectSubs)
        {
            foreach (var kvp in projectSubs)
                if (!_substitutions.ContainsKey(kvp.Key))
                    _substitutions[kvp.Key] = kvp.Value;
        }
    }
}

