// G2PManager.cs
// Centralized misaki G2P (Grapheme-to-Phoneme) manager for NarraVoice.
// Caches G2P instances per language so initialization only happens once.
//
// Language codes are derived from voice ID prefixes:
//   af_, am_ → 'a' (American English)
//   bf_, bm_ → 'b' (British English)
//   ef_, em_ → 'e' (Spanish)
//   ff_      → 'f' (French)
//   hf_, hm_ → 'h' (Hindi)
//   if_, im_ → 'i' (Italian)
//   jf_, jm_ → 'j' (Japanese)
//   pf_, pm_ → 'p' (Brazilian Portuguese)
//   zf_, zm_ → 'z' (Mandarin Chinese)

using Python;
using Python.Runtime;

namespace NarraVoice.Core.IPA
{
    /// <summary>
    /// Manages cached misaki G2P instances per language.
    /// Thread-safe via Python GIL.
    /// </summary>
    public static class G2PManager
    {
        // ── Cached G2P instances per language code ────────────────────────────

        private static readonly Dictionary<string, dynamic> _cache = new();

        // ── Language code from voice ID ───────────────────────────────────────

        /// <summary>
        /// Get the misaki language code from a voice ID prefix.
        /// e.g. "af_heart" → "a", "jf_alpha" → "j"
        /// </summary>
        public static string LangCodeFromVoice(string voiceId)
        {
            if (string.IsNullOrEmpty(voiceId)) return "a";
            return voiceId[0].ToString().ToLowerInvariant();
        }

        // ── G2P retrieval ─────────────────────────────────────────────────────

        /// <summary>
        /// Get or create a cached G2P instance for the given language code.
        /// </summary>
        public static dynamic GetG2P(string langCode)
        {
            string key = langCode.ToLowerInvariant();

            if (_cache.TryGetValue(key, out dynamic? cached))
                return cached;

            using (Py.GIL())
            {
                dynamic g2p = CreateG2P(key);
                _cache[key] = g2p;
                return g2p;
            }
        }

        /// <summary>
        /// Get G2P for a specific voice ID.
        /// </summary>
        public static dynamic GetG2PForVoice(string voiceId) =>
            GetG2P(LangCodeFromVoice(voiceId));

        // ── G2P factory ───────────────────────────────────────────────────────

        private static dynamic CreateG2P(string langCode)
        {
            // Map single-letter code to misaki module and options
            return langCode switch
            {
                "a" => CreateEnglishG2P(british: false),  // American English
                "b" => CreateEnglishG2P(british: true),   // British English
                "j" => CreateModuleG2P("ja"),              // Japanese
                "z" => CreateModuleG2P("zh"),              // Mandarin Chinese
                "e" => CreateModuleG2P("es"),              // Spanish
                "f" => CreateModuleG2P("fr"),              // French
                "h" => CreateModuleG2P("hi"),              // Hindi
                "i" => CreateModuleG2P("it"),              // Italian
                "p" => CreateModuleG2P("pt"),              // Brazilian Portuguese
                _ => CreateEnglishG2P(british: false),  // Default to American English
            };
        }

        private static dynamic CreateEnglishG2P(bool british)
        {
            dynamic en = Py.Import("misaki.en");
            return en.G2P(trf: false, british: british, fallback: null);
        }

        private static dynamic CreateModuleG2P(string module)
        {
            try
            {
                dynamic mod = Py.Import($"misaki.{module}");
                return mod.G2P();
            }
            catch
            {
                // Fall back to American English if language module not available
                dynamic en = Py.Import("misaki.en");
                return en.G2P(trf: false, british: false, fallback: null);
            }
        }

        /// <summary>
        /// Clear all cached G2P instances.
        /// Call when Python engine is shutting down.
        /// </summary>
        public static void Clear() => _cache.Clear();
    }
}