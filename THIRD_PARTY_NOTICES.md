# Third-Party Licenses

NarraVoice depends on the following third-party software. NarraVoice's own
source code license is in LICENSE; this file covers everything it uses.

## NuGet Packages

|Package|Version|License|Link|
|-|-|-|-|
|AvalonEdit|6.3.1.120|MIT|https://licenses.nuget.org/MIT|
|DocumentFormat.OpenXml|3.5.1|MIT|https://licenses.nuget.org/MIT|
|KokoroSharp.CPU|0.8.4|MIT|https://licenses.nuget.org/MIT|
|NAudio|2.3.0|MIT|https://licenses.nuget.org/MIT|
|NAudio.Lame|2.1.0|MIT (wrapper); bundles LAME (LGPL)|https://www.nuget.org/packages/NAudio.Lame/2.1.0/License|
|NAudio.WaveFormRenderer|2.0.0|MIT|https://licenses.nuget.org/MIT|
|Ookii.Dialogs.Wpf|5.0.1|BSD-3-Clause|https://licenses.nuget.org/BSD-3-Clause|
|System.Drawing.Common|10.0.11|MIT|https://licenses.nuget.org/MIT|
|Microsoft.ML.OnnxRuntime|1.22.0|MIT|https://www.nuget.org/packages/Microsoft.ML.OnnxRuntime/1.22.0/license|



## External / User-Installed Dependencies

These are **not bundled or redistributed** with NarraVoice. Each is installed
separately by the user and called as an external process or model file.

|Dependency|License|Link|Notes|
|-|-|-|-|
|Kokoro (TTS model)|Apache 2.0|https://huggingface.co/onnx-community/Kokoro-82M-v1.0-ONNX|Local ONNX model, user-downloaded|
|Qwen3-TTS (TTS model)|Apache 2.0|https://github.com/QwenLM/Qwen3-TTS/blob/main/LICENSE|Local model + Python server, user-installed|
|eSpeak NG|GPL-3.0-or-later|https://github.com/espeak-ng/espeak-ng/blob/master/COPYING|Called via external process (espeak-ng.exe); required for Smart IPA. Not bundled.|
|Python|PSF License|https://docs.python.org/3/license.html|Required only for the optional Qwen3-TTS engine. Not bundled.|

## Notes

* NarraVoice does not statically link or redistribute eSpeak NG, Python, or
any AI model weights. These are installed separately by the user and
invoked as external processes or loaded as separate model files.
* NAudio.Lame's C# wrapper is MIT-licensed. It bundles compiled LAME encoder
binaries (libmp3lame32.dll, libmp3lame64.dll) for MP3 encoding. LAME itself
is LGPL-licensed; LGPL permits bundling compiled binaries with closed-source
or open-source applications, provided the library isn't modified and its
own license terms are included (see LAME project: https://lame.sourceforge.io/).

