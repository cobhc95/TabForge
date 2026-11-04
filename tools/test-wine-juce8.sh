#!/usr/bin/env bash
# Stage 8 part 1: does the real Dexed 1.1.0 (JUCE 8 Direct2D) VST3 editor paint under a given Wine build?
# This is a diagnostic experiment, not a gate: it records whether the window opened, a screenshot's pixel
# statistics and the close result, and exits 0 once the run itself completed (a black or absent editor is a
# result, not an infrastructure failure).
#
# Usage: tools/test-wine-juce8.sh --app <TabForge folder> --fixture <Dexed.vst3> --out <folder> --label <name> \
#          [--wine <loader>] [--dll-overrides "d2d1=d"]
# --wine sets TABFORGE_WINE for the packaged engine, so the bundled Wine is never changed. --dll-overrides
# writes a tiny loader wrapper that exports WINEDLLOVERRIDES (the engine sets its own to mscoree,mshtml=).
# Requires: Xvfb, openbox, wmctrl, xdotool, ImageMagick (import/convert/identify), xauth.
set -uo pipefail
export TABFORGE_INTERNAL=1   # packaged diagnostic modes are gated in shipped builds (Stage 13 A5)

APP=""; FIXTURE=""; OUT=""; LABEL=""; WINE=""; OVERRIDES=""
while [ $# -gt 0 ]; do
  case "$1" in
    --app) APP="$2"; shift 2 ;;
    --fixture) FIXTURE="$2"; shift 2 ;;
    --out) OUT="$2"; shift 2 ;;
    --label) LABEL="$2"; shift 2 ;;
    --wine) WINE="$2"; shift 2 ;;
    --dll-overrides) OVERRIDES="$2"; shift 2 ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done
[ -n "$APP" ] && [ -d "$APP" ] || { echo "--app must be a TabForge folder" >&2; exit 2; }
[ -n "$FIXTURE" ] && { [ -d "$FIXTURE" ] || [ -f "$FIXTURE" ]; } || { echo "--fixture not found" >&2; exit 2; }
[ -n "$OUT" ] && [ -n "$LABEL" ] || { echo "--out and --label are required" >&2; exit 2; }
for tool in Xvfb openbox wmctrl xdotool import convert identify xauth; do
  command -v "$tool" >/dev/null 2>&1 || { echo "missing tool: $tool" >&2; exit 1; }
done
mkdir -p "$OUT"
OUT="$(cd "$OUT" && pwd)"
DIR="$OUT/$LABEL"
mkdir -p "$DIR"
export HOME="$DIR/home"
mkdir -p "$HOME"
DISPLAY_NUM="${TABFORGE_XVFB_DISPLAY:-:98}"
export DISPLAY="$DISPLAY_NUM"
Xvfb "$DISPLAY_NUM" -screen 0 1600x1000x24 -nolisten tcp >"$DIR/xvfb.log" 2>&1 &
XVFB_PID=$!
sleep 0.5
openbox --sm-disable >"$DIR/openbox.log" 2>&1 &
WM_PID=$!
cleanup() {
  kill "$WM_PID" 2>/dev/null || true
  kill "$XVFB_PID" 2>/dev/null || true
  wait "$WM_PID" 2>/dev/null || true
  wait "$XVFB_PID" 2>/dev/null || true
}
trap cleanup EXIT
sleep 1

LOADER="$WINE"
if [ -n "$OVERRIDES" ]; then
  # Keep the wrapper in the real Wine's bin: WineRuntime derives WINESERVER, PATH and WINEDLLPATH from the
  # loader's folder, so a wrapper elsewhere would hide wineserver and make wineboot hang.
  LOADER="$(dirname "$WINE")/wine-juce8-$LABEL"
  cat > "$LOADER" <<EOF
#!/bin/sh
# Prefix setup (wineboot/reg/--version) must run without the override; only the plug-in launch gets it.
case "\${1:-}" in
  wineboot|reg|--version) exec '$WINE' "\$@" ;;
esac
WINEDLLOVERRIDES='$OVERRIDES'
export WINEDLLOVERRIDES
exec '$WINE' "\$@"
EOF
  chmod +x "$LOADER"
  sh -n "$LOADER"
  echo "wrapper $LOADER:"; sed 's/^/  /' "$LOADER"
fi
if [ -n "$LOADER" ]; then export TABFORGE_WINE="$LOADER"; fi

