# Testing

TabForge has one headless test suite, built into the executable. It needs no audio device, no installed plug-in and no song files. CI runs it on every push.

## Basic set and full suite

A normal build contains the **basic set**: the architecture and hygiene checks and a few smoke tests. It runs in seconds and is what every rebuild runs. The **full suite** (`tests/full-suite/`, see its README) is compiled in only with `-p:TabForgeFullSuite=true`; CI builds it that way, and so does the maintainer's local rebuild script (`fullsuite` option). The sections below describe the full suite; to run any test of it, build with that switch.

## Run it

From the repository root (the tests find the source tree from the working directory), after a Release build made with `-p:TabForgeFullSuite=true` (a normal build has only the basic set and does not know the gate groups below):

```
src\TabForge\bin\Release\net8.0-windows\TabForge.exe --selftest selftest.log --require ci
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

Areas: `ui`, `settings`, `persistence`, `guitarpro`, `playback`, `recording`, `engine`, `midi`, `notation`, `synthetic`, `fuzz`, `tutorial`, `window-lifetime`, `document-context`, `document-operations`, `architecture`, `interactions`, `leaks`, `audioaudit` and `hygiene`. While you work, run the area you touched (on a full-suite build) plus `architecture,hygiene`; the whole suite runs only for major redesigns and releases.

## The gate (`--require ci`)

`--require ci` makes these groups mandatory: `gp-fixtures`, `synthetic-fixtures`, `fuzz`, `long-import`, `window-lifetime`, `gp-fidelity`, `document-context`, `document-operations`, `architecture`, `interactions`, plus the `source-hygiene` and `installer-parity` checks. A mandatory group fails the run when it did not run, when one of its tests threw, or when it executed fewer checks than its recorded minimum, so a deleted or emptied test cannot pass. The registry is in `src/TabForge/SelfTests/SelfTestRequirements.cs`.

The `interactions` group drives real windows through the entry points the interface uses (keys, commands, the window's own handlers) and checks the song, undo and dirty state, tab marks, playhead and glitch counters across editing, saving, tab switching, tab transfer, plug-in changes and recording while the song plays (`tests/full-suite/Lifecycle/SelfTestInteractions.cs` and the files beside it). A scenario that exposes a known defect reports it as `KNOWN` with its trace id instead of failing; `--require interactions-known` turns those into required checks, so the change that fixes a defect can require it.

## Architecture budget

The `architecture` group compares the compiled assemblies and the source tree with `src/TabForge/ArchitectureBudget.json` (layering, class and method size, mutable statics, comment style, and document-state ownership). A measured value above its budget fails. A value below its budget logs the line to paste; with `TABFORGE_ARCH_STRICT=1` (or `strict-lower`), the setting for merge runs, it fails instead, so the file always matches the code. A budget is raised only by an explicit edit that states the reason in the commit message. `TABFORGE_ARCH_RECORD=<file>` writes the measured values as a new budget file. Size checks on IL bytes run on optimised (Release) builds only.

## Match the CI runner on your own PC

The CI runner has a small desktop (about 1024 x 768) at 100% scale; a developer PC is often 4K at 150%. Windows clamps a top-level window that is larger than the screen, so a layout test written on a big screen can pass locally and fail on the runner. Every test window is opened by `ShowTestWindow`, which fails the test with a clear message when the window is larger than the work area. With `TABFORGE_SELFTEST_SMALLSCREEN=1` the work area is the runner's, so the same failure shows up on your PC. CI sets the variable. The display scale itself cannot be changed from inside the process: lay controls out at explicit sizes instead (see `FitStage` in `tests/full-suite/Editor/SelfTestTrackListFit.cs`). The guard is `tests/full-suite/Views/SelfTestScreen.cs`.

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
| shipped drum maps; readable text tokens | the application's resource files are not next to the test executable | never in a normal build |
| a child process in the job ends when the job closes | `ping.exe` was not found | a stripped system |

Checks that used to be skipped on a clean checkout now run on generated stand-ins:

- .gp compatibility on "sample songs" runs on the generated fixture songs (written as `.gp` files in a temporary folder and imported). The checks that describe a mixed, played real song (mixer values differ from the defaults, no drone longer than 5 s, note count equal to the parser's, note events for the fretboard) apply to real songs only.
- Tuplet import reads a generated song with triplets and quintuplets.
- The save-and-reopen round trips run on a generated dense song.
- VST2 recognition reads a TabForge-generated PE file with an export table instead of an installed third-party plug-in.
- The per-edit undo capture on the largest song is a relative check (at least 4 times cheaper than the old full snapshot, measured in the same run), not an absolute time, so a slow machine cannot fail it and it never needs a skip.

When a local song folder exists, the real files are used instead of the stand-ins.

## Writing a test

- A test is a static method of the `SelfTest` partial class (`tests/full-suite/**/SelfTest*.cs`, and the basic set in `src/TabForge/SelfTests/`), registered in `RunFullSuite` or `SelfTest.Run` with `Guard(TestName)` (`GuardGroup("<group>", TestName)` for a mandatory group) and mapped to an area in the `AreaOf` table.
- Use `Check(name, condition, detail)`, `Eq(name, expected, actual)` and `Skip(name, reason, requirement)`.
- Do not depend on the screen: no top-level window larger than 1024 x 728; measure and arrange a control at an explicit size instead.
- Do not depend on local files: generate the song in code.
- Do not assert an absolute time: compare against a baseline measured in the same run.
- Text files in the repository have no control characters other than tab, CR and LF (a hygiene test checks).
- Documentation is tested too: every file, script and command a public document names must exist in the repository and be part of the public tree, and version claims must match `Directory.Build.props` (`src/TabForge/SelfTests/Hygiene/SelfTestDocsConsistency.cs`).
