#!/usr/bin/env bash
# Unity Build Automation post-build script — uploads the iOS IPA to App Store Connect, from where
# it appears in TestFlight. Set it as the iOS target's "Post-build script path" =
# ci/uba_postbuild_testflight.sh. (UBA itself has no App Store upload step; this is Unity's own
# recipe: xcrun altool with an App Store Connect API key.)
#
# The target must be signed with an App Store provisioning profile (not Ad Hoc) and these
# environment variables set on it:
#   ASC_KEY_ID      Key ID of the App Store Connect API key (e.g. 2X9R4HXF34)
#   ASC_ISSUER_ID   Issuer ID shown above the key list (a UUID)
#   ASC_KEY_P8_B64  base64 of the AuthKey_<KEY_ID>.p8 file
#                   Windows PowerShell: [Convert]::ToBase64String([IO.File]::ReadAllBytes("AuthKey_XXXX.p8"))
#
# Without them the script does nothing and exits 0, so an Ad Hoc (Diawi) target can share it.
set -euo pipefail

if [ -z "${ASC_KEY_ID:-}" ] || [ -z "${ASC_ISSUER_ID:-}" ] || [ -z "${ASC_KEY_P8_B64:-}" ]; then
  echo "[testflight] ASC_KEY_ID / ASC_ISSUER_ID / ASC_KEY_P8_B64 not set — skipping upload."
  exit 0
fi

# UBA passes the player path; for iOS it is the .ipa (or a folder holding it).
IPA="${UNITY_PLAYER_PATH:-${1:-}}"
if [ -d "$IPA" ]; then IPA="$(find "$IPA" -maxdepth 3 -name '*.ipa' | head -1)"; fi
if [ -z "$IPA" ] || [ ! -f "$IPA" ]; then
  echo "[testflight] No .ipa found (UNITY_PLAYER_PATH='${UNITY_PLAYER_PATH:-}'). Is this an iOS build with export enabled?" >&2
  exit 1
fi
echo "[testflight] uploading $IPA"

# altool looks for the key in ~/private_keys/AuthKey_<KEY_ID>.p8.
KEY_DIR="$HOME/private_keys"
mkdir -p "$KEY_DIR"
echo "$ASC_KEY_P8_B64" | base64 --decode > "$KEY_DIR/AuthKey_${ASC_KEY_ID}.p8"
chmod 600 "$KEY_DIR/AuthKey_${ASC_KEY_ID}.p8"
trap 'rm -f "$KEY_DIR/AuthKey_${ASC_KEY_ID}.p8"' EXIT

xcrun altool --upload-app -t ios -f "$IPA" --apiKey "$ASC_KEY_ID" --apiIssuer "$ASC_ISSUER_ID" --output-format normal
echo "[testflight] upload finished — the build shows up in App Store Connect › TestFlight after processing (10–30 min)."
