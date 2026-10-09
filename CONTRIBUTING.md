# Contributing to TabForge

TabForge is a keyboard-driven tablature and notation editor for Windows with a real audio path. It is pre-release software. Issues and pull requests are welcome.

The maintainer reviews every change. Small, focused pull requests are merged fastest.

Contributions are made under the repository licence (`LICENSE`; third-party components are listed in `THIRD_PARTY.md`). Do not add copyrighted songs, samples or artwork. The repository ships one original demo song under `samples/`, and the tests generate their songs in code.

Start with `START_HERE.md` (layers, pathways, where things live) and `docs/RECIPES.md` (file lists for common changes). Read `ARCHITECTURE.md` before you change anything that crosses a process, thread or file-format boundary.

## What you need

- Windows 10 or 11, x64. The app and its tests are Windows-only (WPF).
- The exact .NET SDK named in `global.json`. `dotnet --version` must print it; roll-forward is disabled.
- Nothing else for the managed build.
- Optional: CMake and the MSVC build tools for the native VST3 bridge (`native/tfvst3/BUILD.md`), and Inno Setup 6 or later for the installer (`tools/Package-Release.ps1`).

## Build and run

From the repository root:

```
dotnet restore TabForge.sln --locked-mode
dotnet build TabForge.sln -c Release --no-restore
```

- Restore is locked by the `packages.lock.json` files. When you change a package, regenerate the lock files and commit them with the change.
- Warnings are errors (`Directory.Build.props`). The tree builds with zero warnings.
- Run `src\TabForge\bin\Release\net8.0-windows\TabForge.exe`, optionally with a song path.
- `--profile <folder>` runs with a throw-away settings folder, so your own settings stay untouched. Use it whenever you try a change.

## Run the self-tests

TabForge has one headless test suite built into the executable. No audio device, plug-in or song file is needed. Details are in `docs/TESTING.md`.

From the repository root (the tests find the source tree from the working directory):

```
Start-Process -Wait src\TabForge\bin\Release\net8.0-windows\TabForge.exe -ArgumentList '--selftest','selftest.log','--require','ci'
```

`TabForge.exe` is a GUI-subsystem program, so nothing is printed to the console. Read the last line of the log:

```
TabForge self-test: 4887 passed, 0 failed, 3 skipped
```

That line is the result. The exit code alone is not enough. A failing check is written as `FAIL  <check>  -> <detail>`; search the log for `FAIL` and `SKIP`.

- **One test.** `--only TestName[,TestName]` runs just the named tests. The full command reference (one test, one area, the CI gate, the headless diagnostics) is `docs/DEBUGGING.md`.
- **Areas.** `--areas ui` (or `--areas settings,persistence`) runs one area plus the tests that have no area. Use it while you work. Run the whole suite before you open a pull request. The area names are listed in `docs/TESTING.md`.
- **Groups and `--require ci`.** Some tests belong to named groups. `--require ci` makes the CI groups and two hygiene checks mandatory: a group that did not run, threw, or ran fewer checks than its minimum fails the whole run. A deleted or emptied test cannot pass. The registry is `src/TabForge/SelfTests/SelfTestRequirements.cs`.
- **Small-screen mode.** CI runs on a 1024 x 728 screen at 100% scale. `TABFORGE_SELFTEST_SMALLSCREEN=1` makes your PC judge test windows by the same screen, so a layout test that passes on a big monitor but fails on CI fails for you too.
- **No local songs.** `TABFORGE_NO_LOCAL_SONGS=1` ignores any local song folder, as on a clean checkout.

## Where things live

