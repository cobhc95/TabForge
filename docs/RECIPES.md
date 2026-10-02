# Recipes

Exact file lists for the usual changes. Paths are from the repository root. Each recipe ends with the self-tests to run; how to run them is in `docs/DEBUGGING.md` (`--only`, `--areas`). `START_HERE.md` explains the pathways named here, and `docs/FEATURE_MAP.md` lists the tests by area. Names of methods are written with parentheses; search for the name to find the line.

## Add a note technique

It must show in the tab and be heard in playback.

1. Name: `src/TabForge/Models/SongProject.cs` (`GpEffects`, the list of valid names) and `src/TabForge/Models/TechniqueNames.cs`.
2. Engraving: `src/TabForge/Views/Score/ScoreMarkText.cs` for the mark text; `src/TabForge/Views/StaffNotationRenderer.cs` or `src/TabForge/Views/Score/` for a drawn symbol.
3. Entering it: `src/TabForge/Views/TabEditorControl.Editing.cs` and the palette in `src/TabForge/MainWindow.Palette.cs`. The edit itself runs through `DocumentEdits.Run`.
4. Sound: `src/TabForge/Playback/ScoreToMidiCompiler.Techniques.cs` (velocity, length or articulation) and `src/TabForge/Playback/Models/TechniqueTag.cs`.
5. Files: `src/TabForge/Services/GuitarProBeatReader.cs` (import), `src/TabForge/Services/GuitarProExporter.cs` (export), `src/TabForge/Services/BarGrid.cs` (techniques that survive bar edits).
6. A key for it: follow "Add a command" below.
7. Tests: `TestTechniqueEngraving`, `TestTimelineTechniques`, `TestEditCommands`, `TestGpRoundTripFixes`; add a check that the technique survives a `.gp` round trip.

## Add a setting

Example: a preference "zoom for new tabs".

1. Field and default: `src/TabForge/Services/AppSettings.cs` (the settings class of the right section).
2. Bound: `src/TabForge/Services/SettingsValidator.cs`. Old files: `src/TabForge/Services/SettingsMigration.cs`.
3. Preferences row and search entry: `src/TabForge/Services/SettingsCatalog.cs` and the page builder in `src/TabForge/Views/Preferences/` (see `src/TabForge/Views/Preferences/CommonPages.cs`, `src/TabForge/Views/Preferences/SettingEditors.cs`).
4. Use: read it from the shared `AppSettingsStore`, never from a copy. A new document starts at `DocumentSession.ZoomFactor`; the open path is in `src/TabForge/MainWindow.Documents.cs`.
5. Test: change the value, check it is stored, survives a reload and changes nothing else.
6. Tests: `TestEverySettingIsWired`, `TestPreferencesCatalog`, `TestSettingsStoreSharedAcrossWindows`; run with `--areas settings`.

## Add a track row menu item

1. Item and order: `src/TabForge/Views/TrackRowMenus.cs` (`TrackRowMenus.Build`; an id constant per item).
2. Request and focus: `src/TabForge/Views/ArrangementPanel.TrackMenu.cs`; handler: `src/TabForge/MainWindow.TrackMenu.cs`.
3. Whole-track actions: `src/TabForge/Controllers/TrackClipboardFlow.cs`. A key: follow "Add a command" below.
4. Tests: `TestTrackRowMenu`; run `--areas ui` on a full-suite build.

## Add a command and its hotkey

1. Definition: `src/TabForge/Services/HotkeyCatalog.cs` (`HotkeyCatalog`: id, category, name, default key, description).
2. Presets: `HotkeyPresets` in the same file. A preset with no override inherits the default.
3. Action: add the id to the switch in `src/TabForge/MainWindow.Settings.cs` (`RunHotkey(...)`), calling a method on the class that owns the behaviour.
4. Menu item, if any: `src/TabForge/MainWindow.xaml` with `local:MenuHotkey.Id`; the gesture text then follows the binding.
5. Docs: `TOOLS_AND_HOTKEYS.md`, and a line under "Unreleased" in `CHANGELOG.md`.
6. Tests: `TestMenuGestureTextFollowsBindings`, `TestEditCommands`; run `--areas ui`.

## Add an export option

Example: "copy bars as text".

1. Logic, no WPF: a service in `src/TabForge/Services/` (model: `src/TabForge/Services/AsciiExportService.cs`). Bound its input with `InputLimits`; write files with `FilePathPolicy.WriteAtomically`.
2. Menu and handler: `src/TabForge/MainWindow.xaml` and `src/TabForge/MainWindow.File.cs`. Selected bars come from the editor selection; a clipboard result goes through `src/TabForge/Services/ClipboardService.cs`.
3. A command: follow "Add a command" above.
4. Tests: a service test beside `TestAsciiExport`, and `TestMidiExport` for the pattern of a format test; run `--areas guitarpro`.

## Add an engine command

The engine is a separate process; the application reaches it only through the client.

1. Wire: `src/TabForge.Audio.Contracts/EngineProtocol.cs` (`EngineCommand`: a new value, never reuse a reserved one; document the fields).
2. Send: `src/TabForge/Audio/AudioEngineClient.cs`. State the engine must keep across a restart is also resent by `src/TabForge/Controllers/EngineSyncController.cs`.
3. Receive: `src/TabForge.AudioEngine/EngineHost.cs` (read the frame with bounds, then post to the engine thread), then `src/TabForge.AudioEngine/EngineSession.cs`.
4. Act: `src/TabForge.AudioEngine/Mixing/MixEngine.cs` or `src/TabForge.AudioEngine/Mixing/TrackChain.cs`. The audio callback allocates nothing and takes no lock; hand over state with an immutable object or an atomic.
5. Model and UI: a field on `TrackModel` or `MixerSettings` in `src/TabForge/Models/`, saved as in "Add a persisted song field".
6. Tests: `TestMixer`, `TestMuteSoloTruthTable`, `TestCommandFrameRobustness`, `TestEngineMultiTabPlayback`; run `--areas engine`.

## Add a persisted song field

Example: a song key.

1. Field and default: `src/TabForge/Models/SongProject.cs` (`SongProject`, or `TrackModel` for a per-track value). A copy of the song (clone) must carry it.
2. File: `src/TabForge/Services/ProjectService.cs` writes every property of the model, so a new field is saved. A new default for an existing field needs a format version bump and a migration there.
3. Bound on load: `src/TabForge/Services/ProjectValidator.cs`.
4. Edits: change it through `DocumentEdits.Run` so undo, dirty state and the timeline follow.
5. `.gp` import and export, if the field exists in that format: `src/TabForge/Services/GuitarProScoreInfoReader.cs` and `src/TabForge/Services/GuitarProExporter.cs`.
6. Tests: `TestPersistenceSchema` (frozen defaults), `TestProjectRoundtrip`, `TestModelRoundTrip`, `TestRoundTripSemanticsSuite`; run `--areas persistence`.

## Add a dialog

1. The window: a class in `src/TabForge/Views/` (model: `src/TabForge/Views/PasteOptionsDialog.cs`). It returns its result and holds no song state.
2. Open it through `DialogHost.ShowModal(...)`, so the screenshot tour and tests can intercept it. Errors go through `DialogHost.ShowError(...)`.
3. A multi-step operation (prompts, several outcomes) is a flow class with an interaction interface, as `DocumentSaveFlow` and `ISaveInteractions`; the window implements the interface with dialogs.
4. Size: a test window must fit 1024 x 728. Escape cancels. A new setting-like option also gets a Preferences row.
5. Tests: `TestDialogEscape`, `TestPasteOptionsDialog`; a Flow is driven through a fake interaction object without a window.
