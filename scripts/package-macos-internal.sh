#!/usr/bin/env bash
# Build a self-contained macOS .app + UDZO .dmg for Service Bus Explorer (Avalonia).
# Order: publish → Developer ID codesign → DMG → fastlane notarize + staple (fail-closed).
#
# Usage:
#   ./scripts/package-macos-internal.sh
#   RID=osx-arm64 ./scripts/package-macos-internal.sh
#   NOTARIZE=1 ./scripts/package-macos-internal.sh   # requires Developer ID + ASC API key / fastlane
#
# Optional env:
#   RID                 osx-arm64 only for this feature (default: osx-arm64)
#   SIGNING_IDENTITY    Developer ID Application identity (auto-detect if unset)
#   ENTITLEMENTS        path to entitlements plist (default: src/App/Entitlements.plist)
#   NOTARIZE            1 to notarize via fastlane (default: 0); fail-closed when secrets missing
#   APP_STORE_CONNECT_API_KEY_PATH  fastlane API key JSON (from import-apple-signing.sh)
#   SKIP_LAUNCH_SMOKE   1 to skip GUI launch check (CI)
#   OUTPUT_ROOT         override artifacts directory
#
# Does not upload releases; GitHub Actions workflow preview-packages.yml does that.
# No Mac App Store provisioning profile.

set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT_FILE="$ROOT_DIR/src/App/App.csproj"
OUTPUT_ROOT="${OUTPUT_ROOT:-$ROOT_DIR/artifacts/macos-internal}"
PUBLISH_DIR="$OUTPUT_ROOT/publish"
APP_NAME="Service Bus Explorer"
EXECUTABLE_NAME="ServiceBusExplorer"
BUNDLE_ID="com.servicebusexplorer.internal"
MINIMUM_MACOS_VERSION="13.0"
ENTITLEMENTS="${ENTITLEMENTS:-$ROOT_DIR/src/App/Entitlements.plist}"
NOTARIZE="${NOTARIZE:-0}"
SKIP_LAUNCH_SMOKE="${SKIP_LAUNCH_SMOKE:-0}"

# Feature 002: Apple Silicon only (osx-x64 deferred).
RID="${RID:-osx-arm64}"
case "$RID" in
  osx-arm64) EXPECTED_ARCH="arm64" ;;
  *)
    echo "RID must be osx-arm64 for this feature (got: $RID). osx-x64 is deferred." >&2
    exit 1
    ;;
esac

HOST_ARCH="$(uname -m)"
if [[ "$HOST_ARCH" != "arm64" ]]; then
  echo "Warning: packaging osx-arm64 on host arch $HOST_ARCH; prefer an Apple Silicon runner." >&2
fi

VERSION="$(awk -F'[<>]' '/<Version>/{print $3; exit}' "$PROJECT_FILE")"
NUMERIC_VERSION="${VERSION%%-*}"
APP_DIR="$OUTPUT_ROOT/$APP_NAME.app"
DMG_PATH="$OUTPUT_ROOT/ServiceBusExplorer-$VERSION-$RID.dmg"
CHECKSUM_PATH="$DMG_PATH.sha256"
DMG_SOURCE="$OUTPUT_ROOT/dmg-source"
MOUNT_POINT=""
APP_PID=""

cleanup() {
  if [[ -n "$APP_PID" ]] && kill -0 "$APP_PID" 2>/dev/null; then
    kill -TERM "$APP_PID" 2>/dev/null || true
    wait "$APP_PID" 2>/dev/null || true
  fi

  if [[ -n "$MOUNT_POINT" ]] && mount | grep -Fq " on $MOUNT_POINT "; then
    hdiutil detach "$MOUNT_POINT" -force -quiet || true
  fi

  rm -rf "$DMG_SOURCE"
  if [[ -n "$MOUNT_POINT" ]]; then
    rm -rf "$MOUNT_POINT"
  fi
}
trap cleanup EXIT

if [[ ! -f "$ENTITLEMENTS" ]]; then
  echo "Missing entitlements file: $ENTITLEMENTS" >&2
  exit 1
fi

rm -rf "$OUTPUT_ROOT"
mkdir -p "$APP_DIR/Contents/MacOS" "$APP_DIR/Contents/Resources"

echo "Publishing $PROJECT_FILE ($RID)..."
dotnet publish "$PROJECT_FILE" \
  --configuration Release \
  --runtime "$RID" \
  --self-contained true \
  --output "$PUBLISH_DIR" \
  -p:PublishSingleFile=false \
  -p:DebugType=None \
  -p:DebugSymbols=false

ditto "$PUBLISH_DIR" "$APP_DIR/Contents/MacOS"
chmod 755 "$APP_DIR/Contents/MacOS/$EXECUTABLE_NAME"

