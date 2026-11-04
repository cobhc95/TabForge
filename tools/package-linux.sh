#!/usr/bin/env bash
# TabForge for Linux (x86_64): publish linux-x64 self-contained and pack it as an AppImage (the PRIMARY Linux deliverable; needs appimagetool), plus a plain tar.xz (xz -9e -T0) kept only as the beta fallback until the AppImage is proven on real hardware.
# NON-SHIPPING: Windows stays the only shipped platform until the 0.9.0 trial. REBUILD.cmd and the Windows installer never run this.
#
# Run on Linux (Ubuntu 22.04 or newer; the AppImage runs on the glibc it was built on or newer). Usage:
#   Source/tools/package-linux.sh [--out <folder>] [--wine <folder with bin/wine>] [--helpers <folder>] [--tfportable <folder holding libtfportable.so>] [--tfvst3 <folder holding libtfvst3.so>] [--ffmpeg <folder>] [--appimagetool <path>]
# Wine: --wine (or TABFORGE_WINE_DIR) is the pinned tree from Source/native/wine/BUILD_PROVENANCE.md. It is copied
#   to usr/bin/wine with only debug/development files stripped (no Wine source patched); tfvst32.exe, tfvst64.exe, tfvst3.dll and tfvst3-x86.dll are copied beside the program (from --helpers,
#   default src/TabForge.AudioEngine/native), with ThirdParty/wine/{BUILD_PROVENANCE.md,README.txt,COPYING.LIB,LICENSE,AUTHORS}. wine-mono and
#   wine-gecko are refused. Without --wine the package is made without Windows plug-in support, as before.
# SoundFont: TABFORGE_SOUNDFONT=<GeneralUser-GS.sf2 path> and TABFORGE_SOUNDFONT_LICENSE=<its licence text>; both ship in usr/bin/SoundFonts. libtfportable.so and libtfvst3.so are built with cmake here (or --tfportable / --tfvst3 <folder>).
# appimagetool: https://github.com/AppImage/appimagetool/releases (MIT). It builds the PRIMARY package: without it
#   only the AppDir and the fallback tar.xz are made and the primary deliverable is missing (see the loud warning below).
#   The AppImage uses squashfs xz compression (--comp xz) when that appimagetool build supports it, else its default.
#
# Build machine needs: dotnet SDK, cmake, a C/C++ toolchain, git (cmake fetches the pinned Steinberg VST3 SDK, miniaudio and RtMidi), libasound2-dev, binutils (objdump, strip), xz-utils (xz -9e -T0), python3.
#   Offline VST3 SDK: TABFORGE_VST3SDK=<checkout root> (passed as -DVST3SDK_DIR).
#
# The package (usr/bin of the AppDir, inside the tar.xz and the AppImage) contains:
#   TabForge (+ the self-contained .NET runtime and Avalonia/Skia libraries), libtfportable.so (audio + MIDI), libtfvst3.so (VST3 host helper),
#   libmp3lame.so.0 + libmpg123.so.0 (LAME and mpg123, both LGPL; only if found; RPATH $ORIGIN via patchelf, notice in ThirdParty/libmpg123/), ffmpeg + ThirdParty/ffmpeg/LICENSE.txt (LGPL build; only if --ffmpeg given),
#   SoundFonts/GeneralUser-GS.sf2 + GeneralUser-GS-LICENSE.txt, and the ThirdParty notices that the publish output carries.
#   With --wine: wine/ (pinned runtime) and the four Windows helper files plus ThirdParty/wine/.
# The user must NOT need to install: .NET, a SoundFont, ffmpeg, LAME, a newer libstdc++/libgcc (both native libs link them
# statically), a VST3 helper or Wine.
# Native Linux VST3 editors: the engine hosts a plug-in's view in its own top-level X11 window (X11EmbedWindowID)
# and runs the VST3 host run loop (IRunLoop timers and file descriptors); libX11 is already in the host list below.
#
# Requires on the host (a package cannot ship these; every normal desktop has them, the gate only WARNS when an ELF needs them):
#   libX11.so.6 libXext libXcursor libXrandr libXi libXfixes libXrender libxcb libICE libSM (apt: libx11-6 libxext6 libxcursor1 libxrandr2 libxi6 libxfixes3 libxrender1 libxcb1 libice6 libsm6),
#   libfontconfig.so.1 (libfontconfig1), libfreetype.so.6 (libfreetype6), libGL.so.1 (libgl1), libasound.so.2 (libasound2 / libasound2t64).
#   The bundled Wine adds its optional Unix drivers: libXcomposite, libXdamage, libXinerama, libXxf86vm, libxkbcommon, libxkbregistry,
#   libwayland-client/egl, libpulse, libOpenCL, libavcodec/format/util, libglib/gobject/gstreamer/gstaudio/gsttag/gstvideo, libgphoto2,
#   libpcap, libpcsclite, libsane, libudev, libusb-1.0, libcapi20. They are normal desktop packages; missing optional ones only
#   disable that Wine driver, never audio or plug-in loading.
#   libstdc++.so.6 and libgcc_s.so.1 (libstdc++6, libgcc-s1) are .NET's own Linux requirements; libz.so.1 (zlib1g); libssl/libcrypto are loaded lazily by .NET.
# Dependency gate (step 5): every ELF file in the package is read with `objdump -p`; each NEEDED library must be shipped in the package or be in
# the allowlist below. Core glibc/.NET libs pass silently, host-provided desktop libs (list above) pass with a WARNING, anything else stops the script.
# liblttng-ust (optional tracing of libcoreclrtraceptprovider.so, loaded lazily by .NET) is ignored. The two native libs may not need libstdc++/libgcc_s at all.
# The highest GLIBC_ symbol version over all ELF files is printed and written to MIN_GLIBC.txt beside the packages: that is the oldest glibc the package runs on.
# Extra allowed libraries for a one-off: TABFORGE_NEEDED_EXTRA="libfoo.so.1 libbar.so.2".
set -euo pipefail

