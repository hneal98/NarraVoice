**NarraVoice — Project Context Document**

Status: Active Development | Updated: August 2026

Solution / source: D: (development tree). Runtime data paths are controlled by AppConfig + config.json beside the executable.

# History & Evolution

NarraVoice grew out of an earlier app, **TTS Narrator** (also referred to as TTS Narration), started in early 2026.

### TTS Narrator (Python)

- **Stack:** Python 3 + PySide6, edge-tts (Microsoft Edge cloud voices), ffmpeg merge, PyInstaller EXE
- **Scope:** Single engine, project → smart-chunk → edit → render MP3 → merge audiobook
- **Features:** Global voice/rate/pitch presets (pipe-delimited strings in narration_config.json), per-project substitutions, preview, batch render, .txt/.docx/.pdf import
- **Docs of record:** TTSNarrator_TechSpec.docx, TTSNarrator_Instructions.docx (Version 1.0)

That product established the core loop still used today: named projects, chunk files, render, merge.

### Transition to NarraVoice (C#)

Goals shifted toward **local** synthesis, stronger narrator control, and a more capable editor:

| Area          | TTS Narrator                      | NarraVoice                                                                          |
| ------------- | --------------------------------- | ----------------------------------------------------------------------------------- |
| Language / UI | Python / PySide6                  | C# / WPF (.NET 8)                                                                   |
| TTS           | edge-tts only (cloud)             | Kokoro ONNX (local) + optional Qwen3-TTS (local server)                             |
| Presets       | Global voice\|rate\|pitch strings | Per-project named presets, gutter markers, color, optional blend, optional Instruct |
| Pronunciation | Substitutions only                | Substitutions + Smart IPA (homographs, eSpeak) — Kokoro                             |
| Style control | Rate / pitch only                 | Rate / pitch / volume + Qwen Instruct (session and per-preset)                      |
| Editor        | Single text area                  | SmartTextEditor, preset gutter, silence tags                                        |
| Tooling       | Basic log + player                | Visualizer, Voice Manager, merge tools, multi-story projects                        |

### Name change

**TTS Narrator / TTS Narration** → **NarraVoice** to reflect local narration control (not only "TTS") and a distinct product identity as the feature set moved beyond Edge voices and a single Python GUI.

Experimental work (end-ramp intonation via Signalsmith Stretch, ?+ / .- markers) was explored and later removed; it is not part of the shipping product.

# 1\. Project Overview

NarraVoice is a C# WPF .NET 8 audiobook narration studio. It converts story text to speech using a dual-engine design:

- **Kokoro** (default) — local ONNX model, per-voice .bin files, IPA overrides, voice blending
- **Qwen3-TTS** (optional) — local Python HTTP server, style Instruct text, no IPA markdown

Primary use case: generate audiobooks with narrator control, presets, substitutions, batch render, and MP3 merge.

# 2\. Architecture (high level)

| Layer              | Role                          |
| ------------------ | ----------------------------- |
| NarraVoice (UI)    | Main WPF UI                   |
| Core               | Engine, services, models, IPA |
| Editor             | SmartTextEditor, PresetGutter |

Key types: RenderPipeline, NarraPlayback, AppConfig, QwenServerManager, SubstitutionService, IpaLookupService, ProjectManager.

# 3\. Paths & AppConfig

Paths are centralized in AppConfig. Optional config.json lives next to the executable.

### Layout used on this machine

D:\\Apps\\NarraVoice\\ ← base_dir / app_dir  
config.json  
narration_config.json  
substitutions.json  
voice_preferences.json  
projects\\  
models\\  
kokoro\\  
Kokoro-v1.0.onnx ← main ONNX model  
voices\\ ← individual \*.bin voice files  
Qwen\\  
qwen_server.py ← local HTTP server script  
Qwen3-TTS-1.7B-real\\ ← model weights tree (as installed)  
espeak\\ ← optional full eSpeak NG copy  
source\\ ← .sln / .csproj (development only)

### config.json keys

