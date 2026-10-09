# Band view

Index: [Feature map](../FEATURE_MAP.md). Generated test tables: [tests.md](tests.md).

- **Main code:** `BandViewController`, `BandView`, `BandRow`, `BandLane`, `BandFollow` (follow mode from the score settings), `BandNoteGlow` (sounding-note glow, drawn by the score's `ScorePlayedChip`), `BandLayoutState` (shown tracks, order, rows per screen, row heights), `BandReorder` (row drag and edge auto-scroll) (`src/TabForge/Views/Band/`); `BandLayoutData` (saved with the song, `SongProject.BandLayout`); `BandSettings` (Timeline & Tracks > Band view); the Band layout in `DockLayoutController`
- **Pathway to use:** The controller reads playback from the document and gives every lane one playhead position; a lane engraves its track once (a one-line tab-only `TabEditorControl`) and slides it with a render transform. A click goes back through `IBandViewHost.ShowCursor`.
- **Tests:** `--areas ui`; `TestBandViewRows`, `TestBandLaneCache`, `TestBandLaneClick`, `TestBandLayoutPreset`, `TestBandPillsAndRows`, `TestBandRowSizing`, `TestBandReorder`, `TestBandNoteGlow`, `TestBandFollow`, `TestBandStoppedInstruments`, `TestBandLayoutSaved` and `TestBandLayoutSafety` (release), `TestBandSettings`, `TestBandLaneContent`, `TestBandReorderAutoScroll`, `TestBandEmptyState`
