#!/usr/bin/env bash
# Builds a relocatable-ish Wine tree from the pinned Wine source, on Ubuntu 22.04 (the package's glibc floor).
# This is the release-readiness attempt for TabForge's own Wine build. It is NOT wired into package-linux.sh yet:
# the shipped package still uses the Kron4ek prebuilt until an own build is proven end to end.
#
# Usage: tools/build-wine.sh [--stage <dir>] [--prefix <install prefix>]
# Output: <stage><prefix>/... plus <stage>/BUILD_PROVENANCE.md and <stage>/wine-version.txt.
set -euo pipefail

WINE_VERSION="9.21"
WINE_SOURCE_URL="https://dl.winehq.org/wine/source/9.x/wine-${WINE_VERSION}.tar.xz"
WINE_SOURCE_SHA256="4442b47ffd9b2ea457100e36ed5fd4e6f4d829d9db79a25e605175a988ca2fff"
PREFIX="/opt/tabforge-wine"
STAGE="${RUNNER_TEMP:-/tmp}/wine-own-build"

while [ $# -gt 0 ]; do
  case "$1" in
    --stage) STAGE="$2"; shift 2 ;;
    --prefix) PREFIX="$2"; shift 2 ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done

mkdir -p "$STAGE"
SRC="$STAGE/wine-$WINE_VERSION"
TARBALL="$STAGE/wine-$WINE_VERSION.tar.xz"
BUILD="$STAGE/build"
INSTALL="$STAGE/install"
LOG="$STAGE/build.log"

echo "== Wine $WINE_VERSION from $WINE_SOURCE_URL"
if [ ! -f "$TARBALL" ]; then
  curl -fsSL --retry 3 --retry-delay 2 -o "$TARBALL" "$WINE_SOURCE_URL"
fi
echo "$WINE_SOURCE_SHA256  $TARBALL" | sha256sum -c -
rm -rf "$SRC" "$BUILD" "$INSTALL"
mkdir -p "$SRC" "$BUILD" "$INSTALL"
tar -xf "$TARBALL" -C "$SRC" --strip-components=1

echo "== configure (i386,x86_64 WoW64; no tests)"
cd "$BUILD"
"$SRC/configure" \
  --prefix="$PREFIX" \
  --enable-archs=i386,x86_64 \
  --disable-tests \
  --without-oss \
  --disable-winemenubuilder \
  >"$STAGE/configure.log" 2>&1

echo "== make -j$(nproc)"
set +e
make -j"$(nproc)" >"$LOG" 2>&1
MAKE_STATUS=$?
set -e
if [ "$MAKE_STATUS" -ne 0 ]; then
  echo "Wine build FAILED (make exit $MAKE_STATUS); last 80 log lines:" >&2
  tail -n 80 "$LOG" >&2 || true
  exit "$MAKE_STATUS"
fi
echo "== make install"
make install DESTDIR="$INSTALL" >>"$LOG" 2>&1
ROOT="$INSTALL$PREFIX"
test -x "$ROOT/bin/wine" || { echo "installed tree has no bin/wine at $ROOT" >&2; exit 1; }

echo "== runtime check"
export WINEPREFIX="$STAGE/prefix"
export WINEDEBUG=-all
export WINEDLLOVERRIDES="mscoree,mshtml="
export WINELOADER="$ROOT/bin/wine"
export WINESERVER="$ROOT/bin/wineserver"
export WINEDLLPATH="$ROOT/lib/wine/x86_64-unix:$ROOT/lib/wine/x86_64-windows:$ROOT/lib/wine/i386-unix:$ROOT/lib/wine/i386-windows"
mkdir -p "$WINEPREFIX"
"$ROOT/bin/wine" --version >"$STAGE/wine-version.txt" 2>&1 || { cat "$STAGE/wine-version.txt" >&2; exit 1; }
# Create a prefix and run one built-in command. No display is needed for this; xvfb-run is only a safety net.
if command -v xvfb-run >/dev/null 2>&1; then
  xvfb-run -a "$ROOT/bin/wine" cmd /c echo tabforge-own-wine-ok >>"$STAGE/wine-version.txt" 2>&1 || true
else
  "$ROOT/bin/wine" cmd /c echo tabforge-own-wine-ok >>"$STAGE/wine-version.txt" 2>&1 || true
fi

HASH_WINE="$(sha256sum "$ROOT/bin/wine" | cut -d' ' -f1)"
HASH_SERVER="$(sha256sum "$ROOT/bin/wineserver" | cut -d' ' -f1)"
HASH_NTDLL="$(sha256sum "$ROOT/lib/wine/x86_64-unix/ntdll.so" | cut -d' ' -f1)"
HASH_XZ="$(sha256sum "$TARBALL" | cut -d' ' -f1)"
SIZE="$(du -sh "$ROOT" | cut -f1)"
GLIBC="$(objdump -T "$ROOT/bin/wine" 2>/dev/null | grep -o 'GLIBC_[0-9][0-9.]*' | sed 's/GLIBC_//' | sort -V | tail -n1 || true)"

cat >"$STAGE/BUILD_PROVENANCE.md" <<EOF
# TabForge own Wine build (release-readiness attempt)

Built on $(date -u '+%Y-%m-%d %H:%M UTC') on Ubuntu 22.04 in GitHub Actions, from the pinned Wine source. This is
TabForge's own build, not a third-party prebuilt. It is not shipped yet: the Linux package still uses the Kron4ek
prebuilt until an own build is proven end to end (relocation, package gate, distro containers).

| Item | Value |
| --- | --- |
| Wine version | $WINE_VERSION |
| Source tarball | <$WINE_SOURCE_URL> |
| Source SHA-256 | \`$HASH_XZ\` |
| Configure | \`--prefix=$PREFIX --enable-archs=i386,x86_64 --disable-tests --without-oss --disable-winemenubuilder\` |
| Compiler | $(cc --version 2>/dev/null | sed -n '1p' || echo unknown) |
| Installed size | $SIZE |
| Minimum GLIBC in \`bin/wine\` | \`${GLIBC:-unknown}\` |
| \`bin/wine\` SHA-256 | \`$HASH_WINE\` |
| \`bin/wineserver\` SHA-256 | \`$HASH_SERVER\` |
| \`lib/wine/x86_64-unix/ntdll.so\` SHA-256 | \`$HASH_NTDLL\` |
| \`wine --version\` | \`$(sed -n '1p' "$STAGE/wine-version.txt")\` |

No patches are applied to the Wine source by this script. The build tree is a normal \`make install\` under
\`$PREFIX\`; the runtime test sets \`WINELOADER\`, \`WINESERVER\` and \`WINEDLLPATH\` to the installed tree.
EOF

echo "== own Wine build OK"
cat "$STAGE/wine-version.txt"
echo "provenance: $STAGE/BUILD_PROVENANCE.md"
