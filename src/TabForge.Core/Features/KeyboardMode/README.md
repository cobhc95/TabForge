# Core/KeyboardMode

The pure parts of Keyboard mode: the saved settings, the hands filter, the hand assignment (`Hands/`) and the size of the pane. No WPF and no UI types.

| File | Owns |
|---|---|
| `KeyboardModeSettings.cs` | The saved Keyboard mode values (timing, look-ahead, size, MIDI input, wait, hands, note names, fingers) and their bounds. |
| `KeyboardHands.cs` | The hands filter (both, left, right, left only, right only) and the one call of `HandAssigner` per note source. |
| `KeyboardStripGeometry.cs` | The numbers of the falling-notes strip: key window, key x and width, strip height, pixels per millisecond. |
| `IKeyboardSongPosition.cs` | The port the keyboard view reads its song time through (the playback session gives it). |
| `KeyboardModeSize.cs` | The three pane sizes (Normal, Large, Full). |
| `KeyboardModeNote.cs`, `KeyboardNoteSource.cs`, `KeyboardModeWindow.cs` | The note stream: notes of a keyboard track, the key window, expected presses, notes in a stretch of time (loop unrolled). |
| `KeyboardModeJudge*.cs`, `KeyboardModeScore.cs`, `KeyboardFeedback.cs` | The pure judge, the totals, and the held-key and grade-flash state. |
| `KeyboardDueKeys.cs`, `KeyboardColourMath.cs` | The keys due at a moment per hand; the ink (light or dark) that reads on a key's ground, using `ThemeColours.Contrast`. |
| `KeyboardPlayAlongStub.cs` | The fixed made-up play-along state of the captures (riff, judge, score, feedback); the WPF capture and the Avalonia parity shot both build it. |
| `Hands/` | The hand assignment engine (`HandAssigner`, `HandTypes`); see `Hands/README.md`. |

Does not own: the key outlines, the controls, the view, the judge's drawing or the dock layout swap.
Tests: `TestKeyboardHands`, `TestKeyboardHandsAccuracy`, `TestKeyboardModeLayout`, `TestPreferencesCatalog`.
