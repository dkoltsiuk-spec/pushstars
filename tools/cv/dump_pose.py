"""Dump MediaPipe pose landmarks of a video into the RecordedPoseSource CSV format.

Uses the same pose_landmarker_full model the game ships, so replays in the editor see what the
device would. Needs a Python with `mediapipe` + `opencv-python-headless` (mediapipe has no
wheels for the newest Python — make a venv with one it supports).

    python tools/cv/dump_pose.py CVRecordings/clap/1_clap.MOV CVRecordings/clap/1_clap.pose.csv

Record upright (portrait) with the phone where the game expects it: on the floor, 1.5-2 m in
front. Raw videos stay local (.gitignore); commit the .pose.csv fixtures. Replay/trace a
recording against the live counter with tools/cv/ReplayPose.cs:

    unity command run_script --file tools/cv/ReplayPose.cs --entry ReplayPose.Run         --args '["CVRecordings/clap/1_clap.pose.csv", 11.5, 13.5, 1]'
"""
import cv2, mediapipe as mp, sys
from mediapipe.tasks.python import vision, BaseOptions
src, dst = sys.argv[1], sys.argv[2]
opts=vision.PoseLandmarkerOptions(base_options=BaseOptions(model_asset_path=r"D:\Push Stars\Assets\StreamingAssets\pose_landmarker_full.bytes"),running_mode=vision.RunningMode.VIDEO)
lm=vision.PoseLandmarker.create_from_options(opts)
cap=cv2.VideoCapture(src)
w,h=cap.get(cv2.CAP_PROP_FRAME_WIDTH),cap.get(cv2.CAP_PROP_FRAME_HEIGHT)
out=open(dst,'w',newline='\n')
out.write(f"# pose-recording v1 aspect={w/h:.5f} src={src.split(chr(92))[-1]}\n")
out.write("# t, 33 x (x y z vis) image-normalized, 33 x (x y z) world meters; empty landmark block = no pose\n")
last=-1
while True:
    ok,fr=cap.read()
    if not ok: break
    ms=int(cap.get(cv2.CAP_PROP_POS_MSEC))
    if ms<=last: ms=last+1
    last=ms
    r=lm.detect_for_video(mp.Image(image_format=mp.ImageFormat.SRGB,data=cv2.cvtColor(fr,cv2.COLOR_BGR2RGB)),ms)
    parts=[f"{ms/1000:.3f}"]
    if r.pose_landmarks:
        parts+= [f"{p.x:.4f},{p.y:.4f},{p.z:.4f},{p.visibility:.3f}" for p in r.pose_landmarks[0]]
        parts+= [f"{p.x:.4f},{p.y:.4f},{p.z:.4f}" for p in r.pose_world_landmarks[0]]
    out.write(",".join(parts)+"\n")
print("frames written, last ms", last)
