# Recipes

Exact file lists for the usual changes. Paths are from the repository root. Each recipe ends with the self-tests to run; how to run them is in `docs/DEBUGGING.md` (`--only`, `--areas`). `tools/run-test.ps1` builds the full-suite copy and runs one test by name. `START_HERE.md` explains the pathways named here, and `docs/feature-map/` lists the tests by area. Names of methods are written with parentheses; search for the name to find the line.

## Add a note technique

It must show in the tab and be heard in playback.

1. Name: `src/TabForge/Models/SongProject.cs` (`GpEffects`, the list of valid names), `src/TabForge/Models/TechniqueNames.cs` and one row in `src/TabForge/Models/TechniqueInfo.cs` (mark text, playback kinds, Guitar Pro value id, MusicXML spelling, the MusicXmlArticulation field for a scoop/plop/doit/falloff style mark, or a NotSupported entry with the reason; `TestTechniqueCoverage` fails without a row). Add the mark to the golden list in `TestTechniqueInfoTable`; add the new name to the input lists of `TestTechniqueGpExportGolden` and `TestTechniqueMusicXmlGolden` (and their golden lists, reviewed once) when the file writers should treat it.
2. Engraving: the row's mark text (read by `ScoreMarkText.ShortTechnique`); `src/TabForge/Views/Score/StaffNotationRenderer.cs` or `src/TabForge/Views/Score/` for a drawn symbol.
3. Entering it: `src/TabForge/Views/TabEditorControl.Editing.cs` and the palette in `src/TabForge/MainWindow.Palette.cs`. The edit itself runs through `DocumentEdits.Run`.
4. Sound: the numbers go in the technique's row in `src/TabForge/Models/TechniqueInfo.cs` (the velocity factor, the length cap in slots, the controller value: read with `TechniqueInfo.VelocityFactorOf` and the like); the step itself (where it applies, its cast or rounding, its order against ghost and accent) is a line in `src/TabForge/Playback/ScoreToMidiCompiler.Techniques.cs`, plus `src/TabForge/Playback/Models/TechniqueTag.cs`. Add the technique to the case list of `TestTechniquePlaybackGolden` and capture its hashes once (empty golden list prints them).
5. Files: `src/TabForge/Services/GuitarProBeatReader.cs` (import: a row with a Guitar Pro value id needs no code there for an enum-valued effect), `src/TabForge/Services/GuitarProExporter.cs` (export: a harmonic, fade, wah or arpeggio-style value takes its row's GpId; add the name to the precedence list passed to TechniqueInfo.GpValue; any other effect still needs its own line), `src/TabForge/Services/MusicXmlExportService.cs` (only a spelling other than an articulation), `src/TabForge/Services/BarGrid.cs` (techniques that survive bar edits).
6. A key for it: follow "Add a command" below.
7. Tests: `TestTechniqueEngraving`, `TestTimelineTechniques`, `TestEditCommands`, `TestGpRoundTripFixes`; add a check that the technique survives a `.gp` round trip.

## Add a setting

Example: a preference "zoom for new tabs".

1. Field and default: the section's settings class in `src/TabForge/Services/` (e.g. `EditingSettings.cs`, `TimelineSettings.cs`; `AppSettings.cs` holds the root). A feature may keep its class in its own folder, as `src/TabForge/Services/Video/LiveVideoSettings.cs` does, with one property on the root.
2. Bound: `src/TabForge/Services/SettingsValidator.cs`. Old files: `src/TabForge/Services/SettingsMigration.cs`.
3. Preferences row and search entry: the page's `src/TabForge/Services/SettingsCatalog.<Page>.cs` (rows) and the page builder in `src/TabForge/Views/Preferences/` (see `src/TabForge/Views/Preferences/CommonPages.cs`, `src/TabForge/Views/Preferences/SettingEditors.cs`).
4. Use: read it from the shared `AppSettingsStore`, never from a copy. A value the open window applies gets one line in the applier for its area in `src/TabForge/MainWindow.SettingsApply.cs` (one small method per area: follow, score appearance, timeline, transport, instrument and editing, view toggles); `SyncFromSettings()` in `src/TabForge/MainWindow.Settings.cs` only lists the appliers in order, so a new setting never touches it. The validator already bounds the value, so do not clamp it again there. A new document starts at `DocumentSession.ZoomFactor`; the open path is in `src/TabForge/MainWindow.Documents.cs`.
5. Test: change the value, check it is stored, survives a reload and changes nothing else.
6. Tests: `TestEverySettingIsWired`, `TestPreferencesCatalog`, `TestSettingsStoreSharedAcrossWindows`; run with `--areas settings`.

## Add a track row menu item

1. Item and order: `src/TabForge/Views/Timeline/TrackRowMenus.cs` (`TrackRowMenus.Build`; an id constant per item).
2. Request and focus: `src/TabForge/Views/ArrangementPanel.TrackMenu.cs`; handler: `src/TabForge/MainWindow.TrackMenu.cs`.
3. Whole-track actions: `src/TabForge/Controllers/TrackClipboardFlow.cs`. A key: follow "Add a command" below.
4. Tests: `TestTrackRowMenu`; run `--areas ui` on a full-suite build.

## Add a command and its hotkey

1. Definition: `src/TabForge/Services/HotkeyCatalog.Actions.cs` (one `HotkeyAction` row per id: id, category, name, default key, description, in four slices). Lookup and gestures: `src/TabForge/Services/HotkeyCatalog.cs`.
2. Presets: `HotkeyPresets` in `src/TabForge/Services/HotkeyCatalog.cs`. A preset with no override inherits the default.
3. Action: a plain command that only calls one handler is one line in `BuildCommands()` in `src/TabForge/MainWindow.Commands.cs` (`Click("Id", Handler_Click);` or `Run("Id", Method);`); one with conditions or several steps is a `case` in the `RunHotkey(...)` switch in `src/TabForge/MainWindow.Settings.cs` (or in its `RunPaneHotkey(...)` router for the scale-highlight, string-spacing and Band view commands). Never both (`TestCommandRegistryRouting`).
4. Menu item, if any: one `Item("_Header", Handler_Click, "Id")` line in `src/TabForge/MainWindow.Menus.cs` (see "Add a menu command"); the gesture text then follows the binding.
5. Docs: `TOOLS_AND_HOTKEYS.md`, and a line under "Unreleased" in `CHANGELOG.md`.
6. Tests: `TestMenuGestureTextFollowsBindings`, `TestEditCommands`; run `--areas ui`.

## Add a menu command

Example: File > Export video (MP4). The catalog row and the preset are in "Add a command and its hotkey" above; this is the menu and toolbar side.

1. Menu item: one line in the table `MainWindow.MainMenuGroups()` in `src/TabForge/MainWindow.Menus.cs`, inside the group of its menu: `Item("Export _MIDI…", ExportMidi_Click, "File.ExportMidi"),` (a feature module adds its rows with `FeatureMenuRow`) (header with access key, handler, command id; an optional fourth argument is a tag; a row named Sep is a separator). No XAML: `src/TabForge/MainWindow.xaml` keeps only the menu skeleton and the complex items (checkable, named, bound or dynamic: Metronome, the View toggles, Panels). The table builder is `src/TabForge/Views/MainMenu/MenuTable.cs`; a group goes after the XAML item whose name it gives (null: at the start of the menu).
2. Handler: a `_Click` method in the window partial that owns the feature. A feature module (below) takes a host method instead of a `_Click`.
3. One route for key and menu: one line in `BuildCommands()` in `src/TabForge/MainWindow.Commands.cs`, e.g. `Click("File.Render", Render_Click);` (a feature module registers its own commands) (the catalog row is step 1 of "Add a command and its hotkey"). Files to edit for a plain command: `HotkeyCatalog.Actions.cs` (the row), `MainWindow.Commands.cs` (the line), the handler, and one line in `MainWindow.Menus.cs`.
   A checkable, named or dynamic item still goes in `MainWindow.xaml` (with `local:MenuHotkey.Id`); `TestMainMenuTable` fails on a plain item left in the XAML.
4. Toolbar button, if wanted: a Button in `src/TabForge/MainWindow.xaml` with `local:TooltipShortcuts.Command` set to the id (the Record video button sits in the title bar, left of Settings).
5. Docs: a row in `TOOLS_AND_HOTKEYS.md`, and a line in `CHANGELOG.md`.
6. Tests: `TestMainMenuTable` (every row id is catalogued, no plain item in the XAML), `TestMainMenuTreeGolden` (re-record as in "Change a menu"), `TestMenuGestureTextFollowsBindings`; run `--areas ui` on a full-suite build.

## Change a menu (the menu snapshot)

`TestMainMenuTreeGolden` (`tests/full-suite/Menus/SelfTestMenuGolden.cs`) builds the real main window and writes every main-menu item as one line (header with access key, x:Name, `MenuHotkey.Id`, tag, gesture text, checkable, icon present, enabled binding, nesting, separators) and compares it with `tests/full-suite/Menus/main-menu-tree.golden.txt`. It also dumps the menus built from `MenuSpec` data (track row, bar, selection, section, clip, fretboard / keyboard / drums, score, band, each in several states) against `code-menus.golden.txt`. Checked and enabled state is not recorded: it follows settings and the document. Not covered: the note right-click menu (`MainWindow.ContextMenus.cs`, built from the clicked note), dock panel tab menus, mixer / FX / preferences menus.

- An intended menu change: run the test on a full-suite build with `TABFORGE_RECORD_MENU_GOLDEN=1` (from the repository root, with `--profile <scratch folder>`), review the diff of the two `.golden.txt` files, and commit them with the change.
- The plain items of the main menu come from the table in `MainWindow.Menus.cs`; a change to it re-records the golden like any other menu change. A menu generator must leave both files byte-identical; any diff is a regression unless the change is intended and reviewed.
- Every checkable item needs a settings row or a reason (the item's setting key, or its no-setting reason). `TestContextMenuLean` checks this for the fretboard, score, timeline and Band view menus. Items inside a submenu are found by walking the whole tree (the helper at the top of `tests/full-suite/Views/SelfTestBandSettings.cs`); the Band view menu's Row sizes submenu is the example.

## Add an export option

Example: "export as plain text (.txt)". Export formats are feature-module rows (`src/TabForge/Services/Export/ExportFeatureModule.cs`), so a new format touches the module, the window and the exporter only.

1. Logic, no WPF: a service in `src/TabForge/Services/` (model: `src/TabForge/Services/AsciiExportService.cs`). Bound its input with `InputLimits`; write files with `FilePathPolicy.WriteAtomically`. The save path goes through `src/TabForge/Controllers/ScoreExportController.cs`: add a method there that calls the service.
2. Host method: add one method to `IExportCommandHost` in `ExportFeatureModule.cs`, and implement it in `src/TabForge/MainWindow.File.cs` next to the other export methods (a one-line `public void X() => Exports.X();`).
3. Module: in `ExportFeatureModule` add the three rows. A hotkey row (`FeatureHotkey`: the new `HotkeyAction`, and the catalogue id it follows). A File menu row (`FeatureMenuRow`: `"_File"`, the id of the menu row it follows, the header, the command id). A `FeatureCommand` yielded from the module's Commands method. A row can follow another module row, so keep a chain in order. A row that must sit before a separator stays in `src/TabForge/MainWindow.Menus.cs`, because separators are not module rows.
4. Tests: a service test beside `TestAsciiExport`. Run `TestFeatureModuleContributions`, `TestMainMenuTreeGolden` (byte-identical unless the menu change is intended and reviewed), `TestEveryHotkeyIdHasHandler` and `TestMainMenuTable`; the export group is `--areas guitarpro`.

## Add an engine command

The engine is a separate process; the application reaches it only through the client.

1. Wire: `src/TabForge.Audio.Contracts/EngineProtocol.cs` (`EngineCommand`: a new value, never reuse a reserved one; document the fields). Then one message record with a Write and a Read in `EngineMessages.cs` (or `EngineMessageLists.cs`), and one case with golden hex in `tests/full-suite/Engine/SelfTestEngineMessages.cs`; both sides call that pair, never raw write / read sequences.
2. Send: `src/TabForge/Audio/AudioEngineClient.cs`. State the engine must keep across a restart is also resent by `src/TabForge/Controllers/EngineSyncController.cs`.
3. Receive: `src/TabForge.AudioEngine/EngineHost.cs` (read the frame with bounds, then post to the engine thread), then `src/TabForge.AudioEngine/EngineSession.cs`.
4. Act: `src/TabForge.AudioEngine/Mixing/MixEngine.cs` or `src/TabForge.AudioEngine/Mixing/TrackChain.cs`. The audio callback allocates nothing and takes no lock; hand over state with an immutable object or an atomic.
5. Model and UI: a field on `TrackModel` or `MixerSettings` in `src/TabForge/Models/`, saved as in "Add a persisted song field".
6. Tests: `TestMixer`, `TestMuteSoloTruthTable`, `TestCommandFrameRobustness`, `TestEngineMultiTabPlayback`; run `--areas engine`.

## Add a mixer or FX feature

Example: a per-track control in the mixer. The engine half is "Add an engine command" above; this is the window side.

1. Value: a field on `MixerSettings` in `src/TabForge/Models/MixerModel.cs`, saved with the song as in "Add a persisted song field".
2. Row: in `src/TabForge/Views/MixerWindow.cs` (sections in `src/TabForge/Views/MixerWindow.Groups.cs`). An FX slot: `src/TabForge/Views/FxChainWindow.cs` (sections in `src/TabForge/Views/FxChainWindow.Sections.cs`).
3. Host: `src/TabForge/Views/MixerHost.cs` implements `IMixerHost`, `IFxChainHost` and `IMixerWindowsHost` (it reaches the window only through `IMixerSurface`; `MainWindow.Mixer.cs` implements that and holds a few forwards). Put the new host logic in `MixerHost`, not in `MainWindow`.
4. Send: a value of `EngineCommand` in `src/TabForge.Audio.Contracts/EngineProtocol.cs`, written and read by the shared pair, sent through `src/TabForge/Audio/AudioEngineClient.cs`.
5. Resend: write the engine state in `SyncDocument()` in `src/TabForge/Controllers/EngineSyncController.cs`. `Sync()` runs again after an engine crash, so the value comes back with the engine.
6. Engine: `src/TabForge.AudioEngine/EngineHost.cs` reads the frame, `src/TabForge.AudioEngine/EngineSession.cs` handles it, then `src/TabForge.AudioEngine/Mixing/TrackChain.cs` (one track) or `src/TabForge.AudioEngine/Mixing/MixEngine.cs` (master).
7. Tests: `TestMixer`, `TestMuteSoloTruthTable`, `TestCommandFrameRobustness`; run `--areas engine`.

## Fix a playback bug

Start with the layer, not the code. Reproduce first; the bug fix gets a test that would have caught it.

1. Measure: `--audit <song>` (exit 1 when notes are late or early), `--midi-timing <song> <out.mid>` for bar timing. `PlaybackDiagnostics.Audit()` in `src/TabForge/Playback/PlaybackDiagnostics.cs` gives the same report in code.
2. Wrong or missing notes: `src/TabForge/Playback/ScoreToMidiCompiler.cs` (with its partials `src/TabForge/Playback/ScoreToMidiCompiler.Techniques.cs` and `src/TabForge/Playback/ScoreToMidiCompiler.Clips.cs`) and `src/TabForge/Playback/PlaybackOrder.cs` (the bar order).
3. Late, early or stuttering sound: `src/TabForge/Playback/PlaybackEngine.Scheduler.cs` and `src/TabForge/Audio/SongClock.cs`. Run with `TABFORGE_TRACE=playback` to see the `restore@<ms>ms` line after a seek or loop.
4. Mix or program lost after a jump: `src/TabForge/Playback/PlaybackEngine.Resume.cs` and `src/TabForge/Controllers/EngineSyncController.cs`.
5. Engine side: `%TEMP%\tabforge-audioengine.log` is written by `src/TabForge.AudioEngine/EngineThreads.cs`; then `src/TabForge.AudioEngine/Mixing/SongTransport.cs`.
6. Test: `--areas playback`, plus a narrow test for the bug in the same area.

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

## Add a dock pane

Example: a panel of the workspace. The Band pane is the model. Folder guide: `src/TabForge/Docking/README.md`.

1. Content: a WPF element in its own folder under `src/TabForge/Views/` (the Band pane: `src/TabForge/Views/Band/BandView.cs`). It holds no song state of its own.
2. Register: the row of step 3 registers the pane (`DockPaneSetup.RegisterAll`); add the pane's element to the `id switch` in `InitializeDockWorkspace()` in `src/TabForge/MainWindow.View.cs`. The row's starts-closed flag makes a pane start hidden. `RegisterPanel()` itself is in `src/TabForge/Views/DockWorkspace.cs`.
3. Ids, titles, menu: add one row to `DockPaneTable.Rows` in `src/TabForge/Docking/DockPaneTable.cs`. The Panels menu, the built-in layouts' closed lists and the settings validator (a saved layout with an unknown id is refused) all read that row; never rename an id that shipped.
4. View > Panels: the same row gives the menu item; nothing else to add. `TestDockPaneTable` pins the ids, titles and order, so add the row to its golden lines.
5. Layout rules: placement, drag and the tree are in `src/TabForge/Docking/DockLayoutTree.cs`.
6. Tests: `TestDockPaneTable`, `TestDockRatioNotRewrittenByAutoFit`, `TestBrowserTabShell`, `TestTabUi`; run `--areas ui`, then `--areas architecture,hygiene`.

## Add a note-effect editor

A dialog for one effect (bend, tremolo bar, trill, grace note and harmonic exist; the last three keep numbers in `EffectPresetEntry.Values` and use `ValuesEffectDialog` and `EffectEditorFlow.OpenOrnament`). Folder guide: `src/TabForge/Views/EffectEditors/README.md`.

1. Kind and presets: add the member to `EffectEditorKind` (`src/TabForge/Views/EffectEditors/EffectCurve.cs`) and its built-in list to `EffectPresetStore.BuiltIn` (`src/TabForge/Views/EffectEditors/EffectPresetStore.cs`); user presets are stored by kind in `AppSettings.EffectPresets` (numbers go in `EffectPresetEntry.Values`).
2. Dialog: a class like `CurveEffectDialog` that builds the controls and hands them to `ThemedEditorDialog` (OK / Clean / Cancel, Enter, Esc, tab order). A curve effect reuses `EffectCurveEditor`.
3. Model write: `EffectEdits` (an apply and a clean method per effect; pure; they return whether anything changed) and one case in `EffectEditorFlow.Open` that finds the notes (`ScoreEditCommands.EffectNotes`) and runs the write through `ScoreEditCommands.EditEffect` (one undo step).
4. Entry points: the `effect:` case in `src/TabForge/MainWindow.Palette.cs`, a menu row in `src/TabForge/MainWindow.Menus.cs` under `_Effects` (handler `EffectEditor_Click` in `src/TabForge/MainWindow.EffectEditors.cs`), a command id in `src/TabForge/Services/HotkeyCatalog.cs` with no default key, its case in the hotkey switch of `src/TabForge/MainWindow.Settings.cs`, and the rows in `TOOLS_AND_HOTKEYS.md`.
5. Files and sound: the model fields must already round-trip (`.tforge` serialises the model; `.gp` import and export are in `src/TabForge/Services/GuitarProBeatReader.cs` and `GuitarProExporter.cs`); playback is `src/TabForge/Playback/ScoreToMidiCompiler.Techniques.cs`.
6. Tests: `TestEffectCurveMath`, `TestEffectEditors`, `TestEffectEditorFiles` (register new ones per `docs/DEBUGGING.md`; a saving or import test also goes into the release list in `src/TabForge/SelfTests/SelfTestRelease.cs`).

## Video as the worked example: the hook points it edits

The 0.7 video feature is the model for a self-contained feature: its code lives in two folders and the rest of the app sees only the hooks below. A new feature with the same shape edits the same kinds of file. Feature page: `docs/feature-map/video.md`.

| Hook | Where in Video | Edit for a new feature |
| --- | --- | --- |
| Saved values | `src/TabForge/Services/AppSettings.cs` (properties Video and LiveVideo); the classes are in `src/TabForge/Services/Video/` | One property on the root, the class in the feature's folder |
| Settings bounds, Preferences rows and group, hotkey rows, commands, File menu entry | One module: `src/TabForge/Services/Video/VideoFeatureModule.cs` (rows in `VideoSettingsRows.cs`), merged by `src/TabForge/Services/Features/FeatureRegistry.cs`; see "Add a feature module" | A module file and one line in `FeatureRegistry.Modules` |
| Host methods | `src/TabForge/MainWindow.VideoRecord.cs` (`IVideoCommandHost`) | A host interface the window implements |
| Title bar, toolbar and status | `src/TabForge/MainWindow.xaml` (RecordVideoButton and the REC text); `src/TabForge/MainWindow.VideoRecord.cs` | A button, a status element, and a thin partial |
| Second entry | `src/TabForge/Views/RenderWindow.cs` (the video button of the Render window) | Optional |
| Probes | `src/TabForge/Diagnostics/WindowProbes.PerfRecording.cs` (the --perf-follow option); the `videoexport` case in `src/TabForge/Diagnostics/WindowProbes.CaptureShots.cs` | A probe for the frame rate, a capture case for the window (not yet module-driven) |
| Feature code | `src/TabForge/Services/Video/` and `src/TabForge/Views/Video/` | Own folders with a README each |
| Docs | `TOOLS_AND_HOTKEYS.md` (a row per command), `CHANGELOG.md`, `docs/feature-map/video.md`, `docs/FEATURE_MAP.md` (an index row) | One line each |
| Tests | `tests/full-suite/Recording/SelfTestVideoExport.cs` and `tests/full-suite/Recording/SelfTestLiveVideo.cs`, registered in `tests/full-suite/SelfTestFullSuite.cs` | Full-suite tests, so they run with `--areas recording` on that build |

## Add a feature module

A module is one class in the feature's own folder that implements `IFeatureModule` (`src/TabForge/Services/Features/IFeatureModule.cs`). The central tables pull its rows in when they assemble themselves, with the ids and order the rows would have if written there. Pilot: `src/TabForge/Services/Video/VideoFeatureModule.cs`. Further examples: `src/TabForge/Services/Band/BandFeatureModule.cs` (Band) and `src/TabForge/Services/Export/ExportFeatureModule.cs` (export formats).

1. Write `XFeatureModule : IFeatureModule` in the feature's folder. Override only what the feature adds: Hotkeys (a `HotkeyAction` and the id of the row it follows; rows after one shared anchor come out in reverse, so chain each row after the one before it), MenuRows (menu, the command id it follows, header, id), Layout (a Preferences group and the group it follows), SettingRows (descriptors; keep the rows method next to the feature as a `partial class SettingsCatalog` file), Commands and Normalize. The Layout only inserts: to move a group that already sits in the central table, delete its central row in the same change (Band did).
2. For commands, declare `IXCommandHost : IFeatureHost` with one method per command and let `MainWindow` implement it in the feature's partial; the module yields `FeatureCommand(id, host.Method)`.
3. Add one line to `FeatureRegistry.Modules` in `src/TabForge/Services/Features/FeatureRegistry.cs`.
4. Run `TestFeatureModuleContributions` (ids unique, each in exactly one central table) plus `TestMainMenuTreeGolden`, `TestPreferencesCatalog`, `TestEveryHotkeyIdHasHandler` and `TestSettingsFileSplitSnapshots`. The saved property on `AppSettings` and the saved ids stay as they are.

A second example with a dock pane: `src/TabForge/KeyboardMode/KeyboardModeFeatureModule.cs` (menu row, hotkey, settings group) plus a `DockPaneTable` row (starts closed), the id switch in `InitializeDockWorkspace()` and a thin `src/TabForge/MainWindow.KeyboardMode.cs`; the pane's code stays in `src/TabForge/KeyboardMode/`. Keyboard mode is a layout swap (`KeyboardModeController`, `KeyboardModeLayoutSwap`): like Band it takes a copy of the dock arrangement on entering and puts it back on leaving, and `MainWindow.SaveSettings` writes the copy while it is on, so a mode with a temporary arrangement follows that pattern.

Not module-driven yet: the `AppSettings` property, probes, `TOOLS_AND_HOTKEYS.md` and `CHANGELOG.md`.

## Keep something that moves crisp

A line, edge or cached layer that moves by a fractional amount is drawn as soft, half-covered pixel columns (or resampled, when it is a texture).

1. Round the position and the width to whole device pixels with `src/TabForge/Views/Rendering/PixelSnap.cs` (its Snap and Dpi helpers; a thin line: `PlayheadOverlay`). A moved texture cache needs SnapsToDevicePixels on (see `BandReorder`).
2. A picture drawn for a file (the video frame) keeps its pixels: snap only in the live view (`PlayheadOverlay` has a switch).
3. Measure with `tests/full-suite/Views/MotionSharpnessProbe.cs` (draw at 100% to 175% scale, compare with a whole-pixel move) in a `TestMotionSharpness*` test; run the video goldens too (`TestVideoFrameGolden`, `TestVideoFrameSourceGolden`, from the repository folder).