command -v xz >/dev/null 2>&1 || { echo "xz not found (apt install xz-utils): cannot make the tar.xz package" >&2; exit 1; }
command -v strip >/dev/null 2>&1 || { echo "strip not found (apt install binutils): cannot strip debug symbols" >&2; exit 1; }

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SRC="$(cd "$HERE/.." && pwd)"
ROOT="$(cd "$SRC/.." && pwd)"
RID="linux-x64"
VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$SRC/Directory.Build.props" | head -n1)"
WINE_TARBALL_SHA256="020f8886750ff658d92ea39e696d2a57d3e8f056f00d8dfe45b65cddee5d6fa5"
OUT="${TABFORGE_PACKAGE_OUT:-$ROOT/build/package}/linux"
SOUNDFONT="${TABFORGE_SOUNDFONT:-$ROOT/Docs/ThirdPartyDownloads/generaluser/GeneralUser-GS.sf2}"
SOUNDFONT_LICENSE="${TABFORGE_SOUNDFONT_LICENSE:-$(dirname "$SOUNDFONT")/GeneralUser-GS-LICENSE.txt}"   # GeneralUser GS licence text, shipped beside the .sf2
TFPORTABLE_DIR="${TABFORGE_TFPORTABLE:-}"
TFVST3_DIR="${TABFORGE_TFVST3:-}"
WINE_DIR="${TABFORGE_WINE_DIR:-}"
HELPERS_DIR="${TABFORGE_HELPERS_DIR:-$SRC/src/TabForge.AudioEngine/native}"
APPIMAGETOOL="${APPIMAGETOOL:-$(command -v appimagetool || true)}"

while [ $# -gt 0 ]; do
  case "$1" in
    --out) OUT="$2/linux"; shift 2 ;;
    --wine) WINE_DIR="$2"; shift 2 ;;
    --helpers) HELPERS_DIR="$2"; shift 2 ;;
    --tfportable) TFPORTABLE_DIR="$2"; shift 2 ;;
    --tfvst3) TFVST3_DIR="$2"; shift 2 ;;
    --lame) TABFORGE_LAME="$2"; shift 2 ;;
    --ffmpeg) TABFORGE_FFMPEG_DIR="$2"; shift 2 ;;
    --appimagetool) APPIMAGETOOL="$2"; shift 2 ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done

[ -f "$SOUNDFONT" ] || { echo "SoundFont not found: $SOUNDFONT (set TABFORGE_SOUNDFONT)" >&2; exit 1; }
[ -f "$SOUNDFONT_LICENSE" ] || { echo "SoundFont licence not found: $SOUNDFONT_LICENSE (set TABFORGE_SOUNDFONT_LICENSE)" >&2; exit 1; }

# 0. The pinned Wine tree and the Windows helpers. Checked before the long publish, so a bad bundle fails in seconds.
WINE_ROOT=""
if [ -n "$WINE_DIR" ]; then
  if [ -f "$WINE_DIR/bin/wine" ]; then WINE_ROOT="$WINE_DIR"
  elif [ -f "$WINE_DIR/wine/bin/wine" ]; then WINE_ROOT="$WINE_DIR/wine"
  else echo "Wine bundle not found: $WINE_DIR/bin/wine (pass the tree that contains bin/ and lib/)" >&2; exit 1; fi
  for f in bin/wine bin/wineserver lib/wine/x86_64-unix/ntdll.so share/wine/wine.inf; do
    [ -f "$WINE_ROOT/$f" ] || { echo "Wine bundle is incomplete: missing $WINE_ROOT/$f" >&2; exit 1; }
  done
  if find "$WINE_ROOT" \( -iname '*mono*' -o -iname '*gecko*' \) -print -quit | grep -q .; then
    echo "Wine bundle contains wine-mono or wine-gecko; this package does not ship either" >&2; exit 1
  fi
  for f in tfvst32.exe tfvst64.exe tfvst3.dll tfvst3-x86.dll; do
    [ -f "$HELPERS_DIR/$f" ] || { echo "Windows helper not found: $HELPERS_DIR/$f (set --helpers)" >&2; exit 1; }
    [ "$(head -c2 "$HELPERS_DIR/$f" | od -An -c | tr -d ' ')" = 'MZ' ] || { echo "$f is not a Windows PE file" >&2; exit 1; }
  done
  [ -f "$SRC/native/wine/BUILD_PROVENANCE.md" ] || { echo "Wine provenance missing: $SRC/native/wine/BUILD_PROVENANCE.md" >&2; exit 1; }
  [ -f "$SRC/native/wine/COPYING.LIB" ] || { echo "Wine licence missing: $SRC/native/wine/COPYING.LIB" >&2; exit 1; }
  [ -f "$SRC/native/wine/LICENSE" ] || { echo "Wine licence missing: $SRC/native/wine/LICENSE" >&2; exit 1; }
  [ -f "$SRC/native/wine/AUTHORS" ] || { echo "Wine authors missing: $SRC/native/wine/AUTHORS" >&2; exit 1; }
