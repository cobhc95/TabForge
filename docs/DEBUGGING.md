# Debugging TabForge

How to run one test, one area, the CI gate, and the headless diagnostics. `docs/TESTING.md` explains what the suite covers; this page is the command reference. `TabForge.exe` is a GUI-subsystem program and prints nothing to the console, so every command below writes a file; read that file.

Run every command from the repository root (the source-scanning tests read the folder they start in) and give it a scratch settings folder with `--profile <folder>` so your own settings stay untouched. A run that cannot start exits with 2, a run that found a problem with 1, success is 0.

## The basic set and the full suite

A normal build carries the **basic set** only: the architecture and hygiene checks and a few smoke tests (model round trip, project load/save, settings, one editor entry, one headless engine start). `--selftest <log>` runs it in a few seconds; `--areas basic` selects the same set in a full-suite build. This is what every local rebuild runs.

The **full suite** (about 5,800 checks, `tests/full-suite/`) is compiled in only with `-p:TabForgeFullSuite=true`. Use it for major architecture or framework redesigns, and in CI; everyday work uses the basic set plus narrow tests of what changed. See `tests/full-suite/README.md`.

```
dotnet build src\TabForge\TabForge.csproj -c Release -v q -p:TabForgeFullSuite=true -o out-full
```

## Run one test

```
Start-Process -Wait src\TabForge\bin\Release\net8.0-windows\TabForge.exe -ArgumentList '--selftest','one.log','--only','TestBarSlots'
```

- `--only <TestName>[,<TestName>...]` runs just the named tests. A name is the test method as written in the `Guard(...)` or `GuardGroup(...)` call in `src/TabForge/SelfTests/SelfTest.cs` (basic set) or `tests/full-suite/SelfTestFullSuite.cs`. Names are exact and case-sensitive. A full-suite test is known only to a build made with `-p:TabForgeFullSuite=true`; the maintainer's local rebuild script builds that for you when `TABFORGE_SELFTEST_ONLY=TestA,TestB` is set.
- The log ends with the usual line, `TabForge self-test: N passed, M failed`. Search the log for `FAIL`; the exit code alone is not the result.
- An unknown name fails the run before any test starts and lists the closest registered names.
- `--require <group>` works with `--only` only when every test of that group is named; otherwise the log says so. Use `--only` for the fast loop; the full gate is for major redesigns and releases only.

## Run one area

- `--areas ui` (or `--areas settings,persistence`) runs those areas plus the tests that have no area; leave it out, or use `all`, for everything. The area names and the test-to-area table are in `docs/TESTING.md` and `src/TabForge/SelfTests/SelfTest.cs`.
- Tests that take longer than a second are timed in the log (`time  <test> [<area>] <seconds>`).

## The CI gate

```
--selftest ci.log --require ci
```

