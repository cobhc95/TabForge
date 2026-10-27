# Start here

Read this page first. It tells you where things live and which existing pathway to use, so you can open only the files a change needs.

## What TabForge is

A keyboard-driven tablature and notation editor for Windows (WPF, .NET 8) with its own audio engine. It opens and saves .gp, .gp5 and its own .tforge format, plays a song through a built-in synth or hosted plug-ins, records audio, and renders to files. It is pre-release software. Features and limits: `README.md`.

## Layer map

Three projects. A lower layer never references a higher one; self-tests enforce this (`TestArchitectureLayering`).

| Layer | Folder | Holds |
| --- | --- | --- |
| Wire contracts | `src/TabForge.Audio.Contracts/` | Protocol types shared by app and engine, no dependencies (`EngineCommand`, `EngineEvent`) |
| Audio engine | `src/TabForge.AudioEngine/` | A separate process: mixer, synth, plug-in hosting, recorder, offline renderer. Real-time rules: `docs/architecture/ARCHITECTURE.md`, section 4 |
| Application | `src/TabForge.Avalonia/` | Everything the user sees, plus the data and logic behind it (below) |

Inside the application project:

| Folder | Role | Rule |
| --- | --- | --- |
| `src/TabForge.Core/Models/` | Song data (`SongProject`, `TrackModel`, `MeasureModel`) | No WPF |
| `src/TabForge.Core/Services/` | Musical logic, file formats, settings, limits | No WPF |
| `src/TabForge.Core/Playback/` | Score to timeline to MIDI (`ScoreToMidiCompiler`, `PlaybackEngine`) | No WPF |
| `src/TabForge.Core/Documents/` | One `DocumentSession` per open song: undo, save, close, placement | |
| `src/TabForge.Core/Controllers/` | One responsibility moved out of a window, behind a host interface | Owns state, not layout |
| `src/TabForge.Core/Views/` | Controls and windows (`TabEditorControl`, `PreferencesWindow`) | |
| `src/TabForge.Core/Audio/`, `src/TabForge.Core/Rendering/` | The only way to reach the engine (`AudioEngineClient`) and render jobs | |
| `src/TabForge.Core/Diagnostics/`, `src/TabForge.Avalonia/SelfTests/`, `tests/TabForge.Avalonia.Tests/` | Headless commands, the basic test set and the archived full suite | |

### Folder READMEs

Each folder README is one page: purpose, key types, pathway, what the folder must not depend on, and its tests.

- Application: `src/TabForge.Core/Models/README.md`, `src/TabForge.Core/Services/README.md`, `src/TabForge.Core/Playback/README.md`, `src/TabForge.Core/Documents/README.md`, `src/TabForge.Core/Controllers/README.md`, `src/TabForge.Core/Views/README.md`, `src/TabForge.Core/Audio/README.md`, `src/TabForge.Core/Rendering/README.md`.
- Also: `src/TabForge.Core/Shell/README.md`, `src/TabForge.Core/Docking/README.md`, `src/TabForge.Core/Plugins/README.md`, `src/TabForge.Core/Presets/README.md`, `src/TabForge.Avalonia/Visualization/README.md`, `src/TabForge.Core/Diagnostics/README.md`, `src/TabForge.Avalonia/SelfTests/README.md`, `src/TabForge.Avalonia/Resources/README.md`.
- Engine and contracts: `src/TabForge.AudioEngine/README.md`, `src/TabForge.Audio.Contracts/README.md`.
- Project root: `src/TabForge.Avalonia/README.md` (every root file). Views and services: `src/TabForge.Core/Views/Score/README.md`, `src/TabForge.Core/Views/Band/README.md`, `src/TabForge.Core/Views/Preferences/README.md`, `src/TabForge.Core/Services/Video/README.md`, `src/TabForge.Core/KeyboardMode/README.md`, `src/TabForge.Avalonia/PresetManagement/README.md`, `src/TabForge.Avalonia/CompactMode/README.md`, `src/TabForge.Core/LiveInput/README.md`, `src/TabForge.Core/Views/Video/README.md`, `src/TabForge.Core/Services/Features/README.md`, `src/TabForge.Core/Services/Log/README.md`, `src/TabForge.Core/Playback/Models/README.md`, `src/TabForge.Core/Presets/FullDemoSong/README.md`.
- Engine sub-folders: `src/TabForge.AudioEngine/Audio/README.md`, `src/TabForge.AudioEngine/Editors/README.md`, `src/TabForge.AudioEngine/Isolation/README.md`, `src/TabForge.AudioEngine/Midi/README.md`, `src/TabForge.AudioEngine/Midi/Processors/README.md`, `src/TabForge.AudioEngine/Mixing/README.md`, `src/TabForge.AudioEngine/Output/README.md`, `src/TabForge.AudioEngine/Plugins/README.md`, `src/TabForge.AudioEngine/Synth/README.md`.
- Self-test sub-folders: `src/TabForge.Avalonia/SelfTests/Architecture/README.md`, `src/TabForge.Avalonia/SelfTests/Engine/README.md`, `src/TabForge.Avalonia/SelfTests/Hygiene/README.md`, `src/TabForge.Avalonia/SelfTests/Settings/README.md`, `src/TabForge.Avalonia/SelfTests/Smoke/README.md`.

