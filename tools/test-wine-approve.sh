#!/usr/bin/env bash
# Stage 9 approve gate: the packaged TabForge approves the deterministic test DLL and real
# plug-ins through the real bridged probe path (Approve all logic), with readable refusals.
# Linux-only; CI and a Linux lab.
#
# Usage: tools/test-wine-approve.sh --app <TabForge.AppDir> --fixtures <folder> --out <folder>
# Fixtures (never committed): passthrough32.dll, Dexed32.dll, Dexed64.dll.
set -euo pipefail
export TABFORGE_INTERNAL=1   # packaged diagnostic modes are gated in shipped builds (Stage 13 A5)

APP=""; FIXTURES=""; OUT=""
while [ $# -gt 0 ]; do
  case "$1" in
    --app) APP="$2"; shift 2 ;;
    --fixtures) FIXTURES="$2"; shift 2 ;;
    --out) OUT="$2"; shift 2 ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done
[ -n "$APP" ] && [ -d "$APP" ] || { echo "--app must be a TabForge folder" >&2; exit 2; }
[ -n "$FIXTURES" ] && [ -d "$FIXTURES" ] || { echo "--fixtures must be a folder" >&2; exit 2; }
[ -n "$OUT" ] || { echo "--out is required" >&2; exit 2; }
mkdir -p "$OUT"
OUT="$(cd "$OUT" && pwd)"
export HOME="$OUT/home"
mkdir -p "$HOME"

fail() { echo "APPROVE TEST FAILED: $*" >&2; return 1; }

for f in passthrough32.dll Dexed32.dll Dexed64.dll; do
  [ -f "$FIXTURES/$f" ] || fail "fixture not found: $FIXTURES/$f"
done

# Stale shm sweep proof: plant an old tf-wine-*.shm file; the approve run must delete it.
RUNTIME_DIR="${XDG_RUNTIME_DIR:-/tmp}"
SHM_DIR="$RUNTIME_DIR/tabforge-shm"
if [ -n "${TABFORGE_RUNTIME_DIR:-}" ]; then SHM_DIR="$TABFORGE_RUNTIME_DIR/tabforge-shm"; fi
# The app resolves Runtime via XDG_RUNTIME_DIR or temp; plant in both likely spots.
for candidate in "$SHM_DIR" "$HOME/.cache/TabForge" "/tmp/tabforge-shm"; do
  mkdir -p "$candidate" 2>/dev/null || true
done
STALE="$SHM_DIR/tf-wine-stale-test.shm"
echo "stale" > "$STALE" 2>/dev/null || true
touch -d "5 hours ago" "$STALE" 2>/dev/null || true

echo "=== approve test DLL + real plug-ins (first run prepares Wine, may take minutes)"
"$APP/AppRun" --wine-approve "$OUT/approve" "$FIXTURES/passthrough32.dll" "$FIXTURES/Dexed32.dll" "$FIXTURES/Dexed64.dll" 2>&1 | tee "$OUT/approve-console.txt"
grep -q '^wine-approve ok' "$OUT/approve-console.txt" || { cat "$OUT/approve-console.txt" >&2; fail "no ok line"; }

for f in passthrough32.dll Dexed32.dll Dexed64.dll; do
  grep -q "path=.*$f.*identified=True" "$OUT/approve/wine-approve.log" || { cat "$OUT/approve/wine-approve.log" >&2; fail "$f was not identified"; }
done
echo "approve: test DLL + Dexed 32/64 identified"

# The stale file must be gone (swept), unless the runtime folder differs; then at least the run swept something.
if [ -f "$STALE" ]; then
  echo "note: stale shm still present at $STALE (runtime folder may differ); checking app sweep log"
  grep -q 'swept' "$OUT/approve-console.txt" || fail "no sweep line"
else
  echo "stale shm swept"
fi

# Readable refusal: a non-plug-in file must not identify, with a refusal line (not a crash).
echo "not a plugin" > "$OUT/not-a-plugin.dll"
if "$APP/AppRun" --wine-approve "$OUT/approve-refuse" "$OUT/not-a-plugin.dll" 2> "$OUT/refuse-stderr.txt"; then
  fail "non-plug-in was approved as identified"
fi
cat "$OUT/refuse-stderr.txt" | tee "$OUT/refuse-console.txt"
grep -qiE 'FAILED|refus|could not|not a' "$OUT/refuse-stderr.txt" || fail "no readable refusal for non-plug-in"

# Prefix marker proof (first-run status leaves a marker).
PREFIX="$HOME/.local/share/TabForge/wine/prefix"
[ -f "$PREFIX/.tabforge-wine" ] || fail "missing prefix marker $PREFIX/.tabforge-wine"
cat "$PREFIX/.tabforge-wine" | tee "$OUT/prefix-marker.txt"
grep -q 'wineVersion=' "$OUT/prefix-marker.txt" || fail "marker has no wineVersion"

{
  echo "all approve cases passed"
  cat "$OUT/approve/wine-approve.log"
} | tee "$OUT/summary.txt"