cat >"$APP_DIR/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleDevelopmentRegion</key>
  <string>en</string>
  <key>CFBundleDisplayName</key>
  <string>$APP_NAME</string>
  <key>CFBundleExecutable</key>
  <string>$EXECUTABLE_NAME</string>
  <key>CFBundleIdentifier</key>
  <string>$BUNDLE_ID</string>
  <key>CFBundleInfoDictionaryVersion</key>
  <string>6.0</string>
  <key>CFBundleName</key>
  <string>$APP_NAME</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>CFBundleShortVersionString</key>
  <string>$NUMERIC_VERSION</string>
  <key>CFBundleVersion</key>
  <string>$NUMERIC_VERSION</string>
  <key>LSMinimumSystemVersion</key>
  <string>$MINIMUM_MACOS_VERSION</string>
  <key>NSHighResolutionCapable</key>
  <true/>
</dict>
</plist>
EOF

plutil -lint "$APP_DIR/Contents/Info.plist"
test "$(defaults read "$APP_DIR/Contents/Info" CFBundleIdentifier)" = "$BUNDLE_ID"
test "$(defaults read "$APP_DIR/Contents/Info" CFBundleShortVersionString)" = "$NUMERIC_VERSION"
file "$APP_DIR/Contents/MacOS/$EXECUTABLE_NAME" | grep -Eq "$EXPECTED_ARCH|universal"

