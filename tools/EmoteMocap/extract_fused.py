"""Video -> fused pose track: RTMW 2D + MediaPipe depth. Commercially clean (Apache 2.0 models).

  mocap-env\\Scripts\\python Tools/EmoteMocap/extract_fused.py <video.mp4> <out.json>

Why two models. RTMW (OpenMMLab, whole-body, 133 points) places joints and finger joints in the
picture more precisely than MediaPipe; MediaPipe gives metric 3D (bone lengths, which way a bone
leans toward or away from the lens). Each bone is rebuilt from both: its offset in the picture comes
from RTMW, scaled to metres; its length from MediaPipe; the missing depth is whatever keeps that
length, signed by MediaPipe. So a limb or finger pointing at the lens gets depth from its
foreshortening, which is exact, instead of from a depth guess.

The torso, head and hips keep MediaPipe's 3D. Output is the MediaPipe-layout track the Unity
importer reads (Assets/_Project/Editor/EmoteClipAuthoring.cs), already lifted to 3D; the picture track goes along only for the importer's head-contact test.

Needs: pip install rtmlib onnxruntime-gpu mediapipe opencv-python scipy, plus nvidia-cudnn-cu12
nvidia-cublas-cu12 nvidia-cuda-runtime-cu12 nvidia-cufft-cu12 nvidia-curand-cu12 for the GPU (a Python 3.10 venv works;
see Tools/EmoteMocap/README.md). RTMW weights are fetched by rtmlib from download.openmmlab.com.
"""
import argparse
import json
import sys
from pathlib import Path

import cv2
import numpy as np
import mediapipe as mp
import onnxruntime
from rtmlib import Wholebody

# CUDA/cuDNN come as pip wheels (nvidia-cudnn-cu12 etc.); load them so RTMW runs on the GPU.
if hasattr(onnxruntime, 'preload_dlls'):
    try:
        onnxruntime.preload_dlls()
    except Exception:
        pass

sys.path.insert(0, str(Path(__file__).resolve().parent))
from extract_pose import landmarkers, fill_gaps, smooth  # noqa: E402

# MediaPipe pose index <- COCO-WholeBody index, for the points taken from RTMW's picture.
RTM_BODY = {11: 5, 12: 6, 13: 7, 14: 8, 15: 9, 16: 10}
RTM_HAND_START = {"left": 91, "right": 112}
# Hand skeleton (MediaPipe / OpenPose order): parent of each of the 21 points.
HAND_PARENT = [-1, 0, 1, 2, 3, 0, 5, 6, 7, 0, 9, 10, 11, 0, 13, 14, 15, 0, 17, 18, 19]


def lift(planar, length, depth_hint):
    """A bone's 3D offset from its exact 2D offset (metres, x right, y down), its true length and
    the sign of its depth (z away from the lens)."""
    p = np.linalg.norm(planar)
    depth = np.sqrt(max(0., length * length - p * p))
    sign = np.clip(depth_hint / max(1e-3, .25 * length), -1., 1.)
    return np.array([planar[0], planar[1], depth * sign])


