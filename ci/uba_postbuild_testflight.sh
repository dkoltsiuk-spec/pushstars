#!/usr/bin/env bash
# Unity Build Automation post-build script — uploads the iOS IPA to App Store Connect, from where
# it appears in TestFlight. Set it as the iOS target's "Post-build script path" =
# ci/uba_postbuild_testflight.sh. (UBA itself has no App Store upload step; this is Unity's own
# recipe: xcrun altool with an App Store Connect API key.)
#
# The target must be signed with an App Store provisioning profile (not Ad Hoc) and these
# environment variables set on it:
#   ASC_KEY_ID      Key ID of the App Store Connect API key (e.g. 2X9R4HXF34)
#   ASC_ISSUER_ID   Issuer ID shown above the key list (a UUID). Must be a TEAM key
#                   (Users and Access › Integrations › App Store Connect API › Team Keys).
#   ASC_KEY_P8_B64  base64 of the AuthKey_<KEY_ID>.p8 file
#                   Windows PowerShell: [Convert]::ToBase64String([IO.File]::ReadAllBytes("AuthKey_XXXX.p8"))
#   ASC_APP_ID      optional but recommended: the app's numeric Apple ID (App Store Connect ›
#                   the app › App Information › Apple ID). With it the upload names the bundle
#                   explicitly instead of letting altool guess — the IPA also carries
#                   MediaPipeUnity.framework, whose own Info.plist says com.github.homuler.mediapipe.unity.
# Check the key from a PC before a 40-minute build: node ci/check_asc_key.mjs (see that file).
#
# Without the key variables the script does nothing and exits 0, so an Ad Hoc (Diawi) target can
# share it. With them, a failed upload fails the build (altool itself exits 0 on auth errors).
set -euo pipefail

# Values pasted from Windows often carry a trailing CR or spaces, which turns into a wrong "kid" /
# "iss" in the token and a bare 401.
trim() { printf '%s' "$1" | tr -d '[:space:]'; }
ASC_KEY_ID="$(trim "${ASC_KEY_ID:-}")"
ASC_ISSUER_ID="$(trim "${ASC_ISSUER_ID:-}")"
ASC_KEY_P8_B64="$(trim "${ASC_KEY_P8_B64:-}")"
ASC_APP_ID="$(trim "${ASC_APP_ID:-}")"

if [ -z "$ASC_KEY_ID" ] || [ -z "$ASC_ISSUER_ID" ] || [ -z "$ASC_KEY_P8_B64" ]; then
  echo "[testflight] ASC_KEY_ID / ASC_ISSUER_ID / ASC_KEY_P8_B64 not set — skipping upload."
  exit 0
fi

fail() { echo "[testflight] FAILED: $*" >&2; exit 1; }

[[ "$ASC_KEY_ID" =~ ^[A-Z0-9]{10}$ ]] || fail "ASC_KEY_ID '$ASC_KEY_ID' is not a 10-character key ID."
[[ "$ASC_ISSUER_ID" =~ ^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$ ]] \
  || fail "ASC_ISSUER_ID '$ASC_ISSUER_ID' is not a UUID (it is the Issuer ID above the Team Keys list)."

# UBA passes the player path; for iOS it is the .ipa (or a folder holding it).
IPA="${UNITY_PLAYER_PATH:-${1:-}}"
if [ -d "$IPA" ]; then IPA="$(find "$IPA" -maxdepth 3 -name '*.ipa' | head -1)"; fi
if [ -z "$IPA" ] || [ ! -f "$IPA" ]; then
  fail "no .ipa found (UNITY_PLAYER_PATH='${UNITY_PLAYER_PATH:-}'). Is this an iOS build with export enabled?"
fi

# Read the identity from the MAIN app's Info.plist, not from any embedded framework.
WORK="$(mktemp -d)"
PLIST_ENTRY="$(unzip -Z1 "$IPA" | grep -E '^Payload/[^/]+\.app/Info\.plist$' | head -1)"
[ -n "$PLIST_ENTRY" ] || fail "no Payload/*.app/Info.plist inside $IPA."
unzip -p "$IPA" "$PLIST_ENTRY" > "$WORK/Info.plist"
pb() { /usr/libexec/PlistBuddy -c "Print :$1" "$WORK/Info.plist"; }
BUNDLE_ID="$(pb CFBundleIdentifier)"
VERSION="$(pb CFBundleShortVersionString)"
BUILD="$(pb CFBundleVersion)"
echo "[testflight] uploading $IPA"
echo "[testflight] app: $BUNDLE_ID $VERSION ($BUILD), key $ASC_KEY_ID, issuer $ASC_ISSUER_ID"

# altool looks for the key in ~/private_keys/AuthKey_<KEY_ID>.p8.
KEY_DIR="$HOME/private_keys"
KEY_FILE="$KEY_DIR/AuthKey_${ASC_KEY_ID}.p8"
mkdir -p "$KEY_DIR"
trap 'rm -f "$KEY_FILE"; rm -rf "$WORK"' EXIT
printf '%s' "$ASC_KEY_P8_B64" | base64 --decode > "$KEY_FILE" || fail "ASC_KEY_P8_B64 is not valid base64."
chmod 600 "$KEY_FILE"
grep -q -- '-----BEGIN PRIVATE KEY-----' "$KEY_FILE" \
  || fail "ASC_KEY_P8_B64 does not decode to a .p8 key (no BEGIN PRIVATE KEY line) — re-encode the AuthKey file."

AUTH=(--apiKey "$ASC_KEY_ID" --apiIssuer "$ASC_ISSUER_ID")
if [ -n "$ASC_APP_ID" ]; then
  CMD=(xcrun altool --upload-package "$IPA" -t ios --apple-id "$ASC_APP_ID" --bundle-id "$BUNDLE_ID"
       --bundle-version "$BUILD" --bundle-short-version-string "$VERSION" "${AUTH[@]}" --output-format normal)
else
  CMD=(xcrun altool --upload-app -f "$IPA" -t ios "${AUTH[@]}" --output-format normal)
fi

set +e
"${CMD[@]}" 2>&1 | tee "$WORK/altool.log"
STATUS=${PIPESTATUS[0]}
set -e

if [ "$STATUS" -ne 0 ] || grep -Eq 'ERROR:|NOT_AUTHORIZED|Failed to|UPLOAD FAILED' "$WORK/altool.log"; then
  if grep -q 'NOT_AUTHORIZED\|status code 401' "$WORK/altool.log"; then
    echo "[testflight] 401 = Apple rejected the API key itself. Check, in this order:" >&2
    echo "[testflight]   1) the key is a TEAM key and not revoked; ASC_KEY_ID is that key's ID;" >&2
    echo "[testflight]   2) ASC_ISSUER_ID is the Issuer ID shown above the Team Keys list;" >&2
    echo "[testflight]   3) ASC_KEY_P8_B64 was made from THAT key's AuthKey_<ID>.p8." >&2
    echo "[testflight]   Test all three locally: node ci/check_asc_key.mjs" >&2
  fi
  fail "altool exit $STATUS — the build was NOT uploaded."
fi
echo "[testflight] upload finished — the build shows up in App Store Connect › TestFlight after processing (10–30 min)."
