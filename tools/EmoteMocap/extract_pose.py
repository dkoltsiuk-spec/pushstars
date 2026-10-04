"""Video -> pose track for emote mocap. Python + MediaPipe Tasks + OpenCV.

Runs the pose landmarker (33 body points, metric world coordinates) and the hand landmarker
(21 points per hand) over a reference video of our hero, assigns each detected hand to the
body side whose wrist it sits on, smooths every track over time and writes JSON that
Tools > Push Stars > Emotes > Import Mocap turns into a humanoid clip.

  python Tools/EmoteMocap/extract_pose.py <video.mp4> <out.json> [--start S] [--end S]

Coordinates are MediaPipe's: x to the image's right, y down, z away from the camera.
"""
import argparse
import json
from pathlib import Path

import cv2
import numpy as np
import mediapipe as mp
from scipy.signal import savgol_filter

ROOT = Path(__file__).resolve().parents[2]
POSE_MODEL = ROOT / 'Assets' / 'StreamingAssets' / 'pose_landmarker_full.bytes'
HAND_MODEL = ROOT / 'Packages' / 'com.github.homuler.mediapipe' / 'PackageResources' / 'MediaPipe' / 'hand_landmarker.bytes'

vision = mp.tasks.vision
BaseOptions = mp.tasks.BaseOptions


def landmarkers():
    pose = vision.PoseLandmarker.create_from_options(vision.PoseLandmarkerOptions(
        base_options=BaseOptions(model_asset_buffer=POSE_MODEL.read_bytes()),
        running_mode=vision.RunningMode.VIDEO, num_poses=1,
        min_pose_detection_confidence=.5, min_tracking_confidence=.5))
    # Per-frame IMAGE mode on a zoomed crop around each wrist: fists, a hand against the face
    # and a hand pointed at the lens are all missed at full-frame scale.
    hands = vision.HandLandmarker.create_from_options(vision.HandLandmarkerOptions(
        base_options=BaseOptions(model_asset_buffer=HAND_MODEL.read_bytes()),
        running_mode=vision.RunningMode.IMAGE, num_hands=1,
        min_hand_detection_confidence=.2, min_hand_presence_confidence=.2))
    return pose, hands


def fill_gaps(track):
    """Linear interpolation over frames where a track is missing (None)."""
    n = len(track)
    known = [i for i, v in enumerate(track) if v is not None]
    if not known: return None
    shape = np.asarray(track[known[0]]).shape
    out = np.zeros((n,) + shape)
    for k in range(len(known)):
        out[known[k]] = track[known[k]]
    for i in range(n):
        if track[i] is not None: continue
        before = [k for k in known if k < i]
        after = [k for k in known if k > i]
        if before and after:
            a, b = before[-1], after[0]
            u = (i - a) / (b - a)
            out[i] = (1 - u) * out[a] + u * out[b]
        else:
            out[i] = out[before[-1] if before else after[0]]
    return out


def smooth(x, window):
    if x is None or len(x) < 5: return x
    w = min(window, len(x) - (1 - len(x) % 2))
    if w < 5: return x
    return savgol_filter(x, w, 2, axis=0)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('video'); ap.add_argument('out')
    ap.add_argument('--start', type=float, default=0.); ap.add_argument('--end', type=float, default=1e9)
    ap.add_argument('--window', type=int, default=9)
    args = ap.parse_args()

    cap = cv2.VideoCapture(args.video)
    fps = cap.get(cv2.CAP_PROP_FPS) or 24.
    pose_lm, hand_lm = landmarkers()
    body, body_img, hands = [], [], {'left': [], 'right': []}
    index = 0
    width = cap.get(cv2.CAP_PROP_FRAME_WIDTH); height = cap.get(cv2.CAP_PROP_FRAME_HEIGHT)
    while True:
        ok, frame = cap.read()
        if not ok: break
        t = index / fps
        index += 1
        rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
        image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb)
        ms = int(round(t * 1000))
        p = pose_lm.detect_for_video(image, ms)
        if t < args.start or t > args.end: continue
        if p.pose_world_landmarks:
            body.append([[l.x, l.y, l.z] for l in p.pose_world_landmarks[0]])
            body_img.append([[l.x, l.y] for l in p.pose_landmarks[0]])
        else:
            body.append(None); body_img.append(None)
        found = {'left': None, 'right': None}
        if p.pose_landmarks:
            H, W = rgb.shape[:2]
            lm = p.pose_landmarks[0]
            for side, ids in (('left', (15, 17, 19, 21)), ('right', (16, 18, 20, 22))):
                cx = np.mean([lm[i].x for i in ids]) * W
                cy = np.mean([lm[i].y for i in ids]) * H
                half = int(.09 * H)
                x0, y0 = int(max(0, cx - half)), int(max(0, cy - half))
                x1, y1 = int(min(W, cx + half)), int(min(H, cy + half))
                if x1 - x0 < 20 or y1 - y0 < 20: continue
                crop = cv2.resize(np.ascontiguousarray(rgb[y0:y1, x0:x1]), (512, 512), interpolation=cv2.INTER_CUBIC)
                r = hand_lm.detect(mp.Image(image_format=mp.ImageFormat.SRGB, data=crop))
                if r.hand_world_landmarks:
                    found[side] = [[l.x, l.y, l.z] for l in r.hand_world_landmarks[0]]
        for side in hands: hands[side].append(found[side])

    frames = len(body)
    # The model occasionally loses the torso for a few frames when an arm sweeps across the
    # chest: the shoulders collapse to a few cm apart, or the left and right sides swap (the far
    # arm then swings behind the back and the head turns away). Drop those frames — 3D and 2D,
    # and their neighbours — and let the gap filler bridge them from the good frames around.
    widths = [np.linalg.norm(np.subtract(v[11], v[12])) if v is not None else np.nan for v in body]
    image_widths = [(v[11][0] - v[12][0]) if v is not None else np.nan for v in body_img]  # subject's left is image right
    median, image_median = np.nanmedian(widths), np.nanmedian(image_widths)
    bad = {i for i in range(frames)
           if (not np.isnan(widths[i]) and widths[i] < .7 * median)
           or (not np.isnan(image_widths[i]) and image_widths[i] < .6 * image_median)}
    for i in sorted(bad | {j for i in bad for j in range(i - 2, i + 3) if 0 <= j < frames}):
        body[i] = None
        body_img[i] = None
    if bad: print(f'  dropped {len(bad)} unreliable torso frames (+2 either side): {sorted(bad)}')
    body = smooth(fill_gaps(body), args.window)
    body_img = smooth(fill_gaps(body_img), args.window)
    # Flat arrays (frame-major, x y z per point) so Unity's JsonUtility can read them.
    # bodyImage: normalized image x y per point. The 2D positions are what the model is sure of;
    # its depth is a guess that flattens limbs reaching toward the lens.
    out = {'fps': fps, 'frames': frames, 'body': body.reshape(-1).tolist(),
           'bodyImage': body_img.reshape(-1).tolist(), 'aspect': width / height}
    for side, track in hands.items():
        filled = smooth(fill_gaps(track), max(5, args.window - 2))
        out[side + 'Coverage'] = sum(v is not None for v in track) / max(1, frames)
        out[side] = filled.reshape(-1).tolist() if filled is not None else []
    Path(args.out).parent.mkdir(parents=True, exist_ok=True)
    Path(args.out).write_text(json.dumps(out))
    print(f'{Path(args.video).name}: {frames} frames @ {fps:.0f} fps, hands L {out["leftCoverage"]:.0%} '
          f'R {out["rightCoverage"]:.0%}')


if __name__ == '__main__':
    main()
