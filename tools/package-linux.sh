#!/usr/bin/env bash
# TabForge for Linux (x86_64): publish linux-x64 self-contained and pack it as an AppImage (needs appimagetool), plus a plain tar.gz.
# NON-SHIPPING: Windows stays the only shipped platform until the 0.9.0 trial. REBUILD.cmd and the Windows installer never run this.
#
# Run on Linux (Ubuntu 22.04 or newer; the AppImage runs on the glibc it was built on or newer). Usage:
#   Source/tools/package-linux.sh [--out <folder>] [--tfportable <folder holding libtfportable.so>] [--appimagetool <path>]
# appimagetool: https://github.com/AppImage/appimagetool/releases (MIT). Without it only the tar.gz and the AppDir are made.
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SRC="$(cd "$HERE/.." && pwd)"
ROOT="$(cd "$SRC/.." && pwd)"
RID="linux-x64"
VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$SRC/Directory.Build.props" | head -n1)"
OUT="${TABFORGE_PACKAGE_OUT:-$ROOT/build/package}/linux"
SOUNDFONT="${TABFORGE_SOUNDFONT:-$ROOT/Docs/ThirdPartyDownloads/generaluser/GeneralUser-GS.sf2}"
TFPORTABLE_DIR="${TABFORGE_TFPORTABLE:-}"
APPIMAGETOOL="${APPIMAGETOOL:-$(command -v appimagetool || true)}"

while [ $# -gt 0 ]; do
  case "$1" in
    --out) OUT="$2/linux"; shift 2 ;;
    --tfportable) TFPORTABLE_DIR="$2"; shift 2 ;;
    --lame) TABFORGE_LAME="$2"; shift 2 ;;
    --appimagetool) APPIMAGETOOL="$2"; shift 2 ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done

[ -f "$SOUNDFONT" ] || { echo "SoundFont not found: $SOUNDFONT (set TABFORGE_SOUNDFONT)" >&2; exit 1; }

# 1. A scratch copy of the sources: the portable publish restores without the locked Windows lock files and must not rewrite them.
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
mkdir -p "$WORK/Source"
(cd "$SRC" && tar --exclude=./tests --exclude=./third_party --exclude='./native' --exclude=obj --exclude=bin --exclude=TestResults -cf - .) | tar -C "$WORK/Source" -xf -

# 2. Publish.
PUBLISH="$WORK/publish"
dotnet publish "$WORK/Source/src/TabForge.Avalonia/TabForge.Avalonia.csproj" -c Release -r "$RID" --self-contained true \
  -p:TabForgePortableBuild=true -p:EnableWindowsTargeting=true -p:RestoreLockedMode=false -p:PublishReadyToRun=false \
  -p:ContinuousIntegrationBuild=true -p:Deterministic=true -o "$PUBLISH"

# 2b. libtfportable (miniaudio audio + RtMidi MIDI, native/tfportable) built with CMake for this OS, unless a built one is given with --tfportable.
if [ -z "$TFPORTABLE_DIR" ]; then
  command -v cmake >/dev/null 2>&1 || { echo "cmake not found: install it or pass --tfportable" >&2; exit 1; }
  cmake -S "$SRC/native/tfportable" -B "$WORK/tfportable-build" -DCMAKE_BUILD_TYPE=Release 
  cmake --build "$WORK/tfportable-build" --config Release
  TFPORTABLE_DIR="$(dirname "$(find "$WORK/tfportable-build" -name 'libtfportable.so' | head -n1)")"
fi

# 2c. libmp3lame (LAME, LGPL-2.1+): a separate, replaceable shared library next to the program, never linked in. Give it with --lame <file> or TABFORGE_LAME,
#     else the system copy is used (apt install libmp3lame0). Its licence text is in THIRD_PARTY.md; the source is at https://lame.sourceforge.io/.
LAME="${TABFORGE_LAME:-}"
if [ -z "$LAME" ]; then
  for c in /usr/lib/x86_64-linux-gnu/libmp3lame.so.0 /usr/lib64/libmp3lame.so.0 /usr/lib/libmp3lame.so.0; do [ -f "$c" ] && LAME="$c" && break; done
fi

# 3. The AppDir: usr/bin holds the program (GmBank.DefaultBundledPath = <program folder>/SoundFonts/GeneralUser-GS.sf2).
APPDIR="$OUT/TabForge.AppDir"
rm -rf "$APPDIR"
mkdir -p "$APPDIR/usr/bin/SoundFonts" "$APPDIR/usr/share/icons/hicolor/256x256/apps" "$APPDIR/usr/share/applications"
cp -R "$PUBLISH/." "$APPDIR/usr/bin/"
cp "$SOUNDFONT" "$APPDIR/usr/bin/SoundFonts/GeneralUser-GS.sf2"
if [ -n "$TFPORTABLE_DIR" ] && [ -f "$TFPORTABLE_DIR/libtfportable.so" ]; then
  cp "$TFPORTABLE_DIR/libtfportable.so" "$APPDIR/usr/bin/"                       # miniaudio + RtMidi (native/tfportable), built per OS
else
  echo "warning: libtfportable.so not given (--tfportable); the app starts without audio and MIDI backends" >&2
fi
if [ -n "$LAME" ]; then cp "$LAME" "$APPDIR/usr/bin/libmp3lame.so.0"; else echo "warning: libmp3lame not found (apt install libmp3lame0 or --lame); MP3 export is off" >&2; fi
chmod +x "$APPDIR/usr/bin/TabForge"
python3 -I "$HERE/icon-from-ico.py" "$SRC/src/TabForge.Avalonia/Assets/TabForge.ico" --png "$APPDIR/usr/share/icons/hicolor/256x256/apps/tabforge.png"
cp "$APPDIR/usr/share/icons/hicolor/256x256/apps/tabforge.png" "$APPDIR/tabforge.png"

cat > "$APPDIR/tabforge.desktop" <<'DESKTOP'
[Desktop Entry]
Type=Application
Name=TabForge
Comment=Guitar tab and score editor with a built-in band and audio recording
Exec=TabForge %F
Icon=tabforge
Terminal=false
Categories=AudioVideo;Audio;Music;
MimeType=application/x-tabforge;application/x-gp5;application/x-gp4;application/x-gp3;application/x-gp;
StartupWMClass=TabForge
DESKTOP
cp "$APPDIR/tabforge.desktop" "$APPDIR/usr/share/applications/tabforge.desktop"

cat > "$APPDIR/AppRun" <<'APPRUN'
#!/bin/sh
HERE="$(dirname "$(readlink -f "$0")")"
export DOTNET_BUNDLE_EXTRACT_BASE_DIR="${XDG_CACHE_HOME:-$HOME/.cache}/TabForge/bundle"
exec "$HERE/usr/bin/TabForge" "$@"
APPRUN
chmod +x "$APPDIR/AppRun"

# 4. The packages.
( cd "$OUT" && tar -czf "TabForge-$VERSION-$RID.tar.gz" TabForge.AppDir )
if [ -n "$APPIMAGETOOL" ] && [ -x "$APPIMAGETOOL" ]; then
  ARCH=x86_64 "$APPIMAGETOOL" "$APPDIR" "$OUT/TabForge-$VERSION-x86_64.AppImage"
else
  echo "appimagetool not found: only the AppDir and the tar.gz were made" >&2
fi
echo "done: $OUT"
