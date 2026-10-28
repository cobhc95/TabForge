#!/usr/bin/env bash
# TabForge for macOS (Apple Silicon): publish osx-arm64 self-contained and wrap it as TabForge.app (+ a zip, and a dmg when hdiutil exists).
# NON-SHIPPING: Windows stays the only shipped platform until the 0.9.0 trial. REBUILD.cmd and the Windows installer never run this.
#
# Run on a Mac (needs the .NET 10 SDK; python3, sips-free: the icon comes from icon-from-ico.py). Usage:
#   Source/tools/package-mac.sh [--out <folder>] [--tfportable <folder holding libtfportable.dylib>]
# Signing and notarising need YOUR Apple Developer ID, so they are commented placeholders in the last section: fill the variables and uncomment.
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SRC="$(cd "$HERE/.." && pwd)"                        # the Source folder
ROOT="$(cd "$SRC/.." && pwd)"                        # the workspace (holds Docs/ThirdPartyDownloads)
RID="osx-arm64"
VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$SRC/Directory.Build.props" | head -n1)"
OUT="${TABFORGE_PACKAGE_OUT:-$ROOT/build/package}/mac"
SOUNDFONT="${TABFORGE_SOUNDFONT:-$ROOT/Docs/ThirdPartyDownloads/generaluser/GeneralUser-GS.sf2}"
TFPORTABLE_DIR="${TABFORGE_TFPORTABLE:-}"

while [ $# -gt 0 ]; do
  case "$1" in
    --out) OUT="$2/mac"; shift 2 ;;
    --tfportable) TFPORTABLE_DIR="$2"; shift 2 ;;
    --lame) TABFORGE_LAME="$2"; shift 2 ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done

[ "$(uname -s)" = "Darwin" ] || echo "warning: not running on macOS; the bundle is assembled but cannot be signed or opened here" >&2
[ -f "$SOUNDFONT" ] || { echo "SoundFont not found: $SOUNDFONT (set TABFORGE_SOUNDFONT)" >&2; exit 1; }

# 1. A scratch copy of the sources: the portable publish restores without the locked Windows lock files and must not rewrite them.
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
mkdir -p "$WORK/Source"
(cd "$SRC" && tar --exclude=./tests --exclude=./third_party --exclude='./native' --exclude=obj --exclude=bin --exclude=TestResults -cf - .) | tar -C "$WORK/Source" -xf -

# 2. Publish. TabForgePortableBuild picks the net10.0 target; EnableWindowsTargeting lets Contracts restore its (unused) Windows target.
PUBLISH="$WORK/publish"
dotnet publish "$WORK/Source/src/TabForge.Avalonia/TabForge.Avalonia.csproj" -c Release -r "$RID" --self-contained true \
  -p:TabForgePortableBuild=true -p:EnableWindowsTargeting=true -p:RestoreLockedMode=false -p:PublishReadyToRun=false \
  -p:ContinuousIntegrationBuild=true -p:Deterministic=true -o "$PUBLISH"

# 2b. libtfportable (miniaudio audio + RtMidi MIDI, native/tfportable) built with CMake for this OS, unless a built one is given with --tfportable.
if [ -z "$TFPORTABLE_DIR" ]; then
  command -v cmake >/dev/null 2>&1 || { echo "cmake not found: install it or pass --tfportable" >&2; exit 1; }
  cmake -S "$SRC/native/tfportable" -B "$WORK/tfportable-build" -DCMAKE_BUILD_TYPE=Release -DCMAKE_OSX_ARCHITECTURES=arm64 -DCMAKE_OSX_DEPLOYMENT_TARGET=13.0
  cmake --build "$WORK/tfportable-build" --config Release
  TFPORTABLE_DIR="$(dirname "$(find "$WORK/tfportable-build" -name 'libtfportable.dylib' | head -n1)")"
fi

# 2c. libmp3lame (LAME, LGPL-2.1+): a separate, replaceable shared library next to the program, never linked in. Give it with --lame <file> or TABFORGE_LAME,
#     else the system copy is used (brew install lame). Its licence text is in THIRD_PARTY.md; the source is at https://lame.sourceforge.io/.
LAME="${TABFORGE_LAME:-}"
if [ -z "$LAME" ]; then
  for c in /opt/homebrew/lib/libmp3lame.0.dylib /opt/homebrew/lib/libmp3lame.dylib /usr/local/lib/libmp3lame.0.dylib; do [ -f "$c" ] && LAME="$c" && break; done
fi

