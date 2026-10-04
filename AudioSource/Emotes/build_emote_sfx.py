"""Emote stingers for Push Stars. Python + NumPy + SciPy, no samples, no voices.

One short instrumental cue per emote, in the same synthesized family as the UI and
reward sounds (AudioSource/SoundDesignDemo). Writes 48 kHz 16-bit stereo WAVs to
Assets/_Project/Art/Emotes/Sounds/emote_<id>.wav, where Tools > Push Stars >
Emotes > Build Emotes picks them up by name.

Run from the repository root:  python AudioSource/Emotes/build_emote_sfx.py
"""
from pathlib import Path
import numpy as np
from scipy import signal
from scipy.io import wavfile

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Assets' / '_Project' / 'Art' / 'Emotes' / 'Sounds'
OUT.mkdir(parents=True, exist_ok=True)
SR = 48000
RNG = np.random.default_rng(300926)


def time(d): return np.arange(round(d * SR)) / SR
def hz(m): return 440 * 2 ** ((m - 69) / 12)


def fade(x, attack=.003, release=.03):
    x = x.copy()
    a, r = min(len(x), int(attack * SR)), min(len(x), int(release * SR))
    ramp = (lambda n: np.linspace(0, 1, n)[:, None]) if x.ndim == 2 else (lambda n: np.linspace(0, 1, n))
    if a: x[:a] *= ramp(a)
    if r: x[-r:] *= ramp(r)[::-1]
    return x


def stereo(x, pan=0):
    if x.ndim == 2: return x
    return x[:, None] * np.array([np.cos((pan + 1) * np.pi / 4), np.sin((pan + 1) * np.pi / 4)])


def put(dst, x, sec, gain=1, pan=0):
    i = round(sec * SR)
    x = stereo(x, pan)
    n = min(len(x), len(dst) - i)
    if n > 0: dst[i:i + n] += x[:n] * gain


def canvas(d): return np.zeros((int(d * SR), 2))


def band(x, low, high, order=2):
    return signal.sosfilt(signal.butter(order, [low, high], fs=SR, btype='bandpass', output='sos'), x)


def lowpass(x, cutoff, order=2):
    return signal.sosfilt(signal.butter(order, cutoff, fs=SR, btype='lowpass', output='sos'), x)


def noise(d, low=200, high=9000): return band(RNG.normal(0, 1, len(time(d))), low, high)


def saw(phase): return 2 * (phase / (2 * np.pi) % 1) - 1


def square(phase, duty=.5): return np.where(phase / (2 * np.pi) % 1 < duty, 1., -1.)


def glide_phase(f0, f1, d, curve=1.):
    t = time(d); u = (t / d) ** curve
    f = f0 * (f1 / f0) ** u
    return 2 * np.pi * np.cumsum(f) / SR


def echo(x, length=.5, wet=.16):
    y = canvas(len(x) / SR + length); put(y, x, 0)
    for delay, amp, pan in [(.083, wet, -.6), (.127, wet * .8, .6), (.211, wet * .5, -.4)]:
        put(y, x, delay, amp, pan)
    return fade(y, .002, .08)


def bell(m, d=.6):
    t = time(d); f = hz(m)
    return fade((np.sin(2 * np.pi * f * t) * np.exp(-t * 9) + .3 * np.sin(2 * np.pi * f * 2.76 * t) * np.exp(-t * 20)) * .7)


def marimba(m, d=.45):
    t = time(d); f = hz(m)
    return fade(np.sin(2 * np.pi * f * t) * np.exp(-t * 14) + .35 * np.sin(2 * np.pi * f * 4 * t) * np.exp(-t * 40), .002, .03)


def brass(m, d, vibrato=0., bright=2600, glide_to=None):
    t = time(d)
    f0 = hz(m); f1 = hz(glide_to) if glide_to is not None else f0
    f = f0 * (f1 / f0) ** (t / d)
    f = f * (1 + vibrato * np.sin(2 * np.pi * 5.5 * t) * np.clip(t / .25, 0, 1))
    phase = 2 * np.pi * np.cumsum(f) / SR
    x = saw(phase) + .5 * saw(phase * 1.004)
    x = lowpass(x, bright)
    env = np.minimum(1, t / .03) * np.exp(-t * .8)
    return fade(x * env * .5, .01, .06)


def kick(d=.4):
    t = time(d); phase = 2 * np.pi * (48 * t + 110 * .02 * (1 - np.exp(-t / .02)))
    return fade(np.tanh(1.6 * np.sin(phase)) * np.exp(-t * 11) + .12 * noise(d, 1500, 7000) * np.exp(-t * 160))


def snare(d=.22):
    t = time(d)
    return fade(.65 * noise(d, 900, 9500) * np.exp(-t * 22) + .25 * np.sin(2 * np.pi * 190 * t) * np.exp(-t * 28))


def hat(d=.07):
    t = time(d); return fade(noise(d, 6500, 17000) * np.exp(-t * 70), .001, .01)


