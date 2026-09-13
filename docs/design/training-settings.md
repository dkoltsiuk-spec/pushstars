# Training settings

In TRAINING mode the exercise button to the right of BATTLE opens this sheet.
PUSHUP is enabled; SQUATS and PULLUP remain locked. The supplied button/lock/dumbbell
PNGs are in `UI/Sprites/TrainingSettings`; the blue sheet uses the shared mode sheet
silhouette as its mask. Both sheet exports have straight bottom corners and retain
their upper handle. Translucent dumbbells drift behind the controls.
Display labels use the shared ModeOutline material: a black outline and a hard black
underlay offset downward, matching the supplied PUSHUP lettering reference. Captions
and the workout summary retain their lighter body style.

Settings persist under `training.sets` and `training.rest`. Defaults: three sets,
60 seconds of rest. Sets are bounded to 1–10; rest is 30, 60, 90 seconds or −1
(manual/infinite). Every set lasts up to 60 seconds. Estimates include rest only
between sets: 3×60 + 2×60 = 300 seconds. Multiple sets with manual rest show infinity
instead of an invented duration.

START snapshots the validated plan in `FightRequest.Workout`, requests the dedicated
Training mode and loads the dedicated Training scene. BATTLE also starts the saved
plan in TRAINING mode. The existing camera/countdown run each set behind the authored
training UI. During rest the rep counter is disabled; timed rest resumes automatically,
and START SET continues early or ends manual rest. Every new set rearms the camera
and runs the countdown. NEXT ends the current set early. The exit control leaves the workout.

Completed sets bank their XP and repetitions once. The best individual 60-second
record is saved as a ghost, while the final screen totals repetitions and XP across
sets. Training does not change onboarding completion, seed trophies or advance bosses.
The last set goes directly to the workout result without an extra rest interval.

`TrainingSettingsRegression.Run` covers duration math, bounds, invalid saved rest,
manual/timed progression, duplicate completion and final-set behavior.
`ModeSelectionPlayValidation.Run` checks the authored UI, persistence, responsive
layouts and START request/scene routing in an isolated Unity project. Its training
preferences are restored afterwards. No backend publication is part of this change.

Before validation, create the isolated project's Training scene with TrainingScreenSetup
and refresh its generated Main copy when needed. The launch check verifies the dedicated
Training scene, its FightController, the first-set caption and actual rest controls.
No bundles were published.
