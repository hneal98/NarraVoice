// VoiceProfile.cs
// Represents the voice settings for a single rendering pass.
// Equivalent to the profile dict used throughout narration_core.py
// e.g. { "voice": "af_heart", "rate": "+7%", "pitch": "+0Hz" }

using System.Text.Json.Serialization;


namespace NarraVoice.Core.Models
{
    /// <summary>
    /// Voice settings for a single rendering pass.
    /// Immutable once created — create a new instance to change settings.
    /// </summary>
    public sealed class VoiceProfile
    {
        /// <summary>Voice ID e.g. "af_heart", "am_santa".</summary>
        [JsonPropertyName("voice")]
        public string Voice { get; init; } = "af_heart";

        /// <summary>Speaking rate e.g. "+0%", "+7%", "-10%".</summary>
        [JsonPropertyName("rate")]
        public string Rate { get; init; } = "+0%";

        /// <summary>Pitch adjustment e.g. "+0st", "+2st", "-3st".</summary>
        [JsonPropertyName("pitch")]
        public string Pitch { get; init; } = "+0st";

        /// <summary>Volume adjustment e.g. "100%", "75%", "150%".</summary>
        [JsonPropertyName("volume")]
        public string Volume { get; init; } = "100%";

        /// <summary>Rise semitones for ?+ pitch ramp.</summary>
        [JsonPropertyName("riseSemiTones")]
        public double RiseSemiTones { get; set; } = 7.0;

        /// <summary>Fall semitones for .- pitch ramp.</summary>
        [JsonPropertyName("fallSemiTones")]
        public float FallSemiTones { get; set; } = 2.0f;

        /// <summary>Ramp duration in milliseconds.</summary>
        [JsonPropertyName("rampMs")]
        public int RampMs { get; set; } = 700;

        /// <summary>Formant counter-shift in semitones, applied opposite to pitch during a ramp.</summary>
        [JsonPropertyName("formantSemitones")]
        public float FormantSemitones { get; set; } = 0.5f;
        
        /// <summary>Engine derived from voice ID prefix: "qwen" if Voice starts with "qwen_", otherwise "kokoro".</summary>
        public string Engine => Voice.StartsWith("qwen_", System.StringComparison.OrdinalIgnoreCase)
            ? "qwen"
            : "kokoro";

        public string? Instruct { get; set; }

        /// <summary>Duration stretch factor applied to the ramp segment (1.0 = no stretch).</summary>
        [JsonPropertyName("durationStretchFactor")]
        public float DurationStretchFactor { get; set; } = 1.05f;

        /// <summary>Intensity/gain multiplier reached by the end of the ramp (1.0 = no boost).</summary>
        [JsonPropertyName("intensityGainAtEnd")]
        public float IntensityGainAtEnd { get; set; } = 1.15f;


        // ── Constructors ──────────────────────────────────────────────────────

        public VoiceProfile() { }

        public VoiceProfile(string voice, string rate, string pitch,
            string volume = "100%", float riseSemiTones = 2.0f,
            float fallSemiTones = 2.0f, int rampMs = 700,
            float formantSemitones = .5f, float durationStretchFactor = 1.05f,
            float intensityGainAtEnd = 1.20f)
        {
            Voice = voice;
            Rate = rate;
            Pitch = pitch;
            Volume = volume;
            RiseSemiTones = riseSemiTones;
            FallSemiTones = fallSemiTones;
            RampMs = rampMs;
            FormantSemitones = formantSemitones;
            DurationStretchFactor = durationStretchFactor;
            IntensityGainAtEnd = intensityGainAtEnd;
        }

        // ── Parsed helpers ────────────────────────────────────────────────────

        /// <summary>
        /// Rate as a float multiplier for Kokoro's speed parameter.
        /// "+7%" → 1.07, "-10%" → 0.90, "+0%" → 1.00
        /// </summary>
        public float SpeedMultiplier
        {
            get
            {
                string s = Rate.Replace("%", "").Trim();
                if (float.TryParse(s, out float pct))
                    return 1.0f + (pct / 100.0f);
                return 1.0f;
            }
        }

        /// <summary>
        /// Pitch as semitones for Rubber Band pitch shifting.
        /// "+2st" → 2.0, "-3st" → -3.0, "+0st" → 0.0
        /// </summary>
        public float PitchSemitones
        {
            get
            {
                string s = Pitch.Replace("st", "").Trim();
                if (float.TryParse(s, out float st))
                    return st;
                return 0.0f;
            }
        }

        /// <summary>
        /// Volume as a float multiplier.
        /// "100%" → 1.0, "50%" → 0.5, "150%" → 1.5
        /// </summary>
        public float VolumeMultiplier
        {
            get
            {
                string s = Volume.Replace("%", "").Trim();
                if (float.TryParse(s, out float pct))
                    return pct / 100.0f;
                return 1.0f;
            }
        }

        /// <summary>True if pitch shifting is needed (non-zero pitch).</summary>
        public bool NeedsPitchShift => PitchSemitones != 0.0f;

        /// <summary>True if volume adjustment is needed.</summary>
        public bool NeedsVolumeAdjust => VolumeMultiplier != 1.0f;

        // ── Default profile ───────────────────────────────────────────────────

        /// <summary>Default neutral voice profile.</summary>
        public static VoiceProfile Default => new("af_heart", "+0%", "+0st", "100%");

        public override string ToString() =>
            $"VoiceProfile(voice={Voice}, rate={Rate}, pitch={Pitch}, volume={Volume})";
    }
}