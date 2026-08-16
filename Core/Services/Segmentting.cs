// SegmentTiming.cs
// Holds the timing data for a single synthesized text segment.
// Used to position words accurately on the waveform visualization.

namespace NarraVoice.Core.Services
{
    public class SegmentTiming
    {
        /// <summary>Original text for this segment.</summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>Start time in seconds within the full audio.</summary>
        public double StartTime { get; set; }

        /// <summary>Duration in seconds of this segment's audio.</summary>
        public double Duration { get; set; }

        /// <summary>Token IDs used to synthesize this segment.</summary>
        public int[] Tokens { get; set; } = Array.Empty<int>();
    }
}