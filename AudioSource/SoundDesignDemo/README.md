# Push Stars — sound audition v1

34-second audio concept, prepared 2026-09-12. This is an audition pack, not an
installed Unity audio system. No existing scenes or scripts were changed.

## Listen

- `exports/push_stars_sound_demo.mp3` — complete preview, including Grady.
- `exports/push_stars_sound_demo.wav` — uncompressed 48 kHz stereo master.
- `exports/music_sport_loop_120bpm.wav` — original 16-second electronic loop.
- `exports/voice_10_grady.wav`, `exports/voice_100_grady.wav` — processed voices.
- `exports/demo_*_stem.wav` — separate music, effects and voice tracks.

## Timeline

| Time | Sound |
|---|---|
| 00:00 | Original electronic sports music, 120 BPM |
| 00:00.65–00:02.9 | Two taps, navigation swipe, confirmation |
| 00:03–00:04.2 | Countdown |
| 00:05–00:08.7 | Eight repetition confirmations, three variants |
| 00:09 | Milestone impact and Grady: «Десять!» |
| 00:12–00:14 | Reward counter and completion |
| 00:14.5 | Shop purchase |
| 00:16–00:20 | Case unlock, energy buildup, epic reveal |
| 00:21–00:24.8 | Upgrade steps and completion |
| 00:26 | Large achievement impact and Grady: «Сто повторений!» |
| 00:30–00:34 | Victory motif and fade |

## Provenance and rebuild

Music and effects are synthesized from oscillators and seeded noise by
`build_demo.py`; no sampled music or external sound libraries are used.
Speech is generated with Higgsfield Seed Audio using the selected preset Grady
(voice ID `e2a2d2e6-9ed2-59cd-82af-feaa27f8a678`). Generation job:
`66baf1a8-0da1-429e-92cf-0aa514c70c8e`.

Exact speech prompt: `Десять! ... Сто повторений!`. Settings: WAV, pitch -2.
Source voice is stored in `raw/grady.wav`; silence boundaries are preserved in
`raw/voice_split.json`. Speech processing adds filtering, saturation and short
stereo delays; the music ducks around both milestone announcements.

Run `python AudioSource/SoundDesignDemo/build_demo.py` from the repository root.
Requires Python, NumPy, SciPy and FFmpeg. `exports/manifest.json` contains all
cue times and numeric verification. WAV exports are 16-bit PCM at 48 kHz; the
audition MP3 is 256 kbps. The music loop includes wrapped synthesis tails.

Voice lines 20, 30, 40, 50 and 70 and full game integration are the next
production stage after assessing this sonic direction. The 100-repetition
announcement is an audio audition, not a change to match limits.
