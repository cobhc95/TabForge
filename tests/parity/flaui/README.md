# FlaUI parity baselines

Text baselines of TabForge's window behaviour, recorded so a later UI port can be checked against them.
A test tool only: `ParityRecorder` is not referenced by the app and nothing in the app depends on it.

## Run

From the repository root, after building TabForge into `%TEMP%\tf-j2` (the default location):

```
dotnet build Source/tests/parity/flaui/ParityRecorder/ParityRecorder.csproj -c Release -o %TEMP%\tf-j2-rec
%TEMP%\tf-j2-rec\ParityRecorder.exe --check      # compare with baseline/, print the BASELINE line
%TEMP%\tf-j2-rec\ParityRecorder.exe --accept     # re-record baseline/
%TEMP%\tf-j2-rec\ParityRecorder.exe --ab <A.exe> <B.exe> --out <folder>   # A/B: every scenario on both builds
```

Options: `--exe <TabForge.exe>`, `--baseline <folder>`, `--dump <folder>` (write what was recorded, for review).

The A/B mode writes `folder\A` and `folder\B`, prints a unified diff of the differing lines (made by `git diff --no-index`)
and ends with `AB: N scenarios, M differences`. N counts the scenario lines (ids such as `F04` or `S05`) of build A; M counts
the lines removed from A, over all files. The A/B runs are off-screen; nothing is shown on the desktop.
The last line of `--check` is `BASELINE: N files, M differences`; `M` counts files that differ or are missing.
Exit code 0 means no differences.

A run starts the TabForge processes it owns, all off-screen with `--capture`, and kills only those process IDs. The
recorder sends no mouse or keyboard input, moves no window and changes no focus; the function scenarios drive the app
through UI Automation patterns only. Each run takes several minutes.

## Files in baseline/

| File | Content | Source |
|---|---|---|
| `tree-main.txt` | Main window element tree (ControlType, AutomationId, Name, enabled, keyboard-focusable, bounds relative to the window) | capture |
| `tree-tool-mixer.txt`, `tree-tool-preferences.txt`, `tree-tool-songinfo.txt`, `tree-tool-shortcuts.txt` | Tool window trees | capture |
| `hit-test-main.txt` | WM_NCHITTEST result per sample in the title bar interior (letter grid, legend of codes) | live |
| `cursor-main.txt` | Not captured as a cursor (see below); the WM_SETCURSOR handled flag per sample | live |
| `scenarios.txt` | Off-screen scenarios: window messages and UIA patterns only | capture |
| `scenarios-live.txt`, `hit-test-main.txt`, `cursor-main.txt` | Retired real-input files: each holds a MANUAL line (the real-input scenarios are on the owner's hand-test list) | retired |
| `scenarios-function.txt` | Function scenarios F01-F15 on an off-screen copy of the demo song (scratch copy under the capture output folder): menus, playback, copy/paste, undo/redo, dialogs, tool windows, keyboard mode, save. `tree=` is a hash of the window's tree with the playhead cell removed | capture |
| `tree-fn-<window>.txt` | Full element tree of each tool window or dialog opened by the function scenarios (playhead cell removed) | capture |

Volatile values are replaced with tokens (paths, GUIDs, times, dates, handles, numbers of six or more digits).
Bounds are physical pixels at the DPI in each file's header.

## Scenario status

Recorded:
- S01 double-click title bar: Normal -> Maximized (live, real input). S02 double-click again: back to Normal.
- S03 Win+Up, Win+Down, Win+Down (live, real keys): Normal -> Maximized -> Normal -> Minimized.
- S04 tab drag, first tab onto second (live, real pointer): the tab order is recorded before and after.
- S07 right-click on the score at the right screen edge (live): menu items and whether the menu stays on screen.
- S09 Alt+Space (live): the system menu items.
- S10 TransformPattern resize to 900x600 and to 10x10 (capture): the sizes the window accepts.
- S11 WM_SYSCOMMAND SC_CLOSE on the Mixer window (capture): closed.

MANUAL (needs a human, or not reproducible here):
- S05 Tab-key focus order: in both runs focus did not move across 30 presses, so no order was recorded.
  Capture (window messages) and live (real Tab) agree on that. Check by hand in the running app.
- S06 Ctrl+wheel zoom: a posted MK_CONTROL wheel message changed nothing in the tree. Check by hand.
- S08 Snap Layouts flyout over the maximise button: needs a human to judge the hover flyout.

## Not captured, and why

- **Cursor per sample.** The cursor a window sets is applied to the pointer's own thread, which another
  process cannot read. `cursor-main.txt` records only the WM_SETCURSOR handled flag.
- **Resize edges.** The resize border's hit test reads the live pointer (`GetCursorPos`), not the message point, and
  its results near the edges changed between runs on this machine. Only the title bar interior is recorded.
- **Multi-DPI monitors and touchpad input** are not covered; the baseline is for one display at the DPI in its header.

## Findings for the port

- `src/TabForge/Shell/WpfResizeBorderFrame.cs` (`WindowProc`) decides the resize codes from `GetCursorPos` rather
  than the `lParam` of WM_NCHITTEST. Hit-testing a point other than the pointer gives the pointer's answer.
- This app's custom title bar answers HTCLIENT, not HTCAPTION, so a double-click on it is handled by WPF, not by
  the non-client area.
- In the live run, 30 real Tab presses after setup did not move keyboard focus to any element of the app (S05). Worth
  checking by hand whether the score editor takes Tab, or focus is lost after the window changes state.

## Maintenance

- Re-record with `--accept` only when a difference is intended, and say why in the commit.
- The scratch folder is `%TEMP%\tf-j2-parity`; it is recreated on every run.
