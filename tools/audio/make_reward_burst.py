"""Synthesises Resources/Audio/reward_burst.wav: rewards bursting out onto the home screen at
the start of their flight to the HUD. A soft low pop under a quick rising sparkle arpeggio;
the per-icon reward_tick arrivals and reward_complete follow it. Fully procedural."""
import os
import wave
import numpy as np
from scipy.signal import fftconvolve, butter, sosfilt

RATE = 48000
LENGTH = .9
rng = np.random.default_rng(5)
n = int(LENGTH * RATE)
out = np.zeros((n, 2))


def place(signal, start, gain, pan):
    i = int(start * RATE)
    k = min(len(signal), n - i)
    out[i:i + k, 0] += signal[:k] * gain * (1 - max(0, pan))
    out[i:i + k, 1] += signal[:k] * gain * (1 + min(0, pan))


# Soft pop: a short falling sine, no click.
m = int(.2 * RATE)
s = np.arange(m) / RATE
freq = 70 + 60 * np.exp(-s / .03)
pop = np.sin(2 * np.pi * np.cumsum(freq) / RATE) * np.exp(-s / .06) * np.minimum(1, s / .004)
place(pop, 0, .55, 0)

# Sparkle: plucked tones climbing a major arpeggio, spread across the stereo field.
m = int(.5 * RATE)
s = np.arange(m) / RATE
for k, (f, pan) in enumerate([(1046.5, -.5), (1318.5, -.2), (1568.0, .1), (2093.0, .35), (2637.0, .6)]):
    pluck = (np.sin(2 * np.pi * f * s) + .3 * np.sin(4 * np.pi * f * s)) * np.exp(-s / .16) * np.minimum(1, s / .002)
    place(pluck, .02 + k * .035, .16, pan)

ir_len = int(.5 * RATE)
ir = sosfilt(butter(2, 5000, fs=RATE, output="sos"), rng.standard_normal(ir_len)) * np.exp(-np.arange(ir_len) / RATE / .12)
ir /= np.sqrt(np.sum(ir ** 2))
for ch in range(2):
    out[:, ch] += fftconvolve(out[:, ch], ir)[:n] * .2
fade = int(.08 * RATE)
out[-fade:] *= np.linspace(1, 0, fade)[:, None]
out /= np.max(np.abs(out)) / .85

path = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", "Assets", "_Project", "Resources", "Audio", "reward_burst.wav"))
with wave.open(path, "wb") as w:
    w.setnchannels(2); w.setsampwidth(2); w.setframerate(RATE)
    w.writeframes((out * 32767).astype("<i2").tobytes())
print("wrote", path)