| You want to change | Look in |
| --- | --- |
| Score data | `src/TabForge/Models/` (plain data, no WPF) |
| Musical logic, file formats, settings, limits | `src/TabForge/Services/` (no WPF; a test enforces it) |
| Playback compiler and scheduler | `src/TabForge/Playback/` (score to timeline to MIDI) |
| Open songs, undo, save and close flows | `src/TabForge/Documents/` (one `DocumentSession` per song) |
| Controls and windows | `src/TabForge/Views/`, `src/TabForge/Visualization/`, `src/TabForge/Docking/` |
| Responsibilities moved out of windows | `src/TabForge/Controllers/` |
| Window shell helpers | `src/TabForge/Shell/` |
| Audio client and rendering | `src/TabForge/Audio/`, `src/TabForge/Rendering/` (the engine is reached only through the client) |
| Audio engine, mixer, plug-in hosting, synth, recorder | `src/TabForge.AudioEngine/` (real-time rules: `ARCHITECTURE.md`, section 4) |
| Wire contracts shared by both sides | `src/TabForge.Audio.Contracts/` (no dependencies) |
| Native VST3 bridge | `native/tfvst3/` |
| Headless commands and tests | `src/TabForge/Diagnostics/`, `src/TabForge/SelfTests/` (the basic set) and `tests/full-suite/` (the full suite) |
| Build, packaging, CI | `Directory.Build.props`, `tools/`, `installer/`, `.github/workflows/` |

To find the code, the pathway and the self-tests for a feature, start with `docs/FEATURE_MAP.md` and the feature's page in `docs/feature-map/`. The test tables in `docs/feature-map/tests.md` are generated (`TabForge.exe --feature-map`) and a self-test keeps them current.

A command is made bindable in `HotkeyCatalog`, in the hotkey presets and in `TOOLS_AND_HOTKEYS.md`. A setting lives in the settings classes, `SettingsValidator` and `SettingsMigration`, and has a Preferences row.

## Adding a feature without growing the big windows

`MainWindow` (many partial files) and `TabEditorControl` are already large. They are composition roots: they wire things together. New behaviour goes into a small class that owns it, not into another partial file.

The pattern, with `TrackListFitController` as the model:

1. A small class owns the state and the behaviour.
2. A narrow interface (here `ITrackListFitHost`) lists only what the class needs from its window.
3. The window implements the interface and creates the class.
4. A self-test drives the class through a fake host. No window is created.

Use the shared pathways instead of writing a new one:

- **Edits.** A model edit goes through `DocumentEdits.Run`. It makes exactly one undo step (nothing is stored when nothing changed), one dirty change and one timeline invalidation, whichever entry point started the edit. Do not mark the project changed again afterwards.
- **Saving.** Saving, including the questions it may ask, is `DocumentSaveFlow`. A window answers the questions with dialogs; a test answers them directly through `ISaveInteractions`.
- **Opening a song.** Where a newly opened song goes (new tab, replace, beside an unsaved one) is decided by `DocumentPlacement`.
- **Media.** Everything that touches a song's audio files (waveforms, clip playback, drops, linked-audio review) receives the song's `MediaContext` explicitly (`DocumentSession.Media`). Never look a document up from a window, the focused tab or a global.
- **Subscriptions.** Subscribe through `OwnedSubscriptions` so a closed window can be collected (the `window-lifetime` and `leaks` tests check this).
- **Files.** Write through `FilePathPolicy.WriteAtomically`. Bound every read with `InputLimits`.

