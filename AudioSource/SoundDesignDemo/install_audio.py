"""Install only instrumental assets into Unity; never copy narration or demo mixes."""
from pathlib import Path
import shutil
import uuid
import numpy as np
from scipy.io import wavfile

ROOT = Path(__file__).resolve().parent
PROJECT = ROOT.parents[1]
TARGET = PROJECT / 'Assets/_Project/Resources/Audio'
TARGET.mkdir(parents=True, exist_ok=True)
NAMES = ['ui_tap_01', 'ui_tap_02', 'ui_tap_03', 'ui_confirm', 'ui_back', 'ui_swipe',
         'rep_01', 'rep_02', 'rep_03', 'countdown', 'reward_tick', 'reward_complete',
         'case_unlock', 'case_reveal_epic', 'case_upgrade_finish', 'shop_purchase',
         'victory', 'music_sport_loop_120bpm']
for name in NAMES:
    shutil.copyfile(ROOT/'exports'/f'{name}.wav', TARGET/f'{name}.wav')

def save(name, x, rate=48000):
    x=x.copy()
    attack,release=min(len(x),240),min(len(x),1440)
    shape=(-1,1) if x.ndim==2 else (-1,)
    x[:attack]*=np.linspace(0,1,attack).reshape(shape)
    x[-release:]*=np.linspace(1,0,release).reshape(shape)
    x*=.8/max(np.max(np.abs(x)),1e-8)
    wavfile.write(TARGET/f'{name}.wav',rate,(x*32767).astype(np.int16))

# The reveal animation is 0.7 s: take the last 0.7 s of the original 1.6 s rise.
sr,x=wavfile.read(ROOT/'exports/case_charge.wav')
save('case_charge_short',x[int(.9*sr):int(1.6*sr)].astype(float),sr)
sr,x=wavfile.read(ROOT/'exports/case_upgrade_steps.wav')
save('case_upgrade_short',x[:int(.4*sr)].astype(float),sr)
t=np.arange(12000)/48000
save('rep_rejected',(np.sin(2*np.pi*220*t)+.25*np.sin(2*np.pi*660*t))*np.exp(-t*10))

# Commit stable asset identities. Import settings are set by GameAudioImporter.
for folder in [TARGET]:
    meta=Path(str(folder)+'.meta')
    if not meta.exists(): meta.write_text(f'fileFormatVersion: 2\nguid: {uuid.uuid4().hex}\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n')
print(f'Installed {len(NAMES)+3} instrumental WAV files into {TARGET}')
