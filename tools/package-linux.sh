#!/usr/bin/env bash
# TabForge for Linux (x86_64): publish linux-x64 self-contained and pack it as an AppImage (needs appimagetool), plus a plain tar.gz.
# NON-SHIPPING: Windows stays the only shipped platform until the 0.9.0 trial. REBUILD.cmd and the Windows installer never run this.
#
# Run on Linux (Ubuntu 22.04 or newer; the AppImage runs on the glibc it was built on or newer). Usage:
#   Source/tools/package-linux.sh [--out <folder>] [--tfportable <folder holding libtfportable.so>] [--tfvst3 <folder holding libtfvst3.so>] [--ffmpeg <folder>] [--appimagetool <path>]
# SoundFont: TABFORGE_SOUNDFONT=<GeneralUser-GS.sf2 path> and TABFORGE_SOUNDFONT_LICENSE=<its licence text>; both ship in usr/bin/SoundFonts. libtfportable.so and libtfvst3.so are built with cmake here (or --tfportable / --tfvst3 <folder>).
# appimagetool: https://github.com/AppImage/appimagetool/releases (MIT). Without it only the tar.gz and the AppDir are made.
#
# Build machine needs: dotnet SDK, cmake, a C/C++ toolchain, git (cmake fetches the pinned Steinberg VST3 SDK, miniaudio and RtMidi), libasound2-dev, binutils (objdump), python3.
#   Offline VST3 SDK: TABFORGE_VST3SDK=<checkout root> (passed as -DVST3SDK_DIR).
#
# The package (usr/bin of the AppDir, inside the tar.gz and the AppImage) contains:
#   TabForge (+ the self-contained .NET runtime and Avalonia/Skia libraries), libtfportable.so (audio + MIDI), libtfvst3.so (VST3 host helper),
#   libmp3lame.so.0 + libmpg123.so.0 (LAME and mpg123, both LGPL; only if found; RPATH $ORIGIN via patchelf, notice in ThirdParty/libmpg123/), ffmpeg + ThirdParty/ffmpeg/LICENSE.txt (LGPL build; only if --ffmpeg given),
#   SoundFonts/GeneralUser-GS.sf2 + GeneralUser-GS-LICENSE.txt, and the ThirdParty notices that the publish output carries.
# The user must NOT need to install: .NET, a SoundFont, ffmpeg, LAME, a newer libstdc++/libgcc (both native libs link them statically), a VST3 helper.
# Known limit: on Linux the VST3 plug-in editor window is not hosted yet (tfv3_has_editor answers 0); plug-ins load and process audio only.
#
# Requires on the host (a package cannot ship these; every normal desktop has them, the gate only WARNS when an ELF needs them):
#   libX11.so.6 libXext libXcursor libXrandr libXi libXfixes libXrender libxcb libICE libSM (apt: libx11-6 libxext6 libxcursor1 libxrandr2 libxi6 libxfixes3 libxrender1 libxcb1 libice6 libsm6),
#   libfontconfig.so.1 (libfontconfig1), libfreetype.so.6 (libfreetype6), libGL.so.1 (libgl1), libasound.so.2 (libasound2 / libasound2t64).
#   libstdc++.so.6 and libgcc_s.so.1 (libstdc++6, libgcc-s1) are .NET's own Linux requirements; libz.so.1 (zlib1g); libssl/libcrypto are loaded lazily by .NET.
# Dependency gate (step 5): every ELF file in the package is read with `objdump -p`; each NEEDED library must be shipped in the package or be in
# the allowlist below. Core glibc/.NET libs pass silently, host-provided desktop libs (list above) pass with a WARNING, anything else stops the script.
# liblttng-ust (optional tracing of libcoreclrtraceptprovider.so, loaded lazily by .NET) is ignored. The two native libs may not need libstdc++/libgcc_s at all.
# The highest GLIBC_ symbol version over all ELF files is printed and written to MIN_GLIBC.txt beside the packages: that is the oldest glibc the package runs on.
# Extra allowed libraries for a one-off: TABFORGE_NEEDED_EXTRA="libfoo.so.1 libbar.so.2".
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SRC="$(cd "$HERE/.." && pwd)"
ROOT="$(cd "$SRC/.." && pwd)"
RID="linux-x64"
VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$SRC/Directory.Build.props" | head -n1)"
OUT="${TABFORGE_PACKAGE_OUT:-$ROOT/build/package}/linux"
SOUNDFONT="${TABFORGE_SOUNDFONT:-$ROOT/Docs/ThirdPartyDownloads/generaluser/GeneralUser-GS.sf2}"
SOUNDFONT_LICENSE="${TABFORGE_SOUNDFONT_LICENSE:-$(dirname "$SOUNDFONT")/GeneralUser-GS-LICENSE.txt}"   # GeneralUser GS licence text, shipped beside the .sf2
TFPORTABLE_DIR="${TABFORGE_TFPORTABLE:-}"
TFVST3_DIR="${TABFORGE_TFVST3:-}"
APPIMAGETOOL="${APPIMAGETOOL:-$(command -v appimagetool || true)}"