echo "=== $LABEL: fixture=$FIXTURE wine=${WINE:-bundled} overrides=${OVERRIDES:-none} loader=${LOADER:-bundled}"
DIAG="$DIR/diag"
mkdir -p "$DIAG"
"$APP/AppRun" --wine-editor "$FIXTURE" 1 "$DIAG" 60000 >"$DIR/editor.log" 2>&1 &
PID=$!
OPENED="no"
for _ in $(seq 1 600); do
  [ -f "$DIAG/editor-open.txt" ] && { OPENED="yes"; break; }
  kill -0 "$PID" 2>/dev/null || break
  sleep 0.2
done
[ -f "$DIAG/editor-open.txt" ] && OPENED="yes"

WIN=""
if [ "$OPENED" = yes ]; then
  for _ in $(seq 1 100); do
    WIN="$(xdotool search --name '^TabForge - ' 2>/dev/null | head -n1 || true)"
    [ -n "$WIN" ] && break
    sleep 0.1
  done
fi

BYTES=0; COLORS=0; STDDEV=0; GEOM="none"; PAINTED="no"
if [ -n "$WIN" ]; then
  xdotool getwindowgeometry --shell "$WIN" > "$DIR/geometry.txt" 2>&1 || true
  xwininfo -root -tree > "$DIR/window-tree.txt" 2>&1 || true
  xwininfo -id "$WIN" -tree >> "$DIR/window-tree.txt" 2>&1 || true
  GEOM="$(sed -n 's/^WIDTH=//p' "$DIR/geometry.txt" 2>/dev/null)x$(sed -n 's/^HEIGHT=//p' "$DIR/geometry.txt" 2>/dev/null)"
  for _ in $(seq 1 60); do
    gx="$(xdotool getwindowgeometry --shell "$WIN" 2>/dev/null | sed -n 's/^X=//p')"
    gy="$(xdotool getwindowgeometry --shell "$WIN" 2>/dev/null | sed -n 's/^Y=//p')"
    gw="$(xdotool getwindowgeometry --shell "$WIN" 2>/dev/null | sed -n 's/^WIDTH=//p')"
    gh="$(xdotool getwindowgeometry --shell "$WIN" 2>/dev/null | sed -n 's/^HEIGHT=//p')"
    if [ -n "$gx" ] && [ -n "$gy" ] && [ -n "$gw" ] && [ -n "$gh" ] && [ "$gw" -gt 0 ] && [ "$gh" -gt 0 ]; then
      if import -window root "$DIR/root.png" 2>/dev/null; then
        convert "$DIR/root.png" -crop "${gw}x${gh}+${gx}+${gy}" +repage "$DIR/editor.png" 2>/dev/null || true
      fi
    fi
    [ -s "$DIR/editor.png" ] || { sleep 0.5; continue; }
    BYTES="$(stat -c %s "$DIR/editor.png")"
    COLORS="$(convert "$DIR/editor.png" -format %k info: 2>/dev/null || echo 0)"
    STDDEV="$(convert "$DIR/editor.png" -colorspace Gray -format '%[fx:standard_deviation]' info: 2>/dev/null || echo 0)"
    if awk -v b="$BYTES" -v c="$COLORS" -v d="$STDDEV" 'BEGIN { exit !(b > 5000 && c > 32 && d > 0.02) }'; then
      PAINTED="yes"
      break
    fi
    sleep 0.5
  done
fi

CLOSE="not-opened"
if [ -n "$WIN" ]; then
  wmctrl -i -c "$(printf '0x%x' "$WIN")" >/dev/null 2>&1 || true
  CLOSE="sent"
  for _ in $(seq 1 150); do
    kill -0 "$PID" 2>/dev/null || { CLOSE="exited"; break; }
    sleep 0.2
  done
  if kill -0 "$PID" 2>/dev/null; then
    kill "$PID" 2>/dev/null || true
    CLOSE="killed"
  fi
else
  wait "$PID" 2>/dev/null || true
fi

LINE="label=$LABEL wine=${WINE:-bundled} overrides=${OVERRIDES:-none} opened=$OPENED painted=$PAINTED bytes=$BYTES colors=$COLORS stddev=$STDDEV geometry=$GEOM close=$CLOSE"
printf '%s\n' "$LINE" | tee -a "$OUT/summary.txt" > "$DIR/result.txt"
echo "$LINE"
exit 0
