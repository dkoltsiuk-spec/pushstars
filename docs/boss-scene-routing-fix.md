# Embedded scene routing and boss presentation

The app now loads the authored scenes included in Build Settings on every platform.
`OtaSceneLoader` remains as a compatibility entry point for existing callers, but
does not initialize Addressables, download catalogs, or load cached remote scenes.
Home navigation also uses the embedded Main scene. Both iOS build entry points no
longer build OTA content, and automatic Addressables builds with the player are
disabled in the project settings. Legacy publishing tools remain available for
older installed clients; their output is not consumed by the updated app.

Boss progression IDs and model IDs are separate: the existing five-stage ladder
maps novice, athlete, champion and goblin-guard to the novice goblin model, and
goblin-king to the king model. Portraits now use the same mapping. Missing boss
models report an explicit error instead of silently displaying the player's body.
Build validation rejects missing boss models, animators or presentation components
before an iOS build starts. Bosses without a resource portrait retain the authored
goblin icon in the scene.

Regression entry points:

- `PushStars.Editor.FirstBossValidation.Run`: every boss in preparation, battle
  and results; the selected model survives progression; the player and ghost
  presentations remain separate; shipping scenes and boss resources exist.
- `PushStars.Editor.FightFlowRegression.Run`: bindings in all seven authored
  fight and reward scenes.

With the Editor outside Play Mode, the existing mobile-validation runner accepts
`boss-routing` in `Temp/pushstars.mobile-validation` to run both checks and write
`output/mobile-audit/boss-routing.txt`.

These changes require a new app binary. Publishing an OTA bundle cannot update
the C# routing code in an already installed app. Device verification should use
the new build, including an upgrade over an installation with an old OTA cache,
offline launch, the second boss, READY, results, rewards and HOME.

## Verification on 2026-09-19

- Unity Editor compilation completed successfully.
- iOS player scripts compiled with `PUSHSTARS_MEDIAPIPE`
  (`output/mobile-audit/ios-compilation.txt`).
- All five boss models passed preparation, battle and results checks, including
  the second boss (`output/first-boss/athlete-FightPreparation.png`).
- Seven authored fight/reward scenes passed binding checks
  (`Logs/fight-flow-regression.txt`).
- Boss combat Play Mode passed READY, damage, knockout, extra-rep rejection and
  HOME navigation (`output/boss-combat/play-validation.txt`).
- Fight/reward Play Mode passed all seven screens, optional case routes and
  repeated-claim protection (`Logs/fight-screens/validation.txt`).
- Tests restored the Editor scene setup and preserved player progress.

These are Editor checks, not a physical-device acceptance test.
