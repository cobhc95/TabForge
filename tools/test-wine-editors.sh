#!/usr/bin/env bash
# Opens real Windows plug-in editors through the packaged TabForge Wine bridge on a virtual X server and proves each
# one is non-blank by pixels, resizable and closable (MsgEditorClosed), then reopened. Linux-only; CI and a Linux lab.
#
# Usage: tools/test-wine-editors.sh --app <TabForge.AppDir or installed folder> --fixtures <folder> --out <folder>
# Fixtures (never committed): Dexed32.dll, Dexed64.dll, Dexed.vst3 (bundle), TDRNova32.vst3.
# Requires: Xvfb, xdotool, ImageMagick (import/convert/identify), xauth.
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
for tool in Xvfb openbox wmctrl xdotool import convert identify xauth; do
  command -v "$tool" >/dev/null 2>&1 || { echo "missing tool: $tool" >&2; exit 1; }
done
mkdir -p "$OUT"
OUT="$(cd "$OUT" && pwd)"
DISPLAY_NUM="${TABFORGE_XVFB_DISPLAY:-:97}"
XVFB_PID=""
if [ "${TABFORGE_USE_EXISTING_DISPLAY:-0}" = 1 ]; then
  # Stage 17 Wayland leg: the caller already runs a headless compositor with XWayland and set DISPLAY to it.
  [ -n "${DISPLAY:-}" ] || { echo "TABFORGE_USE_EXISTING_DISPLAY=1 needs DISPLAY set" >&2; exit 2; }
  echo "using existing DISPLAY=$DISPLAY (no Xvfb)"
else
  export DISPLAY="$DISPLAY_NUM"
  Xvfb "$DISPLAY_NUM" -screen 0 1600x1000x24 -nolisten tcp >"$OUT/xvfb.log" 2>&1 &
  XVFB_PID=$!
  sleep 0.5
fi
# A real window manager makes the close path the same as a user's title-bar close: WM_DELETE_WINDOW -> Wine WM_CLOSE.
openbox --sm-disable >"$OUT/openbox.log" 2>&1 &
WM_PID=$!
cleanup() { kill "$WM_PID" 2>/dev/null || true; kill "$XVFB_PID" 2>/dev/null || true; wait "$WM_PID" 2>/dev/null || true; wait "$XVFB_PID" 2>/dev/null || true; }
trap cleanup EXIT
export HOME="${TABFORGE_EDITOR_HOME:-$OUT/home}"
mkdir -p "$HOME"
sleep 1

fail() { echo "EDITOR TEST FAILED: $*" >&2; return 1; }

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

