# Controllers

One responsibility moved out of a window, each behind a host interface. A controller owns state and behaviour, not layout.

## How to change me
1. Entry files: the matching `*Controller` / `*Flow` file here and its `I*Host` interface.
2. Owner class: the controller named for the behaviour; never `MainWindow`.
3. Tests to run: `TestEditControllers`, `TestUndoController` (full-suite build); `--areas ui,architecture`, plus `--areas architecture,hygiene` (see the test box in `docs/TESTING.md`).
4. Docs to update: this README, `START_HERE.md` pathways, `ArchitectureBudget.json`.

## Key types
- `EngineSyncController`: keeps the audio engine in step with the shown song and resends state after an engine restart.
- `PlaybackViewController`, `TransportControlsController`: playback display and transport buttons.
- `SelectionLoopController`: the selected (loop) area and the shared-selection to timeline sync.
- `TrackController`, `ArrangementController`, `ClipEditController`, `SectionEditFlow`: track, timeline and clip edits. `BarRangeFlow`: the timeline bar-range commands and one owner-scoped reusable Delete prompt. `EmptyBarFlow`: deleting bars that hold no notes (with the question and the clip warning).
- `ArrangementGestureState`: the arrangement gestures' pending undo steps (track edit, mix edit, track and section drags), the playing section and the fitted track count, against `IArrangementGestureHost`.
- `TrackClipboardFlow`: whole-track copy, cut, paste, duplicate and delete (track row menu and track-row hotkeys).
- `BarCommandFlow`: the Bar menu commands (insert, append, delete, time and key signature, clef, triplet feel, directions, double bar, repeat-bar marks, section name) against `IBarCommandHost`. `GlobalTuningController`: retuning every pitched track at once and the tuning button label. `TimelineMenuController`: the timeline right-click menus (selection, bar, section, clip) and their commands, against `ITimelineMenuHost`.
- `AutosaveController`, `UpdateCheckController`, `RecordingController`: autosave, update check, recording. `LiveMidiThru`: one live MIDI message to a track's sound (transpose, audio vs MIDI output). `ImportQueueController`: the background score-import queue and its status wiring.
- `WindowKeyRouter`: keyboard routing order. `HotkeyMaps`: the window's gesture maps (everywhere, clip, track list).
- `WindowCloseFlow`: saves and closes in progress, the window-close questions, the input gate while a save runs, degraded mode.
- `DocumentTabsController`: the tab commands (switch, duplicate, reorder, close one / others / to the right, Save-then-close). `ScoreExportController`: File > Export MIDI / ASCII / MusicXML / PDF and Save as template.
- `AppliedSettings`: what the window last applied from the settings, so a sync only redoes what changed.
- Window panes that build WPF controls live in `src/TabForge/Views/` with the same host pattern (`IPaneHost`): `DockLayoutController`,
  `ScoreZoomController`, `ToolPaletteController`, `InstrumentPanelController`, `MixerWindowsController`, `TrackGridDragController`.

## Pathway
- A controller talks to its window through an `I...Host` interface held in a field, not through delegates.
- Song changes go through `DocumentEdits.Run`.
- Each class starts with an Owns / Does not own / Tests comment; read it before the code.
- New behaviour goes into a small class like these, not into another `MainWindow` partial file.

## Must not depend on
A concrete window type; the audio engine project. Layout and WPF controls stay in `src/TabForge/Views/`.

## Tests
`TestWindowLifetime`, `TestDocumentOperations`, `TestEditControllers`, `TestKeyRoutingOrder`, `TestTrackListFit`, `TestUpdateCheck`, `TestRecordingPipeline`, `TestAutosaveRecovery`, `TestDocumentContext`.