def whoosh(d, low=500, high=7000, up=True):
    t = time(d); u = t / d
    env = np.sin(np.pi * u) ** 1.5
    x = RNG.normal(0, 1, len(t))
    centre = np.geomspace(low, high, 8) if up else np.geomspace(high, low, 8)
    out = np.zeros_like(x)
    seg = len(x) // 8
    for i, c in enumerate(centre):
        s = slice(i * seg, len(x) if i == 7 else (i + 1) * seg)
        out[s] = band(x, c * .6, min(c * 1.6, 20000))[s]
    return fade(out * env * .6, .01, .05)


def thud(d=.5):
    t = time(d); phase = 2 * np.pi * (60 * t + 80 * .03 * (1 - np.exp(-t / .03)))
    return fade(np.tanh(1.3 * np.sin(phase)) * np.exp(-t * 9) + .3 * noise(d, 150, 2500) * np.exp(-t * 25))


def sparkle(d=.8, count=9, low=84, high=100):
    y = canvas(d)
    for i in range(count):
        put(y, bell(int(RNG.integers(low, high)), .35) * .35, i * d / count * .6, 1, RNG.uniform(-.7, .7))
    return y


def woodblock(m, d=.12):
    t = time(d); f = hz(m)
    return fade(np.sin(2 * np.pi * f * t) * np.exp(-t * 60) + .4 * np.sin(2 * np.pi * f * 2.3 * t) * np.exp(-t * 90), .001, .01)


def whistle(f0, d, trill=0.):
    t = time(d)
    f = f0 * (1 + trill * np.sin(2 * np.pi * 28 * t))
    phase = 2 * np.pi * np.cumsum(f) / SR
    x = np.sin(phase) + .15 * noise(d, f0 * .8, f0 * 1.3) * .3
    env = np.minimum(1, t / .015) * np.minimum(1, (d - t) / .03)
    return x * env * .5


# ── Cues ─────────────────────────────────────────────────────────────────────────────

def hello():
    y = canvas(1.0)
    put(y, marimba(84), 0, .8, -.2); put(y, marimba(91), .12, .8, .2)
    put(y, marimba(96, .6), .24, .6, 0)
    put(y, whoosh(.35, 1500, 6000) * .25, 0)
    return echo(y)


def laugh():
    y = canvas(1.6)
    notes = [81, 79, 81, 79, 76, 74]
    for i, m in enumerate(notes):
        put(y, brass(m, .12, vibrato=.02, bright=3200), .02 + i * .15, .9, (-1) ** i * .2)
    put(y, brass(72, .45, vibrato=.035, bright=2400, glide_to=67), .95, .9)
    return echo(y, wet=.1)


def boo():
    # The sad trombone: three falling steps and a long wobble.
    y = canvas(2.6)
    for i, m in enumerate([62, 61, 60]):
        put(y, brass(m, .38, vibrato=.006, bright=1500), i * .42, 1)
    put(y, brass(59, 1.2, vibrato=.03, bright=1400, glide_to=58), 1.26, 1)
    return fade(y, .002, .2)


def loser():
    # Playground taunt "na-na-na-na-naa" on a bright reed, with a snare flam at the end.
    y = canvas(1.9)
    s = 0.
    for m, d in [(79, .22), (76, .22), (81, .22), (79, .3), (76, .5)]:
        tt = time(d)
        ph = 2 * np.pi * hz(m) * tt
        note = lowpass(square(ph, .3) * .5 + saw(ph) * .4, 3400) * np.minimum(1, tt / .015) * np.exp(-tt * 2)
        put(y, fade(note * .45, .005, .04), s, 1, (-1) ** int(s * 10) * .15)
        s += d
    put(y, snare(), s - .02, .5); put(y, snare(), s + .06, .7)
    return echo(y, wet=.1)


def you():
    # A pointed "dun!": low stab with a riser into it.
    y = canvas(1.5)
    put(y, whoosh(.35, 800, 6000, True) * .6, 0)
    chord = sum(brass(m, .6, bright=3000) for m in (50, 57, 62, 65)) * .5
    put(y, chord, .38, 1)
    put(y, kick(.5), .38, .9)
    put(y, noise(.3, 2000, 10000) * np.exp(-time(.3) * 14) * .25, .38)
    return echo(y, wet=.12)


def gg():
    y = canvas(1.3)
    for i, m in enumerate([72, 76, 79, 84]):
        put(y, bell(m, .7) * .6, i * .075, 1, (i % 3 - 1) * .4)
    put(y, sparkle(.9, 7, 88, 100), .28, .7)
    put(y, kick(.3) * .4, 0)
    return echo(y)


def flex():
    y = canvas(1.2)
    put(y, kick(.5), 0, 1)
    chord = sum(brass(m, .55, bright=3800) for m in (55, 62, 67, 71)) * .55
    put(y, chord, 0, 1)
    put(y, noise(.4, 3000, 12000) * np.exp(-time(.4) * 9) * .2, 0)
    put(y, sparkle(.6, 5, 90, 101), .18, .5)
    return echo(y, wet=.12)


