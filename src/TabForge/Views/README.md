# Views

WPF controls and windows. Layout and drawing live here; data and rules do not.

## Key types
- `TabEditorControl` (with `StaffNotationRenderer`): the score and tab editor.
- `ArrangementPanel` (partial files): the timeline of tracks and clips.
- `InstrumentPanel`: fretboard, drum and keyboard display.
- `PreferencesWindow`, `MixerWindow`, `FxChainWindow`: settings, mixer and plug-in chains.
- `CommandPalette`, `DialogHost`, `ContextMenuLayouts`: command search, dialogs, menus.
- `TrackRowMenus`, `DeleteTrackPrompt`: the track row's right-click menu contents and its themed delete question.

## Pathway
- A window or control asks a controller or a service; it does not edit the song itself (`DocumentEdits.Run`).
- Drawing is lightweight: no per-frame layout work; playback only moves the playhead.
- Menus follow `ContextMenuLayouts`; every setting-like option also has a row in `PreferencesWindow`.

## Must not depend on
The audio engine project directly; reach it through `AudioEngineClient`.

## Tests
`TestTabEditorRenderInvariance`, `TestTabEditorLayoutMatrix`, `TestTabEditorInputScript`, `TestTabEditorLifetime`, `TestTabEditorPlaybackAllocation`, `TestNotationLayout`, `TestArrangementGeometry`, `TestContextMenuLean`, `TestPreferencesCatalog`, `TestEditorNavigation`.