- base_dir, app_dir, models_dir, voices_dir, projects_dir
- kokoro_model_path — e.g. D:\\Apps\\NarraVoice\\models\\kokoro\\Kokoro-v1.0.onnx
- qwen_server_script — e.g. D:\\Apps\\NarraVoice\\models\\Qwen\\qwen_server.py
- espeak_path — Program Files install or full copy under espeak

- device: cpu | cuda | dml

Call sites for ONNX, Qwen script, and eSpeak should resolve through AppConfig (not fixed D:\\AI_Models or D:\\QwenTTS strings).

# 4\. Voices

### Kokoro

- Individual .bin files (~522KB) from Hugging Face onnx-community/Kokoro-82M-v1.0-ONNX — not a single voices-v1.0.bin
- Stored under VoicesDir (typically models)
- Dropdown labels: Category — Female/Male — Name
- Engine field: kokoro
- Default visible set: en-us and en-gb only (voice_preferences.json)
- Voice Manager: Show All / Hide All / English Only; missing files download when manager opens
- Blended voices via Blend UI

### Qwen3-TTS (optional)

- Voice ids prefix qwen_ — category Qwen3-TTS
- Treated as installed without Kokoro .bin files
- Require Python + local server + model weights
- Labels: Qwen3-TTS — Name; Engine: qwen
- **Instruct…** button visible only for qwen_\* voices (session instruct)
- **Preset Instruct…** button visible only when a named preset using a qwen_\* voice is selected

# 5\. Qwen3 Engine

Qwen is a second synthesis path, not a drop-in for every Kokoro feature.

### Server

- Local HTTP server: <http://127.0.0.1:8765>
- Started via: python {qwen_server_script}
- EnsureRunningAsync reuses process if /health already OK
- GenerateAsync: POST /generate with text, speaker, language=English, optional instruct
- Returns WAV bytes; long HTTP timeout; health wait ~60s
- Shutdown kills the process tree
- Developed/tested on Python 3.13.14; other versions unverified

### What Qwen does not use

- Smart IPA and \[word\](/ipa/) overrides — Kokoro only
- Kokoro voice .bin blending

### Instruct

Short delivery/style notes (pace, energy, mood). Prefer brief tone instructions; long narrative text may be spoken instead of only shaping delivery.

There are **two independent instruct sources**:

| Control                       | Scope                         | Persistence                              |
| ----------------------------- | ----------------------------- | ---------------------------------------- |
| **Instruct…** (next to Voice) | Current chunk / unmarked text | Session only — not saved in project.json |
| **Preset Instruct…**          | Lines marked with that preset | Saved on the preset in project.json      |

**Render priority for a segment**

1. If the line is marked with a preset that has Instruct → use the preset's Instruct

2. Else if session Instruct is set → use session Instruct

3. Else → no instruct

Kokoro voices always ignore instruct.

# 6\. VoiceProfile & Presets

### Live fields

- Voice ID, Rate (+N%), Pitch (+Nst → sample-rate pitch), Volume (N%)
- Engine: qwen if Voice starts with qwen_, else kokoro
- Instruct: optional string for Qwen only (from session or from preset via ProfileFor)

### Removed

Experimental end-of-phrase pitch ramp work (Signalsmith Stretch, ?+ / .- markers, and related VoiceProfile/Preset fields such as Rise/Fall/Formant/Ramp/Duration/Intensity) was removed and is not part of the product.

### Presets

- Named sets of voice / rate / pitch / volume + gutter color
- **Optional Instruct** (Qwen only) — edited via **Preset Instruct…**, stored in project.json
- Optional Voice2/Voice3 + weights; ToKokoroVoice mixes when blended
- Gutter markers in chunk_assignments.json; from line until next marker
- Before first marker = default profile; \__none__ disarms gutter
- **Save Preset** writes voice/rate/pitch/volume/color; existing Instruct on that preset is preserved (change Instruct only via Preset Instruct…)
- **Delete Preset** removes the preset from the project

# 7\. Projects & Chunking

