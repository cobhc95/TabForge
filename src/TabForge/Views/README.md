# Views

WPF controls and windows. Layout and drawing live here; data and rules do not.

## Key types
- `TabEditorControl` (with `StaffNotationRenderer`): the score and tab editor.
- `ArrangementPanel` (partial files): the timeline of tracks and clips. Owned helpers behind host interfaces: `TrackColumnLayout`
  (`ITrackColumnHost`: column order, widths, header, column drag), `AddLaneController` (`IAddLaneHost`: the Add-track lane),
  `GroupDragController` (`IGroupDragHost`: group header drag), `TuningButtonController` (`ITuningButtonHost`), `TrackRowWidgets`
  (`ITrackRowWidgetHost`: pan menu, mix-edit gestures, instrument picker), `ResizeShade`, `ArrangementAutomation.cs` (screen-reader peers).
  `TrackControlWidgets`: stateless sliders, M/S buttons and colour palette shared with the Mixer.
- `TrackTimeline` (partial files): the arrangement's drawing surface; it renders, dispatches mouse input and keeps the public API. Gesture state lives in owned helpers that reach it through small host interfaces:
  - `ClipGestureController` (`IClipGestureHost`): clip press, move, trims, fade handles, right-click, cancel, and the clip events.
  - `SectionEdgeController` (`ISectionEdgeHost`): section edge resize and Ctrl+drag marker-only moves.
  - `SectionTipController` (`ISectionTipHost`): the section lane's hover hint.
  - `AreaMoveController` (`IAreaMoveHost`): moving the selected bar range.
- `InstrumentPanel`: fretboard, drum and keyboard display.
- `PreferencesWindow`, `MixerWindow`, `FxChainWindow`: settings, mixer and plug-in chains.
- `CommandPalette`, `DialogHost`, `ContextMenuLayouts`: command search, dialogs, menus.
- `TrackRowMenus`, `DeleteTrackPrompt`: the track row's right-click menu contents and its themed delete question.
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

## Tests
`TestTabEditorRenderInvariance`, `TestTabEditorLayoutMatrix`, `TestTabEditorInputScript`, `TestTabEditorLifetime`, `TestTabEditorPlaybackAllocation`, `TestNotationLayout`, `TestArrangementGeometry`, `TestContextMenuLean`, `TestPreferencesCatalog`, `TestEditorNavigation`, `TestZoomComboShowsValue`, `TestFretboardPaneSize`, `TestMixer`.
