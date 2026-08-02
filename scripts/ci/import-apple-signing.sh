#!/usr/bin/env bash
# Import Developer ID .p12 + prepare ephemeral App Store Connect API key for fastlane.
# Expects env:
#   MACOS_CERTIFICATE_BASE64  base64-encoded .p12
#   MACOS_CERTIFICATE_PWD     .p12 password
#   KEYCHAIN_PASSWORD         password for the temporary keychain
# For fastlane notarize (preferred CI):
#   APP_STORE_CONNECT_API_KEY_ID
#   APP_STORE_CONNECT_ISSUER_ID
#   APP_STORE_CONNECT_API_KEY_P8_BASE64
# Optional laptop fallback (not required for CI notarize):
#   APPLE_ID / APPLE_TEAM_ID / NOTARY_TOOL_PASSWORD — store notarytool profile AC_PASSWORD
#
# No Mac App Store provisioning profile is used or required.
set -euo pipefail

: "${MACOS_CERTIFICATE_BASE64:?MACOS_CERTIFICATE_BASE64 is required}"
: "${MACOS_CERTIFICATE_PWD:?MACOS_CERTIFICATE_PWD is required}"
: "${KEYCHAIN_PASSWORD:?KEYCHAIN_PASSWORD is required}"

KEYCHAIN_NAME="${KEYCHAIN_NAME:-sbe-build.keychain-db}"
CERT_PATH="${RUNNER_TEMP:-/tmp}/sbe-developer-id.p12"
ASC_DIR="${RUNNER_TEMP:-/tmp}/sbe-asc-api"
P8_PATH="$ASC_DIR/AuthKey.p8"
API_KEY_JSON="$ASC_DIR/api_key.json"

echo "$MACOS_CERTIFICATE_BASE64" | base64 --decode >"$CERT_PATH"

security create-keychain -p "$KEYCHAIN_PASSWORD" "$KEYCHAIN_NAME"
security set-keychain-settings -lut 21600 "$KEYCHAIN_NAME"
security unlock-keychain -p "$KEYCHAIN_PASSWORD" "$KEYCHAIN_NAME"
security import "$CERT_PATH" -k "$KEYCHAIN_NAME" -P "$MACOS_CERTIFICATE_PWD" \
  -T /usr/bin/codesign -T /usr/bin/security -T /usr/bin/productsign
security set-key-partition-list -S apple-tool:,apple:,codesign: -s -k "$KEYCHAIN_PASSWORD" "$KEYCHAIN_NAME"
security list-keychains -d user -s "$KEYCHAIN_NAME" $(security list-keychains -d user | sed -e s/\"//g)

rm -f "$CERT_PATH"

IDENTITY="$(
  security find-identity -v -p codesigning "$KEYCHAIN_NAME" \
    | awk '/Developer ID Application/ {print $2; exit}'
)"
if [[ -z "$IDENTITY" ]]; then
  echo "No Developer ID Application identity found after import." >&2
  security find-identity -v -p codesigning "$KEYCHAIN_NAME" >&2 || true
  exit 1
fi
echo "Imported signing identity: $IDENTITY"
if [[ -n "${GITHUB_ENV:-}" ]]; then
  echo "SIGNING_IDENTITY=$IDENTITY" >>"$GITHUB_ENV"
fi
export SIGNING_IDENTITY="$IDENTITY"

# Ephemeral ASC API key material for fastlane (no secrets echoed).
if [[ -n "${APP_STORE_CONNECT_API_KEY_ID:-}" && -n "${APP_STORE_CONNECT_ISSUER_ID:-}" && -n "${APP_STORE_CONNECT_API_KEY_P8_BASE64:-}" ]]; then
  mkdir -p "$ASC_DIR"
  chmod 700 "$ASC_DIR"
  echo "$APP_STORE_CONNECT_API_KEY_P8_BASE64" | base64 --decode >"$P8_PATH"
  chmod 600 "$P8_PATH"

  # fastlane API key JSON file format:
  # https://docs.fastlane.tools/app-store-connect-api/#using-fastlane-api-key-json-file
  python3 - <<PY
import json, os
path = os.environ.get("API_KEY_JSON", "$API_KEY_JSON")
payload = {
    "key_id": os.environ["APP_STORE_CONNECT_API_KEY_ID"],
    "issuer_id": os.environ["APP_STORE_CONNECT_ISSUER_ID"],
    "key": open("$P8_PATH", "r", encoding="utf-8").read(),
    "in_house": False,
}
with open(path, "w", encoding="utf-8") as f:
    json.dump(payload, f)
os.chmod(path, 0o600)
print(f"Wrote ASC API key JSON for fastlane (path redacted).")
PY

  if [[ -n "${GITHUB_ENV:-}" ]]; then
    echo "APP_STORE_CONNECT_API_KEY_PATH=$API_KEY_JSON" >>"$GITHUB_ENV"
  fi
  export APP_STORE_CONNECT_API_KEY_PATH="$API_KEY_JSON"
  echo "Prepared App Store Connect API key for fastlane notarize"
elif [[ "${NOTARIZE:-0}" == "1" || "${REQUIRE_ASC_API_KEY:-0}" == "1" ]]; then
  echo "ASC API key secrets required for notarize (APP_STORE_CONNECT_API_KEY_ID, APP_STORE_CONNECT_ISSUER_ID, APP_STORE_CONNECT_API_KEY_P8_BASE64)." >&2
  exit 1
else
  echo "ASC API key secrets not provided; fastlane notarize will fail if NOTARIZE=1."
fi

# Optional laptop fallback only — not the CI primary path.
if [[ -n "${APPLE_ID:-}" && -n "${APPLE_TEAM_ID:-}" && -n "${NOTARY_TOOL_PASSWORD:-}" ]]; then
  xcrun notarytool store-credentials "AC_PASSWORD" \
    --apple-id "$APPLE_ID" \
    --team-id "$APPLE_TEAM_ID" \
    --password "$NOTARY_TOOL_PASSWORD"
  echo "Stored notarytool profile AC_PASSWORD (laptop fallback)"
fi
