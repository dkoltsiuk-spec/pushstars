#!/usr/bin/env bash
# Unity Build Automation pre-build script (set as "Pre-build script path" = ci/uba_prebuild.sh).
# Runs on the macOS builder BEFORE Unity opens the project. The MediaPipe plugin (391 MB) is
# gitignored, so we fetch the UPM tarball from the public release and drop it into Packages/,
# then copy the pose model into StreamingAssets (the device build can't use the editor-only loader).
set -euo pipefail

TGZ_URL="https://github.com/homuler/MediaPipeUnityPlugin/releases/download/v0.16.3/com.github.homuler.mediapipe-0.16.3.tgz"
DEST="Packages/com.github.homuler.mediapipe"
MODEL="pose_landmarker_lite.bytes"

echo "[prebuild] cwd=$(pwd)"

if [ -f "$DEST/package.json" ]; then
  echo "[prebuild] plugin already present — skipping download"
else
  echo "[prebuild] downloading MediaPipe plugin tarball..."
  curl -fL --retry 3 -o /tmp/mp.tgz "$TGZ_URL"
  rm -rf /tmp/mp_extract && mkdir -p /tmp/mp_extract
  tar xzf /tmp/mp.tgz -C /tmp/mp_extract
  PKGJSON="$(find /tmp/mp_extract -maxdepth 3 -name package.json | head -1)"
  PKGDIR="$(dirname "$PKGJSON")"
  echo "[prebuild] extracted package dir: $PKGDIR"
  mkdir -p "$DEST"
  cp -R "$PKGDIR"/. "$DEST"/
fi

echo "[prebuild] copying model into StreamingAssets..."
mkdir -p Assets/StreamingAssets
cp -f "$DEST/PackageResources/MediaPipe/$MODEL" "Assets/StreamingAssets/$MODEL"

echo "[prebuild] done."
ls -la "$DEST/Runtime/Plugins" 2>/dev/null | head
