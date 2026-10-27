# Testing

TabForge has one headless test suite, built into the executable. It needs no audio device, no installed plug-in and no song files. CI runs it on every push.

<!-- TEST-BOX-START -->
> **How to run a test (canonical)**
> - Every build: the basic area. `build\TabForge\TabForge.exe --selftest <log> --areas basic`, then read the "N passed, M failed" line (the exe prints nothing; read the log).
> - One test (`--only TestX`): `build\TabForge\TabForge.exe --selftest <log> --only TestX`. An unknown name reports \"0 run\".
> - Register a test: add an `AreaOf` entry and a `Guard` call in `Source/src/TabForge.Avalonia/SelfTests/SelfTest.cs`. '
> - Before a release: `--areas release`. The full suite (`--require ci,document-context`) runs weekly and at manager checkpoints only.
<!-- TEST-BOX-END -->

## Removed in Batch 2 (canonical note)

In Batch 2 the former full suite and its sibling test files (among them the GuitarPro round-trip semantics, engine-message, settings-parity, interactions, keyboard-hands and video-encoder tests) were removed from this checkout. Every document that says "removed in Batch 2" points here. Results and file names that such a document quotes are historical snapshots; the checks that still run are the basic set and the release area, described below.

## Basic set and full suite

The **basic set** is the architecture and hygiene checks and a few sub-second smoke tests (model round trip, project save and load, one editor entry, settings wiring, one headless engine start). It holds about 580 checks, runs in about 5 seconds and is what every rebuild runs; `--selftest <log>` with no `--areas` or `--only` selects it. Nothing that opens a window, renders, imports or plays belongs in it. The **full suite** (former full suite, removed in Batch 2; its README went with it) was part of the same executable and is selected with `--areas` or `--only`, or run whole with `--require ci`. The sections below describe the full suite.

There is no `essential` area and no `SelfTests/Smoke` folder: window-opening checks live in their own areas (see Areas below) and run with `--areas <area>` (or `--only`), in the release set where listed, and in the full suite.

## Release gate (`--areas release`)

`--areas release` is the curated release set: the basic set plus saving, atomic writes and recovery, import and malformed-input containment, playback and editing interaction, document context, plug-in trust and input limits, and the real two-process Explorer handover plus final-window shutdown check (the list is `ReleaseTestNames` in `Source/src/TabForge.Avalonia/SelfTests/SelfTestRelease.cs`). It runs in about 2 minutes. `Source/tools/Package-Release.ps1` (and so the upstream release workflow, historical: this checkout has no CI config) always publishes a separate copy, runs it with `--areas release` and refuses to package on any failure. The maintainer's local rebuild script has a `release` option that runs the same gate. Push CI stays on the basic set; the full suite runs weekly.

## Run it

From the repository root (the tests find the source tree from the working directory), after a Release build:

```
build\TabForge\TabForge.exe --selftest selftest.log --require ci
```

`TabForge.exe` is a GUI-subsystem program: start it and wait for it to exit (`Start-Process -Wait` in PowerShell); nothing is printed to the console. Read the last line of the log:

```
TabForge self-test: 4887 passed, 0 failed, 3 skipped
```

The exit code alone is not the result: the log must say `0 failed`. Lines that start with `FAIL` name the failing check and why; lines that start with `SKIP` are the skipped checks (see below).

| Option | Effect |
| --- | --- |
| `--areas basic` | the basic set only (architecture, hygiene, smoke; no untagged tests) |
| `--areas ui,settings` | run only those areas (plus the tests with no area); omit it, or use `all`, for everything |
| `--require ci` | make the gate groups mandatory (see below); CI and release packaging use it |
| `--profile <folder>` | use a throw-away settings folder, so your own settings are untouched |
| `TABFORGE_SELFTEST_SMALLSCREEN=1` | judge test windows against the CI runner's screen (1024 x 728, 100% scale) whatever your screen is |
| `TABFORGE_NO_LOCAL_SONGS=1` | ignore any local song folder, as on a clean checkout |

Areas (11, see `AreaOf` in `Source/src/TabForge.Avalonia/SelfTests/SelfTest.cs`): `architecture`, `core`, `engine`, `fuzz`, `guitarpro`, `hygiene`, `persistence`, `playback`, `recording`, `settings` and `synthetic`. While you work, run the area you touched (on a full-suite build) plus `architecture,hygiene`; the whole suite runs only for major redesigns and releases.

## The gate (`--require ci`)

