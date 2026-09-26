"""Synthesises the reward-flow exit sounds (Resources/Audio). Both are deep "whooms", never airy:
a low reversed swell rises into a pass at PASS seconds, a sub drop lands there, and low air
trails off across the stereo field while the next screen opens.
  aura_whoosh.wav   — the Aura screen's collect: the skull flies into the camera. PASS sits
                      near the end of the skull's flight in AuraStampPresentation.SampleClaim.
  ui_transition.wav — every other reward screen → next screen seam. Lighter and shorter; PASS
                      matches ScreenTransition's cover time used by RewardScreen (0.3 s).
Fully procedural."""
import os
import wave
import numpy as np
from scipy.signal import butter, sosfilt, fftconvolve

RATE = 48000


def lowpass(signal, cutoff, order=4):
    return sosfilt(butter(order, cutoff, btype="low", fs=RATE, output="sos"), signal)


def band(signal, low, high, order=2):
    return sosfilt(butter(order, [low, high], btype="band", fs=RATE, output="sos"), signal)


def whoom(name, PASS, LENGTH, drop_from, drop_to, drop_decay, sub_gain, air_band, air_gain, seed):
    rng = np.random.default_rng(seed)
    n = int(LENGTH * RATE)
    t = np.arange(n) / RATE
    out = np.zeros((n, 2))

    # 1. Reversed swell: a dark boom tail played backwards, so it sucks in toward the pass.
    m = int(PASS * RATE)
    s = np.arange(m) / RATE
    tail = lowpass(rng.standard_normal(m), 420) * np.exp(-s / .12)
    swell = tail[::-1] * 1.4
    swell += np.sin(2 * np.pi * np.cumsum(38 + 50 * (s / PASS) ** 2) / RATE) * (s / PASS) ** 3 * .5
    out[:m, 0] += swell
    out[:m, 1] += swell * .92

    # 2. Sub drop at the pass, saturated so phone speakers keep it; its octave stays audible.
    m = int(.9 * RATE)
    s = np.arange(m) / RATE
    freq = drop_to + (drop_from - drop_to) * np.exp(-s / .07)
    phase = 2 * np.pi * np.cumsum(freq) / RATE
    drop = np.tanh(np.sin(phase) * np.exp(-s / drop_decay) * 2.6) * np.minimum(1, s / .008)
    drop += .4 * np.sin(2 * phase) * np.exp(-s / (drop_decay * .55))
    i = int(PASS * RATE)
    k = min(m, n - i)
    out[i:i + k, 0] += drop[:k] * sub_gain
    out[i:i + k, 1] += drop[:k] * sub_gain

    # 3. Low air body, panned across the stereo field as it passes.
    air = lowpass(band(rng.standard_normal(n), *air_band), air_band[1] * 1.1)
    env = np.where(t < PASS, (t / PASS) ** 2, np.exp(-(t - PASS) / .22))
    pan = np.clip((t - PASS) * 5, -1, 1)
    out[:, 0] += air * env * air_gain * (1 - .4 * pan)
    out[:, 1] += air * env * air_gain * (1 + .4 * pan)

    # Short dark room so it decays instead of stopping.
    ir = lowpass(rng.standard_normal(int(.6 * RATE)), 900) * np.exp(-np.arange(int(.6 * RATE)) / RATE / .18)
    ir /= np.sqrt(np.sum(ir ** 2))
    for ch in range(2):
        out[:, ch] += fftconvolve(out[:, ch], ir)[:n] * .15

    fade = int(.1 * RATE)
    out[-fade:] *= np.linspace(1, 0, fade)[:, None]
    out /= np.max(np.abs(out)) / .89

    path = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", "Assets", "_Project", "Resources", "Audio", name))
    with wave.open(path, "wb") as w:
        w.setnchannels(2)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes((out * 32767).astype("<i2").tobytes())
    print("wrote", path)


whoom("aura_whoosh.wav", PASS=.34, LENGTH=1.3, drop_from=105, drop_to=34, drop_decay=.32,
      sub_gain=1.0, air_band=(110, 650), air_gain=.55, seed=23)
whoom("ui_transition.wav", PASS=.3, LENGTH=.9, drop_from=130, drop_to=58, drop_decay=.16,
      sub_gain=.55, air_band=(150, 900), air_gain=.7, seed=31)
