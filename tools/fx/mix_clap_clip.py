"""Builds output/clap-impact/clap-impact.mp4 from the frames and sound list written by
Tools > Push Stars > CV > Record Clap Impact Clip (output/clap-impact/clip/).

events.txt holds one line per sound the game would have started: <seconds> <clip> <volume>.
The fight's music bed runs underneath at the workout level and ducks for the clap cues the
way GameAudio.PlayClip ducks it. Usage: python tools/fx/mix_clap_clip.py"""
import glob
import os
import subprocess
import wave
import numpy as np

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
CLIP = os.path.join(ROOT, "output", "clap-impact", "clip")
AUDIO = os.path.join(ROOT, "Assets", "_Project", "Resources", "Audio")
OUT = os.path.join(ROOT, "output", "clap-impact", "clap-impact.mp4")
RATE, FPS = 48000, 60
MUSIC, MUSIC_VOLUME, MUSIC_FROM = "music_action_groove", 0.15, 20.0
DUCK, DUCK_DOWN, DUCK_UP = 0.4, 1.2, 0.35  # GameAudio.Update: level and volume units per second


def read(name, start=0.0, seconds=None):
    with wave.open(os.path.join(AUDIO, name + ".wav")) as w:
        rate, channels = w.getframerate(), w.getnchannels()
        w.setpos(int(start * rate))
        count = w.getnframes() - int(start * rate) if seconds is None else int(seconds * rate)
        data = np.frombuffer(w.readframes(count), "<i2").astype(np.float32) / 32768
    data = data.reshape(-1, channels)
    if channels == 1:
        data = np.repeat(data, 2, axis=1)
    if rate != RATE:
        index = np.minimum((np.arange(0, len(data), rate / RATE)).astype(int), len(data) - 1)
        data = data[index]
    return data


frames = sorted(glob.glob(os.path.join(CLIP, "f*.jpg")))
length = len(frames) / FPS
events = [line.split() for line in open(os.path.join(CLIP, "events.txt")) if line.strip()]
out = np.zeros((int(length * RATE), 2), dtype=np.float32)

# Music: target level per sample, then slewed like the game slews it.
target = np.full(len(out), MUSIC_VOLUME, dtype=np.float32)
for at, clip, _ in events:
    if clip.startswith("clap_"):
        i = int(float(at) * RATE)
        target[i:i + int(min(len(read(clip)) / RATE, 2.5) * RATE)] = MUSIC_VOLUME * DUCK
level = np.empty_like(target)
current = MUSIC_VOLUME
step = 480  # 10 ms blocks
for i in range(0, len(target), step):
    goal = target[i]
    rate = DUCK_DOWN if goal < current else DUCK_UP
    current += np.clip(goal - current, -rate * step / RATE, rate * step / RATE)
    level[i:i + step] = current
music = read(MUSIC, MUSIC_FROM, length)
out[:len(music)] += music[:len(out)] * level[:len(music), None]

for at, clip, volume in events:
    data = read(clip)
    i = int(float(at) * RATE)
    n = min(len(data), len(out) - i)
    out[i:i + n] += data[:n] * float(volume)

wav = os.path.join(CLIP, "mix.wav")
with wave.open(wav, "wb") as w:
    w.setnchannels(2)
    w.setsampwidth(2)
    w.setframerate(RATE)
    w.writeframes((np.clip(out, -1, 1) * 32767).astype("<i2").tobytes())

subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-framerate", str(FPS),
                "-i", os.path.join(CLIP, "f%05d.jpg"), "-i", wav,
                "-c:v", "libx264", "-crf", "18", "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "192k",
                "-shortest", OUT], check=True)
print("wrote", OUT, f"{len(frames)} frames, {length:.1f}s, peak {np.max(np.abs(out)):.2f}")
