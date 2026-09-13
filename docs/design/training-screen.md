# Training screen

`Assets/_Project/Scenes/Training.unity` is the authored training scene. Both START in
training settings and BATTLE with TRAINING selected open it. It reuses the existing
camera, rep detection and FightController, with a separate TrainingScreen presenter. The
boss, PVP and onboarding screens retain their own layouts.

The scene contains loading, exercise, rest, pause and completed-workout views.
The exercise view reuses the measurement HUD itself: its authored anchors, safe-area
layout, character framing, guidance banner and lower controls. The tempo slot shows
the current set, the title says PUSHUP and the finish button says NEXT. It does not
use the rest/result view's fixed composition or a separate avatar render surface.
Exercise shows the current set, live repetitions, technique and remaining set time.
NEXT finishes a live set early. Rest shows a sleeping character, a stopwatch,
completed-set markers, the rest countdown and elapsed workout time. +15 sec extends
timed rest; manual rest displays infinity and waits for START SET. Pause freezes
counting and both workout/rest timers. HOME and the red X return to the main screen.

The result uses the workout's total reps, average rep technique and earned XP.
No sample aura or streak rewards are granted or presented as real earnings. The
existing XP policy is unchanged. +1 more set extends the completed workout while
retaining its totals, up to the existing limit of ten sets. Reopening results does
not award completed sets again. Extra sets do not overwrite the saved settings.

The supplied Group 528/529/530 and Vector (16) images live in
`UI/Sprites/TrainingScreen`. Character rest and preview images are rendered from the
project's male/female rigs; live exercise displays the camera-driven avatar. The
lettering uses the shared black outline/underlay, with a lighter preset for the
large counters. Rest and result compositions scale uniformly within the viewport;
the active set follows the measurement screen's responsive layout.

`TrainingScreenSetup.Run` creates the scene only when missing and preserves an
existing authored layout. Open Training directly in the editor to preview the UI
without camera tracking or rewards.
`TrainingMeasurementLayout.Run` explicitly refreshes the copied measurement layout
and camera settings from the current Fight scene when those have been edited.
`TrainingScreenValidation.Run` runs isolated
preview transitions and captures each view at 390×844 and a smaller viewport.
`TrainingSettingsRegression.Run` covers deadlines, +15 sec, manual rest, completion,
extra sets and bounds. `ModeSelectionPlayValidation.Run` checks the real Main →
Training route, rest extension, pause and starting the next set without test rewards.
No backend or OTA bundle publication is part of this change.
