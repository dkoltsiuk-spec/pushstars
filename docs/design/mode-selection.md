# Home mode selector

The button left of BATTLE opens the authored `ModeSelectionOverlay` in Main.
The supplied PNGs live in `Assets/_Project/UI/Sprites/ModeSelection`. Their original
pixels are unchanged; sprite import rectangles remove transparent artboard margins.
The background skulls drift upward using unscaled time and are clipped to the sheet.
Cards and their info icons fade and rise into place in PVP / BOSS / TRAINING order,
65 ms apart, with a subtle scale-in. The entrance finishes in 440 ms and restarts
cleanly on reopening. Info artwork is 28×26 reference points.

Tap a card to save PVP / BOSS / TRAINING (`selected_game_mode`) and update the home
button's label and icon. Selection does not start a fight. BATTLE starts the selected
route: existing ghost search for PVP, boss preparation for BOSS, and the saved multi-set
workout for TRAINING (see training-settings.md). Live opponent matchmaking is not implemented
by this UI change; the PVP info text states the current ghost fallback explicitly.

Each blue info button opens its mode description without selecting the card.
The handle, backdrop, and Android Back/Escape dismiss the sheet. Back dismisses an
open description first. Transitions support repeated taps, and the entire sheet scales
to fit narrow screens and landscape. The bottom margin keeps cards above device insets.

## Online

`OnlinePresence` persists between scenes and posts an authenticated heartbeat every
30 seconds while the app is in the foreground. `heartbeatPresence` writes one server
expiry per account and returns a Firestore count of accounts active in the last
75 seconds. Multiple devices for one account count once. Client-provided identities,
timestamps, and counts are ignored. The collection is private under the existing rules.
Expired documents are excluded immediately; optional Firestore TTL on `expiresAt`
can remove their storage later. Account deletion removes the presence document too.

The UI displays `—` when authentication, connectivity, or the endpoint is unavailable;
it never substitutes the mockup's 317 or a fabricated zero. This is app-wide active
presence, not the number currently waiting in the matchmaking queue.

To publish to the configured Firebase project:

```powershell
firebase deploy --only functions:heartbeatPresence,functions:onUserDeleted --project push-stars-d620e
```

No database rule/index change is needed. A device needs the updated client to begin
sending heartbeats; old released clients cannot appear in this count.

## Authoring and verification

The scene is already installed. Edit its normal Unity UI objects directly.
`Tools > Push Stars > Main > Add Mode Selector` installs only when absent.
`Rebuild Mode Selector` replaces only this feature's own objects using the source
layout; it should be used deliberately after editing the generator.

Backend checks: `node --test functions/test/heartbeatPresence.test.js`.
Unity Play checks: `PushStars.Editor.ModeSelectionPlayValidation.Run` in a disposable
copy, with graphics enabled and without `-quit`. It checks selection, info, closure,
animation, unknown online, and 390×844 / 320×568 / 844×390 layouts, then restores the
selection preference. Reports and screenshots are in `Logs/mode-selection`.
