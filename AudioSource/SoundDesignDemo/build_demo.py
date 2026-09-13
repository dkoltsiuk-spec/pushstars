"""Original Push Stars sound-design audition. Python + NumPy + SciPy + FFmpeg.

Run from any directory. Optional raw/grady.wav and raw/voice_split.json supply
the generated Grady narration. No Unity scenes or runtime files are modified.
"""
from pathlib import Path
import json
import subprocess
import numpy as np
from scipy import signal
from scipy.io import wavfile

ROOT = Path(__file__).resolve().parent
OUT = ROOT / 'exports'
OUT.mkdir(parents=True, exist_ok=True)
SR = 48000
RNG = np.random.default_rng(120926)

def time(d): return np.arange(round(d * SR)) / SR
def hz(m): return 440 * 2 ** ((m - 69) / 12)
def fade(x, attack=.003, release=.025):
    x = x.copy()
    a, r = min(len(x), int(attack*SR)), min(len(x), int(release*SR))
    if a: x[:a] *= np.linspace(0, 1, a)[:, None] if x.ndim == 2 else np.linspace(0, 1, a)
    if r: x[-r:] *= np.linspace(1, 0, r)[:, None] if x.ndim == 2 else np.linspace(1, 0, r)
    return x
def norm(x, peak=.85): return x * (peak / max(np.max(np.abs(x)), 1e-8))
def stereo(x, pan=0):
    if x.ndim == 2: return x
    return x[:, None] * np.array([np.cos((pan+1)*np.pi/4), np.sin((pan+1)*np.pi/4)])
def put(dst, x, sec, gain=1, pan=0):
    i = round(sec*SR)
    x = stereo(x, pan) if dst.ndim == 2 else x
    n = min(len(x), len(dst)-i)
    if n > 0: dst[i:i+n] += x[:n] * gain
def noise(d, low=200, high=9000):
    x = RNG.normal(0, 1, len(time(d)))
    return signal.sosfilt(signal.butter(2, [low, high], fs=SR, btype='bandpass', output='sos'), x)
def bell(m=78, d=.45):
    t = time(d); f = hz(m)
    return fade((np.sin(2*np.pi*f*t) * np.exp(-t*10) + .26*np.sin(2*np.pi*f*2.76*t)*np.exp(-t*22))*.7)
def kick(d=.45):
    t = time(d); phase = 2*np.pi*(47*t+100*.021*(1-np.exp(-t/.021)))
    return fade(np.tanh(1.5*np.sin(phase))*np.exp(-t*12) + .15*noise(d, 1500, 7000)*np.exp(-t*170))
def snare(d=.22):
    t=time(d)
    return fade(.6*noise(d, 900, 9500)*np.exp(-t*24)+.22*np.sin(2*np.pi*185*t)*np.exp(-t*30))
def hat(d=.08):
    t=time(d); return fade(noise(d, 6500, 17000)*np.exp(-t*65), .001, .01)
def impact(d=1.5, big=False):
    t=time(d)
    sub=np.sin(2*np.pi*(42*t+64*.05*(1-np.exp(-t/.05))))*np.exp(-t*5)
    body=noise(d, 250, 6000)*np.exp(-t*11)
    sparkle=noise(d, 5000, 15000)*np.exp(-t*3)
    return fade(np.tanh(sub*1.3)*.65+body*.4+sparkle*(.15 if big else .06))
def pluck(m, d=.32):
    t=time(d); f=hz(m)
    x=sum(np.sin(2*np.pi*f*k*t)*np.exp(-t*(7+k*3))/k**1.35 for k in range(1,9))
    return fade(x, .004, .025)
def rise(d=1.5):
    t=time(d); u=t/d
    x=noise(d, 1400, 10000)*u**1.8*.48
    x+=np.sin(2*np.pi*(180*t+420*t*t/d))*.12*u**2
    return fade(x, .06, .02)
def echo(x, length=.7, wet=.18):
    y=np.zeros((len(x)+int(length*SR),2)); put(y,x,0)
    for delay,amp,pan in [(.079,wet,-.6),(.121,wet*.8,.6),(.197,wet*.55,-.4),(.293,wet*.3,.4)]:
        put(y,x,delay,amp,pan)
    return fade(y, .002, .1)
