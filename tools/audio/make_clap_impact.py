"""Synthesises the duel's clap push-up cues into Resources/Audio:
  clap_riser.wav        the hands leave the floor: a short suck of air that cuts off
  clap_impact_1..4.wav  the landing: boom + thunder crack + a marimba note one step up each clap
  clap_max.wav          the fifth clap (Aura cap): the same hit, bigger, with a rolling tail
  clap_rival.wav        the opponent's clap: the hit heard from the other side, duller, no note
Fully procedural, so there are no third-party samples. Every impact lands at sample 0 —
ClapImpactEffect starts the clip on the landing frame. Also writes a listening demo to
output/clap-impact/demo.wav (rep tick, riser, hit; five claps up the ladder)."""
import os
import wave
import numpy as np
from scipy.signal import butter, sosfilt, fftconvolve

RATE = 48000
ROOT = os.path.join(os.path.dirname(__file__), "..", "..")
AUDIO = os.path.abspath(os.path.join(ROOT, "Assets", "_Project", "Resources", "Audio"))
DEMO = os.path.abspath(os.path.join(ROOT, "output", "clap-impact"))
# D minor pentatonic, up from D5: the brand motif's key (D-F-A), one step per clap.
LADDER = [587.33, 698.46, 783.99, 880.00]
MAX_NOTE = 1174.66


def band(signal, low, high, order=4):
    return sosfilt(butter(order, [low, high], btype="band", fs=RATE, output="sos"), signal)


def lowpass(signal, cutoff, order=2):
    return sosfilt(butter(order, cutoff, btype="low", fs=RATE, output="sos"), signal)


class Mix:
    def __init__(self, length):
        self.t = np.arange(int(RATE * length)) / RATE
        self.out = np.zeros((len(self.t), 2))

    def place(self, signal, start, gain=1.0, pan=0.0):
        i = int(start * RATE)
        n = min(len(signal), len(self.t) - i)
        if n <= 0:
            return
        self.out[i:i + n, 0] += signal[:n] * gain * (1 - max(0.0, pan))
        self.out[i:i + n, 1] += signal[:n] * gain * (1 + min(0.0, pan))

    def room(self, rng, decay, wet):
        n = int(decay * 3.5 * RATE)
        s = np.arange(n) / RATE
        for ch in range(2):
            ir = band(rng.standard_normal(n) * np.exp(-s / decay), 220, 6500, 2)
            ir /= np.sqrt(np.sum(ir ** 2))
            self.out[:, ch] += fftconvolve(self.out[:, ch], ir)[: len(self.t)] * wet

    def finish(self, loudness=0.135, fade=0.06):
        """Levelled by RMS, not by peak: a stray crackle spike must not make one rung of the
        ladder quieter than the last. A soft limiter then holds the peaks under full scale."""
        n = int(fade * RATE)
        self.out[-n:] *= np.linspace(1, 0, n)[:, None]
        self.out *= loudness / np.sqrt(np.mean(self.out ** 2))
        self.out = np.tanh(self.out * 1.2) / 1.2
        return self.out


def save(path, data):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with wave.open(path, "wb") as w:
        w.setnchannels(2)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes((np.clip(data, -1, 1) * 32767).astype("<i2").tobytes())
    print("wrote", path)


def riser():
    """0.22 s: air drawn in, pitch and brightness rising, cut dead where the hit goes. Measured
    on a real set (IMG_1158, 30 fps detector): the clap confirms 0.23-0.26 s after the takeoff is
    noticed, so this is over just before the hit and never rings on under it."""
    rng = np.random.default_rng(11)
    length = 0.22
    mix = Mix(length + 0.03)
    n = int(length * RATE)
    k = np.arange(n) / n
    noise = rng.standard_normal(n)
    # A band that climbs: sum of fixed bands faded in one after another.
    swept = np.zeros(n)
    for i, (low, high) in enumerate([(300, 900), (700, 2000), (1600, 4200), (3500, 9000)]):
        start = i * 0.18
        gate = np.clip((k - start) / 0.35, 0, 1) * np.clip((start + 0.75 - k) / 0.3, 0, 1) if i < 3 else np.clip((k - start) / 0.35, 0, 1)
        swept += band(noise, low, high, 2) * gate
    mix.place(swept * k ** 2.2, 0, 0.5)
    freq = 190 * (4.2 ** k)
    tone = np.sin(2 * np.pi * np.cumsum(freq) / RATE) * k ** 2.6
    mix.place(tone, 0, 0.16)
    out = mix.out
    cut = int(0.012 * RATE)
    out[n - cut:n] *= np.linspace(1, 0, cut)[:, None]
    out[n:] = 0
    out /= np.max(np.abs(out)) / 0.7
    return out


