"""Builds an audio-accurate preview video from a play-mode recording.

Input (written by the editor recorder): output/aura-stamp/play/fNNNN_<ms>.png frames and
output/aura-stamp/audio_log.txt with lines
    S <start ms> <clip> <volume> <pitch>        a sound effect the game actually played
    M <ms> <volume> <clip time s> <clip>        music source volume changes
Frames keep their real timestamps (variable frame rate), effects are placed where they were
played, and the music bed follows the logged volume. Usage: python mix_recording.py <out.mp4>"""
import glob
import os
import subprocess
import sys
import wave
import numpy as np

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
REC = os.path.join(ROOT, "output", "aura-stamp")
AUDIO = os.path.join(ROOT, "Assets", "_Project", "Resources", "Audio")
RATE = 48000


def read(name):
    with wave.open(os.path.join(AUDIO, name + ".wav")) as w:
        data = np.frombuffer(w.readframes(w.getnframes()), "<i2").astype(np.float32) / 32768
        data = data.reshape(-1, w.getnchannels())
        if w.getnchannels() == 1:
            data = np.repeat(data, 2, axis=1)
        if w.getframerate() != RATE:
            src = np.arange(len(data)) / w.getframerate()
            dst = np.arange(int(len(data) * RATE / w.getframerate())) / RATE
            data = np.stack([np.interp(dst, src, data[:, c]) for c in range(2)], axis=1)
        return data


def main(out):
    frames = sorted(glob.glob(os.path.join(REC, "play", "*.png")))
    times = [int(os.path.basename(f).split("_")[1][:5]) / 1000 for f in frames]
    t0, t1 = times[0], times[-1] + .05
    with open(os.path.join(REC, "play", "list.txt"), "w") as listing:
        for i, f in enumerate(frames):
            duration = (times[i + 1] if i + 1 < len(frames) else t1) - times[i]
            listing.write(f"file '{os.path.basename(f)}'\nduration {max(duration, .001):.4f}\n")
        listing.write(f"file '{os.path.basename(frames[-1])}'\n")

    mix = np.zeros((int((t1 - t0) * RATE) + RATE, 2), np.float32)
    events, music = [], []
    for line in open(os.path.join(REC, "audio_log.txt")):
        parts = line.replace(",", ".").split()  # the editor may log with a decimal comma
        if parts[0] == "S":
            events.append((int(parts[1]) / 1000, parts[2], float(parts[3]), float(parts[4])))
        elif parts[0] == "M":
            music.append((int(parts[1]) / 1000, float(parts[2]), float(parts[3]), parts[4]))
    for start, clip, volume, pitch in events:
        data = read(clip)
        if abs(pitch - 1) > .005:
            src = np.arange(len(data))
            dst = np.arange(0, len(data), pitch)
            data = np.stack([np.interp(dst, src, data[:, c]) for c in range(2)], axis=1)
        i = int((start - t0) * RATE)
        if i < 0:
            data, i = data[-i:], 0
        k = min(len(data), len(mix) - i)
        mix[i:i + k] += data[:k] * volume
    if music:
        track = read(music[0][3])
        offset = int(music[0][2] * RATE) - int((music[0][0] - t0) * RATE)
        idx = (np.arange(len(mix)) + offset) % len(track)
        keys_t = np.array([(m[0] - t0) for m in music])
        keys_v = np.array([m[1] for m in music])
        gain = np.interp(np.arange(len(mix)) / RATE, keys_t, keys_v)
        mix += track[idx] * gain[:, None]
    peak = np.max(np.abs(mix))
    if peak > .98:
        mix *= .98 / peak
    wav = os.path.join(REC, "play", "mix.wav")
    with wave.open(wav, "wb") as w:
        w.setnchannels(2); w.setsampwidth(2); w.setframerate(RATE)
        w.writeframes((mix * 32767).astype("<i2").tobytes())
    subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-f", "concat", "-safe", "0", "-i",
                    os.path.join(REC, "play", "list.txt"), "-i", wav, "-vf", "scale=540:-2,format=yuv420p",
                    "-fps_mode", "vfr", "-c:v", "libx264", "-crf", "20", "-c:a", "aac", "-b:a", "192k",
                    "-shortest", out], check=True)
    print("wrote", out, f"{len(events)} sounds")


if __name__ == "__main__":
    main(sys.argv[1])
