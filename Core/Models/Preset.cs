// Preset.cs
// Represents a named voice preset — a saved combination of voice, rate,
// pitch, and volume that can be assigned to characters or sections.
// Equivalent to preset entries in project.json's "presets" dict.
//
// Example project.json entry:
// "ChickenMan": {
//     "voice": "am_santa",
//     "rate":  "+18%",
//     "pitch": "+2st",
//     "volume": "100%",
//     "color": "#E27B4A"
// }

using System.Text.Json.Serialization;

namespace NarraVoice.Core.Models
{
    /// <summary>
    /// A named voice preset with an associated display color for the gutter.
    /// </summary>
    public sealed class Preset
    {
        /// <summary>Display name of the preset e.g. "ChickenMan", "NannyGrans".</summary>
        [JsonIgnore]
        public string Name { get; set; } = string.Empty;

        /// <summary>Voice ID e.g. "am_santa".</summary>
        [JsonPropertyName("voice")]
        public string Voice { get; set; } = "af_heart";

        /// <summary>Speaking rate e.g. "+18%".</summary>
        [JsonPropertyName("rate")]
        public string Rate { get; set; } = "+0%";

        /// <summary>Pitch adjustment e.g. "+2st".</summary>
        [JsonPropertyName("pitch")]
        public string Pitch { get; set; } = "+0st";

        /// <summary>Volume adjustment e.g. "100%", "75%", "150%".</summary>
        [JsonPropertyName("volume")]
        public string Volume { get; set; } = "100%";

        /// <summary>Rise semitones for ?+ pitch ramp (default 2.0).</summary>
        [JsonPropertyName("riseSemiTones")]
        public float RiseSemiTones { get; set; } = 2.0f;

        /// <summary>Fall semitones for .- pitch ramp (default 2.0).</summary>
        [JsonPropertyName("fallSemiTones")]
        public float FallSemiTones { get; set; } = 2.0f;

        /// <summary>Ramp duration in milliseconds (default 500).</summary>
        [JsonPropertyName("rampMs")]
        public int RampMs { get; set; } = 500;

        /// <summary>
        /// Hex color for gutter display e.g. "#E27B4A".
        /// Used to visually distinguish presets in the gutter widget.
        /// </summary>
        [JsonPropertyName("color")]
        public string Color { get; set; } = "#808080";

        /// <summary>Second blend voice ID (optional).</summary>
        [JsonPropertyName("voice2")]
        public string Voice2 { get; set; } = string.Empty;

        /// <summary>Second voice weight 0-100 (0 = not used).</summary>
        [JsonPropertyName("voice2Weight")]
        public int Voice2Weight { get; set; } = 0;

        /// <summary>Third blend voice ID (optional).</summary>
        [JsonPropertyName("voice3")]
        public string Voice3 { get; set; } = string.Empty;

        /// <summary>Third voice weight 0-100 (0 = not used).</summary>
        [JsonPropertyName("voice3Weight")]
        public int Voice3Weight { get; set; } = 0;

        /// <summary>Primary voice weight — calculated from remaining percentage.</summary>
        [JsonIgnore]
        public int Voice1Weight => 100 - Voice2Weight - Voice3Weight;

        /// <summary>True if this preset uses voice blending.</summary>
        [JsonIgnore]
        public bool IsBlended => !string.IsNullOrEmpty(Voice2) && Voice2Weight > 0;

        /// <summary>
        /// Optional style/instruct string for Qwen (and similar engines).
        /// Empty = use chunk-level instruct or none. Kokoro ignores this.
        /// </summary>
        [JsonPropertyName("instruct")]
        public string Instruct { get; set; } = string.Empty;

        // ── Constructors ──────────────────────────────────────────────────────

        public Preset() { }

        public Preset(string name, string voice, string rate, string pitch,
            string color, string volume = "100%")
        {
            Name = name;
            Voice = voice;
            Rate = rate;
            Pitch = pitch;
            Color = color;
            Volume = volume;
        }

        // ── Conversion ────────────────────────────────────────────────────────

        /// <summary>
        /// Convert this preset to a VoiceProfile for rendering.
        /// </summary>
        public VoiceProfile ToVoiceProfile()
        {
            var p = new VoiceProfile(Voice, Rate, Pitch, Volume);
            if (!string.IsNullOrWhiteSpace(Instruct))
                p.Instruct = Instruct.Trim();
            return p;
        }

        /// <summary>
        /// Build a blended KokoroVoice from the preset's voice slots and weights.
        /// Falls back to single voice if no blending is configured.
        /// </summary>
        public KokoroSharp.Core.KokoroVoice ToKokoroVoice()
        {
            var v1 = KokoroSharp.KokoroVoiceManager.GetVoice(Voice);

            if (!IsBlended)
                return v1;

            float w1 = Voice1Weight / 100f;

            if (!string.IsNullOrEmpty(Voice3) && Voice3Weight > 0)
            {
                var v2 = KokoroSharp.KokoroVoiceManager.GetVoice(Voice2);
                var v3 = KokoroSharp.KokoroVoiceManager.GetVoice(Voice3);
                float w2 = Voice2Weight / 100f;
                float w3 = Voice3Weight / 100f;
                return KokoroSharp.KokoroVoiceManager.Mix((v1, w1), (v2, w2), (v3, w3));
            }
            else
            {
                var v2 = KokoroSharp.KokoroVoiceManager.GetVoice(Voice2);
                float w2 = Voice2Weight / 100f;
                return KokoroSharp.KokoroVoiceManager.Mix((v1, w1), (v2, w2));
            }
        }

        // ── Display ───────────────────────────────────────────────────────────

        public override string ToString() =>
            $"Preset(name={Name}, voice={Voice}, rate={Rate}, pitch={Pitch}, volume={Volume})";
    }
}