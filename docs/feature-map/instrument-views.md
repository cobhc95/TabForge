# Instrument views

Index: [Feature map](../FEATURE_MAP.md). Generated test tables: [tests.md](tests.md). The right-click menu of the same panel is in [note-context-menu.md](note-context-menu.md).

The instrument panel shows the selected track as a fretboard (guitar or bass), a drum kit, or a piano keyboard. The same panel is the instrument row of the Band view and the instrument of the video export.

- **Main code:** `InstrumentPanel` (the WPF element that draws the chosen renderer), `InstrumentPanelController` (view choice, scale finder, fretboard gesture, redraw), `InstrumentVisualizer` (builds the visual state from the song and the playhead), `FretboardRenderer`, `DrumRenderer` and `KeyboardRenderer` (in `Renderers.cs`).
- **Owner folders:** `src/TabForge/Visualization/` (drawing and geometry, no WPF host; README) and `src/TabForge/Views/` (the panel, its controller and the drum map window). The window hooks are `src/TabForge/MainWindow.Instrument.cs` (refresh) and `src/TabForge/MainWindow.View.cs` (right-click menu).
- **Central tables:** the view names are `InstrumentViews` (`src/TabForge/Services/TimelineSettings.cs`); the fretboard settings are `src/TabForge/Services/SettingsCatalog.Fretboard.cs`; the drum sounds are `src/TabForge/Services/DrumMaps.cs` with the JSON maps in `src/TabForge/Resources/DrumMaps/`.
- **Consumers of the same panel:** the Band view (`BandViewController.RefreshInstruments`, one panel per row), the video export (`VideoFrameSource`) and the diagnostic `--render-fretboard`.
- **Tests:** `--areas ui` for most; `--areas playback` for the drum and live preview tests; `--areas engine` for the drum dispatch test; `--areas smoke` for the marker size test. Narrow names (confirmed with `--only` on a full-suite build): `TestInstrumentVisualState`, `TestFretboardGeometry`, `TestFretboardConnector`, `TestFretStrumArrow`, `TestFretMarkerLabelContrast`, `TestFretMarkerSize`, `TestFretboardPaneSize`, `TestKeyboardPaneSize`, `TestScoreScaleMatchesFretboard`, `TestDockDefaultsAndFretboardPosition`, `TestAudioInstrumentPanel`, `TestInstrumentArtwork`, `TestGp5SvgIcons`, `TestDrumEntryAndQuickAddBars`, `TestDrumToPitchedConversion`, `TestDrumAndMelodicDispatchTogether`, `TestLiveInstrumentPreview`. Most are full-suite tests (`tests/full-suite/Views/`, `Playback/`, `Engine/`, `GuitarPro/`); `TestFretMarkerSize` is in `src/TabForge/SelfTests/Settings/`.

## Pathway to use

1. Playback (`PlaybackViewController`) calls RefreshInstrument when the bar or the note changes, every 200 ms, while a drum glow fades, and after edits and selection changes. It only does this when the panel follows playback.
2. `InstrumentPanelController.ShowInstrument` reads `host.Frame` (timeline, playhead, playing or paused, `VisualOptions`) and calls `InstrumentVisualizer.Build` for the selected track, and BuildEditingSelection for the cursor beat.
3. ApplyInstrumentView sets `state.Kind` from the view choice: the track's own choice, else the session's "all tracks" choice, else `Settings.Editing.InstrumentView`. ApplyAppearance then copies the keyboard size and the fretboard look from the settings.
4. `InstrumentPanel.SetState` keeps the state. ApplyActiveState shows the playing or paused state if there is one, else the cursor's selection, else the settings state. It swaps the renderer when Kind changes: Drums gives `DrumRenderer`, Keyboard gives `KeyboardRenderer`, and anything else gives `FretboardRenderer`.
5. OnRender calls `IInstrumentRenderer.Render` with the theme and the horizontal placement (left, centre or right). The fretboard draws its movement line through `FretboardConnector.Between` (not when it follows the score style) and its strum mark through `FretboardStrum`.
6. A click on the fretboard arms a gesture on mouse down. A fret is entered on mouse up through `Editor.Effects.ToggleFretAtPosition`, so a horizontal drag makes no edit. A click on a drum sound writes it on its TAB line (`DrumMaps` TabLine) through the same toggle. The keyboard view has no click entry.

## Where to add or change things

- **Layout or hit testing of the fretboard:** `FretboardGeometry` only. Drawing and clicks both read it, so change it there, not in the renderer.
- **Look of a fretboard element:** `FretboardRenderer` in `Renderers.cs`; marker size in `MarkerSizing`; marker number ink in `MarkerInk`.
- **A new view kind:** add a value to `InstrumentKind` and to `InstrumentViews`, map it in `InstrumentVisualizer.NaturalKind` and ApplyInstrumentView, add a renderer in `Renderers.cs` (implementing `IInstrumentRenderer`), and add its case to the switch in `InstrumentPanel.ApplyActiveState`. Then check the view in the right-click menu (`InstrumentMenus` in `src/TabForge/Views/ContextMenuSpecs.cs`).
- **A new appearance option:** a settings row (see "Add a setting" in `docs/RECIPES.md`), then copy it in `InstrumentPanelController.ApplyAppearance`.
- **Drum sounds and TAB lines:** `DrumMaps.cs` and the JSON files; the per-track map is edited in `DrumMapWindow` (opened from `TrackPropertiesDialog`).

