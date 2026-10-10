# Debugging TabForge

How to run one test, one area, the CI gate, and the headless diagnostics. `docs/TESTING.md` explains what the suite covers; this page is the command reference. `TabForge.exe` is a GUI-subsystem program and prints nothing to the console, so every command below writes a file; read that file.

Run every command from the repository root (the source-scanning tests read the folder they start in) and give it a scratch settings folder with `--profile <folder>` so your own settings stay untouched. A run that cannot start exits with 2, a run that found a problem with 1, success is 0.

For the route from a symptom to its first file, test and log, see `docs/DEBUG_SYMPTOMS.md`.

<!-- TEST-BOX-START -->
> **How to run a test (canonical)**
> - Every build: the basic area. `src\TabForge\bin\Release\net8.0-windows\TabForge.exe --selftest <log> --areas basic`, then read the "N passed, M failed" line (the exe prints nothing; read the log).
> - One test (`--only TestX`): most tests live in `tests/full-suite`, so first build a full-suite copy: `dotnet build src/TabForge/TabForge.csproj -c Release -p:TabForgeFullSuite=true -o <dir>`, then run `<dir>\TabForge.exe --selftest <log> --only TestX`. A normal build reports "0 run" or an unknown name.
> - Register a test: add an `AreaOf` entry in `src/TabForge/SelfTests/SelfTest.cs`, and register it in `tests/full-suite/SelfTestFullSuite.cs` (not a `Guard` in `SelfTest.cs`). Then run `--feature-map` (regenerates `docs/feature-map/tests.md`).
> - Before a release: `--areas release` (full-suite build). The full suite (`--require ci,document-context`) runs weekly and at manager checkpoints only.
<!-- TEST-BOX-END -->

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

- `TABFORGE_SPEED_ONLY="open Preferences;Delete bars"` (environment) limits `--speed-audit` to the actions whose names contain one of the fragments.
- `TABFORGE_SPEED_ONLY="drag start"` times the two drag-start entries (`Diagnostics/WindowProbes.SpeedAuditDrag.cs`): a three-file drag entering the timeline and the first move of a clip drag, each from the pointer event to the ghost in place plus idle.
- `TABFORGE_VIDEO_PERF=<file>` appends one line per layout to the file during `TestVideoExport` in a full-suite test build: ms per frame in each drawing phase (layout, bake, instrument, Band, raster, copy, compose), the hand-off to the encoder (UI-blocked time, including queue waits) and the bake count. Off, the frame source takes no timings.
- `TABFORGE_TIMELINE_SPEED_PROFILE=<song path>` adds cold, cached and revision timing measurements to `TestTimelineSongTimeRepeatGrowth` in a full-suite test build.
- `TABFORGE_DIALOG_EVIDENCE=<absolute PNG path>` saves the scaled marker dialog during `TestDialogEscape` in a full-suite test build.
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

The maintainer-only public-tree release gate compiles a separate full-suite copy before it runs the curated `--areas release` set. Its `-CheckDecisions` option verifies that build selection, the selected-revision doc refresh and the 35-minute process budget without building or publishing.

## Small-screen mode

CI runs on a 1024 x 728 desktop at 100% scale. Set `TABFORGE_SELFTEST_SMALLSCREEN=1` and your PC judges test windows by the same screen, so a layout that fits your monitor but not CI fails locally too. `TABFORGE_NO_LOCAL_SONGS=1` ignores any local song folder, as on a clean checkout. `TABFORGE_TEST_SINGLE_INSTANCE=1` is reserved for `TestSingleInstanceProcessHandover`; it enables a profile-scoped pipe only with a non-real `--profile`.

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
| `--render-timeline <song> <out.png>` | The arrangement timeline, with optional hover and drag states; `lines`, `clips` and `scroll` add the lines between tracks, MIDI clips on tracks 1, 4 and 7, and the collapsed three-row pane scrolled down. |
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
| `--feature-map [file]` | Regenerates `docs/feature-map/tests.md`; run it after adding or renaming a test. |
| `--find <keyword> [out.txt]` | Prints (and writes) at most 40 lines: matching features, owning files and tests from the feature map, test registry and folder READMEs. |
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

Three options open the main window and need `--profile <scratch folder>`:

| Option | What it does |
| --- | --- |
| `--capture <script.json> <outDir>` | Drives the window off-screen from a script and saves screenshots. |
| `--screenshots <folder>` | Photographs menus, panels, settings and dialogs, then exits. |
| `errors.log` | Always on, no switch: every swallowed error from a catch (`Trace.Error`) goes to `errors.log` in the diagnostics folder (256 KB, then `errors.1.log`); identical lines within 1 s are counted, not repeated. Render and pointer-move paths stay silent. |
| `TABFORGE_TRACE=playback` | After every seek, loop wrap or resync the trace line `restore@<ms>ms` lists each channel's program (`prog`), volume (`cc7`) and pan (`cc10`) just sent; check it when a mix-table change seems lost after a jump. |
| `--speed-audit <report.md>` | Times every common action (clicks, scrolling, zoom, resizes, windows, menus, editing, tracks, clips, save, open, tabs) on the off-screen window with the demo song and a long audio clip, stopped and playing, and writes a table: median and worst time to idle, synchronous work, worst frame gap, flags above 50 ms or 33 ms. Clip and track-row menus open a real non-activating popup; long WAV drops extend every track in both transport states. Set `TABFORGE_TRACE=ui` to add the slow-funnel lines with their callers. Silent (master volume 0); works on a copy of the song. |

## Other flags

Internal launches are started by TabForge itself; do not run them by hand. The probes open the main window after a song, like the options above; add `--profile <scratch folder>` too.

- `--approve-night-plugins [all]`: pre-approves the named stress-run plug-ins in the profile's trust store (`all`: every plug-in the scanner finds). Refused without `--profile`. Use for unattended stress runs only.
- `--audio-engine <session> <pid>`: internal. The audio engine process that plug-in playback runs in (no window).
- `--corpus <dir>`: with `--render-identity`, adds the song files in that folder to the corpus (default: environment variable `TF_RENDER_CORPUS`). Use to include your own songs in the identity check.
- `--import-worker <pipe> [parentPid]`: internal. Runs one song import out of process: one request, one reply, then exit. TabForge kills it on cancel or timeout.
- `--pair-save-probe <gp> <tfaudio> <stage> <signal> <new gp> <new audio>`: full-suite builds only. Child of the pair-save kill test: stops at a stage, writes the signal file, waits to be killed.
- `--perf-follow <report>`: plays the song in each score-follow style and records CPU and memory. Use when changing score following.
- `--plugin-host <id> <enginePid> <rate> <block> <1|0> <format> <path> [sha256|none]`: internal. Runs one plug-in per process for the audio engine, so a crashing plug-in ends only its process.
- `--plugin-info <path> <sha256>`: internal. Loads one approved plug-in in a throwaway process and prints `role|vendor|name`; exit 5 when the file is missing or changed.
- `--probe-countin <report>`: turns count-in on through the real button, presses Play and records the channel-10 clicks sent before the music. Use when count-in timing changes.
- `--probe-gm <report>`: headless (no window, no audio device). Feeds the engine's GM synth the score's technique messages and measures pitch, level and tail; exit 1 on any FAIL.
- `--probe-instrument-menu <report>`: opens the instrument panel's right-click menu in each view and reports it.
- `--probe-menus <folder>`: clicks every main-menu item on a temporary copy of the song, photographs the dialogs, cancels them and reports what changed. Skips Exit and Check for updates.
- `--probe-playback-visuals <folder>`: photographs playback with default colours, then with magenta and thicker values, to confirm the settings reach the screen.
- `--probe-record <report>`: records MIDI while the song plays, then checks the clip, its lane, playback, notation and loop takes. Closes the window afterwards.
- `--probe-settings <report>`: changes each Settings row through the live-preview path, records errors and visible change, then restores the originals. Nothing is saved.
- `--probe-update <report>`: one real update check against GitHub, as an old and as the current version. Needs network; nothing opens.
- `--size WxH` (or `--size=WxH`): restores the main window at that size in DIPs, at position 20,20. Ignored with `--capture` or `--speed-audit`.
- `--software-render`: forces WPF software rendering. Use in unattended runs with the monitor off, where hardware rendering leaves the window black.
- `--theme <preset>`: applies a Settings theme preset (for example Dark or Light) to the open window. Use for screenshots in each theme.
- `--tracks all|1,3,5-7`: `--render-bars` option: which tracks to render (default `all`).
- `--views notation,tab,both`: `--render-bars` option: which views to render (default `notation,tab`).
