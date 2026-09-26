"""Synthesises Resources/Audio/aura_stamp.wav: a short riser, then a sub-bass boom with a
crack and a violet shimmer tail. Fully procedural, so there are no third-party samples.
Impact lands at IMPACT seconds; AuraStampPresentation.Impact must match it."""
import os
import wave
import numpy as np
from scipy.signal import butter, sosfilt, fftconvolve

RATE = 48000
IMPACT = 0.16
LENGTH = 1.9
rng = np.random.default_rng(7)
t = np.arange(int(RATE * LENGTH)) / RATE
out = np.zeros((len(t), 2))


def band(signal, low, high, order=4):
    return sosfilt(butter(order, [low, high], btype="band", fs=RATE, output="sos"), signal)


def place(signal, start, gain=1.0, pan=0.0):
    i = int(start * RATE)
    n = min(len(signal), len(t) - i)
    out[i:i + n, 0] += signal[:n] * gain * (1 - max(0.0, pan))
    out[i:i + n, 1] += signal[:n] * gain * (1 + min(0.0, pan))


# Riser: band-passed noise swelling into the hit.
n = int(IMPACT * RATE)
k = np.arange(n) / n
riser = band(rng.standard_normal(n), 900, 7000) * k ** 3 * 0.35
place(riser, 0.0)

# Sub boom: pitch drops 170 Hz -> 44 Hz, soft-clipped for weight on phone speakers.
n = int(1.3 * RATE)
s = np.arange(n) / RATE
freq = 44 + 126 * np.exp(-s / 0.045)
phase = 2 * np.pi * np.cumsum(freq) / RATE
boom = np.sin(phase) * np.exp(-s / 0.42) * np.minimum(1, s / 0.002)
boom = np.tanh(boom * 2.4) / np.tanh(2.4)
# Second harmonic keeps the boom audible where phones cannot reproduce 44 Hz.
boom += 0.35 * np.sin(2 * phase) * np.exp(-s / 0.22)
place(boom, IMPACT, 0.95)

# Transient click and crack.
n = int(0.09 * RATE)
s = np.arange(n) / RATE
click = band(rng.standard_normal(n), 1500, 9000) * np.exp(-s / 0.006)
crack = band(rng.standard_normal(n), 2500, 7500) * np.exp(-s / 0.028)
crack *= 1 + 0.6 * np.sign(np.sin(2 * np.pi * 140 * s))  # gritty crackle
place(click, IMPACT, 0.55)
place(crack, IMPACT + 0.004, 0.22, -0.3)

# Shimmer tail: detuned high partials, slightly spread left/right.
n = int(1.6 * RATE)
s = np.arange(n) / RATE
env = np.minimum(1, s / 0.03) * np.exp(-s / 0.55)
for f, gain, pan in [(1318.5, .05, -.4), (1975.5, .04, .4), (2637.0, .03, -.2), (1320.8, .04, .3)]:
    wobble = 1 + 0.004 * np.sin(2 * np.pi * 5.5 * s)
    place(np.sin(2 * np.pi * f * np.cumsum(wobble) / RATE) * env, IMPACT + 0.02, gain, pan)

# Short synthetic room so the hit decays instead of stopping dead.
ir_len = int(1.1 * RATE)
ir_t = np.arange(ir_len) / RATE
for ch in range(2):
    ir = rng.standard_normal(ir_len) * np.exp(-ir_t / 0.32)
    ir = band(ir, 200, 6000, 2)
    ir /= np.sqrt(np.sum(ir ** 2))
    wet = fftconvolve(out[:, ch], ir)[: len(t)]
    out[:, ch] = out[:, ch] + wet * 0.18

fade = int(0.08 * RATE)
out[-fade:] *= np.linspace(1, 0, fade)[:, None]
out /= np.max(np.abs(out)) / 0.89  # ~ -1 dBFS peak

path = os.path.join(os.path.dirname(__file__), "..", "..", "Assets", "_Project", "Resources", "Audio", "aura_stamp.wav")
with wave.open(os.path.abspath(path), "wb") as w:
    w.setnchannels(2)
    w.setsampwidth(2)
    w.setframerate(RATE)
    w.writeframes((out * 32767).astype("<i2").tobytes())
print("wrote", os.path.abspath(path))
