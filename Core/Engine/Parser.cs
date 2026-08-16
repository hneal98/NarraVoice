// Parser.cs
// Parses NarraVoice chunk text into a list of Nodes for rendering.
// Equivalent to parse_tree() in narration_engine.py.
//
// Recognized tags:
//   <sil:Nms>        — silence of N milliseconds
//
// Kokoro IPA overrides ([word](/ipa/)) are passed through as-is
// within text nodes — Kokoro's own pipeline handles them natively.
//
// Text normalization (collapsing newlines etc.) happens here
// before the nodes are passed to the rendering pipeline.

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace NarraVoice.Core.Engine
{
    /// <summary>
    /// Parses NarraVoice text into a sequence of renderable Nodes.
    /// </summary>
    public static class Parser
    {
        // ── Token pattern — matches <sil:Nms> tags ────────────────────────────

        private static readonly Regex _silenceToken = new(
            @"<sil:(?<ms>\d+)ms>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase
        );

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Parse chunk text into an ordered list of Nodes.
        /// Normalizes whitespace and newlines before parsing.
        /// </summary>
        /// <param name="text">Raw chunk text from the editor.</param>
        /// <returns>List of Text and Silence nodes in order.</returns>
        public static List<Node> Parse(string text)
        {
            if (string.IsNullOrEmpty(text))
                return new List<Node>();

            // Normalize text before parsing
            string normalized = Normalize(text);

            return ParseNormalized(normalized);
        }

        /// <summary>
        /// Normalize chunk text for rendering:
        ///   - Strip leading/trailing whitespace
        ///   - Collapse Windows line endings to Unix
        ///   - Replace blank lines (double newlines) with a 400ms silence tag
        ///   - Replace remaining single newlines with a space
        /// </summary>
        public static string Normalize(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            // Normalize line endings
            text = text.Replace("\r\n", "\n").Replace("\r", "\n");

            // Strip leading/trailing whitespace
            text = text.Trim();

            // Replace paragraph breaks (blank lines) with silence
            text = text.Replace("\n\n", " <sil:400ms> ");

            // Replace remaining single newlines with space
            text = text.Replace("\n", " ");

            return text;
        }

        // ── Private helpers ───────────────────────────────────────────────────

        /// <summary>
        /// Parse normalized text into nodes by scanning for silence tags.
        /// Text between tags becomes TextNodes; tags become SilenceNodes.
        /// </summary>
        private static List<Node> ParseNormalized(string text)
        {
            var nodes = new List<Node>();
            int pos = 0;

            foreach (Match match in _silenceToken.Matches(text))
            {
                int matchStart = match.Index;
                int matchEnd = match.Index + match.Length;

                // Text before this silence tag
                if (matchStart > pos)
                {
                    string segment = text[pos..matchStart].Trim();
                    if (!string.IsNullOrEmpty(segment))
                        nodes.Add(Node.CreateText(segment));
                }

                // The silence tag itself
                if (int.TryParse(match.Groups["ms"].Value, out int ms) && ms > 0)
                    nodes.Add(Node.CreateSilence(ms));

                pos = matchEnd;
            }

            // Any remaining text after the last tag
            if (pos < text.Length)
            {
                string tail = text[pos..].Trim();
                if (!string.IsNullOrEmpty(tail))
                    nodes.Add(Node.CreateText(tail));
            }

            return nodes;
        }
    }
}