## Classes

| File | Purpose |
| --- | --- |
| `src/TabForge/Visualization/InstrumentVisualizer.cs` | Builds `InstrumentVisualState` and `VisualNote` from the song, the playhead and the options; NaturalKind picks the view a track has by default |
| `src/TabForge/Visualization/FretboardGeometry.cs` | String and fret positions, the horizontal placement (`FretboardHorizontalPosition`), the minimum string gap; shared by drawing and hit testing |
| `src/TabForge/Visualization/FretboardConnector.cs` | Decides whether a movement line joins the sounding shape to the next one, and which positions it joins |
| `src/TabForge/Visualization/FretboardStrum.cs` | The strummed-chord arrow beside the nut and the merged tag pill |
| `src/TabForge/Visualization/Renderers.cs` | `IInstrumentRenderer`, `VisualTheme`, the shared `Draw` helpers, and `FretboardRenderer`, `DrumRenderer`, `KeyboardRenderer` |
| `src/TabForge/Visualization/KeyboardPaneSizing.cs` | Key range per keyboard size and the natural key height |
| `src/TabForge/Visualization/MarkerSizing.cs` | The effective size factor of fretboard markers for the marker size setting |
| `src/TabForge/Visualization/MarkerInk.cs` | White or near-black marker numbers, whichever reads on the marker's surface |
| `src/TabForge/Visualization/ColourText.cs` | The one place colour text is parsed (settings, themes, track colours, SVG icons) |
| `src/TabForge/Visualization/VisualOptions.cs` | Display options the builders take (fretboard layout that follows the score, track-row tint) |
| `src/TabForge/Views/InstrumentPanel.cs` | The WPF element: holds the state, picks the renderer, the height the panel needs, hover, placement drag, Shift+F10 and the Menu key |
| `src/TabForge/Views/InstrumentPanel.Percussion.cs` | The drum key map, its click hit test (TryHitPercussion) and the hit glow |
| `src/TabForge/Views/InstrumentPanel.Accessibility.cs` | The screen-reader peer: the panel's name and the notes it shows |
| `src/TabForge/Views/InstrumentPanelController.cs` | View choice (per track and per session), scale finder and highlight, appearance, the fretboard gesture, the redraw |
| `src/TabForge/Views/ScaleFinderWindow.cs` | The scale finder window the controller opens |
| `src/TabForge/Views/DrumMapWindow.cs` | The custom drum map: TAB line, text, staff position and notehead per GM sound |
| `src/TabForge/Services/DrumMaps.cs` | How one GM percussion sound is shown: TAB line, label, staff step, notehead |
| `src/TabForge/MainWindow.Instrument.cs` | The window's RefreshInstrument and the drum pad line helper (a thin partial) |
| `src/TabForge/Controllers/PlaybackViewController.cs` | Decides when playback asks for an instrument redraw |

## Debugging

- The panel as it looks at a moment: `TabForge.exe --render-fretboard <song> <out.png> [width height] [track=N] [now=ms] [scale=...] [view=keyboard]` (`src/TabForge/Diagnostics/DiagnosticCommands.RenderFretboard.cs`).
- A capture script can set the view (`{"instrument":"fretboard"|"keyboard"|"drums"|"auto"}`), the scale highlight (`{"highlight-scale":...}`) and the marker size (`{"marker-size":140}`); see the capture script reference.
- A right-click menu of the panel in each view: `TabForge.exe --probe-instrument-menu <report>` (`src/TabForge/Diagnostics/WindowProbes.InstrumentMenuProbe.cs`).
- Panel not redrawing during playback: check the follow setting first (FollowsFretboard), then the 200 ms rule in `PlaybackViewController`.
- Slow redraws: `TABFORGE_TRACE=ui` adds the slow-path lines for the UI.

## Known limits

- The keyboard view does not take note entry by click. Clicks enter notes only on the Guitar and Bass kinds (CanRepositionFretboard) and on drum sounds of a drum track.
- The per-track and "all tracks" view choices are for this session only. The default is `Settings.Editing.InstrumentView`.
- An audio track shows no instrument, only a short note.
- The panel draws what playback produced, with no presentation animation; the drum glow is the only per-frame redraw, and it runs only while the glow fades.
- `TabEditorControl.Keyboard.cs` is the score's keyboard input, not the piano keyboard view. Search for `KeyboardRenderer` or `KeyboardPaneSizing` for the piano.