def marimba(freq, seconds, bright=1.0):
    """Struck bar: the fundamental, the bar's tuned 4th harmonic and a short knock on top."""
    n = int(seconds * RATE)
    s = np.arange(n) / RATE
    note = np.sin(2 * np.pi * freq * s) * np.exp(-s / 0.20)
    note += 0.42 * bright * np.sin(2 * np.pi * freq * 3.98 * s) * np.exp(-s / 0.055)
    note += 0.16 * bright * np.sin(2 * np.pi * freq * 9.9 * s) * np.exp(-s / 0.016)
    return note * np.minimum(1, s / 0.0015)


def impact(note, seed, big=False):
    rng = np.random.default_rng(seed)
    mix = Mix(1.9 if big else 1.05)

    # Boom: 320 Hz -> 112 Hz. Phone speakers give nothing below ~100 Hz and the phone is two
    # metres away, so the weight sits above that and in the saturated harmonics, not in a sub.
    n = int((1.2 if big else 0.7) * RATE)
    s = np.arange(n) / RATE
    floor = 100 if big else 112
    freq = floor + (320 - floor) * np.exp(-s / (0.05 if big else 0.04))
    phase = 2 * np.pi * np.cumsum(freq) / RATE
    boom = np.sin(phase) * np.exp(-s / (0.3 if big else 0.17)) * np.minimum(1, s / 0.0015)
    boom = np.tanh(boom * 3.2) / np.tanh(3.2)
    boom += 0.6 * np.sin(2 * phase) * np.exp(-s / 0.14)
    boom += 0.4 * np.sin(3 * phase) * np.exp(-s / 0.09)
    mix.place(boom, 0, 0.62)

    # Body: the slap of two palms on a floor, mid-range.
    n = int(0.16 * RATE)
    s = np.arange(n) / RATE
    mix.place(band(rng.standard_normal(n), 160, 900) * np.exp(-s / 0.035), 0, 1.5)

    # Crack: a sharp front, then the thunder's ragged crackle rolling off to one side.
    n = int(0.05 * RATE)
    s = np.arange(n) / RATE
    mix.place(band(rng.standard_normal(n), 1800, 11000) * np.exp(-s / 0.006), 0, 1.5)
    n = int((0.95 if big else 0.42) * RATE)
    s = np.arange(n) / RATE
    gate = lowpass((rng.random(n) > 0.9993 - 0.0007 * np.exp(-s / 0.1)).astype(float) * 60, 900)
    crackle = band(rng.standard_normal(n), 1400, 7500) * (0.25 + np.clip(gate, 0, 3))
    crackle *= np.exp(-s / (0.3 if big else 0.11))
    mix.place(crackle, 0.004, 0.6, -0.35)
    mix.place(np.roll(crackle, int(0.013 * RATE)), 0.004, 0.42, 0.35)

    # The note: a struck bar over a low fifth, so the ladder is heard as rising, not as louder.
    mix.place(marimba(note, 0.9), 0.006, 0.8, 0.1)
    mix.place(marimba(note * 2, 0.5, 0.6), 0.006, 0.24, -0.2)
    n = int(0.5 * RATE)
    s = np.arange(n) / RATE
    stab = np.zeros(n)
    for f in (note / 4, note / 4 * 1.5, note / 2):
        saw = 2 * ((f * s) % 1) - 1
        stab += lowpass(saw, 1500) * np.exp(-s / 0.11)
    mix.place(stab * np.minimum(1, s / 0.003), 0.004, 0.26)

    if big:
        # Finisher: the chord opens up (fifth and octave ring on) and the thunder rolls away.
        for f, gain, pan, at in [(note * 1.5, .5, -.3, .07), (note * 2, .5, .3, .14), (note * 3, .22, 0, .21)]:
            mix.place(marimba(f, 1.0, 0.7), at, gain, pan)
        n = int(1.5 * RATE)
        s = np.arange(n) / RATE
        swell = lowpass(np.abs(rng.standard_normal(n)), 9) * 9
        roll = band(rng.standard_normal(n), 110, 700, 2) * swell * np.exp(-s / 0.5) * np.minimum(1, s / 0.05)
        mix.place(roll, 0.05, 0.55)

    mix.room(rng, 0.3 if big else 0.2, 0.22 if big else 0.16)
    return mix.finish(0.16 if big else 0.135)


