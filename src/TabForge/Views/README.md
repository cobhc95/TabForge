# Views

WPF controls and windows. Layout and drawing live here; data and rules do not.

## How to change me
1. Entry files: the control or window file for the screen; `*.xaml` beside it; owner: the owned controller or view part, not `MainWindow` or `TabEditorControl`.
2. Tests to run: `--areas ui,interactions,architecture` (full-suite build); check in the running app, plus `--areas architecture,hygiene` (see the test box in `docs/TESTING.md`).
3. Docs to update: `CHANGELOG.md`, the owner's test list, `ArchitectureBudget.json`.

## Key types
- `TabEditorControl` (with `StaffNotationRenderer`, in the Score folder): the score and tab editor. `ScoreEditPreparation` owns edit guards and rest-fill wrapping before a transaction. `ScoreLayoutEngine` partials and `ScoreLayoutIncrementalState` own per-bar widths, signature context and conservative stable-range reuse; page systems recompose globally when geometry can move. Tests: `TestScoreLayoutPartial`, `TestNotationLayout`.
- `ArrangementPanel` (partial files): the timeline of tracks and clips. Owned helpers behind host interfaces: `TrackColumnLayout`
  (`ITrackColumnHost`: column order, widths, header, column drag), `AddLaneController` (`IAddLaneHost`: the Add-track lane),
  `GroupDragController` (`IGroupDragHost`: group header drag), `MediaDropController` (`IMediaDropHost`: file drops and the drop ghost), `TuningButtonController` (`ITuningButtonHost`), `TrackRowWidgets`
  (`ITrackRowWidgetHost`: pan menu, mix-edit gestures, the instrument icon button), `TrackSilhouette` (the track-row instrument icons: the owner's line art from `src/TabForge/Assets/Icons/TrackRows/owner-icons.json` and the program-to-icon map), `SectionAutoScrollController` (edge scroll during a section drag), `ResizePreviewController` (splitter-drag shade) with `ResizeShade`, `ArrangementAutomation.cs` (screen-reader peers).
  `TrackControlWidgets`: stateless sliders, M/S buttons and colour palette shared with the Mixer.
- `TrackTimeline` (partial files): the arrangement's drawing surface; it renders, dispatches mouse input and keeps the public API. Gesture state lives in owned helpers that reach it through small host interfaces:
  - `DropPreviewGeometryController` and `TrackTimelineSongTimeMapController`: bounded drop feedback and cached repeat-aware clip endpoints.
  - `ClipGestureController` (`IClipGestureHost`): clip press, move, trims, fade handles, right-click, cancel, and the clip events.
  - `SectionEdgeController` (`ISectionEdgeHost`): section edge resize and Ctrl+drag marker-only moves.
  - `SectionTipController` (`ISectionTipHost`): the section lane's hover hint.
  - `AreaMoveController` (`IAreaMoveHost`): moving the selected bar range.
- `InstrumentPanel`: fretboard, drum and keyboard display (drum key map in `InstrumentPanel.Percussion.cs`); it draws at most at the score's text scale (set by `ScoreZoomController`; test `TestScoreScaleMatchesFretboard`). `BandViewController` (`IBandViewHost`; with `BandView`, `BandRow`, `BandLane`, `BandFollow`, `BandNoteGlow`, `BandLaneAligner` in the Band folder): one row per track; a lane engraves its track once and follows the playhead with the score's follow settings, glowing the sounding notes. `BandLayoutState` keeps the rows' layout in the song (`SongProject.BandLayout`). Tests: `TestBandViewRows`, `TestBandLaneCache`, `TestBandLaneClick`, `TestBandLayoutPreset`, `TestBandLayoutSaved`, `TestBandSettings`.
- `PreferencesWindow`, `MixerWindow` (collapse, colour chip and rules entry in `MixerWindow.Groups.cs`; the rules editor is `GroupRulesDialog`), `FxChainWindow`: settings, mixer and plug-in chains. `ChordFinderWindow`, `SongStatsWindow`, `TrackOutputWindow`: small Tools and Sound windows.
- `CommandPalette`, `DialogHost`, `ContextMenuLayouts`, `BarRangePrompt`: command search, owner-scoped reusable choice prompts, dialogs and menus. `EffectEditorFlow` (Views/EffectEditors, own README): bend and tremolo bar editors.
- `TrackRowMenus`, `DeleteTrackPrompt`, `ConvertTrackPrompts`: track-row menus and themed questions. The track-row, track-control, section and track-properties helpers are in Views/Timeline (own README). `MenuPopupWarmup` opens a temporary diagnostic popup to measure its real HWND/template path without activation.
- Main-window panes, each behind a host interface deriving from `IPaneHost`: `DockLayoutController` (layouts, Panels menu, side panel,
  fretboard pane size, full screen; its host is `DockHost`, the panes are registered by `DockPaneSetup` from `DockPaneTable`), `ScoreZoomController` (zoom box, page width), `ToolPaletteController` (palettes, pinned tools),
  `InstrumentPanelController` (view choice, scale finder, fretboard gesture, the instrument redraw), `ToolActionsFlow` (palette tools that edit through a dialog or a bar property, Check bars, Transpose; `IToolActionsHost`), `ScoreScrollController` (score wheel step, scroll gestures, cursor into view), `TransportSettingsController` (metronome, count-in and loop popups) with `TransportButtonStyle` (the toggle look), `MixerWindowsController` (Mixer and FX chain windows) with `MixerHost` (the host side of the Mixer, FX chain and mixer-windows interfaces, over `IMixerSurface`),
  `TrackGridDragController` (track grid row drag).

## Pathway
- A window or control asks a controller or a service; it does not edit the song itself (`DocumentEdits.Run`).
- Drawing is lightweight: no per-frame layout work; playback only moves the playhead. Anything that moves sits on whole device pixels (`PixelSnap`); a moved texture cache snaps too.
- Menus follow `ContextMenuLayouts`; every setting-like option also has a row in `PreferencesWindow`.

## Must not depend on
The audio engine project directly; reach it through `AudioEngineClient`.

`TestTabEditorRenderInvariance`, `TestTabEditorLayoutMatrix`, `TestTabEditorInputScript`, `TestTabEditorLifetime`, `TestTabEditorPlaybackAllocation`, `TestNotationLayout`, `TestArrangementGeometry`, `TestContextMenuLean`, `TestPreferencesCatalog`, `TestEditorNavigation`, `TestZoomComboShowsValue`, `TestFretboardPaneSize`, `TestMixer`. `PixelSnap` (Rendering folder): rounds offsets and text origins to whole device pixels and builds display-mode text, so a retained layer scrolled by a transform stays crisp (used by the Keyboard mode view). Test: `TestPixelSnap`. `TestMotionSharpnessScore`, `TestMotionSharpnessBand`, `TestMotionSharpnessPlayhead`, `TestMotionSharpnessScoreCaret`, `TestMotionSharpnessTimeline`, `TestMotionSharpnessRowDrag`, `TestTabEditorRenderInvariance`, `TestTabEditorLayoutMatrix`, `TestTabEditorInputScript`, `TestTabEditorLifetime`, `TestTabEditorPlaybackAllocation`, `TestNotationLayout`, `TestArrangementGeometry`, `TestContextMenuLean`, `TestPreferencesCatalog`, `TestEditorNavigation`, `TestZoomComboShowsValue`, `TestFretboardPaneSize`, `TestMixer`.
