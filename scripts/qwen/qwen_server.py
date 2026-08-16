from flask import Flask, request, jsonify, send_file
import torch
from qwen_tts import Qwen3TTSModel
import soundfile as sf
import io
import time

app = Flask(__name__)

MODEL_PATH = r"D:\QwenTTS\Models\models--Qwen--Qwen3-TTS-12Hz-1.7B-CustomVoice\snapshots\0c0e3051f131929182e2c023b9537f8b1c68adfe"

print("Loading Qwen3-TTS model once at startup...")
t0 = time.time()
model = Qwen3TTSModel.from_pretrained(
    MODEL_PATH,
    device_map="cpu",
    dtype=torch.float32,
    local_files_only=True
)
print(f"Model ready in {time.time() - t0:.1f}s. Server listening on port 8765.")

@app.route("/health", methods=["GET"])
def health():
    return "OK", 200

@app.route("/generate", methods=["POST"])
def generate():
    data = request.json
    if not data or "text" not in data or "speaker" not in data:
        return jsonify({"error": "Missing required fields: text, speaker"}), 400
    if len(data["text"]) > 5000:
        return jsonify({"error": "Text too long"}), 400

    try:
        t0 = time.time()
        wavs, sr = model.generate_custom_voice(
            text=data["text"],
            language=data.get("language", "English"),
            speaker=data["speaker"],
            instruct=data.get("instruct") or None,
            max_new_tokens=data.get("max_new_tokens", 4096),
        )
        gen_time = time.time() - t0
        print(f"Generated {len(data['text'])} chars in {gen_time:.1f}s")

        buf = io.BytesIO()
        sf.write(buf, wavs[0], sr, format="WAV")
        buf.seek(0)
        return send_file(buf, mimetype="audio/wav")
    except Exception as e:
        return jsonify({"error": str(e)}), 500

if __name__ == "__main__":
    app.run(host="127.0.0.1", port=8765)