The gate needs a full-suite build (CI builds it with `-p:TabForgeFullSuite=true`; the maintainer's local rebuild script has a `fullsuite` option for it). `--require ci` is what CI (`.github/workflows/windows-ci.yml`) and the release packaging run. It makes the CI groups and two hygiene checks mandatory: a group that did not run, threw, or ran fewer checks than its minimum fails the run. The registry is `src/TabForge/SelfTests/SelfTestRequirements.cs`. Fixture-dependent groups use synthetic songs generated in code, so the gate needs no local song files.

## Small-screen mode

CI runs on a 1024 x 728 desktop at 100% scale. Set `TABFORGE_SELFTEST_SMALLSCREEN=1` and your PC judges test windows by the same screen, so a layout that fits your monitor but not CI fails locally too. `TABFORGE_NO_LOCAL_SONGS=1` ignores any local song folder, as on a clean checkout.

## Headless diagnostics

Each mode is the first argument. They open no window (the two window options below are the exception) and write bounded, path-checked output. `<song>` is any file the app opens. The list below is checked against the command table in `src/TabForge/Diagnostics/DiagnosticCommands.cs` by a self-test, so it cannot drift.

| Command | What it does |
| --- | --- |
| `--selftest <log>` | The built-in test suite (see above). |
| `--audit <song> [out]` | Silent playback timing audit; exit 1 when notes are late or early. |
| `--playtest <song> [seconds] [out]` | The same audit over a playback run of the given length. |
| `--render <song> <out.png>` | The first track's score, drawn off-screen to a PNG. |
| `--render-bars <song> <outdir>` | One PNG per track, bar and view, plus a checks file; exit 1 when a check fails. |
| `--render-fretboard <song> <out.png>` | The fretboard view at a given moment, with scale and style options. |
| `--render-timeline <song> <out.png>` | The arrangement timeline, with optional hover and drag states. |
| `--render-gp-export-dialog <out.png>` | The `.gp` export dialog as an image. |
| `--render-identity <outDir>` | Writes an identity file of rendering hashes over a fixed song corpus (`--png` adds images). |
| `--render-identity-compare <a> <b>` | Compares two identity folders; any change in drawing shows up as a differing case. |
| `--layout-audit <song> <report>` | Engraves every track and lists colliding texts and markings. |
| `--dump <song> <out.txt> [firstBar] [bars]` | The imported model as text: tuning, metre, beats, notes, effects. |
| `--plausibility <file or folder> <out.txt>` | The damaged-file check on one song or a folder of `.gp` files; exit 1 when any would warn. |
| `--import-measure <song> <report> [worker or inproc]` | What opening a file costs: counts, serialised size and peak memory. |
| `--memreport <song> <out.txt>` | What each stage keeps alive and what a single edit costs. |
| `--exportgp <song> <out.gp>` | Writes a `.gp`, reads it back and reports what survived; exit 1 when notes are lost. |
| `--musicxml-export <song> <out.musicxml>` | Exports MusicXML. |
| `--midi-export <song> <out.mid>` | Exports MIDI. |
| `--midi-timing <song> <out.mid>` | Exports MIDI and measures bar-start and end timing error; exit 1 above 1 ms. |
| `--gp-open <file.gp>` | Opens one `.gp` file through the importer and reports the result. |
| `--gp-compare <a.gp> <b.gp> [report]` | Compares two `.gp` files field by field. |
| `--gp-capability <out.md> [folder]` | Runs every capability case and writes the capability record. |
| `--gp-compat-doc <out.md>` | Regenerates the compatibility page. |
| `--gp-loss-coverage <report> [group]` | Lists every measured import and export loss. |
| `--roundtrip-diff <song or @list> <out.txt> [formats]` | Saves and reloads in each format and lists what changed. |
| `--roundtrip-semantics <report>` | Runs only the semantic round-trip suite. |
| `--write-gp-fixture <out.gp> [basic or showcase]` | Writes the synthetic test song as a `.gp` file. |
| `--write-gp-fixtures <dir>` | Writes the whole synthetic fixture set. |
| `--write-gp-probes <dir>` | Writes small one-question `.gp` files for hand checks. |
| `--write-demo-song <out.gp>` | Writes the built-in demo song. |
| `--write-tutorial-starters <dir>` | Writes the starter songs the tutorial uses. |
| `--gendemo [out]` | Writes the built-in demo song as a project file. |
| `--gendiag [dir]` | Writes the purpose-built diagnostic songs with their expected note-on times. |
| `--feature-map [file]` | Regenerates `docs/FEATURE_MAP.md`; run it after adding or renaming a test. |
| `--tutorial-shot <folder> <out.png>` | One tutorial page as an image. |
| `--tutorial-pdf <folder> <out.pdf>` | The tutorial as a PDF. |
| `--audit-gm <song> <report>` | Plays a song through the engine's General MIDI synth, one channel per track, and checks the sounding pitches. |
| `--audit-gm-techniques <report>` | The same check on the built-in technique test song. |
| `--audit-drums <folder> <out.txt>` | Compares percussion tracks and notes against the importer's drum tracks. |
| `--audit-timing <folder> <out.txt>` | Compares the bars played (order, length, tempo, metre) with the importer's tick lookup, per file in a folder. |
| `--audio-audit <song> <outdir>` | Renders a song offline through the real engine process: mix, one stem per track, and a report. |
| `--render-probe <out.wav>` | Checks the offline renderer end to end: length, silence, stems and repeatability. |
| `--probe-audio <report>` | Checks the engine process with real plug-ins: routing, state, CPU and crash recovery. |
| `--probe-midi-latency [report]` | Measures the system MIDI output latency. |
| `--level-match <report> [engine-only]` | Compares the built-in synth's levels with the system synth, or measures it alone. |
| `--pitch-audit <report> [max]` | Measures the sounding octave of remembered plug-in instruments; settings are read, never written. |

Two options open the main window and need `--profile <scratch folder>`:

| Option | What it does |
| --- | --- |
| `--capture <script.json> <outDir>` | Drives the window off-screen from a script and saves screenshots. |
| `--screenshots <folder>` | Photographs menus, panels, settings and dialogs, then exits. |
