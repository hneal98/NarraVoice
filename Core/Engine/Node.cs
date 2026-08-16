// Node.cs
// Represents a single parsed unit of text or silence in the narration pipeline.
// Equivalent to the Node class in narration_engine.py.
//
// The parser splits chunk text into a list of Nodes:
//   - TextNode  — a segment of text to be rendered by Kokoro
//   - SilenceNode — a pause of N milliseconds
//
// The rendering pipeline processes each Node in order,
// concatenating the resulting audio samples.

namespace NarraVoice.Core.Engine
{
    /// <summary>
    /// Type of content this node represents.
    /// </summary>
    public enum NodeType
    {
        /// <summary>Text to be rendered by the Kokoro TTS engine.</summary>
        Text,

        /// <summary>A silent pause of a specified duration.</summary>
        Silence,
    }

    /// <summary>
    /// A single parsed unit in the narration pipeline.
    /// Immutable — created by the Parser, consumed by the RenderPipeline.
    /// </summary>
    public sealed class Node
    {
        /// <summary>Type of this node — Text or Silence.</summary>
        public NodeType Type { get; }

        /// <summary>
        /// For Text nodes: the text content to render.
        /// For Silence nodes: empty string.
        /// </summary>
        public string Text { get; }

        /// <summary>
        /// For Silence nodes: duration in milliseconds.
        /// For Text nodes: 0.
        /// </summary>
        public int SilenceMs { get; }

        // ── Constructors ──────────────────────────────────────────────────────

        private Node(NodeType type, string text, int silenceMs)
        {
            Type = type;
            Text = text;
            SilenceMs = silenceMs;
        }

        /// <summary>Create a text node.</summary>
        public static Node CreateText(string text) =>
            new(NodeType.Text, text ?? string.Empty, 0);

        /// <summary>Create a silence node with duration in milliseconds.</summary>
        public static Node CreateSilence(int milliseconds) =>
            new(NodeType.Silence, string.Empty, milliseconds);

        // ── Convenience properties ────────────────────────────────────────────

        /// <summary>True if this is a text node.</summary>
        public bool IsText => Type == NodeType.Text;

        /// <summary>True if this is a silence node.</summary>
        public bool IsSilence => Type == NodeType.Silence;

        // ── Display ───────────────────────────────────────────────────────────

        public override string ToString() =>
            Type == NodeType.Silence
                ? $"Node('silence', {SilenceMs})"
                : $"Node('text', '{(Text.Length > 60 ? Text[..60] + "..." : Text)}')";
    }
}