fi

# 1. A scratch copy of the sources: the portable publish restores without the locked Windows lock files and must not rewrite them.
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
mkdir -p "$WORK/Source"
(cd "$SRC" && tar --exclude=./tests --exclude=./third_party --exclude='./native' --exclude=obj --exclude=bin --exclude=TestResults -cf - .) | tar -C "$WORK/Source" -xf -

# 2. Publish.
PUBLISH="$WORK/publish"
dotnet publish "$WORK/Source/src/TabForge.Avalonia/TabForge.Avalonia.csproj" -c Release -r "$RID" --self-contained true \
  -p:TabForgePortableBuild=true -p:EnableWindowsTargeting=true -p:RestoreLockedMode=false -p:PublishReadyToRun=false \
  -p:RestoreDisableParallel=true -p:ContinuousIntegrationBuild=true -p:Deterministic=true -o "$PUBLISH"

# 2a. The same licence and provenance documents the Windows package carries, plus the alphaTab patches (MPL-2.0 section 3.2:
#     the Source Code Form of the modified files must accompany the binary). Without these the Linux package would carry the
#     individual licence texts but no written offers (SoundTouch, LAME, mpg123, Wine), no TabForge MIT text and no SBOM.
cp "$SRC/LICENSE" "$PUBLISH/LICENSE.txt"
cp "$SRC/docs/THIRD_PARTY.md" "$PUBLISH/THIRD_PARTY.md"
[ -f "$SRC/docs/reference/SBOM.md" ] || { echo "SBOM.md not found: $SRC/docs/reference/SBOM.md" >&2; exit 1; }
cp "$SRC/docs/reference/SBOM.md" "$PUBLISH/SBOM.md"
mkdir -p "$PUBLISH/licenses"
alpha_patches=( "$SRC"/vendor/alphatab/*.patch )
[ -f "${alpha_patches[0]}" ] || { echo "alphaTab patches not found: $SRC/vendor/alphatab/*.patch" >&2; exit 1; }
for patch in "${alpha_patches[@]}"; do cp "$patch" "$PUBLISH/licenses/alphaTab-$(basename "$patch")"; done

# 2b. libtfportable (miniaudio audio + RtMidi MIDI, native/tfportable) built with CMake for this OS, unless a built one is given with --tfportable.
if [ -z "$TFPORTABLE_DIR" ]; then
  command -v cmake >/dev/null 2>&1 || { echo "cmake not found: install it or pass --tfportable" >&2; exit 1; }
  cmake -S "$SRC/native/tfportable" -B "$WORK/tfportable-build" -DCMAKE_BUILD_TYPE=Release 
  cmake --build "$WORK/tfportable-build" --config Release
  TFPORTABLE_DIR="$(dirname "$(find "$WORK/tfportable-build" -name 'libtfportable.so' | head -n1)")"
fi

# 2b2. libtfvst3 (VST3 host helper, native/tfvst3/posix): without it every VST3 fails with "the VST3 helper is missing". Needs git (pinned SDK fetch) unless TABFORGE_VST3SDK is set.
if [ -z "$TFVST3_DIR" ]; then
  command -v cmake >/dev/null 2>&1 || { echo "cmake not found: install it or pass --tfvst3" >&2; exit 1; }
  cmake -S "$SRC/native/tfvst3/posix" -B "$WORK/tfvst3-build" -DCMAKE_BUILD_TYPE=Release ${TABFORGE_VST3SDK:+-DVST3SDK_DIR="$TABFORGE_VST3SDK"}
  cmake --build "$WORK/tfvst3-build" --config Release
  TFVST3_DIR="$(dirname "$(find "$WORK/tfvst3-build" -name 'libtfvst3.so' | head -n1)")"
fi
[ -f "$TFVST3_DIR/libtfvst3.so" ] || { echo "libtfvst3.so not found in $TFVST3_DIR" >&2; exit 1; }

# 2d. Packaging guard: the private plug-in manifest is test-only and must never reach the publish output.
private_hits="$(find "$PUBLISH" -type f -name 'manifest.private*.json' -print -quit)"
if [ -n "$private_hits" ]; then
  echo "ERROR: the private plug-in manifest is in the publish output; refusing to package: $private_hits" >&2
  exit 1
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
cp "$SOUNDFONT_LICENSE" "$APPDIR/usr/bin/SoundFonts/GeneralUser-GS-LICENSE.txt"
if [ -n "$TFPORTABLE_DIR" ] && [ -f "$TFPORTABLE_DIR/libtfportable.so" ]; then
  cp "$TFPORTABLE_DIR/libtfportable.so" "$APPDIR/usr/bin/"                       # miniaudio + RtMidi (native/tfportable), built per OS
else
  echo "warning: libtfportable.so not given (--tfportable); the app starts without audio and MIDI backends" >&2
fi
cp "$TFVST3_DIR/libtfvst3.so" "$APPDIR/usr/bin/"                                   # VST3 host helper (native/tfvst3/posix), beside libtfportable.so

# 3b. The pinned Wine tree and the Windows helper PEs. The app finds wine at <program folder>/wine/bin/wine, which is
#     usr/bin/wine in the AppDir, so both the tar.xz and the AppImage mount are relocation-safe.
if [ -n "$WINE_ROOT" ]; then
  mkdir -p "$APPDIR/usr/bin/wine" "$APPDIR/usr/bin/ThirdParty/wine"
  cp -a "$WINE_ROOT/." "$APPDIR/usr/bin/wine/"
  chmod +x "$APPDIR/usr/bin/wine/bin/wine" "$APPDIR/usr/bin/wine/bin/wineserver"
  [ -f "$APPDIR/usr/bin/wine/bin/wine64" ] && chmod +x "$APPDIR/usr/bin/wine/bin/wine64"
  # Remove only development files and debug symbols from the Wine tree (owner request); the runtime needs bin, lib and share/wine.
  rm -rf "$APPDIR/usr/bin/wine/include" "$APPDIR/usr/bin/wine/share/man"
  find "$APPDIR/usr/bin/wine" -type f \( -name '*.a' -o -name '*.la' -o -name '*.h' -o -name '*.hpp' -o -name '*.debug' \) -delete
  while IFS= read -r f; do
    [ "$(head -c4 "$f" 2>/dev/null | od -An -c | tr -d ' ')" = '177ELF' ] || continue
    strip --strip-debug "$f" 2>/dev/null || true
  done < <(find "$APPDIR/usr/bin/wine" -type f)
  for f in tfvst32.exe tfvst64.exe tfvst3.dll tfvst3-x86.dll; do
    cp "$HELPERS_DIR/$f" "$APPDIR/usr/bin/$f"
  done
  cp "$SRC/native/wine/BUILD_PROVENANCE.md" "$APPDIR/usr/bin/ThirdParty/wine/BUILD_PROVENANCE.md"
  cp "$SRC/native/wine/COPYING.LIB" "$APPDIR/usr/bin/ThirdParty/wine/COPYING.LIB"
  cp "$SRC/native/wine/LICENSE" "$APPDIR/usr/bin/ThirdParty/wine/LICENSE"
  cp "$SRC/native/wine/AUTHORS" "$APPDIR/usr/bin/ThirdParty/wine/AUTHORS"
  grep -q "$WINE_TARBALL_SHA256" "$SRC/native/wine/BUILD_PROVENANCE.md" || {
    echo "ERROR: the Wine tarball SHA-256 in package-linux.sh does not match BUILD_PROVENANCE.md" >&2; exit 1;
  }
  printf '%s\n' \
    "Wine 9.21 (Kron4ek wine-9.21-amd64-wow64), a third-party prebuilt; not built by TabForge." \
    "The tarball is the vanilla WineHQ build. No Wine source is patched. The packager removes include/, share/man/," \
    "development files (.a/.la/.h/.hpp) and debug symbols, then ships the tree under wine/." \
    "The exact tarball URL, SHA-256, upstream Wine tag/commit, source links and build scripts are in BUILD_PROVENANCE.md." \
    "Licence: GNU LGPL 2.1 or later; the text is COPYING.LIB beside this file; the copyright notice is LICENSE and the" \
    "contributor list is AUTHORS." \
    "Written offer: for at least three years from this release, TabForge will give any third party, on request, a complete" \
    "machine-readable copy of the corresponding Wine source (tag wine-9.21 plus the packaging step), for no more than the cost of" \
    "physically performing the distribution. Ask through https://github.com/cobhc95/TabForge/issues." \
    "No wine-mono and no wine-gecko are included. Replace the whole wine/ folder with another compatible build to use yours." \
    > "$APPDIR/usr/bin/ThirdParty/wine/README.txt"
fi
if [ -n "$LAME" ]; then
  cp "$LAME" "$APPDIR/usr/bin/libmp3lame.so.0"
  LAME_VERSION="$(dpkg-query -W -f='${Version}' libmp3lame0 2>/dev/null || echo 'system build')"
  # libmp3lame NEEDs libmpg123.so.0 (LGPL-2.1+): ship it beside it. The app loads libmp3lame by full path (LameMp3Encoder), so the loader finds libmpg123 only through
  # libmp3lame's own RPATH $ORIGIN, set with patchelf; without patchelf libmpg123 is left to the host and the gate warns.
  MPG=""
  for c in /usr/lib/x86_64-linux-gnu/libmpg123.so.0 /usr/lib64/libmpg123.so.0 /usr/lib/libmpg123.so.0; do [ -f "$c" ] && MPG="$c" && break; done
  if [ -n "$MPG" ] && command -v patchelf >/dev/null 2>&1; then
    cp "$MPG" "$APPDIR/usr/bin/libmpg123.so.0"
    patchelf --set-rpath '$ORIGIN' "$APPDIR/usr/bin/libmp3lame.so.0" "$APPDIR/usr/bin/libmpg123.so.0"
    MPG_VERSION="$(dpkg-query -W -f='${Version}' libmpg123-0 2>/dev/null || echo 'system build')"
    mkdir -p "$APPDIR/usr/bin/ThirdParty/libmpg123"
    printf '%s\n' \
      "libmpg123.so.0 (mpg123 $MPG_VERSION, LGPL-2.1-or-later) is shipped as a separate replaceable library; patchelf" \
      "\$ORIGIN RPATH is set on both libmp3lame.so.0 and libmpg123.so.0 so they find each other, no source is patched." \
      "needed by libmp3lame.so.0. Source: https://mpg123.de/" \
      "Written offer: for at least three years from this release, TabForge will give any third party, on request, a complete" \
      "machine-readable copy of the corresponding mpg123 source, for no more than the cost of physically performing the distribution." \
      "Ask through https://github.com/cobhc95/TabForge/issues." \
      > "$APPDIR/usr/bin/ThirdParty/libmpg123/README.txt"
  else
    echo "warning: libmpg123 or patchelf not found (apt install libmpg123-0 patchelf); libmpg123 is not shipped, MP3 export needs it on the host" >&2
  fi
  mkdir -p "$APPDIR/usr/bin/ThirdParty/lame"
  printf '%s\n' "LAME $LAME_VERSION (LGPL-2.0) is shipped as libmp3lame.so.0, a separate replaceable library; patchelf sets \$ORIGIN RPATH on both libmp3lame.so.0 and libmpg123.so.0, no source is patched." \
    "Source: https://lame.sourceforge.io/ . Written offer: for at least three years from this release, TabForge will give any" \
    "third party, on request, a complete machine-readable copy of the corresponding LAME source, for no more than the cost of" \
    "physically performing the distribution. Ask through https://github.com/cobhc95/TabForge/issues." \
    > "$APPDIR/usr/bin/ThirdParty/lame/README.txt"
else echo "warning: libmp3lame not found (apt install libmp3lame0 or --lame); MP3 export is off" >&2; fi

# 2d. ffmpeg (LGPL-3 static build, a separate program FfmpegLocator finds next to TabForge) for Video export. Give the extracted BtbN "linux64-lgpl" folder
#     (holding bin/ffmpeg and LICENSE.txt) with --ffmpeg <folder> or TABFORGE_FFMPEG_DIR. Source: https://github.com/BtbN/FFmpeg-Builds (build scripts) and https://ffmpeg.org/download.html.
FFMPEG_DIR="${TABFORGE_FFMPEG_DIR:-}"
if [ -n "$FFMPEG_DIR" ] && [ -f "$FFMPEG_DIR/bin/ffmpeg" ]; then
  mkdir -p "$APPDIR/usr/bin/ThirdParty/ffmpeg"
  cp "$FFMPEG_DIR/bin/ffmpeg" "$APPDIR/usr/bin/ffmpeg"
  chmod +x "$APPDIR/usr/bin/ffmpeg"
  cp "$FFMPEG_DIR/LICENSE.txt" "$APPDIR/usr/bin/ThirdParty/ffmpeg/LICENSE.txt"
  if [ -f "$FFMPEG_DIR/BUILD_PROVENANCE.txt" ]; then
    cp "$FFMPEG_DIR/BUILD_PROVENANCE.txt" "$APPDIR/usr/bin/ThirdParty/ffmpeg/BUILD_PROVENANCE.txt"
  fi
  FFMPEG_VERSION="$("$FFMPEG_DIR/bin/ffmpeg" -version 2>/dev/null | sed -n '1p' || echo 'BtbN linux64-lgpl build')"
  printf '%s\n' "ffmpeg ($FFMPEG_VERSION) is shipped unmodified as the separate program 'ffmpeg' next to TabForge; TabForge only runs it as a process." \
    "Build: BtbN/FFmpeg-Builds linux64-lgpl. Build scripts and source: https://github.com/BtbN/FFmpeg-Builds and https://ffmpeg.org/download.html" \
    "Licence: LGPL-3.0-or-later for this build; the text is LICENSE.txt beside this file." \
    "Written offer: for at least three years from this release, TabForge will give any third party, on request, a complete" \
    "machine-readable copy of the corresponding FFmpeg source, for no more than the cost of physically performing the distribution." \
    "Ask through https://github.com/cobhc95/TabForge/issues." \
    "Replace 'ffmpeg' with your own build, or set TABFORGE_FFMPEG, to use another one." > "$APPDIR/usr/bin/ThirdParty/ffmpeg/README.txt"
else
  echo "warning: ffmpeg not given (--ffmpeg <folder> or TABFORGE_FFMPEG_DIR); Video export needs ffmpeg installed on the system" >&2
fi
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

# 4a. Licence gate: the package must carry the texts and written offers its binaries need.
required=(
  LICENSE.txt THIRD_PARTY.md SBOM.md
  licenses/LGPL-2.1_SoundTouch.Net.txt licenses/MPL-2.0_alphaTab.txt licenses/OFL-1.1_Bravura.txt
  licenses/MIT_NAudio.txt licenses/MIT_MeltySynth_and_notices.txt licenses/MIT_NLayer.txt licenses/MIT_NVorbis.txt
  licenses/THIRD-PARTY-NOTICES_DotNet_runtime.txt licenses/THIRD-PARTY-NOTICES_System.Drawing.Common.txt
  licenses/MIT_DotNet_runtime_LICENSE.txt licenses/MIT_DotNet_WindowsDesktop_runtime.txt
  licenses/MIT_Microsoft_System.Drawing.Common.txt licenses/MIT_Steinberg_VST3_SDK.txt
  licenses/GeneralUser-GS_LICENSE.txt licenses/LGPL-2.0_LAME.txt
  licenses/LGPL-2.1_mpg123.txt licenses/PublicDomain-MIT-0_miniaudio.txt licenses/MIT-style_RtMidi.txt
  licenses/BSD-3-Clause_AlphaSkia.txt licenses/BSD-3-Clause_ANGLE.txt licenses/MIT_CLAP.txt
  licenses/MIT_Avalonia.txt licenses/MIT_SkiaSharp.txt licenses/MIT_HarfBuzzSharp.txt
  licenses/THIRD-PARTY-NOTICES_SkiaSharp.txt licenses/MIT_Tmds.DBus.Protocol.txt licenses/MIT_MicroCom.Runtime.txt
  licenses/OFL-1.1_Inter.txt licenses/OFL-1.1_JetBrainsMono.txt
  SoundFonts/GeneralUser-GS-LICENSE.txt
)
for f in "${required[@]}"; do [ -f "$APPDIR/usr/bin/$f" ] || { echo "ERROR: licence file missing from the package: $f" >&2; exit 1; }; done
ls "$APPDIR/usr/bin/licenses/"alphaTab-*.patch >/dev/null 2>&1 || { echo "ERROR: alphaTab patch (MPL-2.0 source) missing from the package" >&2; exit 1; }
if [ -n "$WINE_ROOT" ]; then
  for f in ThirdParty/wine/BUILD_PROVENANCE.md ThirdParty/wine/COPYING.LIB ThirdParty/wine/LICENSE ThirdParty/wine/AUTHORS ThirdParty/wine/README.txt; do
    [ -f "$APPDIR/usr/bin/$f" ] || { echo "ERROR: Wine notice missing from the package: $f" >&2; exit 1; }
  done
fi

# 4b. Package SBOM: the actual hashes of the files in this Linux package, not just the managed package lock data.
row() {
  [ -f "$APPDIR/usr/bin/$2" ] || return 0
  printf '| %s | %s | %s |\n' "$1" "$(sha256sum "$APPDIR/usr/bin/$2" | cut -d' ' -f1)" "$3"
}
{
  echo "# TabForge Linux package SBOM"
  echo
  echo "Generated by \`Source/tools/package-linux.sh\` for TabForge $VERSION on $(date -u '+%Y-%m-%d %H:%M UTC')."
  echo "This is the package-specific companion to \`SBOM.md\`: every hash below is the SHA-256 of the file in this package."
  echo
  echo "| Component | SHA-256 | Source / revision / licence |"
  echo "| --- | --- | --- |"
  row "TabForge Linux package" "TabForge" "TabForge $VERSION; MIT (LICENSE.txt)"
  row "libtfportable.so" "libtfportable.so" "built from native/tfportable; miniaudio 0.11.25 (9634bedb), RtMidi 6.0.0 (1e5b4992); miniaudio MIT-0/public domain, RtMidi MIT-style"
  row "libtfvst3.so" "libtfvst3.so" "built from native/tfvst3/posix; Steinberg VST3 SDK v3.8.1_build_84 (3cdf9ca5); MIT"
  row "tfvst3.dll" "tfvst3.dll" "built from native/tfvst3 with the same VST3 SDK; MIT (TabForge code) + SDK notice"
  row "tfvst3-x86.dll" "tfvst3-x86.dll" "built from native/tfvst3 with the same VST3 SDK; MIT (TabForge code) + SDK notice"
  row "tfvst32.exe" "tfvst32.exe" "original TabForge helper; MIT"
  row "tfvst64.exe" "tfvst64.exe" "original TabForge helper; MIT"
  row "SoundTouch.Net.dll" "SoundTouch.Net.dll" "SoundTouch.Net 2.3.2, unmodified NuGet package; LGPL-2.1-or-later; source offer in THIRD_PARTY.md"
  row "GeneralUser-GS.sf2" "SoundFonts/GeneralUser-GS.sf2" "GeneralUser GS v2.0.3; free-use licence in SoundFonts/GeneralUser-GS-LICENSE.txt"
  row "libmp3lame.so.0" "libmp3lame.so.0" "LAME ${LAME_VERSION:-system build}; LGPL-2.0; separate replaceable library"
  row "libmpg123.so.0" "libmpg123.so.0" "mpg123 ${MPG_VERSION:-system build}; LGPL-2.1-or-later; separate replaceable library"
  row "ffmpeg" "ffmpeg" "${FFMPEG_VERSION:-not shipped}; LGPL build when present; separate program"
  echo
  echo "## Bundled Wine (third-party prebuilt)"
  echo
  echo "| Component | SHA-256 of the pinned tarball | Source / revision / licence |"
  echo "| --- | --- | --- |"
  echo "| Wine 9.21 (Kron4ek wine-9.21-amd64-wow64) | \`$WINE_TARBALL_SHA256\` | Wine tag \`wine-9.21\`, commit \`c9a8333c0f274201faf33d27abda1ebb5ae07cec\`; LGPL-2.1-or-later; source offer in ThirdParty/wine/README.txt |"
  echo
  echo "Wine files are the pinned tarball with headers, development files and debug symbols removed. The tarball hash above"
  echo "identifies the input; \`ThirdParty/wine/BUILD_PROVENANCE.md\` records the packaging step."
} > "$APPDIR/usr/bin/SBOM-package.txt"

# 5. Dependency gate: nothing in the package may need a library the package does not ship or every desktop Linux lacks.
command -v objdump >/dev/null 2>&1 || { echo "objdump not found (apt install binutils): cannot check the package's dependencies" >&2; exit 1; }
ALLOW=" libc.so.6 libm.so.6 libdl.so.2 libpthread.so.0 librt.so.1 libutil.so.1 libresolv.so.2 ld-linux-x86-64.so.2 linux-vdso.so.1 libgcc_s.so.1 libstdc++.so.6
 libz.so.1 libX11.so.6 libXext.so.6 libXcursor.so.1 libXrandr.so.2 libXi.so.6 libXfixes.so.3 libXrender.so.1 libxcb.so.1 libICE.so.6 libSM.so.6
 libfontconfig.so.1 libfreetype.so.6 libGL.so.1 libasound.so.2 libmvec.so.1 ${TABFORGE_NEEDED_EXTRA:-} "
ALLOW=" $(printf '%s' "$ALLOW" | tr '\n' ' ') "   # the list above spans lines: a name at the end of a line must still match " name "
# HOST: allowed, but not guaranteed on every desktop and not shippable: the gate prints a WARNING instead of staying silent (see 'Requires on the host' in the header).
# The Wine entries are its optional Unix drivers: X11/OpenGL/ALSA/PulseAudio/Wayland are normal desktop packages, the rest are
# optional device/media integrations Wine loads only when present. Missing ones do not stop audio or plug-ins.
HOST=" libX11.so.6 libXext.so.6 libXcursor.so.1 libXrandr.so.2 libXi.so.6 libXfixes.so.3 libXrender.so.1 libxcb.so.1 libICE.so.6 libSM.so.6 libfontconfig.so.1 libfreetype.so.6 libGL.so.1 libEGL.so.1 libasound.so.2
 libXcomposite.so.1 libXdamage.so.1 libXinerama.so.1 libXxf86vm.so.1 libxkbcommon.so.0 libxkbregistry.so.0 libwayland-client.so.0 libwayland-egl.so.1
 libOpenCL.so.1 libpulse.so.0 libavcodec.so.58 libavformat.so.58 libavutil.so.56 libglib-2.0.so.0 libgobject-2.0.so.0
 libgstaudio-1.0.so.0 libgstreamer-1.0.so.0 libgsttag-1.0.so.0 libgstvideo-1.0.so.0 libgphoto2.so.6 libgphoto2_port.so.12
 libpcap.so.1 libpcsclite.so.1 libsane.so.1 libudev.so.1 libusb-1.0.so.0 libcapi20.so.3 "
HOST=" $(printf '%s' "$HOST" | tr '\n' ' ') "   # the list spans lines: flatten it before the " name " match
WARNED=" "
# Wine's own Unix libraries load each other by bare name from its lib/wine/*-unix folders; treat those as shipped.
WINE_LIBS=" "
if [ -d "$APPDIR/usr/bin/wine/lib/wine" ]; then
  WINE_LIBS=" $(find "$APPDIR/usr/bin/wine/lib/wine" -type f -printf '%f\n' 2>/dev/null | sort -u | tr '\n' ' ') "
fi
# libstdc++/libgcc_s are allowed for the .NET runtime and Skia (every desktop has them) but NOT for our two libs, which link them statically.
# libasound.so.2 is a hard NEEDED of libtfportable.so (RtMidi ALSA); it is part of every desktop Linux audio stack.
# The core list above is checked first; a name in both lists is treated as HOST (warning).
BAD=0; GLIBC_MAX="2.0"
while IFS= read -r f; do
  [ "$(head -c4 "$f" | od -An -c | tr -d ' ')" = '177ELF' ] || continue
  base="$(basename "$f")"
  needed="$(objdump -p "$f" 2>/dev/null | sed -n 's/^ *NEEDED *//p' || true)"
  for n in $needed; do
    case "$base" in libtfportable.so|libtfvst3.so) case "$n" in libstdc++*|libgcc_s*) echo "ERROR: $base needs $n (static link failed)" >&2; BAD=1; continue ;; esac ;; esac
    [ -f "$APPDIR/usr/bin/$n" ] && continue
    case "$base:$n" in libcoreclrtraceptprovider.so:liblttng-ust*) continue ;; esac   # optional tracing provider, .NET tolerates its absence
    case "$WINE_LIBS" in *" $n "*) continue ;; esac   # a Wine Unix library resolves its siblings inside wine/lib/wine
    case "$HOST" in *" $n "*) case "$WARNED" in *" $n "*) ;; *) WARNED="$WARNED$n "; echo "warning: the package needs $n from the host (first seen in $base); see 'Requires on the host' in package-linux.sh" >&2 ;; esac; continue ;; esac
    case "$ALLOW" in *" $n "*) continue ;; esac
    echo "ERROR: $base needs $n, which the package does not ship and is not in the allowlist (ship it, or add it to ALLOW if every desktop has it)" >&2; BAD=1
  done
  v="$(objdump -T "$f" 2>/dev/null | grep -o 'GLIBC_[0-9][0-9.]*' | sed 's/GLIBC_//' | sort -V | tail -n1 || true)"
  if [ -n "$v" ] && [ "$(printf '%s\n%s\n' "$GLIBC_MAX" "$v" | sort -V | tail -n1)" = "$v" ]; then GLIBC_MAX="$v"; fi
