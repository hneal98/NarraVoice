// HomographDictionary.cs
// Curated homograph dictionary for Smart IPA.
// Each entry maps a lowercase word to a list of IpaEntry records,
// each containing the IPA string and a meaning description.
//
// IPA is pre-corrected for Kokoro:
//   - r  → ɹ  (U+0279) — American English r-sound
//   - g  → ɡ  (U+0261) — correct Unicode g
//   - ˈ  placed before the stressed VOWEL, not before the consonant cluster
//
// Words with only one pronunciation are NOT listed here —
// those go through IpaLookupService with automatic Kokoro correction.

using System.Collections.Generic;

namespace NarraVoice.Core.IPA
{
    /// <summary>
    /// A single IPA pronunciation entry with its meaning description.
    /// </summary>
    public record IpaEntry(string Ipa, string Description);

    /// <summary>
    /// Static dictionary of English homographs — words spelled the same
    /// but pronounced differently depending on meaning.
    /// </summary>
    public static class HomographDictionary
    {
        /// <summary>
        /// Maps lowercase word → list of possible pronunciations with descriptions.
        /// </summary>
        public static readonly Dictionary<string, List<IpaEntry>> Homographs =
            new(System.StringComparer.OrdinalIgnoreCase)
            {
                ["dove"] = new()
            {
                new("/dʌv/",  "the bird"),
                new("/doʊv/", "past tense of dive"),
            },
                ["read"] = new()
            {
                new("/ɹiːd/", "present tense — to read a book"),
                new("/ɹɛd/",  "past tense — she read it yesterday"),
            },
                ["wind"] = new()
            {
                new("/wɪnd/", "moving air — the wind blew"),
                new("/waɪnd/","to coil or turn — wind up the clock"),
            },
                ["tear"] = new()
            {
                new("/tɪɹ/",  "from crying — a tear rolled down"),
                new("/tɛɹ/",  "to rip — don't tear the page"),
            },
                ["lead"] = new()
            {
                new("/liːd/", "to guide — lead the way"),
                new("/lɛd/",  "the metal — a lead pipe"),
            },
                ["live"] = new()
            {
                new("/lɪv/",  "to be alive — they live on a ranch"),
                new("/laɪv/", "live performance — a live show"),
            },
                ["close"] = new()
            {
                new("/kloʊz/","to shut — close the door"),
                new("/kloʊs/","nearby — close to home"),
            },
                ["wound"] = new()
            {
                new("/wuːnd/","an injury — a wound on his leg"),
                new("/waʊnd/","past tense of wind — wound the rope"),
            },
                ["bow"] = new()
            {
                new("/boʊ/",  "a weapon or ribbon — bow and arrow"),
                new("/baʊ/",  "to bend forward — take a bow"),
            },
                ["row"] = new()
            {
                new("/ɹoʊ/",  "a line — a row of seats"),
                new("/ɹaʊ/",  "an argument — quite a row they had"),
            },
                ["sow"] = new()
            {
                new("/soʊ/",  "to plant seeds — sow the field"),
                new("/saʊ/",  "a female pig — the old sow"),
            },
                ["bass"] = new()
            {
                new("/beɪs/", "low musical tone — bass guitar"),
                new("/bæs/",  "the fish — caught a bass"),
            },
                ["desert"] = new()
            {
                new("/dɛzəɹt/",  "arid land — the sandy desert"),
                new("/dɪzˈɜɹt/", "to abandon — desert your post"),
            },
                ["refuse"] = new()
            {
                new("/ɹɪfjuːz/", "to decline — I refuse to go"),
                new("/ɹɛfjuːs/", "rubbish — bags of refuse"),
            },
                ["record"] = new()
            {
                new("/ɹɪkˈɔɹd/", "to capture — record the song"),
                new("/ɹɛkəɹd/",  "a document or disc — break the record"),
            },
                ["present"] = new()
            {
                new("/pɹɪzˈɛnt/", "to show — present the award"),
                new("/pɹɛzənt/",  "a gift or now — open your present"),
            },
                ["object"] = new()
            {
                new("/əbdʒˈɛkt/", "to oppose — I object!"),
                new("/ˈɒbdʒɛkt/", "a thing — a strange object"),
            },
                ["subject"] = new()
            {
                new("/səbdʒˈɛkt/", "to expose — subject to the law"),
                new("/sˈʌbdʒɛkt/", "a topic — the subject of the story"),
            },
                ["conduct"] = new()
            {
                new("/kəndˈʌkt/", "to lead — conduct the orchestra"),
                new("/kˈɒndʌkt/", "behavior — good conduct"),
            },
                ["permit"] = new()
            {
                new("/pəɹmˈɪt/", "to allow — permit me to explain"),
                new("/pˈɜɹmɪt/", "a license — a building permit"),
            },
                ["protest"] = new()
            {
                new("/pɹoʊtˈɛst/", "to object — they protest loudly"),
                new("/pɹˈoʊtɛst/", "a demonstration — join the protest"),
            },
                ["rebel"] = new()
            {
                new("/ɹɪbˈɛl/", "to resist — they rebel against rules"),
                new("/ɹˈɛbəl/", "a person who resists — a rebel at heart"),
            },
                ["invalid"] = new()
            {
                new("/ɪnvˈælɪd/", "not valid — an invalid ticket"),
                new("/ˈɪnvəlɪd/", "a sick person — cared for the invalid"),
            },
                ["minute"] = new()
            {
                new("/mˈɪnɪt/",    "60 seconds — wait a minute"),
                new("/maɪnjˈuːt/", "tiny — a minute detail"),
            },
                ["content"] = new()
            {
                new("/kəntˈɛnt/", "satisfied — felt content"),
                new("/kˈɒntɛnt/", "what is inside — the content of the box"),
            },
                ["contract"] = new()
            {
                new("/kəntɹˈækt/", "to shrink or catch — contract a disease"),
                new("/kˈɒntɹækt/", "a legal agreement — sign the contract"),
            },
                ["contrast"] = new()
            {
                new("/kəntɹˈæst/", "to compare — contrast the two"),
                new("/kˈɒntɹæst/", "a difference — a stark contrast"),
            },
                ["convict"] = new()
            {
                new("/kənvˈɪkt/", "to find guilty — convict the thief"),
                new("/kˈɒnvɪkt/", "a prisoner — an escaped convict"),
            },
                ["digest"] = new()
            {
                new("/daɪdʒˈɛst/", "to process food — digest a meal"),
                new("/dˈaɪdʒɛst/", "a summary — a digest of news"),
            },
                ["entrance"] = new()
            {
                new("/ɛntɹˈæns/", "to enchant — the music will entrance you"),
                new("/ˈɛntɹəns/", "a doorway — use the front entrance"),
            },
                ["excuse"] = new()
            {
                new("/ɛkskjˈuːz/", "to forgive — excuse the interruption"),
                new("/ɛkskjˈuːs/", "a reason — a poor excuse"),
            },
                ["impact"] = new()
            {
                new("/ɪmpˈækt/", "to affect — it will impact the plan"),
                new("/ˈɪmpækt/", "a collision or effect — the impact was huge"),
            },
                ["increase"] = new()
            {
                new("/ɪnkɹˈiːs/", "to grow — sales will increase"),
                new("/ˈɪnkɹiːs/", "a rise — a pay increase"),
            },
                ["insult"] = new()
            {
                new("/ɪnsˈʌlt/", "to offend — don't insult him"),
                new("/ˈɪnsʌlt/", "an offensive remark — a terrible insult"),
            },
                ["perfect"] = new()
            {
                new("/pəɹfˈɛkt/", "to make flawless — perfect your craft"),
                new("/pˈɜɹfɛkt/", "flawless — a perfect day"),
            },
                ["produce"] = new()
            {
                new("/pɹədˈuːs/", "to make — produce a sound"),
                new("/pɹˈoʊduːs/","fresh food — buy some produce"),
            },
                ["progress"] = new()
            {
                new("/pɹəɡɹˈɛs/", "to advance — we progress slowly"),
                new("/pɹˈoʊɡɹɛs/","advancement — good progress"),
            },
                ["project"] = new()
            {
                new("/pɹədʒˈɛkt/", "to throw forward or display — project your voice"),
                new("/pɹˈɒdʒɛkt/", "a task — a big project"),
            },
                ["reject"] = new()
            {
                new("/ɹɪdʒˈɛkt/", "to refuse — reject the offer"),
                new("/ɹˈiːdʒɛkt/","something discarded — a reject pile"),
            },
                ["survey"] = new()
            {
                new("/səɹvˈeɪ/", "to examine — survey the land"),
                new("/sˈɜɹveɪ/", "a questionnaire — fill out the survey"),
            },
                ["suspect"] = new()
            {
                new("/səspˈɛkt/", "to think guilty — I suspect foul play"),
                new("/sˈʌspɛkt/", "a person under suspicion — the main suspect"),
            },
                ["transfer"] = new()
            {
                new("/tɹænsfˈɜɹ/", "to move — transfer the files"),
                new("/tɹˈænsfɜɹ/", "a move — a bank transfer"),
            },
                ["upset"] = new()
            {
                new("/ʌpsˈɛt/", "to disturb — don't upset him"),
                new("/ˈʌpsɛt/", "distressed — feeling upset"),
            },
                ["export"] = new()
            {
                new("/ɛkspˈɔɹt/", "to send abroad — export the goods"),
                new("/ˈɛkspɔɹt/", "something sent abroad — an export business"),
            },
                ["import"] = new()
            {
                new("/ɪmpˈɔɹt/", "to bring in — import the spices"),
                new("/ˈɪmpɔɹt/", "something brought in — a foreign import"),
            },
                ["moped"] = new()
            {
                new("/moʊpt/",   "felt dejected — she moped all day"),
                new("/moʊpɛd/",  "a motorized bicycle — rode his moped"),
            },
                ["agape"] = new()
            {
                new("/əɡˈeɪp/",  "wide open — mouth agape"),
                new("/ˈæɡəpeɪ/", "Christian love — agape love"),
            },
                ["axes"] = new()
            {
                new("/ˈæksɪz/",  "plural of axe — two axes"),
                new("/ˈæksiːz/", "plural of axis — the x and y axes"),
            },
            };

        /// <summary>
        /// Look up a word in the homograph dictionary.
        /// Returns null if the word is not a known homograph.
        /// </summary>
        public static List<IpaEntry>? Lookup(string word)
        {
            if (string.IsNullOrWhiteSpace(word))
                return null;
            return Homographs.TryGetValue(word.Trim(), out var entries)
                ? entries
                : null;
        }
    }
}
