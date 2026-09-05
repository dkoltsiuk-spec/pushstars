# Avatar retargeting

## Ownership and coordinates

Preparation uses `PoseMirrorRetargeter` for pose and `AvatarMirrorAnchor` for screen position/scale.
The anchor's **Mirror X** setting is the single reflection setting for bones, placement and camera
previews. Fight uses selfie mode. Do not add another horizontal flip to the character, stage texture
or skeleton overlay.

Input world landmarks remain hip-centered meters in upright image axes: X right, Y down, Z away
from the sensor. The solver maps these through the actual stage camera rotation, converts into
the avatar root frame, and exchanges anatomical left/right pairs for a selfie reflection.
Depth is not flattened or angle-clamped. Rig segment lengths are retained.

Sensor rotation and raw vertical correction are shared through `CameraFrameOrientation` and the
optional camera-orientation provider. Automatic rotation reads metadata from actual video frames;
an explicit source rotation remains available for device overrides. Sensor corrections happen
before upright rotation, and selfie reflection happens afterwards. Each asynchronous inference
result uses its capture-time orientation/aspect, not the latest phone orientation. This prevents
late results from being interpreted in a different coordinate frame after rotating the device.
If the camera changes raw pixel dimensions, the source drains inference and restarts capture
automatically; the avatar holds/reacquires during that short interruption.

The torso basis carries body-local limb directions. Visible limb segments track independently;
hidden segments hold briefly, then relax. Joint confidence, finite-coordinate guards, adaptive
filtering and angular speed limits protect against spikes. A repeated capture timestamp expires
after 0.35 seconds; it is not a new detection. Acquisition requires at least three distinct samples.
Rep-counting `TrackingQuality` does not gate the visual pose: a profile may be valid visually while
insufficient for scoring. Raw scoring landmarks and anti-cheat rules are unchanged.

Update order: source/session → mirror ownership (100) → push-up clip driver (150).
LateUpdate order: pose/blend (100) → hip placement (200) → fight camera framing (300).
When the plank arms, the evaluated clip receives ownership through a 0.35-second local-pose blend.
At zero mirror weight the retargeter writes no bone transforms. Disarming requires fresh camera
samples and blends back from the displayed clip pose. The camera holds throughout preparation.

## Repeatable Unity checks

Run **Tools → Push Stars → CV → Validate Avatar Retargeting** outside Play mode. This exercises
the imported male/female humanoids in disposable preview scenes and writes
`Logs/retarget-regression.txt`. It does not rebuild, save or replace the open scene. Synthetic tests
are regression checks, not evidence of real-camera accuracy or device performance.

For live-camera comparison, use a separate disposable test scene and run
**Tools → Push Stars → CV → Build Avatar Hybrid Test (mirror → pushup animation)**, then Play.
The builder replaces existing test-stand objects in that scene, so do not run it on a scene with
unsaved work. The status line reports fresh tracking, tracked limb segments, mirror mode and anchor
state. Also test the normal preparation flow from Boot → Main → Battle on the target phone.

| Action | Expected result |
| --- | --- |
| Stand fully visible, then move to each side | Avatar follows the same screen side as the selfie preview. |
| Slowly turn left/right through profile | Torso turns in depth; visible limbs continue tracking. |
| Reach one hand toward the camera, then bend the elbow | Arm depth and elbow bend remain continuous. |
| Hide the far arm behind the torso | No whole-body reset; hidden segments hold, then relax. |
| Approach/retreat without turning | Avatar size changes smoothly; turning alone does not collapse scale. |
| Leave the frame or stop the source | Tracking expires; no frozen input is presented as live movement. |
| Return and hold briefly | Stable reacquisition without snapping across the screen. |
| Enter plank, perform reps, then disarm | Smooth animation handoff; no repeated neutral-pose overwrite. |

Repeat on desktop webcam and target phone, portrait and supported sensor rotations, both avatars,
and typical lighting/clothing. A single RGB camera estimates depth and cannot observe fully hidden
limbs; holding/relaxing uncertain segments is intentional, not reconstructed ground truth.
