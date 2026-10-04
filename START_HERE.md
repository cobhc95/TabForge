# Start here

Read this page first. It tells you where things live and which existing pathway to use, so you can open only the files a change needs.

## What TabForge is

A keyboard-driven tablature and notation editor for Windows (WPF, .NET 8) with its own audio engine. It opens and saves .gp, .gp5 and its own .tforge format, plays a song through a built-in synth or hosted plug-ins, records audio, and renders to files. It is pre-release software. Features and limits: `README.md`.

## Layer map

Three projects. A lower layer never references a higher one; self-tests enforce this (`TestArchitectureLayering`).

| Layer | Folder | Holds |
| --- | --- | --- |
| Wire contracts | `src/TabForge.Audio.Contracts/` | Protocol types shared by app and engine, no dependencies (`EngineCommand`, `EngineEvent`) |
| Audio engine | `src/TabForge.AudioEngine/` | A separate process: mixer, synth, plug-in hosting, recorder, offline renderer. Real-time rules: `ARCHITECTURE.md`, section 4 |
| Application | `src/TabForge/` | Everything the user sees, plus the data and logic behind it (below) |

Inside the application project:

| Folder | Role | Rule |
| --- | --- | --- |
| `src/TabForge/Models/` | Song data (`SongProject`, `TrackModel`, `MeasureModel`) | No WPF |
| `src/TabForge/Services/` | Musical logic, file formats, settings, limits | No WPF |
| `src/TabForge/Playback/` | Score to timeline to MIDI (`ScoreToMidiCompiler`, `PlaybackEngine`) | No WPF |
| `src/TabForge/Documents/` | One `DocumentSession` per open song: undo, save, close, placement | |
| `src/TabForge/Controllers/` | One responsibility moved out of a window, behind a host interface | Owns state, not layout |
| `src/TabForge/Views/` | Controls and windows (`TabEditorControl`, `PreferencesWindow`) | |
| `src/TabForge/Audio/`, `src/TabForge/Rendering/` | The only way to reach the engine (`AudioEngineClient`) and render jobs | |
| `src/TabForge/Diagnostics/`, `src/TabForge/SelfTests/`, `tests/full-suite/` | Headless commands, the basic test set and the archived full suite | |

### Folder READMEs

Each folder README is one page: purpose, key types, pathway, what the folder must not depend on, and its tests.

- Application: `src/TabForge/Models/README.md`, `src/TabForge/Services/README.md`, `src/TabForge/Playback/README.md`, `src/TabForge/Documents/README.md`, `src/TabForge/Controllers/README.md`, `src/TabForge/Views/README.md`, `src/TabForge/Audio/README.md`, `src/TabForge/Rendering/README.md`.
- Also: `src/TabForge/Shell/README.md`, `src/TabForge/Docking/README.md`, `src/TabForge/Plugins/README.md`, `src/TabForge/Presets/README.md`, `src/TabForge/Visualization/README.md`, `src/TabForge/Diagnostics/README.md`, `src/TabForge/SelfTests/README.md`, `src/TabForge/Resources/README.md`.
- Engine and contracts: `src/TabForge.AudioEngine/README.md`, `src/TabForge.Audio.Contracts/README.md`.

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
- **Commands.** Bindable through `HotkeyCatalog`, listed in the presets and in `TOOLS_AND_HOTKEYS.md`.

## Find a feature

1. `docs/FEATURE_MAP.md`: feature to main code, pathway and self-tests.
2. `docs/RECIPES.md`: exact file lists for the usual changes (technique, setting, command, export, engine command, song field, dialog).
3. `CONTRIBUTING.md`: naming, comment style, test rules, pull request checklist.
4. `ARCHITECTURE.md`: processes, threads, IPC limits, state ownership, persistence.
5. The folder README (purpose, key types, pathway, tests) of the folder you are about to change; the list is under "Folder READMEs".
5a. Compatibility and format notes: `docs/COMPATIBILITY.md`.

## Run one test

Build, then from the repository root (the source-scanning tests read the folder they start in):

```
dotnet build src\TabForge\TabForge.csproj -c Release -v q -p:TabForgeFullSuite=true -o out-full
Start-Process -Wait out-full\TabForge.exe -ArgumentList '--selftest','one.log','--only','TestEditCommands','--profile','scratch-profile'
```

- `--only <TestName>[,<TestName>]` runs just those tests; `--areas <area>` runs one area. Names: `docs/FEATURE_MAP.md`.
- The program prints nothing. Read the last line of the log (`N passed, M failed, K skipped`) and search it for FAIL. The exit code alone is not the result.
- `--profile <folder>` keeps your own settings untouched. Always use it.
- A normal build runs only the small basic set (`--selftest <log>`; architecture, hygiene, smoke tests). Narrow tests of the big suite (`tests/full-suite/`) need the `-p:TabForgeFullSuite=true` build above; the full gate (`--selftest <log> --require ci,document-context` on that build) is for major redesigns only. All headless commands: `docs/DEBUGGING.md`.

## Guard rules

Self-tests fail the build of a change that breaks these.

- **Layering.** Models, Services and Playback have no WPF; the app reaches the engine only through the client; the contracts project has no dependencies.
- **Size and shape budgets.** `src/TabForge/ArchitectureBudget.json` caps file and class sizes and lists naming exceptions. A budget may only go down; do not raise one to make room for new code, move the code out instead.
- **Naming.** XController, XFlow, IXHost and XService as in `CONTRIBUTING.md`.
- **Find a feature fast.** `TabForge.exe --find <keyword> out.txt` lists matching features, files and tests in at most 40 lines; read that instead of the whole feature map.
- **Tests.** Every test has an area in the AreaOf table in `src/TabForge/SelfTests/SelfTest.cs`; after adding or renaming tests run `TabForge.exe --feature-map` and commit `docs/FEATURE_MAP.md`.
- **Docs.** A document that names a file, type, test or command must name one that exists. This page and `docs/RECIPES.md` are checked the same way (`TestStartHereAndRecipesInSync`), and this page stays within 150 lines.
- **Text.** Sources contain no stray control characters; comments describe the code as it is, without history.
