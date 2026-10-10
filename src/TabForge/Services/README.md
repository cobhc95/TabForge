# Services

Musical logic, file formats, settings, limits and path policy. No WPF.

## How to change me
1. Entry files: `SettingsCatalog.cs` (page layout, Build) with `SettingsCatalog.General.cs`, `SettingsCatalog.Appearance.cs`, `SettingsCatalog.Score.cs`, `SettingsCatalog.Playback.cs`, `SettingsCatalog.Audio.cs`, `SettingsCatalog.Editing.cs`, `SettingsCatalog.Timeline.cs`, `SettingsCatalog.Fretboard.cs` and `SettingsCatalog.Tabs.cs` holding the rows, `AppSettings.cs` plus one file per settings class (`FollowSettings.cs`, `GeneralSettings.cs`, `AppearanceSettings.cs`, `PluginSettings.cs`, `AudioSettings.cs`, `EditingSettings.cs`, `TimelineSettings.cs`), `HotkeyCatalog.cs` (with `HotkeyCatalog.Actions.cs`, the command table), `ProjectService.cs`.
2. Owner class: `AppSettingsStore` (settings), `FilePathPolicy` (writes), `EditCommands` (edits).
3. Tests to run: `TestSettingsStoreSharedAcrossWindows`, `TestHotkeySettingsMigration` (full-suite build); `--areas settings,persistence`, plus `--areas architecture,hygiene` (see the test box in `docs/TESTING.md`).
4. Docs to update: `TOOLS_AND_HOTKEYS.md` and hotkey presets for commands, `CHANGELOG.md`, `docs/RECIPES.md`.

## Key types
- `ClipLoop`, `ClipTrim`, `TrailingBars`: a clip longer than its media loops (the pieces it plays as); the edge-trim arithmetic; the empty bars removed at the end when clips shrink. `SongExtent` grows the song to hold the clips.
- `AppSettingsStore`: the one shared settings store; `SettingsValidator` bounds values, `SettingsMigration` upgrades old files, `SettingsFileService` reads and writes.
- `FilePathPolicy`: atomic writes, leftover sweeps, reserved-name checks. `InputLimits` bounds every read.
- `ProjectService`: the .tforge format; `GuitarProImporter` and `GuitarProExporter`: .gp and .gp5.
- `EditCommands` (partial files): the note, bar and paste edits the editor calls.
- `CommandRegistry`: the `id -> action` table behind the window's plain commands (filled in `MainWindow.Commands.cs`).
- `HotkeyCatalog`: every bindable command (`HotkeySettings` holds the user's Hotkey 1 and Hotkey 2); `MediaContext` and `MediaPathPolicy`: a song's audio files.
- `MusicXmlExportService`, `MidiExportService`, `AsciiExportService`: exports.
- `VideoEncoder`: writes an MP4 (H.264 + AAC) through Media Foundation (hand-written COM interop). Live mode drops frames when its queue is full; offline mode waits. Tests: `TestVideoEncoderMp4`, `TestVideoEncoder4k60`.
- Live recording (Services/Video folder): `LiveVideoSession` (audio-clock stamping, capture thread), `ScreenRegionGrabber` (GDI), `DxgiRegionGrabber` (Desktop Duplication, hand-written COM in `DxgiCom`), `DesktopRegionGrabber` (picks DXGI, falls back to GDI), `LiveVideoRecordController` (start/stop state machine), `LiveVideoSettings`. The audio comes from the engine master tap (SetMasterTap, MasterAudio). Tests: `TestMasterTapProtocol`, `TestLiveVideoRecord`, `TestDxgiGrabber`.

## Pathway
- Write files only through `FilePathPolicy.WriteAtomically`; bound reads with `InputLimits`.
- A new setting touches `AppSettings`, `SettingsValidator`, `SettingsCatalog` and a row in `PreferencesWindow` (`docs/RECIPES.md`).
- A new command needs a `HotkeyCatalog` entry, the presets and `TOOLS_AND_HOTKEYS.md`.

## Must not depend on
WPF assemblies, other than the listed exceptions in `src/TabForge/SelfTests/Architecture/SelfTestArchitecture.cs`. Only `Program`, diagnostics and the listed exceptions may reference the audio engine.

## Tests
`TestEverySettingIsWired`, `TestSettingsStoreSharedAcrossWindows`, `TestSecurityInputBoundaries`, `TestMediaPathPolicy`, `TestEditCommands`, `TestGuitarProFiles`, `TestMusicXmlExport`, `TestMidiExport`, `TestTforgeCompression`, `TestNewBindableCommands`.

- `VideoViewSpec`, `VideoSettings`: the export view choice and the export window's remembered settings; drawn by `VideoFrameSource` in the Views folder.