def rival():
    """The opponent's landing: the same floor hit with the top taken off and no note, so it
    is told apart from the player's own by ear alone and never competes with it."""
    rng = np.random.default_rng(61)
    mix = Mix(0.75)
    n = int(0.55 * RATE)
    s = np.arange(n) / RATE
    freq = 125 + 170 * np.exp(-s / 0.045)
    phase = 2 * np.pi * np.cumsum(freq) / RATE
    boom = np.sin(phase) * np.exp(-s / 0.15) * np.minimum(1, s / 0.002)
    boom = np.tanh(boom * 2.6) / np.tanh(2.6)
    boom += 0.5 * np.sin(2 * phase) * np.exp(-s / 0.11)
    mix.place(boom, 0, 0.28)
    n = int(0.2 * RATE)
    s = np.arange(n) / RATE
    mix.place(band(rng.standard_normal(n), 220, 1300) * np.exp(-s / 0.04), 0, 2.2)
    # What is left of the crack after a wall: a short dull knock, rolled off above 2.5 kHz.
    mix.place(lowpass(band(rng.standard_normal(n), 900, 5000) * np.exp(-s / 0.014), 2500, 4), 0, 2.4)
    rumble = band(rng.standard_normal(n), 110, 420, 2) * np.exp(-s / 0.07) * np.minimum(1, s / 0.01)
    mix.place(rumble, 0.02, 0.5, -0.2)
    mix.room(rng, 0.16, 0.14)
    return mix.finish(0.12)


def demo(riser_clip, impacts, max_clip):
    """Rep tick (the push to the top), takeoff, landing — five times, as a fight would play them."""
    rep = None
    with wave.open(os.path.join(AUDIO, "rep_01.wav"), "rb") as w:
        raw = np.frombuffer(w.readframes(w.getnframes()), dtype="<i2").astype(float) / 32768
        raw = raw.reshape(-1, w.getnchannels())
        rep = np.repeat(raw[:, :1], 2, axis=1) if w.getnchannels() == 1 else raw
        if w.getframerate() != RATE:
            idx = np.arange(0, len(rep), w.getframerate() / RATE)
            rep = rep[np.minimum(idx.astype(int), len(rep) - 1)]
    out = np.zeros((int(RATE * 12.5), 2))

    def put(clip, at, gain):
        i = int(at * RATE)
        n = min(len(clip), len(out) - i)
        out[i:i + n] += clip[:n] * gain

    for i, clip in enumerate(impacts + [max_clip]):
        at = 0.5 + i * 2.3
        put(rep, at, 0.46)
        put(riser_clip, at + 0.13, 0.5)        # top latches, then the hands leave the floor
        put(clip, at + 0.13 + 0.24, 0.9 if i < 4 else 1.0)
    save(os.path.join(DEMO, "demo.wav"), out / max(1.0, np.max(np.abs(out)) / 0.95))


if __name__ == "__main__":
    riser_clip = riser()
    save(os.path.join(AUDIO, "clap_riser.wav"), riser_clip)
    impacts = [impact(note, 21 + i) for i, note in enumerate(LADDER)]
    for i, clip in enumerate(impacts):
        save(os.path.join(AUDIO, f"clap_impact_{i + 1}.wav"), clip)
    max_clip = impact(MAX_NOTE, 40, big=True)
    save(os.path.join(AUDIO, "clap_max.wav"), max_clip)
    save(os.path.join(AUDIO, "clap_rival.wav"), rival())
    demo(riser_clip, impacts, max_clip)
