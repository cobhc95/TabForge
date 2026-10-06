# Views

WPF controls and windows. Layout and drawing live here; data and rules do not.

## How to change me
1. Entry files: the control or window file for the screen; `*.xaml` beside it; owner: the owned controller or view part, not `MainWindow` or `TabEditorControl`.
2. Tests to run: `--areas ui,interactions,architecture` (full-suite build); check in the running app, plus `--areas architecture,hygiene` (see the test box in `docs/TESTING.md`).
3. Docs to update: `CHANGELOG.md`, the owner's test list, `ArchitectureBudget.json`.

## Key types
- `TabEditorControl` (with `StaffNotationRenderer`): the score and tab editor. `ScoreEditPreparation` owns edit guards and rest-fill wrapping before a transaction. `ScoreLayoutEngine` partials and `ScoreLayoutIncrementalState` own per-bar widths, signature context and conservative stable-range reuse; page systems recompose globally when geometry can move. Tests: `TestScoreLayoutPartial`, `TestNotationLayout`.
- `ArrangementPanel` (partial files): the timeline of tracks and clips. Owned helpers behind host interfaces: `TrackColumnLayout`
  (`ITrackColumnHost`: column order, widths, header, column drag), `AddLaneController` (`IAddLaneHost`: the Add-track lane),
  `GroupDragController` (`IGroupDragHost`: group header drag), `TuningButtonController` (`ITuningButtonHost`), `TrackRowWidgets`
  (`ITrackRowWidgetHost`: pan menu, mix-edit gestures, instrument picker), `ResizeShade`, `ArrangementAutomation.cs` (screen-reader peers).
  `TrackControlWidgets`: stateless sliders, M/S buttons and colour palette shared with the Mixer.
- `TrackTimeline` (partial files): the arrangement's drawing surface; it renders, dispatches mouse input and keeps the public API. Gesture state lives in owned helpers that reach it through small host interfaces:
  - `DropPreviewGeometryController` and `TrackTimelineSongTimeMapController`: bounded drop feedback and cached repeat-aware clip endpoints.
  - `ClipGestureController` (`IClipGestureHost`): clip press, move, trims, fade handles, right-click, cancel, and the clip events.
  - `SectionEdgeController` (`ISectionEdgeHost`): section edge resize and Ctrl+drag marker-only moves.
  - `SectionTipController` (`ISectionTipHost`): the section lane's hover hint.
  - `AreaMoveController` (`IAreaMoveHost`): moving the selected bar range.
- `InstrumentPanel`: fretboard, drum and keyboard display.
- `PreferencesWindow`, `MixerWindow`, `FxChainWindow`: settings, mixer and plug-in chains.
- `CommandPalette`, `DialogHost`, `ContextMenuLayouts`, `BarRangePrompt`: command search, owner-scoped reusable choice prompts, dialogs and menus.
- `TrackRowMenus`, `DeleteTrackPrompt`, `ConvertTrackPrompts`: track-row menus and themed questions. `MenuPopupWarmup` opens a temporary diagnostic popup to measure its real HWND/template path without activation.
- Main-window panes, each behind a host interface deriving from `IPaneHost`: `DockLayoutController` (layouts, Panels menu, side panel,
  fretboard pane size, full screen), `ScoreZoomController` (zoom box, page width), `ToolPaletteController` (palettes, pinned tools),
  `InstrumentPanelController` (view choice, scale finder, fretboard gesture), `MixerWindowsController` (Mixer and FX chain windows),
  `TrackGridDragController` (track grid row drag).

## Pathway
- A window or control asks a controller or a service; it does not edit the song itself (`DocumentEdits.Run`).
- Drawing is lightweight: no per-frame layout work; playback only moves the playhead.
- Menus follow `ContextMenuLayouts`; every setting-like option also has a row in `PreferencesWindow`.

## Must not depend on
The audio engine project directly; reach it through `AudioEngineClient`.

`TestTabEditorRenderInvariance`, `TestTabEditorLayoutMatrix`, `TestTabEditorInputScript`, `TestTabEditorLifetime`, `TestTabEditorPlaybackAllocation`, `TestNotationLayout`, `TestArrangementGeometry`, `TestContextMenuLean`, `TestPreferencesCatalog`, `TestEditorNavigation`, `TestZoomComboShowsValue`, `TestFretboardPaneSize`, `TestMixer`.
