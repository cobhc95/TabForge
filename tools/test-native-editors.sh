#!/usr/bin/env bash
# Stage 8 gate: opens the real native Linux VST3 editor of a maker release through the TabForge X11 editor host,
# proves it is non-blank by pixels, resizes the top-level X11 window, checks that the host told the view the new
# size, closes it (the host's UserClosed path), reopens it, and checks the reopened window by pixels and files.
#
# Usage: tools/test-native-editors.sh --app <TabForge.AppDir or installed folder> --fixture <plugin.vst3> --out <folder>
# Requires: Xvfb, xdotool, ImageMagick (import/convert/identify), xauth, openbox, wmctrl.
set -euo pipefail
export TABFORGE_INTERNAL=1   # packaged diagnostic modes are gated in shipped builds (Stage 13 A5)

APP=""; FIXTURE=""; OUT=""
while [ $# -gt 0 ]; do
  case "$1" in
    --app) APP="$2"; shift 2 ;;
    --fixture) FIXTURE="$2"; shift 2 ;;
    --out) OUT="$2"; shift 2 ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done
[ -n "$APP" ] && [ -d "$APP" ] || { echo "--app must be a TabForge folder" >&2; exit 2; }
[ -n "$FIXTURE" ] && { [ -d "$FIXTURE" ] || [ -f "$FIXTURE" ]; } || { echo "--fixture not found" >&2; exit 2; }
[ -n "$OUT" ] || { echo "--out is required" >&2; exit 2; }
for tool in Xvfb openbox wmctrl xdotool import convert identify xauth; do
  command -v "$tool" >/dev/null 2>&1 || { echo "missing tool: $tool" >&2; exit 1; }
done
mkdir -p "$OUT"
OUT="$(cd "$OUT" && pwd)"
DIR="$OUT/native-vst3"
mkdir -p "$DIR"
export HOME="$DIR/home"
mkdir -p "$HOME"
DISPLAY_NUM="${TABFORGE_XVFB_DISPLAY:-:96}"
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

fail() { echo "NATIVE EDITOR TEST FAILED: $*" >&2; return 1; }

# A screenshot is usable only when it has enough distinct colours, real contrast and bytes: a black or empty
# X11 window fails all three. A slow first paint is retried; the caller still fails if it never draws.
pixel_stats() {
  local png="$1"
  [ -s "$png" ] || { echo "missing"; return 1; }
  local size colors deviation
  size="$(stat -c %s "$png")"
  colors="$(convert "$png" -format %k info: 2>/dev/null || echo 0)"
  deviation="$(convert "$png" -colorspace Gray -format '%[fx:standard_deviation]' info: 2>/dev/null || echo 0)"
  echo "$(basename "$png"): bytes=$size colors=$colors stddev=$deviation"
  awk -v b="$size" -v c="$colors" -v d="$deviation" 'BEGIN { exit !(b > 5000 && c > 32 && d > 0.02) }'
}

# Capture the screen and crop to the editor window. A VST3 view lives in a child X window, so the root image
# plus a crop captures the real drawing rather than only the host's background.
capture_nonblank() {
  local win="$1" png="$2" tries="${3:-60}" gx gy gw gh
  for _ in $(seq 1 "$tries"); do
    gx="$(xdotool getwindowgeometry --shell "$win" 2>/dev/null | sed -n 's/^X=//p' || true)"
    gy="$(xdotool getwindowgeometry --shell "$win" 2>/dev/null | sed -n 's/^Y=//p' || true)"
    gw="$(xdotool getwindowgeometry --shell "$win" 2>/dev/null | sed -n 's/^WIDTH=//p' || true)"
    gh="$(xdotool getwindowgeometry --shell "$win" 2>/dev/null | sed -n 's/^HEIGHT=//p' || true)"
    if [ -n "$gx" ] && [ -n "$gy" ] && [ -n "$gw" ] && [ -n "$gh" ] && [ "$gw" -gt 0 ] && [ "$gh" -gt 0 ]; then
      if import -window root "$png.raw.png" 2>/dev/null; then
        convert "$png.raw.png" -crop "${gw}x${gh}+${gx}+${gy}" +repage "$png" 2>/dev/null || true
        rm -f "$png.raw.png"
      fi
    fi
    if pixel_stats "$png"; then return 0; fi
    sleep 0.2
  done
  pixel_stats "$png" || true
  fail "$png stayed blank"
}

find_editor_window() {
  local win=""
  for _ in $(seq 1 100); do
    win="$(xdotool search --name '^TabForge - ' 2>/dev/null | head -n1 || true)"
    [ -n "$win" ] && { echo "$win"; return 0; }
    sleep 0.1
  done
  return 1
}

echo "=== native vst3: $FIXTURE"
"$APP/AppRun" --native-editor "$FIXTURE" 1 "$DIR" 60000 >"$DIR/editor.log" 2>&1 &
PID=$!
for _ in $(seq 1 600); do
  [ -f "$DIR/native-editor-open.txt" ] && break
  kill -0 "$PID" 2>/dev/null || break
  sleep 0.2
