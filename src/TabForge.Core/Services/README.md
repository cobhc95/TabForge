# Services

Musical logic, file formats, settings, limits and path policy. No WPF.

Files sit in topic folders (namespaces are unchanged): `Settings/`, `Hotkeys/`, `Files/`, `Project/`, `App/`, `Media/`, `Audio/`, `Clips/`, `Import/`, `Export/`, `Midi/`, `Editing/`, `Bars/`, `Sections/`, `Timeline/`, `Tracks/`, `Instruments/`, `Music/`, plus the older `Band/`, `Features/`, `Log/`, `SoundFont/` and `Video/`. A partial class stays in one folder.

## How to change me
1. Entry files (`Source/src/TabForge.Core/Services/`): `SettingsCatalog.cs` (page layout, Build) with `SettingsCatalog.General.cs`, `SettingsCatalog.Appearance.cs`, `SettingsCatalog.Score.cs`, `SettingsCatalog.Playback.cs`, `SettingsCatalog.Audio.cs`, `SettingsCatalog.Editing.cs`, `SettingsCatalog.Timeline.cs`, `SettingsCatalog.Fretboard.cs` and `SettingsCatalog.Tabs.cs` holding the rows, `AppSettings.cs` plus one file per settings class (`FollowSettings.cs`, `GeneralSettings.cs`, `AppearanceSettings.cs`, `PluginSettings.cs`, `AudioSettings.cs`, `EditingSettings.cs`, `TimelineSettings.cs`), `HotkeyCatalog.cs` (with `HotkeyCatalog.Actions.cs`, the command table), `ProjectService.cs`.
2. Owner class: `AppSettingsStore` (settings), `FilePathPolicy` (writes), `EditCommands` (edits).
3. Tests to run: `TestSettingsStoreSharedAcrossWindows`, `TestHotkeySettingsMigration` (full-suite build); `--areas settings,persistence`, plus `--areas architecture,hygiene` (see the test box in `Source/docs/guides/TESTING.md`).
4. Docs to update: `TOOLS_AND_HOTKEYS.md` and hotkey presets for commands, `CHANGELOG.md`. 

