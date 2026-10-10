# KeyboardMode (Keyboard mode)

Keyboard mode (experimental): a dock pane (shown by a layout swap like Band mode) that shows the selected track, whatever its kind, as falling notes over a piano keyboard at its sounding pitches, with a control bar, MIDI play-along, judging and wait mode. The hand engine lives in the Hands folder. Does not own playback timing (`ScoreTimeline`, the playback clock), the dock (`src/TabForge/Docking/DockPaneTable.cs`) or the window (`MainWindow.KeyboardMode.cs` is the only hook).

## Files
| File | Purpose |
| --- | --- |
| `KeyboardModeNote.cs`, `KeyboardNoteSource.cs` | Notes (lane, key, onset, length, hand, written finger) of a track with notation; key window; expected presses for the judge (hands filter, loop unrolled) |
| `KeyboardHands.cs` | The one call site of `HandAssigner` (per source build) and the hands filter (both, left, right, left only, right only) |
| `KeyboardModeJudge*.cs`, `KeyboardModeScore.cs` | Pure judge (windows by the Timing tolerance setting, Strict 60 / 150 ms, Normal 100 / 220, Relaxed 150 / 300 as default; misses, chords, sustain, latency) and totals |
| `KeyboardModeMidiListener.cs` | One client of the shared MIDI input (one device or all) while on; note events to song time, queued for the UI thread; its Thru event passes every channel message on |
| `KeyboardModePracticeTrack.cs` | While a MIDI keyboard is heard: the selected track skipped by the scheduler (`PlaybackEngine.SetPracticeSilence`, never its Mute) and the player's keys played through it (`LiveMidiThru`); cleared exactly |
| `KeyboardModeWaitMode.cs`, `KeyboardModeKeyboardSession.cs` | Wait mode ("Wait for me", no timing limit, chord keys accumulate, never changes the transport); one judged run per start, seek or loop wrap; keys held while stopped show held |
| `KeyboardModeWindow.cs`, `KeyboardModeClock.cs` | Notes in a stretch of time; virtual time across loop wraps |
| `KeyboardModePalette.cs`, `KeyboardColours.cs` | Theme colours, hand colours, lit-key gradients, hit / miss edges, contrast helpers |
| `KeyboardModeFrameController.cs`, `KeyboardModeHost.cs` | Frame step, note source cache, settings applied, MIDI device choice and 2 s rescan, practice track |
| `KeyboardModeControls.cs`, `KeyboardModeControlBar.cs` | The control bar (pane and pop-out): play, stop, speed, wait, skip, loop, hands, look-ahead, names, fingers, MIDI input with a connected dot |
| `KeyboardModeFeatureModule.cs`, `KeyboardModeSettings*.cs` | Menu row, hotkeys (mode, wait, skip, look-ahead longer / shorter), commands, settings and Preferences rows |
| `KeyboardModePopout*.cs` | The view in its own window with the same control bar (F11 / Esc full screen) |
| `KeyboardModeLayoutSwap.cs`, `KeyboardModeController.cs` | The swapped dock layout and putting the real one back |
| `KeyboardModePane.cs`, `KeyboardModeSurface.cs` | Header, control bar and the surface |
| `KeyboardModeView.cs`, `KeyboardModeLayout.cs` | The view; geometry: white key 1 : 6.4, black 58 % x 63 % with piano offsets, strip at most 30 % of the height (more octaves rather than stubby keys), speed cap 640 DIP/s, key outlines |
| `KeyboardPageDrawer.cs`, `KeyboardKeyPainter.cs`, `KeyboardChromeDrawer.cs`, `KeyboardFeedback.cs` | Note bars (key width, names, finger discs, faded hand) and hit / miss marks; lit keys clipped to their own outline; strip, legend, accuracy panel, progress line, grade flash; held-key and grade state |

## Rendering
Retained layers: back, notes page (about three look-aheads tall, redrawn only when time leaves it, the loop, options or size change, or the song jumps), marks (same transform as the page), strip, keys, hud, progress, grade. A frame moves one translate (`PixelSnap`); other layers redraw on change.

## Tests
`TestKeyboardModeNoteStream`, `TestKeyboardModeKeyView`, `TestKeyboardModeKeyboardSource`, `TestKeyboardModeJudge`, `TestKeyboardModeScore`, `TestKeyboardModeMidiListener`, `TestKeyboardModeWaitMode`, `TestKeyboardModeWaitChord`, `TestKeyboardModeWaitTransport`, `TestKeyboardModeTolerance`, `TestKeyboardModePopout`, `TestKeyboardModeLayout`, `TestKeyboardModePracticeTrack`, `TestKeyboardModeControlBar`, `TestKeyboardHands`, `TestPixelSnap`. Capture: `learn-playing` (`keys`, `keys-wait`), `learn-midi-menu`; captures and window tests give the controller their own hub (HubOverride), never the real MIDI devices.

## Play-along (MIDI input)
- Shared input (`AudioEngineClient.MidiInput`, `MidiInputHub`); the default is any available device; Off in the control bar stops listening.
- Driver thread: hub fan-out, the listener's stamp and enqueue, Thru (a dispatcher post). UI thread: drain, judge, practice track sends.
