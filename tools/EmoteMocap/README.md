# Emote mocap

Free gesture emotes (HELLO, GG, BOO, YOU!, LOSER) are motion-captured from reference videos of
our own hero (generated in Higgsfield on a green screen, 2026-09-30), not keyframed by hand.

1. `reference/<name>.mp4` — the source videos (`loser_keypose.png` is the owner's key pose for the "L").
2. `python Tools/EmoteMocap/extract_pose.py reference/<name>.mp4 tracks/<name>.json`
   MediaPipe pose (33 points, 3D + 2D) and hand landmarkers (21 points per hand, found in a
   zoomed crop around each wrist so fists and hands at the face are caught). Frames where the
   model loses the torso (shoulders collapse or swap sides as an arm sweeps across) are dropped
   and bridged; every track is Savitzky–Golay smoothed. Needs `pip install mediapipe opencv-python scipy`.
   Models come from the project (`Assets/StreamingAssets`, the Homuler MediaPipe package).
3. `Tools ▸ Push Stars ▸ Emotes ▸ Build Emotes` → `EmoteClipAuthoring` retargets every frame onto
   the humanoid rig by solving muscles:
   - torso and head follow their rotation relative to the video's standing frame (clamped to
     what a gesture uses — the model's depth can swing a torso round when an arm is raised);
   - arms follow segment directions **lifted from the 2D picture** (exact) with only depth taken
     from the model — its own depth flattens an arm reaching toward the lens;
   - hand roll from the thumb and knuckles; fingers from segment directions, with a clearly
     folded finger (tip hardly further from the wrist than the knuckle) closed outright;
   - a hand that touches the head in the video (the "L") is placed relative to the head; any
     other hand keeps clear of our hero's (bigger) head as the lens sees it;
   - legs and breathing come from the menu idle, and the root is solved per frame so the hips
     stay where the idle has them (otherwise a raised arm swings the pelvis and the feet skate).
   The standing lead-in and tail of each video are trimmed (`Captures` table holds overrides).

## Recording your own gestures (the production path)

One phone on a tripod at chest height, 2–3 m away, portrait, the whole body in frame, even light,
sleeves off the hands; stand still a second before and after; make the gesture big, facing the lens.

**Recording in the app (Emote Lab).** A Development build shows an EMOTE LAB button on the home
screen, next to BOT LAB (`Assets/_Project/Scripts/Fight/EmoteLabScreen.cs`, built at runtime, absent
from release builds). Pick the emote name, take length (3/5/8 s) and countdown (5/8/12 s), press
RECORD, step back; the countdown beeps once a second in huge digits, the frame turns red while
recording, a chime ends the take. Frames are stored as the sensor delivers them (JPEG ~24 fps with
capture times + sensor rotation), saved on the device first, then uploaded in ≤2.5 MB chunks by the
cloud function `saveEmoteCaptureChunk` to the private Storage folder
`emote-captures/<uid>/<id>/` (12 takes per account; RETRY CLOUD SAVE re-sends a take that failed).
On the desktop:

    node Tools/EmoteMocap/pull_captures.js --delete      # -> Tools/EmoteMocap/captures/<name>_<id8>.mp4
    bash Tools/EmoteMocap/mocap.sh Tools/EmoteMocap/captures/<name>_<id8>.mp4 <name>

`pull_captures.js` uses this machine's `firebase login`, turns the frames upright (`--hflip` if a
device delivers its front camera mirrored) and keeps real timing from the capture times.
`captures/` is git-ignored: it is the owner on camera.

`bash Tools/EmoteMocap/mocap.sh <video.mp4> <name>` runs `extract_fused.py`: **RTMW** (OpenMMLab
whole-body, 133 points, Apache 2.0, via `rtmlib`/ONNX) for where every joint and finger joint is in
the picture, **MediaPipe** (Apache 2.0) for bone lengths and which way each bone leans in depth;
each bone's depth is rebuilt from its exact foreshortening at its true length. Environment (outside
the repo): `uv venv D:/MocapTools/mocap-env --python 3.10`, then
`uv pip install rtmlib onnxruntime-gpu mediapipe opencv-python scipy nvidia-cudnn-cu12
nvidia-cublas-cu12 nvidia-cuda-runtime-cu12 nvidia-cufft-cu12 nvidia-curand-cu12`.

GVHMR + HaMeR (SMPL-X/MANO) were evaluated too (`D:/MocapTools`, `*_GVHMR.anim`, editor-only preview
via Tools ▸ Push Stars ▸ Emotes ▸ Preview Test Clips). Their licences forbid commercial output — they
are not a production path. On the AI-generated references all three trackers came out close; the
source, not the tracker, is the limit there.

New gesture: drop a video into `reference/`, extract, add a row to `Captures` in
`Assets/_Project/Editor/EmoteClipAuthoring.cs` and to the emote `Table` in `EmoteSetup.cs`.
