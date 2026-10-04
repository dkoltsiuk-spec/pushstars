#!/usr/bin/env bash
# Phone video -> emote track (RTMW 2D + MediaPipe depth; Apache 2.0 models, commercially clean).
#   bash Tools/EmoteMocap/mocap.sh <video.mp4> <track name>
# Then Tools > Push Stars > Emotes > Build Emotes (the Captures table in EmoteClipAuthoring.cs maps
# track names to clips), or EmoteClipAuthoring.AuthorTrack for a one-off test clip.
set -e
ENV="${MOCAP_ENV:-/d/MocapTools/mocap-env}"
cd "$(dirname "$0")/../.."
"$ENV/Scripts/python.exe" Tools/EmoteMocap/extract_fused.py "$1" "Tools/EmoteMocap/tracks/$2.json" 2>&1 | grep -a "frames @\|dropped"