Layering rules (which project may use which) are described in `ARCHITECTURE.md`, section 1, and enforced by self-tests. The full list will be under [Dependency rules](ARCHITECTURE.md#dependency-rules).

### Feature checklist

Answer these in the pull request description.

1. **Owner.** Which area owns the behaviour (a named class and folder)? It is not `MainWindow` or `TabEditorControl` unless it is wiring.
2. **Pathways.** Which of the shared pathways above does it use (edits, saving, placement, media context, owned subscriptions, file policy, hotkey catalog, settings store)?
3. **New shared state or dependency.** Did you add static state, a new reference between projects or folders, a new package or a new global? If yes, say why nothing existing fits.
4. **Tests.** Which self-test pins the behaviour? Settings: a test must change the setting, check it is stored, survives a reload and changes nothing else.
5. **Text.** Is every new user-visible string checked against a test, so a rename cannot drift unnoticed?
6. **Commands and settings.** Is a new command bindable and in the presets and `TOOLS_AND_HOTKEYS.md`? Is a new setting in the validator, the migration and Preferences?
7. **Docs.** Is there a user-facing line under "Unreleased" in `CHANGELOG.md` (final behaviour only), and are the documents that name what you changed up to date?
8. **Comments.** Do the comments follow the rule below, with no history in them?

### Worked example: a "highlight" option

A new option that highlights something in the score:

- The setting: a field and default, a bound in `SettingsValidator`, a migration entry, a Preferences row.
- The command, if there is one: a `HotkeyCatalog` entry, preset entries and a line in `TOOLS_AND_HOTKEYS.md`.
- The behaviour: a controller with a host interface, created by the window.
- The test: a self-test that runs the controller against a fake host, plus the settings test.
- The changelog line.

## Naming conventions

The suffix says what a type is for. These are the names already in use; follow them for new types.

- `XController` owns the interaction state of one area of the window (selection, recording, autosave) and talks to the window through a host interface. Example: `Controllers/AutosaveController.cs`.
- `XFlow` is a multi-step user operation with prompts and several outcomes, such as saving or closing a document. Example: `Documents/DocumentSaveFlow.cs`.
- `IXHost` is the interface a helper uses to reach its owner. It starts with `I`, and the owner implements it. Example: `IAutosaveHost`.
- `XService` is a stateless or app-wide service with no UI state. Example: `Services/AsciiExportService.cs`.
- `XBuilder` produces a layout or model and `XRenderer` draws it. Example: `Views/Score/ScoreRenderer.cs`.

A self-test (architecture guard G13) fails on a new interface named `*Host` without the `I`, a class named `I*Host`, or an interface named `*Controller`, `*Flow` or `*Service`. Old exceptions are listed under `namingExceptions` in `src/TabForge/ArchitectureBudget.json`, which may only shrink.

## Comment style

Comments explain the code as it is now, in the present tense: what it does, and why it has to be that way.

- No dates, no reviewer names, no ticket numbers, no "before this fix" or "now also" narration.
- If a comment only makes sense to someone who saw an older version, delete it or rewrite it as a plain statement of the rule.
- History belongs in git and in `CHANGELOG.md`.

Good: `// The callback must not allocate: the engine thread has no time for a collection.`
Not good: `// Fixed 2 March: this used to allocate.`

## Writing a test

- Tests are static methods in the `SelfTest` partial class (narrow tests in `tests/full-suite/<Area>/SelfTest*.cs`; only the basic set is in `src/TabForge/SelfTests/`). They are registered in `RunFullSuite` (`tests/full-suite/SelfTestFullSuite.cs`) with `Guard(TestName)`, or `GuardGroup("<group>", TestName)` for a required group, and mapped to an area in the area table of that file (`SelfTest.cs` for the basic set).
- Use `Check(name, condition, detail)`, `Eq(name, expected, actual)` and `Skip(name, reason, requirement)`. A skip is only for something truly unavailable on the machine. Every skip is listed in `docs/TESTING.md`, and CI has a skip budget.
- Do not depend on the screen. A guard fails a test that opens a top-level window larger than 1024 x 728. Lay a control out at an explicit size with `Measure` and `Arrange` (see `FitStage` in `tests/full-suite/Editor/SelfTestTrackListFit.cs`).
- Do not depend on local files. Generate songs in code (`tests/full-suite/GuitarPro/SelfTestSyntheticFixtures.cs`).
- Do not depend on the clock. A performance check compares two paths against each other; it never asserts an absolute time.
- Sources contain no stray control characters (a test checks). Text files use CRLF (`.gitattributes`).

## Pull requests

- One topic per pull request. Say what changed and how you checked it; paste the self-test summary line.
- Do not commit build output, songs, settings or backups.
- CI (`.github/workflows/windows-ci.yml`) must pass: build, self-test with `--require ci`, native bridge comparison, dependency audit and the alphaTab rebuild comparison.

## Reporting problems

Bugs go to the Issues page, with steps or a song you are allowed to share. Security problems: see `SECURITY.md`.
