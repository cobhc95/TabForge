# Views/Video

The 0.7 video feature's WPF side. It is self-contained; the rest of the app reaches it through one menu item, one Render-window button, one toolbar button and one command.

- `VideoFrameSource`: draws the song at a given time off-screen into BGRA pixels (score with page turns, instrument, Band view), in the chosen theme, without touching the live window.
- `VideoExportProgress`: the export progress mapping (audio share, then `Frame i / n` with an ETA, counted from the range start).
- `VideoExportFlow`: File > Export > Video. Offline audio mix, then frame i at time i / fps, into `VideoEncoder`.
- `VideoRecordUi`: the Record video button, the REC indicator and the live-recording settings rows.

The encoder, the screen grabbers and the live session live in `Services/Video` (see `Services/README.md`).

Tests: `TestVideoExport`, `TestVideoFrameGolden`, `TestVideoFrameSourceGolden` (frame pixels per layout and theme), `TestLiveVideoRecord`.
