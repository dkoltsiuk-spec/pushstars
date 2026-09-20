# English interface and mobile validation

Date: 2026-09-15. Unity: 6000.3.24f1. Test host: Windows.

Update (2026-09-19): OTA scene loading has been retired for new builds. See
[embedded scene routing](boss-scene-routing-fix.md). OTA references below describe
the earlier audit, not the current release path.

## Changes

- Translated application copy in runtime C#, authored scenes, prefabs and scene generators: onboarding, calibration, workouts, duels, boss hints, profile, league, settings, results and cases.
- English is the supported interface language. An old Russian preference no longer makes the settings label disagree with the interface. User-entered names are preserved.
- Invalid or empty safe-area reports during initialization or resume keep the last valid layout instead of collapsing the UI.
- Camera tracking releases resources on backgrounding and restarts on return. Repeated pause notifications preserve the restart request; explicit stop cancels it. Capture timestamps remain monotonic across restarts.
- Solo exercise pauses when the application is backgrounded and requires an explicit resume. Rep counting stops with the pause, and the training pause UI follows the controller state.
- iOS build preparation consistently targets iOS 15, bakes font glyphs in both build entry points and fails early if a required pose model is missing.
- Existing Play Mode tests now use authored scenes without regenerating them, allow button press animations to finish, use the correct dialog backdrop, and return to the editor rather than terminating it. The test wrapper restores the original editor scene setup.

## Reproducible checks

Run `python tools/check_english.py`. It scans C# string literals and serialized UI, including wrapped Unity YAML strings and Unicode escapes. Font alphabets and internal GameObject names are excluded. Generated OTA scenes are copied from authored scenes by `OtaSetup.Configure` during content builds.

In Unity, run **Tools → Push Stars → Validate English Mobile App** for isolated regressions and scene binding checks. Results are written to `output/mobile-audit/edit-mode.txt`.

With Unity idle and scenes saved, write one command into `Temp/pushstars.mobile-validation`:

| Command | Coverage |
| --- | --- |
| `edit` | Scene references, English copy, safe areas, camera lifecycle, pose orientation, rewards, layout persistence and training logic |
| `ios-scripts` | Compile iOS managed player assemblies with `PUSHSTARS_MEDIAPIPE` |
| `play-experience` | Onboarding, tabs, profile dialogs, settings, language, sound persistence and HOME |
| `play-fight` | Seven fight/reward scenes, optional case routes and repeated claim protection |
| `play-modes` | Mode selection, exercise settings, rest settings, training navigation and background pause |
| `play-training` | Training loading, exercise, rest, pause, results and extra set |
| `play-boss-map` | Map input, scrolling, rapid taps and cancellation |
| `play-boss-combat` | Preparation, READY, accepted reps, HP, knockout and HOME |
| `play-home` | Preparation HOME navigation |
| `play-measurement` | Measurement route and camera presentation |

The runner refuses to replace unsaved open scenes. Run Play suites sequentially and inspect their result files. Onboarding OS camera permission is not requested by the automated experience test. Profile deletion and external account actions are not invoked.

Backend checks: `node --test functions/test/*.test.js`.

## Evidence

Verified results:

| Check | Result |
| --- | --- |
| English copy audit | 267 C# files and 30 authored assets; no untranslated Cyrillic UI strings |
| Final Edit Mode suite | 27 checks passed, 0 failed; includes all 12 shipping scenes |
| Final iOS script compilation | PASS; includes real MediaPipe and all Push Stars runtime assemblies |
| Backend | 4/4 heartbeat tests; syntax checks passed for all 10 source files |
| Fight and reward Play flow | PASS; seven scenes and optional case routes; player data unchanged |
| Mode/settings/training integration | 107 assertions passed, including background pause and explicit resume |
| Training presentation | 16 assertions passed; loading, exercise, rest, pause, results and extra set |
| Onboarding/profile/settings | PASS; 20 captures across the three phone sizes, preferences restored |
| Boss map Play flow | PASS; duplicate input, scrolling, cancellation and mode restoration |
| Boss combat Play flow | PASS; preparation, HOME, READY, seven accepted reps to knockout, extra rep ignored; no progression awarded |
| Preparation HOME and measurement routes | Both PASS; authored scenes, correct backdrop and centered portrait; camera/reward effects suppressed |

- English audit: `output/mobile-audit/english-copy.json`.
- Edit Mode regression summary: `output/mobile-audit/edit-mode.txt`.
- iOS managed compilation: `output/mobile-audit/ios-compilation.txt`.
- Onboarding/profile/settings screenshots and results: `output/mobile-audit/experience/`.
- Fight/reward screenshots and results: `Logs/fight-screens/`.
- Mode/settings results: `Logs/mode-selection/validation.txt`.
- Training results: `Logs/training-screen/validation.txt`.
- Boss screenshots and results: `output/boss-map/`, `output/boss-combat/`.
- Pose regression: `Logs/retarget-regression.txt`.
- Case ledger regression: `Logs/case-rewards-regression.txt`.

Experience screenshots cover 320×568, 390×844 and 430×932. The capture reproduces the authored CanvasScaler and applies representative safe-area insets for notched and Dynamic Island layouts. These are Unity render targets, not physical-device captures.

## Remaining device and integration verification

This pass does not certify an installed iPhone build. The iOS managed compilation checks platform-specific C#; it does not run Xcode, native linking, Metal/MediaPipe inference or an IPA on a phone. Unity documents the [Xcode/macOS or Build Automation requirement](https://docs.unity3d.com/6000.3/Documentation/Manual/ios-environment-setup.html).

Before a phone release, install the new build on a real iPhone and verify:

1. Fresh launch, camera permission allowed and denied, then permission restored in iOS Settings.
2. Front-camera orientation and real rep counting through a full 60-second set.
   Also check the iOS keyboard while editing a nickname and touch/scroll behavior on the device.
3. Background/foreground, screen lock and interruptions during camera initialization, a live set and rest.
4. Performance, thermal behavior and memory through repeated workouts and scene changes.
5. Offline launch and recovery after network loss; the published OTA catalog must correspond to the new English scenes.

Existing product gaps remain: live PvP/friend networking and Apple/Google/email sign-in are not connected; some settings actions report that privacy/support endpoints are not configured. Notification preference storage does not prove APNs delivery. Those integrations require their own implementation and device/account verification.