# 3. The bundle.
APP="$OUT/TabForge.app"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -R "$PUBLISH/." "$APP/Contents/MacOS/"
mkdir -p "$APP/Contents/MacOS/SoundFonts"
cp "$SOUNDFONT" "$APP/Contents/MacOS/SoundFonts/GeneralUser-GS.sf2"          # GmBank.DefaultBundledPath = <program folder>/SoundFonts/GeneralUser-GS.sf2
if [ -n "$TFPORTABLE_DIR" ] && [ -f "$TFPORTABLE_DIR/libtfportable.dylib" ]; then
  cp "$TFPORTABLE_DIR/libtfportable.dylib" "$APP/Contents/MacOS/"              # miniaudio + RtMidi (native/tfportable), built per OS
else
  echo "warning: libtfportable.dylib not given (--tfportable); the app starts without audio and MIDI backends" >&2
fi
if [ -n "$LAME" ]; then cp "$LAME" "$APP/Contents/MacOS/libmp3lame.0.dylib"; else echo "warning: libmp3lame not found (brew install lame or --lame); MP3 export is off" >&2; fi
chmod +x "$APP/Contents/MacOS/TabForge"
python3 -I "$HERE/icon-from-ico.py" "$SRC/src/TabForge.Avalonia/Assets/TabForge.ico" --png "$WORK/icon.png" --icns "$APP/Contents/Resources/TabForge.icns"

cat > "$APP/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>TabForge</string>
  <key>CFBundleDisplayName</key><string>TabForge</string>
  <key>CFBundleIdentifier</key><string>com.tabforge.app</string>
  <key>CFBundleExecutable</key><string>TabForge</string>
  <key>CFBundleIconFile</key><string>TabForge</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>@VERSION@</string>
  <key>CFBundleVersion</key><string>@VERSION@</string>
  <key>LSMinimumSystemVersion</key><string>13.0</string>
  <key>LSApplicationCategoryType</key><string>public.app-category.music</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>NSMicrophoneUsageDescription</key><string>TabForge records audio from your instrument or microphone when you arm an audio track.</string>
  <key>CFBundleDocumentTypes</key>
  <array>
    <dict>
      <key>CFBundleTypeName</key><string>TabForge or Guitar Pro score</string>
      <key>CFBundleTypeRole</key><string>Editor</string>
      <key>LSHandlerRank</key><string>Alternate</string>
      <key>CFBundleTypeExtensions</key>
      <array><string>tforge</string><string>gp</string><string>gp3</string><string>gp4</string><string>gp5</string></array>
    </dict>
  </array>
</dict>
</plist>
PLIST
sed -i.bak "s/@VERSION@/$VERSION/g" "$APP/Contents/Info.plist" && rm -f "$APP/Contents/Info.plist.bak"

# Hardened-runtime entitlements: microphone / audio input, and loading plug-ins signed by other developers (VST3, CLAP) without library validation.
cat > "$OUT/TabForge.entitlements" <<'ENT'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>com.apple.security.device.audio-input</key><true/>
  <key>com.apple.security.cs.disable-library-validation</key><true/>
  <key>com.apple.security.cs.allow-jit</key><true/>
  <key>com.apple.security.cs.allow-unsigned-executable-memory</key><true/>
</dict>
</plist>
ENT

# 4. Signing, notarising and the dmg. These need an Apple Developer ID (about 99 USD a year) that only the owner has: placeholders, not run.
#    export DEVELOPER_ID="Developer ID Application: Your Name (TEAMID)"
#    export NOTARY_PROFILE="tabforge-notary"      # created once with: xcrun notarytool store-credentials tabforge-notary --apple-id you@example.com --team-id TEAMID
#
#    codesign --force --deep --options runtime --timestamp --entitlements "$OUT/TabForge.entitlements" --sign "$DEVELOPER_ID" "$APP"
#    codesign --verify --deep --strict --verbose=2 "$APP"
#    ditto -c -k --keepParent "$APP" "$OUT/TabForge-notarize.zip"
#    xcrun notarytool submit "$OUT/TabForge-notarize.zip" --keychain-profile "$NOTARY_PROFILE" --wait
#    xcrun stapler staple "$APP"
#    spctl --assess --type execute --verbose "$APP"

( cd "$OUT" && ditto -c -k --keepParent TabForge.app "TabForge-$VERSION-$RID.zip" 2>/dev/null || zip -qr "TabForge-$VERSION-$RID.zip" TabForge.app )
if command -v hdiutil >/dev/null 2>&1; then
  hdiutil create -volname "TabForge $VERSION" -srcfolder "$APP" -ov -format UDZO "$OUT/TabForge-$VERSION-$RID.dmg" >/dev/null
  #   codesign --force --sign "$DEVELOPER_ID" "$OUT/TabForge-$VERSION-$RID.dmg" && xcrun notarytool submit "$OUT/TabForge-$VERSION-$RID.dmg" --keychain-profile "$NOTARY_PROFILE" --wait && xcrun stapler staple "$OUT/TabForge-$VERSION-$RID.dmg"
fi
echo "done: $OUT (unsigned: Gatekeeper will warn until the signing section is filled in)"