sign_bundle() {
  local identity="$1"
  local use_runtime="$2"
  local macos_dir="$APP_DIR/Contents/MacOS"
  local main_exe="$macos_dir/$EXECUTABLE_NAME"
  local -a nested_files=()
  local fname depth_key

  # Sign one path. Developer ID path keeps hardened runtime + timestamp + entitlements.
  codesign_one() {
    local target="$1"
    if [[ "$use_runtime" == "1" ]]; then
      codesign --force --timestamp --options=runtime \
        --entitlements "$ENTITLEMENTS" \
        --sign "$identity" "$target"
    else
      codesign --force --sign "$identity" "$target"
    fi
  }

  # .NET self-contained apps must be signed inside-out:
  #   1) nested MacOS files (deepest-first), excluding the main executable
  #   2) Contents/MacOS/$EXECUTABLE_NAME
  #   3) the outer .app bundle
  # Do not rely on codesign --deep as the only signing pass (Avalonia/Apple guidance).
  # --deep is used below for verification only.
  while IFS= read -r -d '' fname; do
    [[ "$fname" == "$main_exe" ]] && continue
    nested_files+=("$fname")
  done < <(find "$macos_dir" -type f -print0)

  if ((${#nested_files[@]} > 0)); then
    while IFS=$'\t' read -r depth_key fname; do
      [[ -n "$fname" ]] || continue
      codesign_one "$fname"
    done < <(
      for fname in "${nested_files[@]}"; do
        depth_key="${fname//[^\/]/}"
        # Lower sort key => deeper path (more slashes).
        printf '%03d\t%s\n' "$((999 - ${#depth_key}))" "$fname"
      done | LC_ALL=C sort
    )
  fi

  codesign_one "$main_exe"
  codesign_one "$APP_DIR"

  codesign --verify --deep --strict --verbose=1 "$APP_DIR"
}

SIGNING_STATE="unsigned"
NOTARIZATION_STATE="not-notarized"
MANIFEST_SIGNING="unsigned"
MANIFEST_NOTARIZATION="not-notarized"

if [[ -z "${SIGNING_IDENTITY:-}" ]]; then
  SIGNING_IDENTITY="$(
    security find-identity -v -p codesigning 2>/dev/null \
      | awk '/Developer ID Application/ {print $2; exit}'
  )"
fi

if [[ -n "${SIGNING_IDENTITY:-}" ]]; then
  echo "Signing with Developer ID: $SIGNING_IDENTITY"
  sign_bundle "$SIGNING_IDENTITY" 1
  SIGNING_STATE="Developer ID signed (hardened runtime)"
  MANIFEST_SIGNING="developer-id"
else
  if [[ "$NOTARIZE" == "1" ]]; then
    echo "NOTARIZE=1 requires a Developer ID Application identity (fail-closed)." >&2
    exit 1
  fi
  echo "No Developer ID Application identity found; ad-hoc signing (local smoke only)."
  sign_bundle "-" 0
  SIGNING_STATE="ad-hoc signed"
  MANIFEST_SIGNING="ad-hoc"
fi

# Sign → DMG first (never notarize an unsigned release DMG).
mkdir -p "$DMG_SOURCE"
ditto "$APP_DIR" "$DMG_SOURCE/$APP_NAME.app"
ln -s /Applications "$DMG_SOURCE/Applications"

hdiutil create \
  -volname "$APP_NAME Internal" \
  -srcfolder "$DMG_SOURCE" \
  -format UDZO \
  -ov \
  "$DMG_PATH"

if [[ "$NOTARIZE" == "1" ]]; then
  if [[ "$MANIFEST_SIGNING" != "developer-id" ]]; then
    echo "NOTARIZE=1 requires Developer ID signing before DMG notarization (fail-closed)." >&2
    exit 1
  fi

  if [[ -z "${APP_STORE_CONNECT_API_KEY_PATH:-}" ]]; then
    echo "NOTARIZE=1 requires APP_STORE_CONNECT_API_KEY_PATH (from scripts/ci/import-apple-signing.sh)." >&2
    exit 1
  fi
  if [[ ! -f "$APP_STORE_CONNECT_API_KEY_PATH" ]]; then
    echo "ASC API key file missing: $APP_STORE_CONNECT_API_KEY_PATH (fail-closed)." >&2
    exit 1
  fi

  if ! command -v bundle >/dev/null 2>&1 && ! command -v fastlane >/dev/null 2>&1; then
    echo "fastlane (or bundler) is required for notarize (fail-closed)." >&2
    exit 1
  fi

  echo "Notarizing DMG via fastlane (sign → DMG → notarize)..."
  export DMG_PATH BUNDLE_ID
  export APP_STORE_CONNECT_API_KEY_PATH
  pushd "$ROOT_DIR" >/dev/null
  if command -v bundle >/dev/null 2>&1 && [[ -f "$ROOT_DIR/Gemfile" ]]; then
    bundle exec fastlane macos_notarize_dmg dmg_path:"$DMG_PATH" bundle_id:"$BUNDLE_ID" api_key_path:"$APP_STORE_CONNECT_API_KEY_PATH"
  else
    fastlane macos_notarize_dmg dmg_path:"$DMG_PATH" bundle_id:"$BUNDLE_ID" api_key_path:"$APP_STORE_CONNECT_API_KEY_PATH"
  fi
  popd >/dev/null

  xcrun stapler validate "$DMG_PATH"
  NOTARIZATION_STATE="notarized and stapled"
  MANIFEST_NOTARIZATION="notarized"
fi

test "$(hdiutil imageinfo "$DMG_PATH" | awk '/Format Description:/ {sub(/^.*: /, ""); print; exit}')" = "UDIF read-only compressed (zlib)"

MOUNT_POINT="$(mktemp -d "${TMPDIR:-/tmp}/service-bus-explorer-dmg.XXXXXX")"
hdiutil attach -readonly -nobrowse -mountpoint "$MOUNT_POINT" "$DMG_PATH"
MOUNT_POINT="$(cd "$MOUNT_POINT" && pwd -P)"

MOUNTED_APP="$MOUNT_POINT/$APP_NAME.app"
test -d "$MOUNTED_APP/Contents/MacOS"
test -x "$MOUNTED_APP/Contents/MacOS/$EXECUTABLE_NAME"
test -L "$MOUNT_POINT/Applications"
test "$(readlink "$MOUNT_POINT/Applications")" = "/Applications"
test "$(defaults read "$MOUNTED_APP/Contents/Info" CFBundleIdentifier)" = "$BUNDLE_ID"
test "$(defaults read "$MOUNTED_APP/Contents/Info" CFBundleShortVersionString)" = "$NUMERIC_VERSION"
mount | grep -F " on $MOUNT_POINT " | grep -q "read-only"
codesign --verify --deep --strict --verbose=1 "$MOUNTED_APP"

if [[ "$SKIP_LAUNCH_SMOKE" != "1" ]]; then
  "$MOUNTED_APP/Contents/MacOS/$EXECUTABLE_NAME" \
    >"$OUTPUT_ROOT/launch-smoke.log" 2>&1 &
  APP_PID=$!
  sleep 5
  if ! kill -0 "$APP_PID" 2>/dev/null; then
    echo "The mounted application exited during the launch smoke test." >&2
    cat "$OUTPUT_ROOT/launch-smoke.log" >&2
    exit 1
  fi
  kill -TERM "$APP_PID"
  wait "$APP_PID" 2>/dev/null || true
  APP_PID=""
fi

hdiutil detach "$MOUNT_POINT" -quiet
rm -rf "$MOUNT_POINT"
MOUNT_POINT=""

SHA256="$(shasum -a 256 "$DMG_PATH" | awk '{print $1}')"
printf '%s  %s\n' "$SHA256" "$(basename "$DMG_PATH")" >"$CHECKSUM_PATH"
(cd "$OUTPUT_ROOT" && shasum -a 256 -c "$(basename "$CHECKSUM_PATH")")

# Contract keys per specs/002-preview-installer-packaging/contracts/artifact-manifest.md
cat >"$OUTPUT_ROOT/MANIFEST.txt" <<EOF
product=Service Bus Explorer
version=$VERSION
preview=true
artifact.macos.arm64=$(basename "$DMG_PATH")
sha256.macos.arm64=$SHA256
signing.macos.arm64=$MANIFEST_SIGNING
notarization.macos.arm64=$MANIFEST_NOTARIZATION
rid=$RID
minimum_macos=$MINIMUM_MACOS_VERSION
bundle_id=$BUNDLE_ID
EOF

echo "DMG_PATH=$DMG_PATH"
echo "DMG_SIZE_BYTES=$(stat -f '%z' "$DMG_PATH")"
echo "DMG_SHA256=$SHA256"
echo "RID=$RID"
echo "MINIMUM_MACOS_VERSION=$MINIMUM_MACOS_VERSION"
echo "SIGNING_STATE=$SIGNING_STATE"
echo "NOTARIZATION_STATE=$NOTARIZATION_STATE"
