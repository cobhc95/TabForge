# Settings entry-point parity

A setting that can be changed from more than one place must end in the same state from each: the open tabs, the stored value, the menu check mark. `TestSettingsEntryPointParity` (former settings-parity suite, removed in Batch 2 ([canonical note](TESTING.md#removed-in-batch-2-canonical-note)); area `settings`) changes each listed setting through Preferences (the live-preview path, which is also the Apply path) and through its other entry point on a window with two tabs, and compares the observed state. Preferences is modal, so another entry point cannot change a row while it is open; Cancel restores the old values through the same applier.

The rule: every entry point calls one window setter; each settings applier (subscribed to `MainWindowSettings.SettingsChanged` in `MainWindow.Compose.cs`, e.g. `ScoreSettingsApplier`) calls that same setter when the value changed since its last apply.

| Setting (row key) | Entry points | Apply path before | Apply path after | Status |
|---|---|---|---|---|
| Score page layout (`score.pagelayout`) | Preferences row, score right-click > Layout, View.HorizontalScroll-style capture probes | Menu: `SetContinuousScoreView` (tab + default). Preferences: wrote only the default for new songs; the open tabs kept their layout | Both: `SetContinuousScoreView`; Preferences reaches it through `ApplyScoreViewFromSettings`, other tabs get the flag directly | Fixed, tested |
| Score scrolling (`score.scrolling`) | Preferences row, score right-click > Layout, hotkey `View.HorizontalScroll` | Same as above (`SetHorizontalScoreView`) | Same as above | Fixed, tested |
| Default score display (`score.defaultnotation`) | Preferences row, View > Notation, score right-click > Notation | Preferences: `ApplyNotationFromSettings` (switches the shown tab); menus: `SetNotation` | Unchanged (already one path) | Tested |
| Score paper (`appearance.paper`) | Preferences row, View > Dark / Light score page | Preferences: `ApplyAppearance`; menu: `SetPaper` | Unchanged | Tested (shown tab; the paper is app-wide and forced onto each tab when it is shown) |
| Ledger lines (`score.ledger`) | Preferences row, score menu | Preferences: `ApplyScoreAppearanceSettings`; menu: `SetLedgerLines` | Unchanged | Tested |
| Highlight the playing bar (`follow.playingbar`) | Preferences row, View > Highlight playing bar, hotkey `View.PlayingBar` | Preferences: `ApplyFollowSettings` (also sets the menu check); menu: `SetPlayingBar` | Unchanged | Tested |
| Zoom | Score zoom box, wheel, hotkeys | No Preferences row (zoom is per tab, `SongDocument.ZoomFactor`) | Unchanged | Not applicable |
| Fretboard / arrangement visible, timeline note drawing | Preferences rows, View menu | Dock panel / `ApplyTimelineSettings` | Unchanged | Not covered here: the Timeline and View pages belong to the other settings audit |

Not verified: the on-screen look of the layout switch from Preferences (the test checks the editor flags, the tabs' stored flags and the stored value; the capture path was not run because the owner's app is open).