`--require ci` makes these groups mandatory: `gp-fixtures`, `synthetic-fixtures`, `fuzz`, `long-import`, `window-lifetime`, `gp-fidelity`, `document-context`, `document-operations`, `architecture`, `interactions`, plus the `source-hygiene` and `installer-parity` checks. A mandatory group fails the run when it did not run, when one of its tests threw, or when it executed fewer checks than its recorded minimum, so a deleted or emptied test cannot pass. The registry is in `Source/src/TabForge.Avalonia/SelfTests/SelfTestRequirements.cs`.

The `interactions` group drives real windows through the entry points the interface uses (keys, commands, the window's own handlers) and checks the song, undo and dirty state, tab marks, playhead and glitch counters across editing, saving, tab switching, tab transfer, plug-in changes and recording while the song plays (former interactions suite, removed in Batch 2, and the files beside it). A scenario that exposes a known defect reports it as `KNOWN` with its trace id instead of failing; `--require interactions-known` turns those into required checks, so the change that fixes a defect can require it.

## Architecture budget

The `architecture` group compares the compiled assemblies and the source tree with `Source/src/TabForge.Avalonia/ArchitectureBudget.json` (layering, class and method size, mutable statics, comment style, and document-state ownership). A measured value above its budget fails. A value below its budget logs the line to paste; with `TABFORGE_ARCH_STRICT=1` (or `strict-lower`), the setting for merge runs, it fails instead, so the file always matches the code. A budget is raised only by an explicit edit that states the reason in the commit message. `TABFORGE_ARCH_RECORD=<file>` writes the measured values as a new budget file. Size checks on IL bytes run on optimised (Release) builds only.

## Match the CI runner on your own PC

The CI runner has a small desktop (about 1024 x 768) at 100% scale; a developer PC is often 4K at 150%. Windows clamps a top-level window that is larger than the screen, so a layout test written on a big screen can pass locally and fail on the runner. Every test window is opened by `ShowTestWindow`, which fails the test with a clear message when the window is larger than the work area. With `TABFORGE_SELFTEST_SMALLSCREEN=1` the work area is the runner's, so the same failure shows up on your PC. CI sets the variable. The display scale itself cannot be changed from inside the process: lay controls out at explicit sizes instead (see `FitStage` in the former editor track-list-fit test, removed in Batch 2). The guard was a former full-suite Views test, removed in Batch 2.

## Skips

A skip is a check that could not run here; it is reported as `SKIP` and never counted as a pass. CI has a skip budget: the count may not rise, and a new skip needs a reason in this table.

| Skipped check | Why | Where it can happen |
| --- | --- | --- |
| a regression check against a reference song that is not in the repository | the song is copyrighted and never committed; the same bar behaviour is covered by a generated song that always runs | every clean checkout and CI |
| a backup that is a symbolic link is refused | creating symbolic links needs Developer Mode or elevation | a developer PC without it |
| a song folder reached through a junction is refused | the junction could not be created | rare, environment |
| pair save killed at every stage | needs the test to run as `TabForge.exe` itself (it kills and restarts the process) | a renamed or hosted executable |
| a stalled mixer callback holds a replaced chain | the fake plug-in was not called in time | a very slow machine |
| a network plug-in changed-binary test | needs an administrative share on the local machine | machines without one |
| General MIDI synth checks (cold start, device rate, polyphony cap) | the Windows General MIDI sound bank is not available | a PC or runner without it |
| shipped drum maps; readable text tokens | the application's resource files are not next to the test executable | never in the basic set |
| a child process in the job ends when the job closes | `ping.exe` was not found | a stripped system |

Checks that used to be skipped on a clean checkout now run on generated stand-ins:

- .gp compatibility on "sample songs" runs on the generated fixture songs (written as `.gp` files in a temporary folder and imported). The checks that describe a mixed, played real song (mixer values differ from the defaults, no drone longer than 5 s, note count equal to the parser's, note events for the fretboard) apply to real songs only.
- Tuplet import reads a generated song with triplets and quintuplets.
- The save-and-reopen round trips run on a generated dense song.
- VST2 recognition reads a TabForge-generated PE file with an export table instead of an installed third-party plug-in.
- The per-edit undo capture on the largest song is a relative check (at least 4 times cheaper than the old full snapshot, measured in the same run), not an absolute time, so a slow machine cannot fail it and it never needs a skip.

When a local song folder exists, the real files are used instead of the stand-ins.

## Writing a test

- A test is a static method of the `SelfTest` partial class (former full-suite `SelfTest*.cs` files, removed in Batch 2, and the basic set in `Source/src/TabForge.Avalonia/SelfTests/`), registered in `RunFullSuite` or `SelfTest.Run` with `Guard(TestName)` (`GuardGroup("<group>", TestName)` for a mandatory group) and mapped to an area in the `AreaOf` table.
- Use `Check(name, condition, detail)`, `Eq(name, expected, actual)` and `Skip(name, reason, requirement)`.
- Do not depend on the screen: no top-level window larger than 1024 x 728; measure and arrange a control at an explicit size instead.
- Do not depend on local files: generate the song in code.
- Do not assert an absolute time: compare against a baseline measured in the same run.
- Text files in the repository have no control characters other than tab, CR and LF (a hygiene test checks).
- Documentation is tested too: every file, script and command a public document names must exist in the repository and be part of the public tree, and version claims must match `Directory.Build.props` (`Source/src/TabForge.Avalonia/SelfTests/Hygiene/SelfTestDocsConsistency.cs`).
- Every file in `Source/docs/media/screenshots` and `Source/docs/media/animations` is named by a Markdown file; the public export copies only referenced pictures (`TestDocImagesAreReferenced`).

## Runner options for measured and sharded runs

- `--timing <csv>`: records wall-clock for every test and area (columns `area,test,ms,checks,failed`; area rows use `(area)`, the last row `(total wall)`) and logs the 20 slowest tests and the total.
- `--core-shard <n>`: the untagged core tests run only when `n` is 0; other values skip them. `--areas` runs include core unless this flag says otherwise.
- `--group-report <json>`: writes each `--require` group's state (name, Ran, Threw, Partial, Checks) at the end of the run.
- `--merge-group-reports <a.json,b.json> --require ci`: runs no test; sums Checks, ORs Ran/Threw/Partial per group, prints the same `required: group ...` gate lines and exits non-zero when a required group is missing or below its minimum.

## Sharded full suite (the `run-suite` script in `tools`)

The local-only `run-suite` script (PowerShell, in `tools`) builds the app once (`%TEMP%\tf-suite-build`) and runs the suite as parallel child `TabForge.exe` processes, each with its own `--profile`, log, `--timing` csv and `--group-report` json under `%TEMP%\tf-suite-<lane>.*`, started in the repository root. It then merges the group reports with `--merge-group-reports ... --require ci,document-context`, prints one line per lane, a `TOTAL TabForge self-test: N passed, M failed` line and the 10 slowest tests, and exits 1 when any lane failed or timed out (exit 124 inside the lane) or the merged gate fails. The whole run holds one build slot (`%TEMP%\tf-slot1..3.lock`); the children need the machine to themselves.

| Lane | Contents | Order |
| --- | --- | --- |
| L0a / L0b | window-lifetime group, basic set (architecture, hygiene, smoke) and the core tests (`--core-shard 0`); then the single-instance hand-over (`--areas release --only ...`, so the curated release tests do not repeat) | first, alone |
| P1 | engine, playback, ui | parallel |
| P2 | guitarpro, interactions, document-operations, persistence, document-context, synthetic | parallel |
| P3 | settings, notation, midi, leaks, fuzz, tutorial | parallel |
| W | the workflow area without the monkey | parallel |
| M1..M2 | `TestWorkflowMonkey` seed ranges (`--monkey-first` / `--monkey-seeds`): 8 seeds in 2 shards of 4; with `TABFORGE_MONKEY_FULL=1` 20 seeds (the serial default) in 4 shards of 5 | parallel |
| R | recording, audioaudit, bench | last, alone |

R runs last and alone because the recording tests use real-time audio and failed under load before (an older run: master tap peak 0); the other lanes only compete for CPU, R would also compete for audio callbacks. Every lane except L0a passes `--core-shard 1`. At start the script checks that every area in the AreaOf tables is in exactly one lane, so a new area fails the run until it is placed in the lane table at the top of the script.

Options: `-Lanes L0,P3` (subset; the merged gate is then not enforced), `-DryRun` (lane table, exact commands and coverage check, nothing built or run), `-Quick` (skips R and the monkey), `-MaxParallel n` (default min(4, CPUs/3)), `-Timeout minutes` (default 45 per child). Measured: `-Lanes L0,P3` reported 1174 passed; the serial runner on the same areas (`--areas window-lifetime,basic,settings,notation,midi,leaks,fuzz,tutorial`) reports 1167, which is L0a (888) plus P3 (279); L0b adds the 7 hand-over checks.

CI is unchanged (historical: the upstream windows-ci workflow; this checkout has no CI config): it runs the full suite serially in one step of `build-and-selftest` (20-minute job limit, `FULL_SUITE=true` on schedule and manual dispatch). To use the lanes there, replace the `--require ci` run in that step by a call to the `run-suite` script and raise `timeout-minutes`; the script builds its own copy, so the Build step could then be dropped. The script writes its logs to `$env:TEMP`, so the upload step would need `path: $env:TEMP/tf-suite-*.log`. That change needs a CI run to confirm and is left to the owner.