def warmup():
    y = canvas(1.1)
    put(y, whistle(2900, .16, trill=.03), 0, 1)
    put(y, whistle(2900, .45, trill=.05), .24, 1)
    return fade(y, .002, .05)


def threat():
    y = canvas(1.6)
    for i, s in enumerate([0, .38]):
        t = time(1.0)
        low = np.sin(2 * np.pi * hz(38 - i) * t) + .5 * saw(2 * np.pi * hz(38 - i) * t)
        hit = lowpass(low, 700) * np.exp(-t * 3.5) * .7
        put(y, fade(hit, .003, .1), s, 1)
        put(y, kick(.5) * .7, s)
        put(y, noise(.3, 200, 3000) * np.exp(-time(.3) * 18) * .3, s)
    return fade(y, .002, .15)


def hiphop():
    bpm = 94; beat = 60 / bpm
    y = canvas(beat * 8 + .4)
    for b in range(8):
        s = b * beat
        if b in (0, 3, 4, 5): put(y, kick(), s, .9)
        if b in (2, 6): put(y, snare(), s, .8)
        put(y, hat(), s, .35, .3); put(y, hat(), s + beat / 2, .22, -.3)
    bass = [40, 40, 43, 45, 40, 40, 38, 43]
    for b, m in enumerate(bass):
        put(y, lowpass(saw(2 * np.pi * hz(m) * time(beat * .8)), 400) * np.exp(-time(beat * .8) * 3) * .35, b * beat)
    put(y, marimba(76, .3) * .4, beat * 1.5); put(y, marimba(79, .3) * .4, beat * 5.5)
    return fade(y, .002, .3)


def snake():
    # A pungi line on a Phrygian-dominant scale over a drone.
    y = canvas(2.8)
    t = time(2.8)
    drone = lowpass(square(2 * np.pi * hz(50) * t, .3), 900) * .12
    put(y, fade(drone, .2, .4), 0)
    melody = [(62, .25), (63, .25), (66, .25), (67, .5), (66, .2), (63, .2), (62, .6)]
    s = .1
    for m, d in melody:
        tt = time(d)
        f = hz(m) * (1 + .012 * np.sin(2 * np.pi * 6 * tt))
        ph = 2 * np.pi * np.cumsum(f) / SR
        note = lowpass(square(ph, .22) * .6 + saw(ph) * .3, 2800) * np.minimum(1, tt / .02)
        put(y, fade(note * .35, .01, .04), s)
        s += d
    return echo(y, wet=.1)


def giddyup():
    y = canvas(2.2)
    trot = [0, .16, .36, .52, .72, .88, 1.08, 1.24]
    for i, s in enumerate(trot):
        put(y, woodblock(79 if i % 2 else 74), s, .9, -.3 if i % 2 else .3)
    slide = np.sin(glide_phase(1400, 3000, .45, .7)) * np.minimum(1, time(.45) / .02) * .4
    put(y, fade(slide, .01, .08), 1.4)
    put(y, fade(np.sin(glide_phase(3000, 1800, .25)) * .35, .005, .06), 1.85)
    return fade(y, .002, .1)


def backflip():
    # Timed to the clip: take-off ~1.0 s, upside down ~1.5 s, landing ~1.85 s.
    y = canvas(2.7)
    put(y, whoosh(.5, 400, 6000, True), .9, .9)
    put(y, whoosh(.45, 5000, 700, False), 1.35, .7)
    put(y, thud(.5), 1.82, 1)
    put(y, sparkle(.7, 7, 88, 101), 1.9, .6)
    return fade(y, .002, .1)


CUES = {
    'hello': (hello, .72), 'laugh': (laugh, .7), 'loser': (loser, .75), 'boo': (boo, .75), 'you': (you, .8), 'gg': (gg, .7),
    'flex': (flex, .8), 'warmup': (warmup, .6), 'threat': (threat, .85), 'hiphop': (hiphop, .7),
    'snake': (snake, .7), 'giddyup': (giddyup, .75), 'backflip': (backflip, .8),
}

TARGET_RMS = {'warmup': .07, 'snake': .085, 'boo': .1, 'loser': .09}

if __name__ == '__main__':
    for name, (build, peak) in CUES.items():
        x = stereo(build())
        # Loudness, not peak: a sustained trombone at the same peak as a click is several
        # times louder. Scale to a common RMS and let the peak cap the transients.
        rms = np.sqrt(np.mean(x ** 2))
        x = x * min(peak / max(np.max(np.abs(x)), 1e-8), TARGET_RMS.get(name, .1) / max(rms, 1e-8))
        wavfile.write(OUT / f'emote_{name}.wav', SR, (x * 32767).astype(np.int16))
        print(f'emote_{name}.wav  {len(x) / SR:.2f}s')