`MainWindow` (many partial files) and `TabEditorControl` are composition roots. They wire things together; new behaviour goes into a small class with a host interface, never into another partial file.

## Golden pathways

Use these instead of writing a new route.

- **Edit the song.** `DocumentEdits.Run` takes the session and a mutate function. It makes one undo step, one dirty change and one timeline invalidation. Do not mark the project changed again afterwards.
- **Save and close.** `DocumentSaveFlow` and `DocumentCloseFlow` own the steps and the questions. A window answers through dialogs; a test answers through `ISaveInteractions`.
- **Open a song.** `DocumentPlacement` decides where it goes (new tab, replace, beside an unsaved one).
- **Explicit document context.** Anything touching a song's audio files takes that song's `MediaContext` (`DocumentSession.Media`). Never find a document through a window, the focused tab or a global.
- **Reach the engine.** Only through `AudioEngineClient`. After an engine restart, `EngineSyncController` resends the state to every window; code that sends engine state must be resendable from there.
- **Subscribe to events.** Through `OwnedSubscriptions`, so a closed window can be collected.
- **Settings.** One shared `AppSettingsStore`. Bounds in `SettingsValidator`, old files in `SettingsMigration`, rows in `PreferencesWindow`.
- **Files.** Write with `FilePathPolicy.WriteAtomically`; bound every read with `InputLimits`.
- **Commands.** Bindable through `HotkeyCatalog`, listed in the presets and in `docs/guides/TOOLS_AND_HOTKEYS.md`. A plain command is one line in `BuildCommands()` (`MainWindow.Commands.cs`); one with conditions or several steps is a case in the `RunHotkey(...)` switch (`MainWindow.Settings.cs`). Steps: `docs/RECIPES.md`.
- **New feature.** A module in the feature's own folder implementing `IFeatureModule`, added to `src/TabForge.Core/Services/Features/FeatureRegistry.cs`. The window gets thin hooks only. Steps: `docs/RECIPES.md`, "Add a feature module".

## Find a feature

1. `docs/FEATURE_MAP.md`: index of features. Each feature's page in `docs/feature-map/` gives main code, pathway and self-tests. `docs/feature-map/files.md` lists every source file by folder with its purpose (generated).
2. `docs/RECIPES.md`: exact file lists for the usual changes (technique, setting, command, export, engine command, song field, dialog).
3. `CONTRIBUTING.md`: naming, comment style, test rules, pull request checklist.
4. `docs/architecture/ARCHITECTURE.md`: processes, threads, IPC limits, state ownership, persistence.
5. The folder README (purpose, key types, pathway, tests) of the folder you are about to change; the list is under "Folder READMEs".
5a. Compatibility and format notes: `docs/reference/COMPATIBILITY.md`.
5b. Symptom to first file, first test and first log: `docs/guides/DEBUG_SYMPTOMS.md`.

## Run one test

Build, then from the repository root (the source-scanning tests read the folder they start in):

```
dotnet build src\TabForge\TabForge.csproj -c Release -v q -o out-full
Start-Process -Wait out-full\TabForge.exe -ArgumentList '--selftest','one.log','--only','TestEditCommands','--profile','scratch-profile'
```

- `--only <TestName>[,<TestName>]` runs just those tests; `--areas <area>` runs one area. Names: `docs/feature-map/tests.md`.
- The program prints nothing. Read the last line of the log (`N passed, M failed, K skipped`) and search it for FAIL. The exit code alone is not the result.
- `--profile <folder>` keeps your own settings untouched. Always use it.
- A normal build runs only the small basic set (`--selftest <log>`; architecture, hygiene, smoke tests). Narrow tests of the big suite (`tests/TabForge.Avalonia.Tests/`) need the `-p:TabForgeFullSuite=true` build above; the full gate (`--selftest <log> --require ci,document-context` on that build) is for major redesigns only. All headless commands: `docs/guides/DEBUGGING.md`.

## Guard rules

Self-tests fail the build of a change that breaks these.

- **Layering.** Models, Services and Playback have no WPF; the app reaches the engine only through the client; the contracts project has no dependencies.
- **Size and shape budgets.** `src/TabForge.Avalonia/ArchitectureBudget.json` caps file and class sizes and lists naming exceptions. Lower a budget when a change shrinks the code. Raise one only with a reason in the commit message, and never cut behaviour to fit a budget. New files stay at 500 lines or fewer, so move code into a new class instead of growing a large file.
- **Naming.** XController, XFlow, IXHost and XService as in `CONTRIBUTING.md`.
- **Find a feature fast.** `TabForge.exe --find <keyword> out.txt` lists matching features, files and tests in at most 40 lines; read that instead of the whole feature map.
- **Tests.** Every test has an area in the AreaOf table in `src/TabForge.Avalonia/SelfTests/SelfTest.cs`; after adding or renaming tests run `TabForge.exe --feature-map` and commit `docs/feature-map/tests.md`.
- **Docs.** A document that names a file, type, test or command must name one that exists. This page and `docs/RECIPES.md` are checked the same way (`TestStartHereAndRecipesInSync`), and this page stays within 150 lines.
- **Text.** Sources contain no stray control characters; comments describe the code as it is, without history.