## Key types
- `ClipLoop`, `ClipTrim`, `TrailingBars`: a clip longer than its media loops (the pieces it plays as); the edge-trim arithmetic; the empty bars removed at the end when clips shrink. `SongExtent` grows the song to hold the clips.
- `AppSettingsStore`: the one shared settings store; `SettingsValidator` bounds values, `SettingsMigration` upgrades old files, `SettingsFileService` reads and writes.
- `FilePathPolicy`: atomic writes, leftover sweeps, reserved-name checks. `InputLimits` bounds every read.
- Import and MIDI helpers (moved in R12): `DrumMaps` (drum notes per kit and notation style), `NoteMapper` (the paste mapping report), `MidiClipToTab` (a recorded MIDI clip written into a track), `ImportWorker` (the out-of-process score import: its pipe protocol and limits).
- `ProjectService`: the .tforge format; `GuitarProImporter` (with `GuitarProBarConverter`, `GuitarProBeatReader`, `GuitarProTrackConverter`) and `GuitarProExporter`: .gp and .gp5. `GuitarProBarConverter` places an on-beat grace and its half-slot principal in one cell and gives a tie the pitch of the first note of its chain. Adding a format: `../../../docs/guides/ADD_IMPORT_FORMAT.md`.
- `EditCommands` (partial files): the note and bar edits the editor calls. Copy, cut and paste live here too: the clip format (`ScoreClip`), the one score clipboard over the `IScoreClipboard` port (`ClipboardService`), the paste rules (`PasteCommands` in `EditCommands.Paste*.cs`, `BarLocalPaste`) and the timeline clip rules (`TimelineClips`).
- `BarRangeEditor`, `BarFill`, `BarGrid`, `BarRangeGaps`, `BarRangePromptText`, `BarSignatures`, `EmptyBars`, `SectionLayout`, `ShortNoteFill`: the bar-range edits and their gap, fill and prompt rules. `SectionReorderService`, `UserTemplates`, `EmbeddedBinding`, `GpExportPreflight`: section moves, user templates, embedded song data and the export preflight.
- `EditCommands` (partial files): the note and bar edits the editor calls. Pasting is in `Source/src/TabForge.Core/Services/EditCommands.Paste*.cs`.
- `BarStructureEdits` (insert, delete, repeat and duplicate bars as one-undo-step document edits), `WholeBarCut` (the model half of a whole-bar cut or delete over a range, with its clip question port `IBarDeleteQuestions`), `BarRangeEditor`, `BarFill`, `BarGrid`, `BarRangeGaps`, `BarRangePromptText`, `BarSignatures`, `EmptyBars`, `SectionLayout`, `ShortNoteFill`: the bar-range edits and their gap, fill and prompt rules. `SectionReorderService`, `UserTemplates`, `EmbeddedBinding`, `GpExportPreflight`: section moves, user templates, embedded song data and the export preflight.
- `AudioDataFile`, `ProjectValidator`: the audio data file and the project checks it runs before a save or an open.
- `MediaPathPolicy`, `MediaContext`, `MediaAccess`: the linked-audio gate. A media path is classified local, removable, network or device; a local link is judged by the file it leads to through `IPathResolver` (`Source/src/TabForge.Core/Platform/`). `MediaContext` takes its approvals store and persist callback from the app (`SongDocument`). `WaveformCache` (in `Audio/`) reads the waveforms through this gate.
- `CommandRegistry`: the `id -> action` table behind the window's plain commands (filled in `MainWindow.Commands.cs`).
- `MediaStoredPaths`, `MediaCollectPlan`, `MediaCollector`, `MediaCollectApply`, `MediaSavePolicy`, `MediaCollectLayout` (`MediaLayouts`), `MediaRelink`: media collect and relink. A clip path inside the song folder is written relative on save (the live song keeps the absolute path); a save may copy audio from outside the folder into `<Song> Media` or, in the One song folder layout, move the song into a folder named after it with the audio in `Audio` (plan first, copy on a worker under a temp name then rename, never overwrite, never delete originals); the path change is one undo step; missing files are found and relinked by name (a folder search covers every subfolder; several matches pick the common size, then the newest). The window side is `MainWindowMediaCollect` and `MediaSaveStep`.
- `AudioSettings`, `MediaApproval`: the playback preferences and the linked-audio folders the user approved, as the settings file holds them. `MediaApproval` was split out of `MediaPathPolicy.cs` (P2-D) and both are in Core now.
- Settings classes in Core: `AppearanceSettings`, `EditingSettings` (with `PasteQuestions`), `TimelineSettings`, `BandSettings`, `TabSettings`, `KeyboardModeSettings` and `KeyboardHands` (`KeyboardMode/`). `AppSettings`, `PluginSettings`, the `SettingsCatalog` partials, `AppSettingsStore`, `SettingsMigration`, `SettingsValidator` and `SettingsFileService` are in Core, so the WPF and Avalonia apps read and write one settings file format; feature settings bounds run through `CatalogComposition.NormalizeSettings`.
- `PasteSpecialOptions`: the Paste Special choices (repeat, placement, octave shift, keep string and fret, bar settings), normalised to their ranges; the paste itself is `PasteCommands.PasteSpecial` in the app.
- `HotkeyCatalog`: every bindable command (`HotkeySettings` holds the user's Hotkey 1 and Hotkey 2).
- `AlphaTabBoundary`: the one route to the bundled alphaTab. It checks the patched build and sets the per-import limit.
- `MusicXmlExportService`, `MidiExportService`, `AsciiExportService`: exports. `Export/LegacyGp/` (`LegacyGpExport`, `LegacyGpWriter*`, `LegacyGpPreflight`, `LegacyGpFormat`): a copy of the song as .gp5 / .gp4 / .gp3, built from the same score model as the .gp export, with the list of what the chosen format changes or leaves out.
- `VideoEncoder`: writes an MP4 (H.264 + AAC) through Media Foundation (hand-written COM interop). Live mode drops frames when its queue is full; offline mode waits. Tests: `TestVideoEncoderMp4`, `TestVideoEncoder4k60`.
- Live recording (Services/Video folder): `LiveVideoSession` (audio-clock stamping, capture thread), `ScreenRegionGrabber` (GDI), `DxgiRegionGrabber` (Desktop Duplication, hand-written COM in `DxgiCom`), `DesktopRegionGrabber` (picks DXGI, falls back to GDI), `LiveVideoRecordController` (start/stop state machine), `LiveVideoSettings`. The audio comes from the engine master tap (SetMasterTap, MasterAudio). Tests: `TestMasterTapProtocol`, `TestLiveVideoRecord`, `TestDxgiGrabber`.

## Pathway
- Write files only through `FilePathPolicy.WriteAtomically`; bound reads with `InputLimits`.
- A new setting touches `AppSettings`, `SettingsValidator`, `SettingsCatalog` and a row in `PreferencesWindow`.
- A new command needs a `HotkeyCatalog` entry, the presets and `TOOLS_AND_HOTKEYS.md`.

## Must not depend on
WPF assemblies, other than the listed exceptions in the architecture tests (`Source/src/TabForge.Avalonia/SelfTests/Architecture/`). Only `Program`, diagnostics and the listed exceptions may reference the audio engine.

## Tests
`TestEverySettingIsWired`, `TestSettingsStoreSharedAcrossWindows`, `TestSecurityInputBoundaries`, `TestMediaPathPolicy`, `TestEditCommands`, `TestGuitarProFiles`, `TestMusicXmlExport`, `TestMidiExport`, `TestTforgeCompression`, `TestNewBindableCommands`.

- `VideoViewSpec`, `VideoSettings`: the export view choice and the export window's remembered settings; drawn by `VideoFrameSource` in the Views folder.