done < <(find "$APPDIR/usr/bin" -type f)
for lib in libtfportable.so libtfvst3.so; do [ -f "$APPDIR/usr/bin/$lib" ] || { echo "ERROR: $lib is missing from the package" >&2; BAD=1; }; done
if [ -n "$WINE_ROOT" ]; then
  [ -x "$APPDIR/usr/bin/wine/bin/wine" ] || { echo "ERROR: wine/bin/wine is missing or not executable in the package" >&2; BAD=1; }
  [ -x "$APPDIR/usr/bin/wine/bin/wineserver" ] || { echo "ERROR: wine/bin/wineserver is missing or not executable in the package" >&2; BAD=1; }
  for f in tfvst32.exe tfvst64.exe tfvst3.dll tfvst3-x86.dll; do
    [ -f "$APPDIR/usr/bin/$f" ] || { echo "ERROR: $f is missing from the package" >&2; BAD=1; }
    [ "$(head -c2 "$APPDIR/usr/bin/$f" 2>/dev/null | od -An -c | tr -d ' ')" = 'MZ' ] || { echo "ERROR: $f is not a Windows PE file" >&2; BAD=1; }
  done
  for f in BUILD_PROVENANCE.md COPYING.LIB LICENSE AUTHORS README.txt; do [ -f "$APPDIR/usr/bin/ThirdParty/wine/$f" ] || { echo "ERROR: ThirdParty/wine/$f is missing" >&2; BAD=1; }; done