def chime(notes, step=.085, dur=1.2):
    y=np.zeros((int(dur*SR),2))
    for i,m in enumerate(notes): put(y,bell(m,.7),i*step,.65,(i%3-1)*.45)
    return echo(y)

assets={}
def asset(name,x):
    assets[name]=norm(x,.8)
    return assets[name]

for i in range(3):
    t=time(.115)
    x=.42*np.sin(2*np.pi*(650+80*i)*t)*np.exp(-t*75)+.23*noise(.115,1800,9500)*np.exp(-t*180)
    asset(f'ui_tap_{i+1:02}',fade(x,.001,.02))
asset('ui_confirm',chime([78,85],.07,.4))
asset('ui_back',chime([81,78],.06,.3))
asset('ui_swipe',fade(noise(.22,1000,6500)*np.sin(np.pi*time(.22)/.22)**2))
for i in range(3):
    t=time(.20); f=hz(66+i*.18)
    x=np.sin(2*np.pi*(f*t+10*.015*(1-np.exp(-t/.015))))*np.exp(-t*27)
    x+=.25*np.sin(2*np.pi*f*2*t)*np.exp(-t*45)+.05*noise(.2,1600,7000)*np.exp(-t*100)
    asset(f'rep_{i+1:02}',fade(x))
asset('countdown',bell(78,.19))
asset('reward_tick',bell(90,.16))
asset('reward_complete',chime([78,81,85,90],.07))
asset('case_unlock',echo(impact(.35)*.35+np.pad(assets['ui_tap_01'],(0,int(.35*SR)-len(assets['ui_tap_01']))),.25,.12))
asset('case_charge',echo(rise(1.6),.3,.10))
y=np.zeros((SR*3,2));put(y,impact(2,True),0,.8);put(y,chime([66,73,78,81,85,90],.075,1.8),.07,.65)
asset('case_reveal_epic',y)
asset('case_upgrade_steps',chime([66,69,73,78,81,85],.19,1.8))
y=np.zeros((int(2.6*SR),2));put(y,impact(1.6,True),0,.75);put(y,chime([78,85,90,97],.045,1.4),.025,.75)
asset('case_upgrade_finish',y)
asset('shop_purchase',chime([73,78,85],.065,.8))
asset('milestone_10',echo(impact(.65),.35,.1))
asset('milestone_100',assets['case_reveal_epic'].copy())
y=np.zeros((SR*3,2))
for i, notes in enumerate([[66,69,73],[69,73,78],[73,78,81]]):
    for m in notes: put(y,pluck(m,1.3),i*.28,.35)
put(y,impact(1.5),.56,.35)
asset('victory',echo(y,.5,.18))

def music():
    # 8 bars at 120 BPM. F# minor - D - A - E. Original synthesized composition.
    duration=16; tail=2; y=np.zeros((SR*(duration+tail),2)); beat=.5
    roots=[42,42,38,38,45,45,40,40]
    for bar,root in enumerate(roots):
        start=bar*2; chord=[root+24,root+27 if root==42 else root+28,root+31,root+36]
        t=time(2.5)
        env=np.minimum(t/.18,1)*np.minimum((2.5-t)/.5,1)
        pad=sum((np.sin(2*np.pi*hz(m)*t)+.35*np.sin(2*np.pi*hz(m)*1.003*t))*.055 for m in chord)*env
        put(y,pad,start,.7,-.2)
        for step in range(8):
            at=start+step*.25; m=root+(12 if step in [3,6] else 0)
            t=time(.22)
            bass=sum(np.sin(2*np.pi*hz(m)*k*t)/k**1.5 for k in range(1,6))*np.exp(-t*13)
            put(y,fade(np.tanh(bass*1.6)*.23),at+.025,.9)
            put(y,hat(),at,.055 if step%2==0 else .085,.3 if step%2 else -.3)
            if step in [0,4,6]: put(y,kick(),at,.31)
            if step in [2,6]: put(y,snare(),at,.21)
            if step in [1,3,4,7]:
                note=chord[[0,2,1,3][[1,3,4,7].index(step)]]+12
                put(y,echo(pluck(note,.35),.4,.18),at,.095,(step%3-1)*.35)
    # Wrap sustained tails into the beginning to make the exported 16 s loop seamless.
    loop=y[:SR*duration].copy(); loop[:SR*tail]+=y[SR*duration:]
    return norm(loop,.68)

