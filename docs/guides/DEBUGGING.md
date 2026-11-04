# Debugging TabForge

How to run one test, one area, the CI gate, and the headless diagnostics. `Source/docs/guides/TESTING.md` explains what the suite covers; this page is the command reference. `TabForge.exe` is a GUI-subsystem program and prints nothing to the console, so every command below writes a file; read that file.

Run every command from the repository root (the source-scanning tests read the folder they start in) and give it a scratch settings folder with `--profile <folder>` so your own settings stay untouched. A run that cannot start exits with 2, a run that found a problem with 1, success is 0.

For the route from a symptom to its first file, test and log, see `Source/docs/guides/DEBUG_SYMPTOMS.md`.

## Run one test

```
Start-Process -Wait build\TabForge\TabForge.exe -ArgumentList '--selftest','one.log','--only','TestBarSlots'
```

- `--only <TestName>[,<TestName>...]` runs just the named tests. A name is the test method as written in the `Guard(...)` or `GuardGroup(...)` call in `Source/src/TabForge.Avalonia/SelfTests/SelfTest.cs` (basic set). Names are exact and case-sensitive. The maintainer's local rebuild script runs the named tests when `TABFORGE_SELFTEST_ONLY=TestA,TestB` is set.
- The log ends with the usual line, `TabForge self-test: N passed, M failed`. Search the log for `FAIL`; the exit code alone is not the result.
- An unknown name fails the run before any test starts and lists the closest registered names.
- `--require <group>` works with `--only` only when every test of that group is named; otherwise the log says so.

## Run one area

- `--areas ui` (or `--areas settings,persistence`) runs those areas plus the tests that have no area; leave it out, or use `all`, for everything. The area names and the test-to-area table are in `Source/docs/guides/TESTING.md` and `Source/src/TabForge.Avalonia/SelfTests/SelfTest.cs`.
- Tests that take longer than a second are timed in the log (`time  <test> [<area>] <seconds>`).
- `--require ci` makes the gate groups mandatory: a group that did not run, threw or ran too few checks fails the run.

## Small-screen mode

Set `TABFORGE_SELFTEST_SMALLSCREEN=1` and your PC judges test windows by the same screen, so a layout that fits your monitor but not CI fails locally too. `TABFORGE_TEST_SINGLE_INSTANCE=1` is reserved for `TestSingleInstanceProcessHandover`; it enables a profile-scoped pipe only with a non-real `--profile`.

## Process modes

Each mode is the first argument. They open no window and write bounded, path-checked output. The list below is checked against the modes `Source/src/TabForge.Avalonia/App/Program.cs` routes by a self-test, so it cannot drift.

| Command | What it does |
| --- | --- |
| `--selftest <log>` | The built-in test suite (see above). |
| `--audio-engine <session> <pid>` | Internal. The audio engine process that plug-in playback runs in. TabForge starts it; do not run it by hand. |
| `--plugin-host <id> <enginePid> <rate> <block> <1\|0> <format> <path> [sha256\|none]` | Internal. Runs one plug-in per process for the audio engine, so a crashing plug-in ends only its process. |
| `--plugin-info <path> <sha256>` | Internal. Loads one approved plug-in in a throwaway process and prints `role|vendor|name`; exit 5 when the file is missing or changed. |
| `--import-worker <pipe> [parentPid]` | Internal. Runs one song import out of process: one request, one reply, then exit. TabForge kills it on cancel or timeout. |

## Self-test options and traces

- `--timing <csv>`: with `--selftest`, writes the time of every test to a CSV file.
- `--core-shard <n>`: with `--selftest`, runs the untagged (core) tests only for shard 0 or when no shard is given, so CI can split the tagged tests across shards.
- `--group-report <json>` and `--merge-group-reports <list>`: write the per-group result of a run, and merge several such reports into one verdict.
- `errors.log`: always on, no switch. Every swallowed error from a catch (`Trace.Error`) goes to `errors.log` in the diagnostics folder (256 KB, then `errors.1.log`); identical lines within 1 s are counted, not repeated. Render and pointer-move paths stay silent.
- `TABFORGE_TRACE=playback`: after every seek, loop wrap or resync the trace line `restore@<ms>ms` lists each channel's program (`prog`), volume (`cc7`) and pan (`cc10`) just sent; check it when a mix-table change seems lost after a jump. The same switch also writes the playhead trace (`PlayheadTrace`): one `bar` line per performed bar with the performed position and the source bar, the score's bar and lit-note count against the expected count and the fretboard's count, the bars the timeline shows and the late note-ons; and one `frame` line per frame for 1 s after each start or jump (drawn and engine ms, engine bar, output latency, audio engine running and its latency ticks).