fi
VST3_SYMS="$(objdump -T "$APPDIR/usr/bin/libtfvst3.so" 2>/dev/null || true)"   # not piped into grep -q: with pipefail an early grep exit would SIGPIPE objdump
case "$VST3_SYMS" in *tfv3_create*) ;; *) false ;; esac || { echo "ERROR: libtfvst3.so does not export tfv3_create (visibility/export broken)" >&2; BAD=1; }
[ "$BAD" = 0 ] || { echo "package dependency check FAILED" >&2; exit 1; }
if [ -n "$WINE_ROOT" ]; then
  WINE_DLLPATH="$APPDIR/usr/bin/wine/lib/wine/x86_64-unix:$APPDIR/usr/bin/wine/lib/wine/x86_64-windows:$APPDIR/usr/bin/wine/lib/wine/i386-unix:$APPDIR/usr/bin/wine/lib/wine/i386-windows"
  if ! WINE_VERSION="$(env -i HOME="${HOME:-/tmp}" PATH="$APPDIR/usr/bin/wine/bin:/usr/bin:/bin" \
      WINELOADER="$APPDIR/usr/bin/wine/bin/wine" WINESERVER="$APPDIR/usr/bin/wine/bin/wineserver" WINEDLLPATH="$WINE_DLLPATH" \
      "$APPDIR/usr/bin/wine/bin/wine" --version 2>&1)"; then
    echo "ERROR: the packaged Wine loader does not run: $WINE_VERSION" >&2; exit 1
  fi
  echo "bundled Wine: $WINE_VERSION"