- Projects under ProjectsDir; each folder has project.json
- Setup: chunks/, audio/, temp_stories/, audiobooks/
- Chunking: ~2500 characters at sentence boundaries
- Files: {slug}\_NNNN.txt + {slug}\_NNNN.txt.orig (orig never overwritten)
- Save rotates .001.bak–.003.bak; Restore Chunk uses .orig
- Multi-story: story_files\[\], current_story_index; after audiobook complete, next story can auto-chunk
- Atomic JSON writes for project.json, assignments, narration_config, prefs
- LastProject in narration_config.json for reopen on launch
- Preset entries in project.json may include an "instruct" string field

# 8\. Render Pipeline

- Default engine Kokoro; if profile.Engine == qwen → SynthQwenAsync
- Pitch via sample-rate adjustment (Kokoro native and Qwen both 24000 Hz; non-zero pitch changes effective rate, e.g. +1.3st ≈ 25796 Hz)
- Volume multiplier when profile volume ≠ 100%
- Substitutions applied before synthesis (split punctuation → apply subs → tokenize)
- Quote characters stripped (phantom-sound prevention)
- Silence: &lt;sil:Nms&gt; tags; blank lines → ~400ms pause; single newlines → spaces
- Preset gutter: SplitByPresetChanges → per-segment synthesis
- Per-segment VoiceProfile from ProfileFor (preset voice/rate/pitch/volume/instruct, else chunk defaults)
- **Multi-preset concat:** each segment is saved at its real rate, then resampled to **24000 Hz** when needed (e.g. WdlResamplingSampleProvider) before samples are joined. Final buffer is always labeled KokoroSampleRate (24000). Live preview uses 24000 after resample so it matches the saved file.
- Preview/chunk → WAV; merge → single MP3
- Cancel: KokoroJob.Cancel + stop playback (current ONNX segment may finish first)
- Preview segment timings stored for Visualizer

# 9\. Substitutions & Smart IPA

- Global substitutions.json under AppDir; whole-word, longest-first
- Smart IPA: HomographDictionary (EN) then eSpeak NG; insert \[word\](/ipa/)
- Kokoro symbol fixes: r→ɹ, g→ɡ, stress mark placement
- eSpeak via AppConfig.EspeakPath
- Do not use substitution + inline IPA on the same word
- Smart IPA / IPA markdown: Kokoro only — not for Qwen

# 10\. Visualizer

- Waveform, YIN pitch (Hz), RMS energy
- Segment navigation from preview boundaries
- Alignment sliders for word labels if needed

# 11\. Playback

- In-render preview: NarraPlayback (in-memory)
- AudioPlayerService: file load/play/pause/stop/unload + SaveToMp3
- Unload before overwrite to avoid file locks

# 12\. Known Limitations

- Kokoro prosody limited — most voices flat on questions; prefer bf_alice / af_jessica or blend
- Pitch slider = overall pitch, not intonation contour
- ONNX warmup can click at start of short segments
- Cancel not instant during ONNX
- Qwen needs Python + server + disk for models
- Session Instruct is not persisted across app restarts (by design)
- Resampling pitched Kokoro segments to 24000 Hz for multi-preset mixes is lossy in theory; in practice fine for speech (fixed Aug 2026 — no longer a silent mislabel bug)

# 13\. Dependencies (summary)

- KokoroSharp / KokoroOnnx — TTS
- NAudio / NAudio.Lame — play + MP3
- AvalonEdit, ScottPlot — editor / visualizer
- Python 3.13.14 — Qwen server only
- eSpeak NG — Smart IPA fallback

# 14\. Working with this codebase

- Ask for current file before large edits — code moves fast
- Do not put Apply() before SplitTextOnPunctuation
- NarraPlayer is deleted — use NarraPlayback only
- Wire ONNX / Qwen script / eSpeak through AppConfig when touching paths
- User Guide documents live features only
- Session Instruct (\_currentInstruct) vs preset Instruct must stay separate; do not copy session instruct into presets on Save Preset