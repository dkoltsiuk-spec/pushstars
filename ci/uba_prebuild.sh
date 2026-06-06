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

# Placeholder GoogleService-Info.plist so Firebase's iOS build scripts (Crashlytics) don't fail.
# The CV test never initializes Firebase at runtime, so dummy values are fine. NOT committed.
if [ ! -f "Assets/GoogleService-Info.plist" ]; then
  echo "[prebuild] writing placeholder GoogleService-Info.plist..."
  cat > "Assets/GoogleService-Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
	<key>API_KEY</key><string>AIzaSyDUMMYDUMMYDUMMYDUMMYDUMMYDUMMY00</string>
	<key>GCM_SENDER_ID</key><string>000000000000</string>
	<key>PLIST_VERSION</key><string>1</string>
	<key>BUNDLE_ID</key><string>com.pushstars.app</string>
	<key>PROJECT_ID</key><string>push-stars-d620e</string>
	<key>STORAGE_BUCKET</key><string>push-stars-d620e.appspot.com</string>
	<key>IS_ADS_ENABLED</key><false/>
	<key>IS_ANALYTICS_ENABLED</key><false/>
	<key>IS_APPINVITE_ENABLED</key><true/>
	<key>IS_GCM_ENABLED</key><true/>
	<key>IS_SIGNIN_ENABLED</key><true/>
	<key>GOOGLE_APP_ID</key><string>1:000000000000:ios:0000000000000000000000</string>
</dict>
</plist>
PLIST
fi

echo "[prebuild] done."
ls -la "$DEST/Runtime/Plugins" 2>/dev/null | head
