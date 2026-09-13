# Battle exercise settings

The button to the right of BATTLE opens the authored `BattleSettingsOverlay` in Main.
The title is `BOSS SETTINGS` for bosses and `PVP 1V1 SETTINGS` for PVP.
Training opens its own settings sheet (see training-settings.md). The battle layout uses the supplied Group 520–523 PNGs
in `Assets/_Project/UI/Sprites/BattleSettings` without changing their pixels.

PUSHUP is the supported exercise for every existing fight route. Tapping it confirms
the exercise and closes the sheet; BATTLE still starts the fight. SQUATS and PULLUP
use the supplied grey artwork with locks and remain non-interactable.

The blue sheet has a clipped, translucent skull field using the same upward drift
as the mode selector. Cards reveal in order. The handle, backdrop and Android Back
dismiss it. The panel scales to fit small portrait screens and landscape.

The existing main scene is patched without rebuilding its other UI. Normal authoring
is through the scene objects. `Tools > Push Stars > Main > Add Battle Settings`
installs the panel only when missing. Mode/exercise settings are covered together by
`ModeSelectionPlayValidation.Run` in an isolated Unity copy; this checks both mode
titles, unavailable exercises, selection without starting a fight, skull motion,
reopening and responsive layouts. No backend deployment is involved.
