using System.Runtime.InteropServices;

namespace NarraVoice.Core.IPA
{
    /// <summary>
    /// Smart IPA lookup using homograph dictionary + eSpeak NG fallback.
    /// </summary>
    public class IpaLookupService
    {
        public string CurrentLangCode { get; set; } = "en";

        public void SetLanguageFromVoice(string voiceId)
        {
            CurrentLangCode = LangCodeFromVoice(voiceId);
        }

        // ── Public API ────────────────────────────────────────────────────────



        public List<IpaEntry> Lookup(string word)
        {
            if (string.IsNullOrWhiteSpace(word))
                return new List<IpaEntry>();

            string key = word.Trim().ToLowerInvariant();

            if (CurrentLangCode.StartsWith("en"))
            {
                var homographEntries = HomographDictionary.Lookup(key);
                if (homographEntries != null && homographEntries.Count > 0)
                {
                    return homographEntries
                        .Select(e => new IpaEntry(
                            ApplyKokoroCorrections(e.Ipa), e.Description))
                        .ToList();
                }
            }

            string? result = LookupViaESpeakNG(word);
            if (!string.IsNullOrEmpty(result))
                return new List<IpaEntry> { new(result, "") };

            return new List<IpaEntry>();
        }

        public string? LookupSingle(string word)
        {
            if (string.IsNullOrWhiteSpace(word))
                return null;
            return LookupViaESpeakNG(word);
        }

        /// <summary>
        /// Format a word + IPA as a Kokoro markdown override: [word](/ipa/)
        /// </summary>
        public static string FormatForInsert(string word, string ipa)
        {
            string cleanIpa = ipa.Trim('/');
            return $"[{word}](/{cleanIpa}/)";
        }

        // ── eSpeak NG lookup ──────────────────────────────────────────────────

        private const string EspeakExe = @"C:\Program Files\eSpeak NG\espeak-ng.exe";

        [DllImport("libespeak-ng.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void espeak_ng_InitializePath([MarshalAs(UnmanagedType.LPStr)] string? path);

        [DllImport("libespeak-ng.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int espeak_ng_Initialize(nint context);

        [DllImport("libespeak-ng.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int espeak_ng_InitializeOutput(int output, int buflength, IntPtr device);

        [DllImport("libespeak-ng.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr espeak_TextToPhonemes(IntPtr text, int textmode, int phonememode);

        private bool _espeakInitialized = false;

        private string? LookupViaESpeakNG(string word)
        {
            try
            {
                var process = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = EspeakExe,
                        Arguments = $"-q --ipa -v {CurrentLangCode} \"{word}\"",
                        StandardOutputEncoding = System.Text.Encoding.UTF8,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();

                string stdout = process.StandardOutput.ReadToEnd();
                if (string.IsNullOrWhiteSpace(stdout)) return null;

                Console.WriteLine($"Raw output hex:");

                foreach (char c in stdout)
                    Console.Write($"{(int)c:X4} ");
                Console.WriteLine();
                string ipa = stdout.Trim();
                string corrected = ApplyKokoroCorrections(ipa);
                return $"/{corrected}/";

            }
            catch (Exception ex)
            {
                Console.WriteLine($"eSpeak error: {ex.Message}");
                return null;
            }
        }

        // ── Helper: Language code mapping ─────────────────────────────────────

        private static string LangCodeFromVoice(string voiceId)
        {
            if (string.IsNullOrEmpty(voiceId))
                return "en";

            string prefix = voiceId.Length >= 1 ? voiceId[0].ToString() : "a";

            return prefix switch
            {
                "a" => "en-US",
                "b" => "en-GB",
                "j" => "ja",
                "z" => "zh",
                "f" => "fr",
                "d" => "de",
                "e" => "es",
                "i" => "it",
                "p" => "pt",
                _ => "en"
            };
        }

        // ── Kokoro IPA corrections ────────────────────────────────────────────

        public static string ApplyKokoroCorrections(string ipa)
        {
            if (string.IsNullOrEmpty(ipa))
                return ipa;

            ipa = ipa.Replace('r', 'ɹ');
            ipa = ipa.Replace('g', 'ɡ');
            ipa = FixStressMark(ipa);

            return ipa;
        }

        private static readonly HashSet<char> _vowels = new()
        {
            'a', 'e', 'i', 'o', 'u', 'æ', 'ɑ', 'ɒ', 'ɔ', 'ə', 'ɛ', 'ɜ', 'ɪ', 'ʊ', 'ʌ', 'ɹ', 'y'
        };

        private static string FixStressMark(string ipa)
        {
            if (!ipa.Contains('ˈ'))
                return ipa;

            var result = new System.Text.StringBuilder();
            int i = 0;
            while (i < ipa.Length)
            {
                char ch = ipa[i];
                if (ch == 'ˈ')
                {
                    var consonants = new System.Text.StringBuilder();
                    int j = i + 1;
                    while (j < ipa.Length &&
                           !_vowels.Contains(ipa[j]) &&
                           !"/([]ˈˌ ".Contains(ipa[j]))
                    {
                        consonants.Append(ipa[j]);
                        j++;
                    }
                    result.Append(consonants);
                    result.Append('ˈ');
                    i = j;
                }
                else
                {
                    result.Append(ch);
                    i++;
                }
            }
            return result.ToString();
        }
    }
}