bed=music(); assets['music_sport_loop_120bpm']=bed
events=[]; sfx=np.zeros((34*SR,2)); speech=np.zeros_like(sfx)
def cue(sec,name,gain=.55,pan=0):
    put(sfx,assets[name],sec,gain,pan);events.append({'time':sec,'asset':name})
cue(.65,'ui_tap_01',.28,-.2);cue(1.12,'ui_tap_02',.28,.2)
cue(1.65,'ui_swipe',.23);cue(2.15,'ui_confirm',.38)
for at in [3,3.5,4]:cue(at,'countdown',.24)
for i in range(8):cue(5+i*.5,f'rep_{i%3+1:02}',.33)
cue(9,'milestone_10',.42)
for i in range(17):cue(12+i*.067,'reward_tick',.15, .3*np.sin(i))
cue(13.2,'reward_complete',.45)
cue(14.5,'shop_purchase',.45)
cue(16,'case_unlock',.55);cue(16.5,'case_charge',.6);cue(18.1,'case_reveal_epic',.65)
cue(21,'case_upgrade_steps',.48);cue(22.2,'case_upgrade_finish',.6)
cue(26,'milestone_100',.7);cue(30,'victory',.6)

voice_path=ROOT/'raw'/'grady.wav'; split_path=ROOT/'raw'/'voice_split.json'
voiced=False
if voice_path.exists() and split_path.exists():
    rate,raw=wavfile.read(voice_path)
    if raw.dtype.kind in 'iu': raw=raw.astype(float)/np.iinfo(raw.dtype).max
    if raw.ndim==2:raw=raw.mean(axis=1)
    if rate!=SR:raw=signal.resample_poly(raw,SR,rate)
    spans=json.loads(split_path.read_text())
    for label,at in [('10',9.13),('100',26.17)]:
        a,b=spans[label]; x=raw[round(a*SR):round(b*SR)]
        x=signal.sosfilt(signal.butter(2,[85,11000],fs=SR,btype='bandpass',output='sos'),x)
        x=norm(np.tanh(norm(x,.75)*1.6),.76)
        fx=echo(fade(x,.007,.04),.7,.11 if label=='10' else .17)
        asset('voice_'+label+'_grady',fx)
        put(speech,fx,at,.86)
        events.append({'time':at,'asset':'voice_'+label+'_grady'})
    voiced=True

music_track=np.zeros_like(sfx);put(music_track,bed,0,.28);put(music_track,bed,16,.35)
gain=np.ones(len(music_track));t=np.arange(len(gain))/SR
for a,b,amount in [(8.85,11.5,.24),(16.4,18.4,.45),(25.8,29.3,.19)]:
    down=np.clip((t-a)/.12,0,1)*np.clip((b-t)/.45,0,1)
    gain*=1-down*(1-amount)
music_track*=gain[:,None];music_track=fade(music_track,.5,2.3)
mix=fade(music_track+sfx+speech,.03,1.0)
mix=norm(mix,.87)

def write(path,x):
    wavfile.write(path,SR,np.round(np.clip(x,-1,1)*32767).astype(np.int16))
for name,x in assets.items():write(OUT/(name+'.wav'),x)
write(OUT/'push_stars_sound_demo.wav',mix)
write(OUT/'demo_music_stem.wav',music_track)
write(OUT/'demo_sfx_stem.wav',sfx*.8)
if voiced:write(OUT/'demo_voice_stem.wav',speech)
subprocess.run(['ffmpeg','-hide_banner','-loglevel','error','-y','-i',str(OUT/'push_stars_sound_demo.wav'),'-codec:a','libmp3lame','-b:a','256k',str(OUT/'push_stars_sound_demo.mp3')],check=True)
report={'sample_rate':SR,'duration_seconds':len(mix)/SR,'voice_included':voiced,'peak_dbfs':float(20*np.log10(np.max(np.abs(mix)))),'rms_dbfs':float(20*np.log10(np.sqrt(np.mean(mix**2)))),'finite':bool(np.isfinite(mix).all()),'events':sorted(events,key=lambda e:e['time']),'asset_count':len(assets)}
(OUT/'manifest.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps({k:v for k,v in report.items() if k!='events'},indent=2))
