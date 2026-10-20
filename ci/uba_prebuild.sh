#!/usr/bin/env bash
# Unity Build Automation pre-build script (set as "Pre-build script path" = ci/uba_prebuild.sh).
# Runs on the macOS builder BEFORE Unity opens the project. The MediaPipe plugin (391 MB) is
# gitignored, so we fetch the UPM tarball from the public release and drop it into Packages/,
# then copy the pose model into StreamingAssets (the device build can't use the editor-only loader).
set -euo pipefail

TGZ_URL="https://github.com/homuler/MediaPipeUnityPlugin/releases/download/v0.16.3/com.github.homuler.mediapipe-0.16.3.tgz"
DEST="Packages/com.github.homuler.mediapipe"
# FULL model (was lite): the lite skeleton falls apart at the bottom of a frontal rep — the
# owner's previous app ran full/heavy in the same setup and tracked cleanly. LITE ships too:
# it's the runtime fallback when the GPU delegate fails (full-on-CPU is a ~12fps trap).
MODEL="pose_landmarker_full.bytes"
MODEL_FALLBACK="pose_landmarker_lite.bytes"

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

# The plugin's own tests carry a Resources folder (lenna.png, thumb-up.jpg, an mp3), and every
# Resources folder ships in the player whether anything loads it or not — ~2 MB of nothing.
rm -rf "$DEST/Tests"

echo "[prebuild] copying models into StreamingAssets..."
mkdir -p Assets/StreamingAssets
cp -f "$DEST/PackageResources/MediaPipe/$MODEL" "Assets/StreamingAssets/$MODEL"
cp -f "$DEST/PackageResources/MediaPipe/$MODEL_FALLBACK" "Assets/StreamingAssets/$MODEL_FALLBACK"

# GoogleService-Info.plist (gitignored). Preferred source: the UBA env var
# GOOGLE_SERVICE_INFO_PLIST_B64 — base64 of the real plist, set in the UBA target's
# "Environment variables" (locally: `base64 -i Assets/GoogleService-Info.plist | pbcopy`).
# Fallback: a placeholder with dummy values so Firebase's iOS build scripts still link;
# the app then boots without a backend (AppBootstrap logs "Firebase unavailable" and the
# UI falls back to mock data).
if [ -n "${GOOGLE_SERVICE_INFO_PLIST_B64:-}" ]; then
  echo "[prebuild] writing REAL GoogleService-Info.plist from env var..."
  echo "$GOOGLE_SERVICE_INFO_PLIST_B64" | base64 --decode > "Assets/GoogleService-Info.plist"
elif [ ! -f "Assets/GoogleService-Info.plist" ]; then
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

# Resources/PostHogConfig.json (gitignored): without it Analytics sends nothing, silently.
# Source: the UBA env var POSTHOG_API_KEY — the "phc_…" project key of PushUp Stars (310522),
# public by design, so plain text, no base64. POSTHOG_HOST is optional (EU by default).
# A local file is kept when the variable is unset.
POSTHOG_CONFIG="Assets/_Project/Resources/PostHogConfig.json"
if [ -n "${POSTHOG_API_KEY:-}" ]; then
  POSTHOG_API_KEY="$(printf '%s' "$POSTHOG_API_KEY" | tr -d '[:space:]')"
  POSTHOG_HOST="${POSTHOG_HOST:-https://eu.i.posthog.com}"
  if ! printf '%s' "$POSTHOG_API_KEY" | grep -Eq '^phc_[A-Za-z0-9_-]+$'; then
    echo "[prebuild] ERROR: POSTHOG_API_KEY is not a phc_ project key" >&2
    exit 1
  fi
  if ! printf '%s' "$POSTHOG_HOST" | grep -Eq '^https://[A-Za-z0-9.-]+/?$'; then
    echo "[prebuild] ERROR: POSTHOG_HOST must look like https://eu.i.posthog.com" >&2
    exit 1
  fi
  echo "[prebuild] writing PostHogConfig.json from env var (key ${POSTHOG_API_KEY:0:8}…, host $POSTHOG_HOST)"
  mkdir -p "$(dirname "$POSTHOG_CONFIG")"
  cat > "$POSTHOG_CONFIG" <<JSON
{
  "apiKey": "$POSTHOG_API_KEY",
  "host": "$POSTHOG_HOST",
  "sendFromEditor": false
}
JSON
elif [ -f "$POSTHOG_CONFIG" ]; then
  echo "[prebuild] PostHogConfig.json already present — keeping it"
else
  echo "[prebuild] WARNING: POSTHOG_API_KEY not set — this build sends NO analytics"
fi

echo "[prebuild] done."
ls -la "$DEST/Runtime/Plugins" 2>/dev/null | head
