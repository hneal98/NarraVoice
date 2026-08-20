**NarraVoice**

User Guide

NarraVoice is a local text-to-speech narration studio for turning story files into audiobooks. The default engine is Kokoro. An optional Qwen3-TTS engine is available when Python and the Qwen server are set up.

# **Quick Setup**

Use this once when installing or moving NarraVoice on a new machine.

### **1\. Folder layout**

A typical layout on a data drive:

D:\\Apps\\NarraVoice\\

config.json

narration_config.json

substitutions.json

voice_preferences.json

projects\\

models\\kokoro\\Kokoro-v1.0.onnx

models\\kokoro\\voices\\ ← \*.bin voice files

models\\Qwen\\qwen_server.py ← optional

models\\Qwen\\Qwen3-TTS-1.7B-real\\ ← optional model tree

espeak\\ ← optional full eSpeak NG copy

### **2\. config.json (example)**

{

"base_dir": "D:\\\\Apps\\\\NarraVoice",

"app_dir": "D:\\\\Apps\\\\NarraVoice",

"models_dir": "D:\\\\Apps\\\\NarraVoice\\\\models",

"voices_dir": "D:\\\\Apps\\\\NarraVoice\\\\models\\\\kokoro\\\\voices",

"projects_dir": "D:\\\\Apps\\\\NarraVoice\\\\projects",

"kokoro_model_path": "D:\\\\Apps\\\\NarraVoice\\\\models\\\\kokoro\\\\Kokoro-v1.0.onnx",

"qwen_server_script": "D:\\\\Apps\\\\NarraVoice\\\\models\\\\Qwen\\\\qwen_server.py",

"espeak_path": "C:\\\\Program Files\\\\eSpeak NG\\\\espeak-ng.exe",

"device": "cpu"

}

Place config.json next to the NarraVoice executable. Adjust paths if your folders differ. eSpeak can stay in Program Files; only copy the full eSpeak NG folder under espeak\\ if you want it portable.

### **3\. Kokoro voices**

On first use, open Tools → Voice Manager / Download Voices. Missing English voice .bin files can download automatically. Defaults show en-us and en-gb only; use Show All for other languages.

### **4\. Optional: Qwen3**

• Install Python 3.13.14 (version used in development)

• Place qwen_server.py and the model tree under models\\Qwen\\ (or set qwen_server_script)

• First Qwen preview starts the local server at <http://127.0.0.1:8765>

### **5\. First project**

New Project → pick a story file → edit a chunk → Preview. When chunks are rendered after editing, merge produces one MP3 under audiobooks\\ from each chunk mp3.

# **Quick Reference**

| **Task**                     | **How**                                                                            |
| ---------------------------- | ---------------------------------------------------------------------------------- |
| **Add pronunciation**        | Right-click word → Smart IPA (Kokoro)                                              |
| **Insert a pause**           | Silence button, &lt;sil:500ms&gt;, or blank line                                   |
| **Hear current chunk**       | Preview                                                                            |
| **Hear selected text**       | Highlight text to listen to, then click Preview                                    |
| **Replay last preview**      | Play                                                                               |
| **Flat question intonation** | Use bf_alice or af_jessica, or Blend them in                                       |
| **Manage which voices show** | Tools → Voice Manager / Download Voices                                            |
| **Qwen style note**          | Select a Qwen voice then click Instruct… button that appears below voice dropdown. |
| **Phantom sound**            | See Phantom Sounds below                                                           |

# **Getting Started**

## **Creating a Project**

New Project — Start Here, or File → New Project. Select one or more story files (.txt, .docx, or .pdf). Multiple files are treated as a series. NarraVoice chunks the first story and loads it into the editor.

## **Opening a Project**

File → Open Project. The last project often reopens automatically.

## **Where files live**

Projects, models, voices, and app settings follow config.json under your data root (for example D:\\Apps\\NarraVoice).

• File → Open Audio Folder — rendered chunk WAVs

• File → Open Audiobooks Folder — finished MP3s

# **Stories, Chunks, and Audiobooks**

Stories longer than about 2500 characters are split into chunks at sentence boundaries. Each chunk is a text file under chunks/ plus a .orig snapshot from the original split.

• Edit and preview one chunk at a time, or use Resume / Batch Render

• Each rendered chunk becomes a WAV in audio/

• When all chunks of a story are done, Merge builds one MP3 in audiobooks/

• Multi-story: after one story finishes, the next story can be chunked automatically

Restore Chunk returns the text from the original split (.orig), not the last automatic .bak backup.

# **The Editor**

## **Smart IPA (Kokoro)**

Right-click a word → Smart IPA. English words may offer more than one reading; otherwise eSpeak NG is used. You can insert a Kokoro override \[word\](/ipa/) or add the word to the global substitution list.

Do not also keep a global substitution and an inline IPA override on the same word. Smart IPA applies to Kokoro only — not to Qwen voices.

## **Pronunciation Substitutions**

Tools → Pronunciation Substitutions. Global list for all projects. Whole-word matching; longer keys win.

## **Silence**

Silence button, or type &lt;sil:500ms&gt;. A blank line between paragraphs inserts a short pause (~400 ms). Single newlines become spaces.