fi
echo "package dependency check passed; minimum glibc required: $GLIBC_MAX"
# Packaging guard: refuse to create the tar.xz or AppImage if the private plug-in manifest slipped into the tree.
private_hits="$(find "$APPDIR" -type f -name 'manifest.private*.json' -print -quit)"
if [ -n "$private_hits" ]; then
  echo "ERROR: the private plug-in manifest is in the AppDir; refusing to package: $private_hits" >&2
  exit 1
fi
echo "$GLIBC_MAX" > "$OUT/MIN_GLIBC.txt"

# 4. The packages. -9e is the strongest xz preset; -T0 uses all cores.
( cd "$OUT" && tar -cf - TabForge.AppDir | xz -9e -T0 > "TabForge-$VERSION-$RID.tar.xz" )
if [ -n "$APPIMAGETOOL" ] && [ -x "$APPIMAGETOOL" ]; then
  APPIMAGE="$OUT/TabForge-$VERSION-x86_64.AppImage"
  APPIMAGE_DONE=""
  if "$APPIMAGETOOL" --help 2>&1 | grep -q -- '--comp'; then
    echo "AppImage squashfs compression: trying xz (the strongest this tool may support)"
    if ARCH=x86_64 "$APPIMAGETOOL" --comp xz "$APPDIR" "$APPIMAGE" >"$OUT/appimage-xz.log" 2>&1; then
      APPIMAGE_DONE=1
    else
      echo "appimagetool refused xz; falling back to its default compression"
      rm -f "$APPIMAGE"
    fi
  fi
  if [ -z "$APPIMAGE_DONE" ]; then
    ARCH=x86_64 "$APPIMAGETOOL" "$APPDIR" "$APPIMAGE"
  fi
else
  echo "WARNING: appimagetool not found, so the PRIMARY Linux package (the AppImage) was NOT produced: only the AppDir and the beta-fallback tar.xz were made (get appimagetool from https://github.com/AppImage/appimagetool/releases and re-run)" >&2
fi
echo "primary Linux package (AppImage):"
for f in "$OUT"/*.AppImage; do
  [ -e "$f" ] || { echo "  (no AppImage was produced)"; break; }
  stat -c '  %n %s bytes' "$f"
done
echo "beta fallback (tar.xz):"
for f in "$OUT"/*.tar.xz; do
  [ -e "$f" ] || continue
  stat -c '  %n %s bytes' "$f"
done
echo "done: $OUT"
