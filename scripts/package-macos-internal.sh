#!/usr/bin/env bash

set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT_FILE="$ROOT_DIR/src/App/App.csproj"
OUTPUT_ROOT="$ROOT_DIR/artifacts/macos-internal"
PUBLISH_DIR="$OUTPUT_ROOT/publish"
APP_NAME="Service Bus Explorer"
EXECUTABLE_NAME="ServiceBusExplorer"
BUNDLE_ID="com.servicebusexplorer.internal"
MINIMUM_MACOS_VERSION="13.0"
HOST_ARCH="$(uname -m)"

case "$HOST_ARCH" in
  arm64)
    RID="osx-arm64"
    ;;
  x86_64)
    RID="osx-x64"
    ;;
  *)
    echo "Unsupported macOS architecture: $HOST_ARCH" >&2
    exit 1
    ;;
esac

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

rm -rf "$OUTPUT_ROOT"
mkdir -p "$APP_DIR/Contents/MacOS" "$APP_DIR/Contents/Resources"

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
file "$APP_DIR/Contents/MacOS/$EXECUTABLE_NAME" | grep -q "$HOST_ARCH"

SIGNING_IDENTITY="$(
  security find-identity -v -p codesigning 2>/dev/null \
    | awk '/Developer ID Application/ {print $2; exit}'
)"
SIGNING_STATE="unsigned"

if [[ -n "$SIGNING_IDENTITY" ]]; then
  if codesign --force --deep --timestamp \
    --sign "$SIGNING_IDENTITY" "$APP_DIR"; then
    SIGNING_STATE="Developer ID signed with secure timestamp"
  else
    codesign --force --deep --timestamp=none \
      --sign "$SIGNING_IDENTITY" "$APP_DIR"
    SIGNING_STATE="Developer ID signed without secure timestamp"
  fi

  codesign --verify --deep --strict --verbose=1 "$APP_DIR"
else
  codesign --force --deep --sign - "$APP_DIR"
  codesign --verify --deep --strict --verbose=1 "$APP_DIR"
  SIGNING_STATE="ad-hoc signed"
fi

mkdir -p "$DMG_SOURCE"
ditto "$APP_DIR" "$DMG_SOURCE/$APP_NAME.app"
ln -s /Applications "$DMG_SOURCE/Applications"

hdiutil create \
  -volname "$APP_NAME Internal" \
  -srcfolder "$DMG_SOURCE" \
  -format UDZO \
  -ov \
  "$DMG_PATH"

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

hdiutil detach "$MOUNT_POINT" -quiet
rm -rf "$MOUNT_POINT"
MOUNT_POINT=""

SHA256="$(shasum -a 256 "$DMG_PATH" | awk '{print $1}')"
printf '%s  %s\n' "$SHA256" "$(basename "$DMG_PATH")" >"$CHECKSUM_PATH"
(cd "$OUTPUT_ROOT" && shasum -a 256 -c "$(basename "$CHECKSUM_PATH")")

echo "DMG_PATH=$DMG_PATH"
echo "DMG_SIZE_BYTES=$(stat -f '%z' "$DMG_PATH")"
echo "DMG_SHA256=$SHA256"
echo "RID=$RID"
echo "MINIMUM_MACOS_VERSION=$MINIMUM_MACOS_VERSION"
echo "SIGNING_STATE=$SIGNING_STATE"
echo "NOTARIZATION_STATE=not notarized"
