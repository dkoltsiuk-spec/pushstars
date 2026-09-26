"""Synthesises Resources/Audio/aura_skull.wav for the skull reveal: a digital glitch stutter,
then a distorted 808 with a phonk-style cowbell when the skull locks in. Fully procedural.
The hit lands at LOCK seconds; AuraStampPresentation.SkullLock - SkullStart must match it."""
import os
import wave
import numpy as np
from scipy.signal import butter, sosfilt

RATE = 48000
LOCK = 0.38
LENGTH = 1.7
rng = np.random.default_rng(11)
out = np.zeros((int(RATE * LENGTH), 2))


def band(signal, low, high, order=4):
    return sosfilt(butter(order, [low, high], btype="band", fs=RATE, output="sos"), signal)


def place(signal, start, gain=1.0, pan=0.0):
    i = int(start * RATE)
    n = min(len(signal), len(out) - i)
    out[i:i + n, 0] += signal[:n] * gain * (1 - max(0.0, pan))
    out[i:i + n, 1] += signal[:n] * gain * (1 + min(0.0, pan))


def crush(signal, hold, bits):
    held = np.repeat(signal[::hold], hold)[: len(signal)]
    steps = 2 ** bits
    return np.round(held * steps) / steps


# Glitch stutter: short crushed slices of noise and square tones with hard gaps.
start = 0.0
for k in range(8):
    length = rng.uniform(.025, .045)
    n = int(length * RATE)
    s = np.arange(n) / RATE
    if k % 3 == 1:
        slice_ = np.sign(np.sin(2 * np.pi * rng.uniform(180, 1300) * s)) * .5
    else:
        slice_ = rng.standard_normal(n) * .45
    slice_ = crush(slice_, int(rng.integers(6, 18)), 4) * np.minimum(1, (n - np.arange(n)) / 60)
    place(slice_, start, .5, rng.uniform(-.6, .6))
    start += length + rng.uniform(.004, .018)
    if start > LOCK - .03:
        break

# Distorted 808 at the lock.
n = int(1.25 * RATE)
s = np.arange(n) / RATE
freq = 46 + 70 * np.exp(-s / .05)
phase = 2 * np.pi * np.cumsum(freq) / RATE
boom = np.tanh(np.sin(phase) * np.exp(-s / .5) * 4) * np.minimum(1, s / .002)
boom += .3 * np.tanh(np.sin(2 * phase) * np.exp(-s / .25) * 3)
place(boom, LOCK, .8)

# Cowbell: two detuned squares through a band-pass, the classic phonk ping.
def cowbell(pitch):
    n = int(.35 * RATE)
    s = np.arange(n) / RATE
    tone = np.sign(np.sin(2 * np.pi * 587 * pitch * s)) + np.sign(np.sin(2 * np.pi * 845 * pitch * s))
    return band(tone, 600, 3200) * np.exp(-s / .085)

place(cowbell(1.0), LOCK, .16, -.2)
place(cowbell(.94), LOCK + .19, .12, .2)

fade = int(.08 * RATE)
out[-fade:] *= np.linspace(1, 0, fade)[:, None]
out /= np.max(np.abs(out)) / .89

path = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", "Assets", "_Project", "Resources", "Audio", "aura_skull.wav"))
with wave.open(path, "wb") as w:
    w.setnchannels(2)
    w.setsampwidth(2)
    w.setframerate(RATE)
    w.writeframes((out * 32767).astype("<i2").tobytes())
print("wrote", path)