# Capture the screen and crop to the editor window. A VST3 view lives in a child X window, which
# `import -window <top>` does not include; the root image plus a crop captures the real drawing.
capture_nonblank() {
  local win="$1" png="$2" tries="${3:-60}" gx gy gw gh
  for _ in $(seq 1 "$tries"); do
    gx="$(xdotool getwindowgeometry --shell "$win" | sed -n 's/^X=//p')"
    gy="$(xdotool getwindowgeometry --shell "$win" | sed -n 's/^Y=//p')"
    gw="$(xdotool getwindowgeometry --shell "$win" | sed -n 's/^WIDTH=//p')"
    gh="$(xdotool getwindowgeometry --shell "$win" | sed -n 's/^HEIGHT=//p')"
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

run_case() {
  local name="$1" path="$2" instrument="$3"
  local dir="$OUT/$name"
  mkdir -p "$dir"
  echo "=== $name: $path"
  [ -e "$path" ] || fail "fixture not found: $path"
  "$APP/AppRun" --wine-editor "$path" "$instrument" "$dir" 45000 >"$dir/editor.log" 2>&1 &
  local pid=$!
  for _ in $(seq 1 900); do
    [ -f "$dir/editor-open.txt" ] && break
    kill -0 "$pid" 2>/dev/null || break
    sleep 0.2
  done
  if [ ! -f "$dir/editor-open.txt" ]; then
    cat "$dir/editor.log" >&2 || true
    fail "$name: the editor did not open"
  fi
  cat "$dir/editor-open.txt"
  local win
  win="$(find_editor_window)" || { cat "$dir/editor.log" >&2 || true; fail "$name: no editor window"; }
  xdotool getwindowgeometry --shell "$win" > "$dir/geometry-open.txt"
  xwininfo -root -tree > "$dir/window-tree.txt" 2>&1 || true
  xwininfo -id "$win" -tree >> "$dir/window-tree.txt" 2>&1 || true

  # The first shot: the plug-in's own drawing inside the helper-owned top-level window. Some editors paint late.
  sleep 0.5
  capture_nonblank "$win" "$dir/editor.png"

  # Resize the helper-owned window; the helper tells the VST3 view and reports the new client size.
  xdotool windowsize "$win" 900 650
  sleep 1
  xdotool getwindowgeometry --shell "$win" > "$dir/geometry-resized.txt"
  capture_nonblank "$win" "$dir/editor-resized.png"

  # Closing the window the way a user does (title-bar close through the WM) must send MsgEditorClosed and let the
  # diagnostic reopen the editor. wmctrl talks to openbox, which sends WM_DELETE_WINDOW to Wine. The diagnostic writes
  # editor-sized.txt when it has processed the queued MsgEditorSized, so it is checked after the close.
  xdotool windowactivate --sync "$win" 2>/dev/null || true
  wmctrl -i -c "$(printf '0x%x' "$win")" || fail "$name: the window manager could not close the editor"
  wait "$pid" || { cat "$dir/editor.log" >&2 || true; fail "$name: --wine-editor failed"; }
  grep -q '^wine-editor ok' "$dir/editor.log" || { cat "$dir/editor.log" >&2 || true; fail "$name: no ok line"; }
  for _ in $(seq 1 30); do [ -f "$dir/editor-sized.txt" ] && break; sleep 0.1; done
  [ -f "$dir/editor-sized.txt" ] || fail "$name: the helper did not report the resize (MsgEditorSized)"
  cat "$dir/editor-sized.txt"
  grep -q '^editor-closed=yes' "$dir/editor-closed.txt" || fail "$name: MsgEditorClosed was not recorded"
  grep -q '^reopen ' "$dir/editor-reopen.txt" || fail "$name: the editor did not reopen"
  echo "$name: PASS"
}

run_case dexed32-vst2 "$FIXTURES/Dexed32.dll" 1
if [ "${TABFORGE_EDITOR_CASES:-all}" = all ]; then
  run_case dexed64-vst2 "$FIXTURES/Dexed64.dll" 1
  run_case dexed-vst3 "$FIXTURES/Dexed.vst3" 1
  run_case tdrnova32-vst3 "$FIXTURES/TDRNova32.vst3" 0
else
  # Stage 17 Wayland leg: one proven editor case under XWayland; the full set stays on Xvfb (Stage 7).
  for selected in $TABFORGE_EDITOR_CASES; do
    case "$selected" in
      dexed32-vst2) run_case dexed32-vst2 "$FIXTURES/Dexed32.dll" 1 ;;
      dexed64-vst2) run_case dexed64-vst2 "$FIXTURES/Dexed64.dll" 1 ;;
      dexed-vst3) run_case dexed-vst3 "$FIXTURES/Dexed.vst3" 1 ;;
      tdrnova32-vst3) run_case tdrnova32-vst3 "$FIXTURES/TDRNova32.vst3" 0 ;;
      *) echo "unknown editor case: $selected" >&2; exit 2 ;;
    esac
  done
fi

{
  echo "all editor cases passed"
  case_list="${TABFORGE_EDITOR_CASES:-all}"
  [ "$case_list" = all ] && case_list="dexed32-vst2 dexed64-vst2 dexed-vst3 tdrnova32-vst3"
  for case in $case_list; do
    echo "$case: $(cat "$OUT/$case/editor-open.txt")"
    echo "  $(cat "$OUT/$case/editor-sized.txt")"
  done
} | tee "$OUT/summary.txt"
