# Camera entry and push-up presentation

The boss portraits use `PortraitImage`, a RawImage subclass that clips its mesh to the valid camera texture coordinates. Crops can retain transparent padding outside the camera without stretching its border pixels into vertical bars. The scene builder preserves this component when updating authored screens.

The live mirror rejects offscreen and low-confidence joints, requires five valid torso samples over at least 0.3 seconds, and blends into tracking over at least 0.45 seconds. Position and scale use damped movement with bounded speed. Animation handoffs ease in and out. These filters affect display only; exercise scoring still receives the original landmarks.

The live boss player portrait now contains the entire stage-camera viewport, with transparent padding when the display aspect differs. It no longer fits a crop around the body, which used to cancel the distance and position measured by AvatarMirrorAnchor. The player stage camera also holds its initial framing through the push-up handoff, avoiding a second automatic zoom when the plank arms. Preparation portraits and opponent framing retain their body-fit presentation. Push-up bottom shoulder height is wrist height plus 56% of arm length (previously 64%); the pose solver retains fixed wrist and toe contacts.

Validation:

- `CameraPresentationRegression.Run`: transparent crop edges, full-camera framing without distortion across four display aspects, and bounded placement at 30/60/120 FPS.
- `RetargetRegression.Run`: both rigs, selfie modes, occlusion, close-camera entry, reacquisition and animation handoff.
- `PushupPosePreview.Run`: contact stability, bone lengths, ground clearance and both models across the repetition.
- `BossCombatValidation.Run`: actual scene bindings, fixed crop and visible wrists/fingers at top/middle/bottom, plus four display sizes. Phase screenshots use temporary CPU-skinned meshes because repeated editor camera renders can reuse an earlier skinning buffer.

Reports are under `Logs/` and `output/boss-combat/`. These deterministic editor checks do not measure webcam latency or tracking quality on a physical phone.

## Measurement waiting screen

The first BATTLE search (without a ghost recording) loads Fight through `OtaSceneLoader`.
Editor loads now use the authored scene directly, rather than a generated or cached OTA copy;
device builds still use OTA with the embedded fallback. The generated Fight copy was synchronized
with the authored scene, whose measurement backdrop is Main's `IMG_0916.PNG`.
OTA content builds also synchronize these source copies through `OtaSetup.Configure`.

The solo portrait has no horizontal offset. The waiting mirror pose opens the arms slightly
and places the feet approximately shoulder width apart. Each limb chain derives its side
from its shoulder/hip, so an inward-pointing imported knee cannot cross the relaxed legs.
These directions affect untracked/resting limbs; tracked directions and push-up correction
continue to use their existing inputs.

`RetargetRegression.Run` now includes waiting-stance clearance for both models and both selfie
settings (36 checks). `MeasurementPresentationValidation.Run` exercises the BATTLE loader from
Main in Play mode and checks the visible backdrop and portrait centering with CV disabled.
It records `output/measurement/waiting.png` and `output/measurement/validation.txt` without
performing reps or awarding rewards.

## Preparation framing

Preparation plays the complete StandIdle animation, including hips and legs, unchanged from the main-screen Idle clip. The stage camera frames once after idle evaluation. DuelReadyPanel retains that portrait crop and ground reference instead of re-fitting the animated body each frame. The crop refreshes when the card reopens, the stage/texture changes, or the portrait size changes. Cached editor portraits use StandIdle.

PreparationStanceRegression.Run checks both fighters on both rigs for 360 frames: fixed camera and crop, hip/leg/foot rotations matching the main-screen clip, moving idle, responsive resize, and released preparation framing on READY.

Results and boss preparation now also hold their portrait crop after the stage camera is framed, with refresh on reopening or display/texture changes. Results enter the same standing presentation without the old WarriorIdle override. RewardSummary uses a static portrait after the battle scene unloads, or copies supplied texture/UV once in FillSummary; its Update animates reward effects only. PreparationStanceRegression covers live portrait paths in both FightPreparation and FightResults (eight fighter/rig combinations), bypassing editor fallback portraits.
