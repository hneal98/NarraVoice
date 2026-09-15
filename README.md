# NarraVoice

Offline Windows TTS narration studio (Kokoro + optional Qwen3-TTS).  
Not affiliated with narravoice.com.

A local, offline text-to-speech narration studio for turning story files into audiobooks — no cloud APIs, no per-character billing, no subscription.

**Two engines in one app**
- **Kokoro** — fast, fully local, lightweight, and CPU-friendly
- **Qwen3-TTS** — stronger emotional expression and natural intonation (optional)

Use either (or both) in the same project with the same editor, presets, chunking, and export pipeline.

---

## Getting Started

Most people should start with **Kokoro only**. It is the simplest and most reliable way to use NarraVoice.

### Option A — Recommended (Kokoro Only)

This is the best path for most users, especially if you are coming from the book and just want to start narrating.

1. Go to the [Releases](https://github.com/hneal98/NarraVoice/releases) page
2. Download the latest release `.zip`
3. Extract it to a folder (example: `C:\NarraVoice`)
4. Download the Kokoro model and voices (see below)
5. Edit `config.json` so the paths match your folder
6. Run the `.exe`

You do **not** need Qwen3, Python, or any extra setup to use NarraVoice this way.

### Option B — Add Qwen3-TTS Later (Optional)

Qwen3-TTS is better at emotional expression and natural intonation.  
Use it when you want more expressive character voices or richer delivery.

It requires extra setup:
- Python 3.13.x
- The Qwen model files
- The `qwen_server.py` script

It also runs more slowly on computers without a strong GPU.  
You can add it later after you are already comfortable using Kokoro.

### Option C — Build from Source

Only needed if you want to modify the code.

**Prerequisites**
- Windows 10/11 (64-bit)
- .NET SDK
- Visual Studio 2022 or the `dotnet` CLI

**Basic steps**
1. Clone this repository
2. Restore and build the solution
3. Copy the required model files into the output folder
4. Edit `config.json` with correct paths
5. Run the application

---

## Required Models & Dependencies

### Kokoro (Required for normal use)

NarraVoice needs the Kokoro model to generate speech.

1. Place `Kokoro-v1.0.onnx` here:
   ```
   models/kokoro/Kokoro-v1.0.onnx
   ```
2. Place the voice `.bin` files here:
   ```
   models/kokoro/voices/
   ```
3. After the first launch, you can also use **Tools → Voice Manager / Download Voices** to get missing English or other language voices.

### eSpeak NG (Required)

Helps with pronunciation and IPA support.

- Install eSpeak NG normally, **or**
- Copy the full eSpeak NG folder into the `espeak\` folder for a portable setup

### Qwen3-TTS (Optional)

Only needed if you want stronger emotional expression and better intonation.

- Requires Python 3.13.x
- Requires the Qwen model files and `qwen_server.py`
- Skip this completely if you are happy with Kokoro

---

## Quick Setup

### Folder layout

A typical install looks like this:

```text
NarraVoice/
├── NarraVoice.exe
├── config.json
├── narration_config.json
├── substitutions.json
├── voice_preferences.json
├── projects/
├── models/
│   ├── kokoro/
│   │   ├── Kokoro-v1.0.onnx
│   │   └── voices/          ← *.bin voice files
│   └── Qwen/                ← optional
│       ├── qwen_server.py
│       └── Qwen3-TTS-.../
└── espeak/                  ← optional portable eSpeak NG
```

### config.json

Place `config.json` next to the executable and update every path to match your install.

```json
{
  "base_dir": "C:\\Path\\To\\NarraVoice",
  "app_dir": "C:\\Path\\To\\NarraVoice",
  "models_dir": "C:\\Path\\To\\NarraVoice\\models",
  "voices_dir": "C:\\Path\\To\\NarraVoice\\models\\kokoro\\voices",
  "projects_dir": "C:\\Path\\To\\NarraVoice\\projects",
  "kokoro_model_path": "C:\\Path\\To\\NarraVoice\\models\\kokoro\\Kokoro-v1.0.onnx",
  "qwen_server_script": "C:\\Path\\To\\NarraVoice\\models\\Qwen\\qwen_server.py",
  "espeak_path": "C:\\Program Files\\eSpeak NG\\espeak-ng.exe",
  "device": "cpu"
}
```

> **Important:** Most setup problems come from incorrect paths in `config.json`.  
> Make sure every path actually exists on your machine.

### Your first project

1. Click **New Project**
2. Select a `.txt`, `.docx`, or `.pdf` story file
3. Edit a chunk
4. Click **Preview**
5. When all chunks are rendered, click **Merge** to create the final MP3

**Import tip:** NarraVoice splits on sentence boundaries. If a header or unpunctuated line merges with the next line, place your cursor at the break and press **Enter** to split it manually.

---

## Why NarraVoice

- **Fully offline** — both TTS engines run locally; nothing is sent to a cloud API
- **CPU-first** — designed to run well without a dedicated GPU
- **Dual-engine** — mix Kokoro and Qwen3-TTS voices in the same project
- **Per-line voice control** — preset/gutter system lets you assign different voices, pitch, rate, or volume to any stretch of text
- **PDF / DOCX / TXT ingestion** — drop in a story file and it chunks automatically at sentence boundaries
- **Full pipeline** — edit → preview → render → merge into one finished MP3
- **Combine external audio** — can also merge audio files that were not generated inside NarraVoice
- **Built-in visualizer** — waveform, pitch (Hz), and energy (RMS) view
- **Pronunciation control** — Smart IPA overrides for Kokoro + global substitution list for both engines

---

## NarraVoice vs. Cloud TTS

| Feature              | NarraVoice (Local)                          | Cloud TTS (ElevenLabs, OpenAI, etc.)      |
|----------------------|---------------------------------------------|-------------------------------------------|
| **Cost**             | Free — no subscriptions or per-character fees | Per-character billing                     |
| **Privacy**          | Fully private — nothing leaves your PC      | Text sent to third-party servers          |
| **Hardware**         | CPU-optimized                               | Requires internet + server availability   |
| **Dialogue Control** | Per-line presets with gutter colors         | Manual splitting / harder multi-voice work |
| **Fine-Tuning**      | Smart IPA + local dictionaries              | Limited control over names & heteronyms   |

---

## Voice & Preset System

Save a Voice + Rate + Pitch + Volume combination as a named **preset** (each gets a gutter color). Arm a preset in the left margin and it applies from that line until the next marker. This is how you handle multi-character dialogue or shift a single character’s tone mid-scene.

For **Qwen voices only**, a preset can also carry an **Instruct** (a short style note such as “speak angrily”). There is also a separate **session Instruct** for quick experiments that is *not* saved between restarts.

**Render priority for a given line:**
1. Preset Instruct (if present)
2. Session Instruct (if set)
3. No instruct

Kokoro voices ignore Instruct — they only use pitch, rate, and volume.

---

## Pronunciation

- **Kokoro**: Right-click any word → **Smart IPA** (uses a built-in dictionary with eSpeak NG fallback), or add it to the global substitution list.
- **Qwen3**: Does not use IPA. Spell the word the way it should sound (e.g. “reed” for the present tense of “read”).
- Do not combine a global substitution and an inline IPA override on the same word.
- Heteronyms (`read/read`, `lead/lead`, etc.) still need manual overrides. NarraVoice gives you the tools instead of guessing.

---

## System Requirements

| Specification             | Minimum                              | Recommended                          |
|---------------------------|--------------------------------------|--------------------------------------|
| **Operating System**      | Windows 10 (64-bit)                  | Windows 11 (64-bit)                  |
| **Processor**             | Intel Core i5 / AMD Ryzen 5 (4 cores)| Intel Core i7 / AMD Ryzen 7 or better|
| **Memory (RAM)**          | 8 GB                                 | 16 GB or more                        |
| **Storage**               | 2 GB free                            | 5 GB free (SSD preferred)            |
| **Execution**             | Standalone `.exe`                    | Local environment                    |

Cloud runtimes, Linux containers, and macOS are not supported in this standalone desktop version.

---

## Troubleshooting

**Phantom sounds**  
Usually caused by empty segments from stray quotes, runs of punctuation, or too many blank lines. Prefer the Silence button or a `<sil:500ms>` tag instead of blank lines for pacing.

**Pitch**  
Stay roughly within −6 to +6 semitones. Higher values tend to sound harsh. Pitch changes overall pitch only — it is not a substitute for better natural prosody.

**Flat question intonation**  
Most Kokoro voices are limited here. Prefer `bf_alice`, `af_jessica`, or a blend of them.

**Cancel button**  
Stopping an in-progress Kokoro generation is not always instant.

---

## Known Limitations

- Most Kokoro voices have limited question intonation.
- Cancel during synthesis is not always reliable.
- Qwen3 requires a separate Python + server + model install and is slower on CPU.
- Multi-voice dialogue *inside a single sentence* often sounds unnatural. Switch voices at line or paragraph boundaries when possible.
- Session Instruct is intentionally not saved across restarts.
- Heteronyms require manual pronunciation overrides.

---

## Full Documentation

- [User Guide](./docs/NarraVoiceUserGuide.md) — complete editor reference, keyboard shortcuts, and settings
- [Context Document](./docs/NarraVoiceContext.md) — additional technical details

---

## License

NarraVoice is licensed under the MIT License — see [LICENSE](LICENSE).

Third-party packages and user-installed models are covered in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).  

Kokoro and Qwen3-TTS model weights are Apache 2.0 and are **not** redistributed with this project. You must download them separately.