while [ $# -gt 0 ]; do
  case "$1" in
    --out) OUT="$2/linux"; shift 2 ;;
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

# 1. A scratch copy of the sources: the portable publish restores without the locked Windows lock files and must not rewrite them.
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
mkdir -p "$WORK/Source"
(cd "$SRC" && tar --exclude=./.git --exclude=./tests --exclude=./third_party --exclude='./native' --exclude=obj --exclude=bin --exclude=TestResults -cf - .) | tar -C "$WORK/Source" -xf -

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

# 2b2. libtfvst3 (VST3 host helper, native/tfvst3/posix): without it every VST3 fails with "the VST3 helper is missing". Needs git (pinned SDK fetch) unless TABFORGE_VST3SDK is set.
if [ -z "$TFVST3_DIR" ]; then
  command -v cmake >/dev/null 2>&1 || { echo "cmake not found: install it or pass --tfvst3" >&2; exit 1; }
  cmake -S "$SRC/native/tfvst3/posix" -B "$WORK/tfvst3-build" -DCMAKE_BUILD_TYPE=Release ${TABFORGE_VST3SDK:+-DVST3SDK_DIR="$TABFORGE_VST3SDK"}
  cmake --build "$WORK/tfvst3-build" --config Release
  TFVST3_DIR="$(dirname "$(find "$WORK/tfvst3-build" -name 'libtfvst3.so' | head -n1)")"
fi
[ -f "$TFVST3_DIR/libtfvst3.so" ] || { echo "libtfvst3.so not found in $TFVST3_DIR" >&2; exit 1; }

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
if [ -n "$LAME" ]; then
  cp "$LAME" "$APPDIR/usr/bin/libmp3lame.so.0"
  # libmp3lame NEEDs libmpg123.so.0 (LGPL-2.1+): ship it beside it. The app loads libmp3lame by full path (LameMp3Encoder), so the loader finds libmpg123 only through
  # libmp3lame's own RPATH $ORIGIN, set with patchelf; without patchelf libmpg123 is left to the host and the gate warns.
  MPG=""
  for c in /usr/lib/x86_64-linux-gnu/libmpg123.so.0 /usr/lib64/libmpg123.so.0 /usr/lib/libmpg123.so.0; do [ -f "$c" ] && MPG="$c" && break; done
  if [ -n "$MPG" ] && command -v patchelf >/dev/null 2>&1; then
    cp "$MPG" "$APPDIR/usr/bin/libmpg123.so.0"
    patchelf --set-rpath '$ORIGIN' "$APPDIR/usr/bin/libmp3lame.so.0" "$APPDIR/usr/bin/libmpg123.so.0"
    mkdir -p "$APPDIR/usr/bin/ThirdParty/libmpg123"
    printf '%s
' "libmpg123.so.0 (mpg123, LGPL-2.1-or-later) is shipped unmodified as a separate replaceable library, needed by libmp3lame.so.0. Source: https://mpg123.de/"       > "$APPDIR/usr/bin/ThirdParty/libmpg123/README.txt"
  else
    echo "warning: libmpg123 or patchelf not found (apt install libmpg123-0 patchelf); libmpg123 is not shipped, MP3 export needs it on the host" >&2
  fi
else echo "warning: libmp3lame not found (apt install libmp3lame0 or --lame); MP3 export is off" >&2; fi

# 2d. ffmpeg (LGPL-3 static build, a separate program FfmpegLocator finds next to TabForge) for Video export. Give the extracted BtbN "linux64-lgpl" folder
#     (holding bin/ffmpeg and LICENSE.txt) with --ffmpeg <folder> or TABFORGE_FFMPEG_DIR. Source: https://github.com/BtbN/FFmpeg-Builds (build scripts) and https://ffmpeg.org/download.html.
FFMPEG_DIR="${TABFORGE_FFMPEG_DIR:-}"
if [ -n "$FFMPEG_DIR" ] && [ -f "$FFMPEG_DIR/bin/ffmpeg" ]; then
  mkdir -p "$APPDIR/usr/bin/ThirdParty/ffmpeg"
  cp "$FFMPEG_DIR/bin/ffmpeg" "$APPDIR/usr/bin/ffmpeg"
  chmod +x "$APPDIR/usr/bin/ffmpeg"
  cp "$FFMPEG_DIR/LICENSE.txt" "$APPDIR/usr/bin/ThirdParty/ffmpeg/LICENSE.txt"
  printf '%s\n' "ffmpeg (LGPL-3.0-or-later build, static) is shipped unmodified as the separate program 'ffmpeg' next to TabForge; TabForge only runs it as a process." \
    "Build: BtbN/FFmpeg-Builds linux64-lgpl. Build scripts and source: https://github.com/BtbN/FFmpeg-Builds and https://ffmpeg.org/download.html" \
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