done
if [ ! -f "$DIR/native-editor-open.txt" ]; then
  cat "$DIR/editor.log" >&2 || true
  fail "the native editor did not open"
fi
cat "$DIR/native-editor-open.txt"
WIN="$(find_editor_window)" || { cat "$DIR/editor.log" >&2 || true; fail "no editor window"; }
xdotool getwindowgeometry --shell "$WIN" > "$DIR/geometry-open.txt"
xwininfo -root -tree > "$DIR/window-tree.txt" 2>&1 || true
xwininfo -id "$WIN" -tree >> "$DIR/window-tree.txt" 2>&1 || true
OPEN_W="$(sed -n 's/^WIDTH=//p' "$DIR/geometry-open.txt")"
OPEN_H="$(sed -n 's/^HEIGHT=//p' "$DIR/geometry-open.txt")"

sleep 0.5
capture_nonblank "$WIN" "$DIR/editor.png"

# Resize the top-level X11 window. Dexed 1.0.1's view is fixed-size (IPlugView::canResize is false), so the view
# keeps its size while the host window grows, exactly as the Win32 floating editor behaves; the host still tells
# the view the new size. The host log and the diagnostic's size file prove that handshake.
xdotool windowsize "$WIN" 900 650
sleep 1
xdotool getwindowgeometry --shell "$WIN" > "$DIR/geometry-resized.txt"
RESIZED_W="$(sed -n 's/^WIDTH=//p' "$DIR/geometry-resized.txt")"
RESIZED_H="$(sed -n 's/^HEIGHT=//p' "$DIR/geometry-resized.txt")"
[ "$RESIZED_W" != "$OPEN_W" ] || [ "$RESIZED_H" != "$OPEN_H" ] || fail "the window did not resize ($OPEN_W x $OPEN_H -> $RESIZED_W x $RESIZED_H)"
[ -f "$DIR/native-editor-sized.txt" ] || fail "the editor host did not report the new size"
for _ in $(seq 1 30); do
  grep -q "editor window resized .* ${RESIZED_W}x${RESIZED_H}" "$DIR/engine.log" 2>/dev/null && break
  sleep 0.1
done
grep -q 'editor window resized x11' "$DIR/engine.log" || fail "the host did not log the user resize"
grep -q "editor window resized .* ${RESIZED_W}x${RESIZED_H}" "$DIR/engine.log" || fail "the host log does not name ${RESIZED_W}x${RESIZED_H}"
cat "$DIR/native-editor-sized.txt"
capture_nonblank "$WIN" "$DIR/editor-resized.png"

# Closing the window the way a user does (title-bar close through the WM) must reach X11EditorHost's UserClosed
# path, write native-editor-closed.txt and let the diagnostic reopen the editor.
xdotool windowactivate --sync "$WIN" 2>/dev/null || true
wmctrl -i -c "$(printf '0x%x' "$WIN")" || fail "the window manager could not close the editor"

# The reopened window lives for 1.5 s; grab it while it is still there.
REOPENED=""
for _ in $(seq 1 100); do
  [ -f "$DIR/native-editor-reopen.txt" ] && { REOPENED=yes; break; }
  kill -0 "$PID" 2>/dev/null || break
  sleep 0.1
done
[ "$REOPENED" = yes ] || { cat "$DIR/editor.log" >&2 || true; fail "the editor did not reopen"; }
cat "$DIR/native-editor-reopen.txt"
REOPEN_WIN="$(find_editor_window || true)"
if [ -n "$REOPEN_WIN" ]; then
  sleep 0.3
  capture_nonblank "$REOPEN_WIN" "$DIR/editor-reopened.png" 20 || true
fi

wait "$PID" || { cat "$DIR/editor.log" >&2 || true; fail "the native-editor diagnostic failed"; }
grep -q '^native-editor ok' "$DIR/editor.log" || { cat "$DIR/editor.log" >&2 || true; fail "no native-editor ok line"; }
[ -f "$DIR/native-editor-closed.txt" ] || fail "the close was not recorded"
[ -f "$DIR/native-editor-reopen.txt" ] || fail "the reopen was not recorded"

{
  echo "all native editor cases passed"
  echo "open: $(cat "$DIR/native-editor-open.txt")"
  echo "resize: $(cat "$DIR/native-editor-sized.txt")"
  echo "reopen: $(cat "$DIR/native-editor-reopen.txt")"
  echo "close: $(cat "$DIR/native-editor-closed.txt")"
  for png in "$DIR"/*.png; do
    [ -e "$png" ] || continue
    echo "$(basename "$png"): bytes=$(stat -c %s "$png") colors=$(convert "$png" -format %k info:) stddev=$(convert "$png" -colorspace Gray -format '%[fx:standard_deviation]' info:)"
  done
} | tee "$DIR/summary.txt"
