# Controllers

One responsibility moved out of a window, each behind a host interface. A controller owns state and behaviour, not layout.

## Key types
- `EngineSyncController`: keeps the audio engine in step with the shown song and resends state after an engine restart.
- `PlaybackViewController`, `TransportControlsController`: playback display and transport buttons.
- `TrackController`, `ArrangementController`, `ClipEditController`, `SectionEditFlow`: track, timeline and clip edits.
- `TrackClipboardFlow`: whole-track copy, cut, paste, duplicate and delete (track row menu and track-row hotkeys).
- `AutosaveController`, `UpdateCheckController`, `RecordingController`: autosave, update check, recording.
- `WindowKeyRouter`: keyboard routing order.

## Pathway
- A controller talks to its window through an `I...Host` interface held in a field, not through delegates.
- Song changes go through `DocumentEdits.Run`.
- Each class starts with an Owns / Does not own / Tests comment; read it before the code.
- New behaviour goes into a small class like these, not into another `MainWindow` partial file.

## Must not depend on
A concrete window type; the audio engine project. Layout and WPF controls stay in `src/TabForge/Views/`.

## Tests
`TestWindowLifetime`, `TestDocumentOperations`, `TestEditControllers`, `TestKeyRoutingOrder`, `TestTrackListFit`, `TestUpdateCheck`, `TestRecordingPipeline`, `TestAutosaveRecovery`.