# 5. Dependency gate: nothing in the package may need a library the package does not ship or every desktop Linux lacks.
command -v objdump >/dev/null 2>&1 || { echo "objdump not found (apt install binutils): cannot check the package's dependencies" >&2; exit 1; }
ALLOW=" libc.so.6 libm.so.6 libdl.so.2 libpthread.so.0 librt.so.1 libutil.so.1 libresolv.so.2 ld-linux-x86-64.so.2 linux-vdso.so.1 libgcc_s.so.1 libstdc++.so.6
 libz.so.1 libX11.so.6 libXext.so.6 libXcursor.so.1 libXrandr.so.2 libXi.so.6 libXfixes.so.3 libXrender.so.1 libxcb.so.1 libICE.so.6 libSM.so.6
 libfontconfig.so.1 libfreetype.so.6 libGL.so.1 libasound.so.2 libmvec.so.1 ${TABFORGE_NEEDED_EXTRA:-} "
ALLOW=" $(printf '%s' "$ALLOW" | tr '\n' ' ') "   # the list above spans lines: a name at the end of a line must still match " name "
# HOST: allowed, but not guaranteed on every desktop and not shippable: the gate prints a WARNING instead of staying silent (see 'Requires on the host' in the header).
HOST=" libX11.so.6 libXext.so.6 libXcursor.so.1 libXrandr.so.2 libXi.so.6 libXfixes.so.3 libXrender.so.1 libxcb.so.1 libICE.so.6 libSM.so.6 libfontconfig.so.1 libfreetype.so.6 libGL.so.1 libEGL.so.1 libasound.so.2 "
WARNED=" "
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
    case "$HOST" in *" $n "*) case "$WARNED" in *" $n "*) ;; *) WARNED="$WARNED$n "; echo "warning: the package needs $n from the host (first seen in $base); see 'Requires on the host' in package-linux.sh" >&2 ;; esac; continue ;; esac
    case "$ALLOW" in *" $n "*) continue ;; esac
    echo "ERROR: $base needs $n, which the package does not ship and is not in the allowlist (ship it, or add it to ALLOW if every desktop has it)" >&2; BAD=1
  done
  v="$(objdump -T "$f" 2>/dev/null | grep -o 'GLIBC_[0-9][0-9.]*' | sed 's/GLIBC_//' | sort -V | tail -n1 || true)"
  if [ -n "$v" ] && [ "$(printf '%s\n%s\n' "$GLIBC_MAX" "$v" | sort -V | tail -n1)" = "$v" ]; then GLIBC_MAX="$v"; fi
done < <(find "$APPDIR/usr/bin" -type f)
for lib in libtfportable.so libtfvst3.so; do [ -f "$APPDIR/usr/bin/$lib" ] || { echo "ERROR: $lib is missing from the package" >&2; BAD=1; }; done
VST3_SYMS="$(objdump -T "$APPDIR/usr/bin/libtfvst3.so" 2>/dev/null || true)"   # not piped into grep -q: with pipefail an early grep exit would SIGPIPE objdump
case "$VST3_SYMS" in *tfv3_create*) ;; *) false ;; esac || { echo "ERROR: libtfvst3.so does not export tfv3_create (visibility/export broken)" >&2; BAD=1; }
[ "$BAD" = 0 ] || { echo "package dependency check FAILED" >&2; exit 1; }
echo "package dependency check passed; minimum glibc required: $GLIBC_MAX"
echo "$GLIBC_MAX" > "$OUT/MIN_GLIBC.txt"

# 4. The packages.
( cd "$OUT" && tar -czf "TabForge-$VERSION-$RID.tar.gz" TabForge.AppDir )
if [ -n "$APPIMAGETOOL" ] && [ -x "$APPIMAGETOOL" ]; then
  ARCH=x86_64 "$APPIMAGETOOL" "$APPDIR" "$OUT/TabForge-$VERSION-x86_64.AppImage"
else
  echo "appimagetool not found: only the AppDir and the tar.gz were made" >&2
fi
echo "done: $OUT"
