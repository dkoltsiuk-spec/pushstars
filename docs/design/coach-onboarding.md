# Coach onboarding

The authored `Assets/_Project/Scenes/Onboarding.unity` now contains character selection, the coach, camera setup, and the actual assessment. The assessment objects are copied together from the authored Fight scene, preserving references between its HUD, session, avatar drivers, and result flow.

## Player flow

1. **Choose your hero.** Both 3D models animate. The selected model is full size and color, with a blue glow and the supplied blue radio button. The other model is 88% scale and grayscale. Selection blends over 320 ms, with the feet anchored. Both portraits are slightly enlarged. The original radio sprites preserve their proportions in smaller bounds. NEXT is centered behind the coach and uses the supplied Group 600.png plate. A first tap dismisses the speech, coach, and wash in sequence, including during the entrance. A transparent tap surface consumes that tap; NEXT becomes available only after dismissal, for a separate second tap. TAP ANYWHERE TO CONTINUE explains the first action.
2. **Assessment introduction.** The same RawImage moves to the center over 950 ms while selection controls fade and the arena background appears. No scene load or model replacement occurs. Back reverses the motion.
3. **Camera access.** The coach explains the request. Only the ALLOW CAMERA action requests system permission. A denial offers OPEN SETTINGS and NOT NOW; returning from settings rechecks authorization.
4. **Phone placement.** The supplied blue panel contains the existing placement artwork and a gently moving phone. All instructions are English. I'M READY checks permission again before enabling tracking.
5. **Assessment.** The coach and blue wash leave in sequence. The original model, stage camera, and RawImage are handed to the real fight controller and HUD. A compensating lens adjustment preserves the hero's visible size while the HUD takes ownership. The selection viewpoint is retained until a real exercise pose is acquired. The existing CV, countdown, reps, pause, finish, and result flow run normally. FINISH and the timer are absent from the guide and appear only in the live assessment.

Hard, translucent contact ellipses follow each avatar's projected feet, including through portrait reparenting into the live HUD. Their dimensions remain independent of stretched UI containers. The selection background has visible translucent lightning, and the coach's lower body is blended by a foreground blue wash beneath the controls.

NOT NOW saves a postponed assessment without awarding a result. Intro completion is saved when entering the assessment or explicitly postponing it. Rapid taps and Back are gated during transitions and while a permission request is pending. Animation uses unscaled time.

## Coach animation

- Blue wash: 220 ms.
- Coach: 420 ms, sliding up and in, scaling from 94%.
- Speech bubble: 240 ms, easing upward from 94% scale.
- Text reveal: up to 700 ms, using TMP character visibility to preserve wrapping.
- Exit: bubble 160 ms, then coach and wash 280 ms.
- Idle: subtle 1.6-point vertical breathing motion.

## Original supplied art

All eleven files are copied without pixel edits from the user's Downloads folder into `Assets/_Project/UI/Sprites/Onboarding/`. Generated experiments were not imported or referenced.

| Source filename | Project filename |
| --- | --- |
| `hf_20260913_142604_8c814bab-dc55-45a0-aa2c-a7764fb4ce6d (1) 1.png` | `coach-welcome.png` |
| `hf_20260913_142604_8c814bab-dc55-45a0-aa2c-a7764fb4ce6d (1) 1 (1).png` | `coach-assessment.png` |
| `hf_20260913_142604_8c814bab-dc55-45a0-aa2c-a7764fb4ce6d (1) 1 (2).png` | `coach-phone.png` |
| `Rectangle 336 (1).png` | `coach-blue-wash.png` |
| `Подсказка (1).png` | `coach-bubble.png` |
| `Rectangle 323.png` | `placement-panel.png` |
| `Group 597.png` | `coach-button.png` |
| `Group 600.png` | `next-button.png` |
| `Group 597 (1).png` | `selection-on.png` |
| `Group 598.png` | `selection-off.png` |
| `Ellipse 108 (1).png` | `selection-glow.png` |

## Editing and verification

Edit the saved Onboarding scene directly. `Tools > Push Stars > Onboarding > Apply Coach Onboarding` is an explicit migration/rebuild, which also refreshes its embedded assessment from Fight. It replaces the coach layout, so save or back up manual changes before invoking it. Ordinary Build Onboarding preserves the authored scene.

`Tools > Push Stars > Onboarding > Validate Coach Flow` exercises both choices, navigation, model and portrait identity, the denied-permission presentation, placement at 320x568 / 390x844 / 430x932, and the actual assessment handoff. Reports and screenshots are written to `output/onboarding/`. The test disables CV and restores the preferences it changes. No test awards reps or rewards.

System permission dialogs, return from iOS/Android Settings, and live camera tracking must also be checked in a device build. A new build/OTA content build is required for delivery to installed apps; this change does not publish content.