## **Chunk controls**

• Save Chunk — save edits

• Restore Chunk — back to .orig from first chunking

• Resume — jump to next unrendered chunk

• Batch Render — render remaining chunks without per-chunk review

# **Voice and Presets**

## **Voice settings**

Voice, Rate, Pitch (semitones), Volume. Pitch changes overall pitch; it does not by itself create natural question rise.

## **Recommended Kokoro voices for prosody**

• bf_alice — best overall question rise/fall

• af_jessica — subtler but real uptick

Blend (⚗) can mix up to three Kokoro voices into a custom voice.

## **Voice Manager**

Tools → Download Voices / Voice Manager — choose which voices appear in the dropdown. Missing Kokoro .bin files can download automatically. Defaults to English only. Qwen voices appear under Qwen3-TTS when configured.

## **Presets and gutter**

Save the current voice/rate/pitch/volume as a named preset (each has a color). Arm a preset and click the left margin to start that voice from that line until the next marker. Choose none so the main sliders apply to the whole chunk.

## **Instruct (Qwen voices only)**

NarraVoice can pass a short **instruct** (style direction) to Qwen-based voices. Kokoro voices ignore instruct.

There are two places instruct can come from:

#### Instruct… (next to Voice)

- Applies to the **current chunk** and to any text that is **not** marked with a preset.
- Session only — not saved with the project. Closing the app clears it.
- Use for one-off experiments or a temporary tone on the whole chunk.

#### Preset Instruct…

- Appears only when a **named preset** is selected **and** that preset uses a Qwen voice.
- Opens the same dialog; the text is stored on that preset in project.json.
- Every line marked with that preset uses its instruct when rendering.
- Survives restarts and is the right place for lasting character or scene styles (e.g. "Speak happily", "Speak angrily").

**Priority when rendering a segment**

1. If the line is marked with a preset that has instruct → use the preset's instruct.
2. Otherwise → use the session Instruct… text, if any.
3. Otherwise → no instruct.

### Presets

Presets store a reusable voice setup for gutter markers:

| Setting        | Saved?                                    |
| -------------- | ----------------------------------------- |
| Voice          | Yes                                       |
| Rate           | Yes                                       |
| Pitch          | Yes                                       |
| Volume         | Yes                                       |
| Color (gutter) | Yes                                       |
| Instruct       | Yes (via **Preset Instruct…**, Qwen only) |

**Save** — writes the current Voice / Rate / Pitch / Volume (and color) into the selected preset, or creates a new one. Existing instruct on that preset is kept; change instruct with **Preset Instruct…**.

**Delete** — removes the selected preset from the project.

Mark lines in the editor with a preset so those segments use that preset's voice settings and, for Qwen, its instruct.

# **Optional: Qwen3-TTS**

Qwen voices appear under Qwen3-TTS. Selecting one shows Instruct….

## **Requirements**

• Python (developed and tested on 3.13.14)

• qwen_server.py and model files under models\\Qwen\\ (or path in config)

• First use starts a local server at <http://127.0.0.1:8765>, or reuses one already running

## **Instruct…**

Optional short delivery note: pace, energy, mood. Keep it brief. Long story-like text may be spoken instead of only shaping delivery.

## **What differs from Kokoro**

- Use normal spelling — no \[word\](/ipa/) overrides
- No Kokoro voice-file blend for Qwen speakers
- Pronunciation is largely the model's job. However, you can achieve better pronunciation by spelling words as they sound, not as they're actually spelled
- Use Instruct for style

# **Previewing and Rendering**

• Preview — render current text or selection, play, save preview WAV

• Play / Pause / Stop — control last preview

• Cancel — stops work; Kokoro may finish the current synthesis segment first

• Render Chunk — save chunk WAV and advance

• Batch + merge — full story MP3 in audiobooks/

• Test Pronunciation — try words without changing the editor

• Visualize — waveform, pitch, and energy for the last preview

• Scratchpad — experiment without touching the project

# **Tips and Troubleshooting**

## **Pitch**

About −6 to +6 semitones. Above roughly +5 can sound harsh. Pitch is overall pitch, not a substitute for choosing a voice with better prosody.

## **Phantom sounds**

Usually an empty segment from quotes, runs of punctuation, or too many blank lines. Prefer the Silence button or a single &lt;sil:…&gt; tag.

## **IPA + substitution conflict**

Do not combine a global substitution and an inline \[word\](/ipa/) on the same string.

## **Data location**

Paths come from config.json beside the app. Large models can stay on a data drive. eSpeak NG is required for Smart IPA fallback unless espeak_path points at a full copy under your data folder.

# **Keyboard Shortcuts**

• Alt + underlined letter — many buttons

• Ctrl + Plus/Minus — zoom editor

• Ctrl + Shift + Plus/Minus — zoom UI

• Ctrl + Alt + Plus/Minus — zoom gutter

# **Known Limitations**

• Most Kokoro voices have limited question prosody

• Cancel during synthesis is not instant and sometimes doesn't work at all

• Qwen needs extra install (Python + server + models), also Qwen is much slower using CPU only

• Multi-voice dialogue in one sentence often sounds unnatural — presets work best for emotion with one narrator voice