def pick_person(keypoints, scores):
    if keypoints is None or len(keypoints) == 0:
        return None, None
    best = int(np.argmax(scores.mean(axis=1)))
    return keypoints[best], scores[best]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('video'); ap.add_argument('out')
    ap.add_argument('--device', default='cuda')
    ap.add_argument('--window', type=int, default=9)
    args = ap.parse_args()

    cap = cv2.VideoCapture(args.video)
    fps = cap.get(cv2.CAP_PROP_FPS) or 24.
    W, H = cap.get(cv2.CAP_PROP_FRAME_WIDTH), cap.get(cv2.CAP_PROP_FRAME_HEIGHT)
    pose_lm, hand_lm = landmarkers()
    rtmw = Wholebody(mode='performance', backend='onnxruntime', device=args.device)

    body, rtm, rtm_score, hands = [], [], [], {'left': [], 'right': []}
    index = 0
    while True:
        ok, frame = cap.read()
        if not ok:
            break
        rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
        p = pose_lm.detect_for_video(mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb), int(round(index / fps * 1000)))
        index += 1
        body.append([[l.x, l.y, l.z] for l in p.pose_world_landmarks[0]] if p.pose_world_landmarks else None)
        k, s = pick_person(*rtmw(frame))
        rtm.append(k / H if k is not None else None)  # frame-height units, x right, y down
        rtm_score.append(s if s is not None else None)
        found = {'left': None, 'right': None}
        if p.pose_landmarks:
            lm = p.pose_landmarks[0]
            for side, ids in (('left', (15, 17, 19, 21)), ('right', (16, 18, 20, 22))):
                cx, cy = np.mean([lm[i].x for i in ids]) * W, np.mean([lm[i].y for i in ids]) * H
                half = int(.09 * H)
                x0, y0, x1, y1 = int(max(0, cx - half)), int(max(0, cy - half)), int(min(W, cx + half)), int(min(H, cy + half))
                if x1 - x0 < 20 or y1 - y0 < 20:
                    continue
                crop = cv2.resize(np.ascontiguousarray(rgb[y0:y1, x0:x1]), (512, 512), interpolation=cv2.INTER_CUBIC)
                r = hand_lm.detect(mp.Image(image_format=mp.ImageFormat.SRGB, data=crop))
                if r.hand_world_landmarks:
                    found[side] = [[l.x, l.y, l.z] for l in r.hand_world_landmarks[0]]
        for side in hands:
            hands[side].append(found[side])
        if index % 30 == 0:
            print(f'  frame {index}', flush=True)

    frames = len(body)
    # Frames where either model loses the torso (collapsed or swapped shoulders) are bridged.
    world_w = np.array([np.linalg.norm(np.subtract(v[11], v[12])) if v is not None else np.nan for v in body])
    image_w = np.array([(v[5][0] - v[6][0]) if v is not None else np.nan for v in rtm])  # subject's left is image right
    bad = {i for i in range(frames) if (world_w[i] < .7 * np.nanmedian(world_w)) or (image_w[i] < .6 * np.nanmedian(image_w))}
    for i in sorted(bad | {j for i in bad for j in range(i - 2, i + 3) if 0 <= j < frames}):
        body[i] = None; rtm[i] = None
    if bad:
        print(f'  dropped {len(bad)} unreliable torso frames (+2 either side)')
    body = smooth(fill_gaps(body), args.window)          # (F, 33, 3) metres
    rtm = smooth(fill_gaps(rtm), args.window)            # (F, 133, 2) frame heights
    scores = fill_gaps(rtm_score)
    mp_hands = {s: fill_gaps(v) for s, v in hands.items()}

    # Metres per frame-height, per frame, from the torso: MediaPipe's metric shoulders-to-hips over
    # RTMW's picture of the same span.
    torso_world = np.linalg.norm((body[:, 11] + body[:, 12]) / 2 - (body[:, 23] + body[:, 24]) / 2, axis=1)
    torso_image = np.linalg.norm((rtm[:, 5] + rtm[:, 6]) / 2 - (rtm[:, 11] + rtm[:, 12]) / 2, axis=1)
    scale = torso_world / np.maximum(1e-4, torso_image)

    # Arms: shoulders stay MediaPipe's; elbow and wrist are rebuilt bone by bone.
    fused = body.copy()
    for sh, el, wr in ((11, 13, 15), (12, 14, 16)):
        for a, b in ((sh, el), (el, wr)):
            planar = (rtm[:, RTM_BODY[b]] - rtm[:, RTM_BODY[a]]) * scale[:, None]
            length = np.percentile(np.linalg.norm(planar, axis=1), 95)
            for f in range(frames):
                fused[f, b] = fused[f, a] + lift(planar[f], length, body[f, b, 2] - body[f, a, 2])

    # The picture track (RTMW, MediaPipe layout, frame-height units) lets the importer tell a hand
    # that touches the head (the "L") from one merely near it.
    image = np.zeros((frames, 33, 2))
    for mp_index, coco in {0: 0, 2: 1, 5: 2, 7: 3, 8: 4, 9: 71, 10: 77, 11: 5, 12: 6, 13: 7, 14: 8, 15: 9, 16: 10,
                           23: 11, 24: 12}.items():
        image[:, mp_index] = rtm[:, coco]
    track = {'fps': fps, 'frames': frames, 'body': fused.reshape(-1).tolist(),
             'bodyImage': image.reshape(-1).tolist(), 'aspect': 1.0}
    for side in ('left', 'right'):
        world = mp_hands[side]
        start = RTM_HAND_START[side]
        coverage = sum(v is not None for v in hands[side]) / max(1, frames)
        if world is None:
            track[side] = []; track[side + 'Coverage'] = 0.; continue
        # Bone lengths from MediaPipe's metric hand (median over the clip).
        lengths = [np.median(np.linalg.norm(world[:, i] - world[:, HAND_PARENT[i]], axis=1)) if i else 0.
                   for i in range(21)]
        out = np.zeros((frames, 21, 3))
        for f in range(frames):
            s = scores[f][start:start + 21] if scores[f] is not None else np.zeros(21)
            reliable = np.mean(s) > .35
            for i in range(1, 21):
                parent = HAND_PARENT[i]
                hint = world[f, i, 2] - world[f, parent, 2]
                if reliable:
                    planar = (rtm[f, start + i] - rtm[f, start + parent]) * scale[f]
                    out[f, i] = out[f, parent] + lift(planar, lengths[i], hint)
                else:  # RTMW unsure of this hand in this frame: MediaPipe's own 3D
                    out[f, i] = out[f, parent] + (world[f, i] - world[f, parent])
        out = smooth(out, max(5, args.window - 2))
        track[side] = out.reshape(-1).tolist()
        track[side + 'Coverage'] = coverage
    Path(args.out).parent.mkdir(parents=True, exist_ok=True)
    Path(args.out).write_text(json.dumps(track))
    print(f'{Path(args.video).name}: {frames} frames @ {fps:.0f} fps, hands L {track["leftCoverage"]:.0%} R {track["rightCoverage"]:.0%}')


if __name__ == '__main__':
    main()
