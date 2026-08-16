// ChunkAssignment.cs
// Data models for chunk preset assignments — the gutter marker system.
// Equivalent to the chunk_assignments.json structure in the Python version.
//
// JSON structure:
// {
//   "chunks": {
//     "0005": {
//       "preset_changes": [
//         { "line": 3,  "preset": "ChickenMan" },
//         { "line": 10, "preset": "Santa-neutral" }
//       ]
//     }
//   }
// }

using System.Text.Json.Serialization;

namespace NarraVoice.Core.Models
{
    /// <summary>
    /// A single preset marker — marks a line where a voice change occurs.
    /// From that line onward, the named preset's voice is used until
    /// the next marker or end of chunk.
    /// </summary>
    public sealed class PresetChange
    {
        /// <summary>1-based line number where the voice change starts.</summary>
        [JsonPropertyName("line")]
        public int Line { get; set; }

        /// <summary>Name of the preset to use from this line onward.</summary>
        [JsonPropertyName("preset")]
        public string Preset { get; set; } = string.Empty;

        public PresetChange() { }

        public PresetChange(int line, string preset)
        {
            Line = line;
            Preset = preset;
        }

        public override string ToString() =>
            $"PresetChange(line={Line}, preset={Preset})";
    }

    /// <summary>
    /// Preset changes for a single chunk.
    /// </summary>
    public sealed class ChunkEntry
    {
        /// <summary>Ordered list of preset change markers for this chunk.</summary>
        [JsonPropertyName("preset_changes")]
        public List<PresetChange> PresetChanges { get; set; } = new();
    }

    /// <summary>
    /// Complete chunk assignments for a project.
    /// Maps chunk key (e.g. "0005") to its preset change markers.
    /// Serializes to/from chunk_assignments.json.
    /// </summary>
    public sealed class ChunkAssignments
    {
        /// <summary>
        /// Dictionary of chunk key → ChunkEntry.
        /// Chunk keys are zero-padded 4-digit strings e.g. "0001", "0005".
        /// </summary>
        [JsonPropertyName("chunks")]
        public Dictionary<string, ChunkEntry> Chunks { get; set; } = new();

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Get preset changes for a chunk by its 1-based index.
        /// Returns an empty list if no markers exist for this chunk.
        /// </summary>
        public List<PresetChange> GetPresetChanges(int chunkIndex1Based)
        {
            string key = $"{chunkIndex1Based:D4}";
            return Chunks.TryGetValue(key, out var entry)
                ? entry.PresetChanges
                : new List<PresetChange>();
        }

        /// <summary>
        /// Set preset changes for a chunk by its 1-based index.
        /// Creates the chunk entry if it doesn't exist.
        /// </summary>
        public void SetPresetChanges(int chunkIndex1Based, List<PresetChange> changes)
        {
            string key = $"{chunkIndex1Based:D4}";
            if (!Chunks.ContainsKey(key))
                Chunks[key] = new ChunkEntry();
            Chunks[key].PresetChanges = changes;
        }

        /// <summary>
        /// Add or update a single marker for a chunk.
        /// If a marker already exists at the given line, updates its preset.
        /// </summary>
        public void SetMarker(int chunkIndex1Based, int line, string presetName)
        {
            string key = $"{chunkIndex1Based:D4}";
            if (!Chunks.ContainsKey(key))
                Chunks[key] = new ChunkEntry();

            var changes = Chunks[key].PresetChanges;
            var existing = changes.Find(c => c.Line == line);
            if (existing != null)
                existing.Preset = presetName;
            else
            {
                changes.Add(new PresetChange(line, presetName));
                changes.Sort((a, b) => a.Line.CompareTo(b.Line));
            }
        }

        /// <summary>
        /// Remove the marker at the given line for a chunk.
        /// </summary>
        public void RemoveMarker(int chunkIndex1Based, int line)
        {
            string key = $"{chunkIndex1Based:D4}";
            if (!Chunks.TryGetValue(key, out var entry))
                return;
            entry.PresetChanges.RemoveAll(c => c.Line == line);
            if (entry.PresetChanges.Count == 0)
                Chunks.Remove(key);
        }
    }
}