# Full self-test suite

The archived, rarely used **full suite**: about 5,800 checks (editor, notation, playback, engine, persistence, Guitar Pro, window lifetime, fuzzing). Every file is a partial of the `SelfTest` class, like the basic set in `src/TabForge/SelfTests/`.

## When to use it
- A major architecture or framework redesign (window and document lifetime, engine and threading, persistence formats, the UI framework).
- CI and, on request, release packaging (`tools/Package-Release.ps1 -FullSuite`).
- To run one narrow test of a feature you changed (`--only <TestName>`).

Everyday work does not run it: a normal build and every `REBUILD.cmd` run only the basic set (about 550 checks, seconds), plus the narrow tests you name.

## How to build and run it
It is compiled in only with the MSBuild switch `TabForgeFullSuite` (this adds `tests/full-suite/**/*.cs` and the `FULL_SUITE` define):

```
dotnet build src\TabForge\TabForge.csproj -c Release -v q -p:TabForgeFullSuite=true -o out-full
Start-Process -Wait out-full\TabForge.exe -ArgumentList '--selftest','full.log','--require','ci,document-context'
```

- Everything: `REBUILD.cmd auto fresh fullsuite` (a separate test build, then the playtest; the launched app stays the normal build).
- Narrow: `--only TestA,TestB`, or `TABFORGE_SELFTEST_ONLY=TestA,TestB` with `REBUILD.cmd`.
- One area: `--areas ui` (area names and every test: `docs/feature-map/tests.md`). `--areas basic` selects only the basic set.

## Layout
- `SelfTestFullSuite.cs`: the registrations (`RunFullSuite`) and the area table of these tests. The basic set's are in `src/TabForge/SelfTests/SelfTest.cs`.
- `SelfTestCore.cs` and the topic folders (`Editor/`, `Engine/`, `GuitarPro/`, `Lifecycle/`, `Notation/`, `Persistence/`, `Playback/`, `Recording/`, `Settings/`, `Views/`, `Fuzz/`): the tests.
- `Lifecycle/SelfTestWindowLifetime.cs` contains the shared fixture and subscription/automation lifetime scenarios; `SelfTestWindowLifetimeCloseAndTransfer.cs` contains close, transfer, preferences and engine-chain scenarios.
- Helpers shared with the basic set (`NewEditor`, `TwoBarSong`, `BuildRichProject`, `PumpUi`, the headless engine kit in `src/TabForge/SelfTests/Engine/`) stay in `src/`.
- Full-suite-only diagnostic commands (`--gp-compare`, `--roundtrip-diff`, `--write-gp-fixture` and the like, `--pair-save-probe`) exist only in a full-suite build.
- `--areas workflow` runs the editing stories (`TestWorkflow`, seconds; `Editor/SelfTestWorkflow.cs`, helpers in `Editor/WorkflowKit.cs`) and the seeded human monkey (`TestWorkflowMonkey`, about 7 minutes); `--monkey-seeds N` and `--monkey-actions N` scale the monkey (default 20 x 1300) and `--monkey-first N` starts at seed N to replay one seed. A failure names the seed, step and last 15 actions.
- `TestSingleInstanceProcessHandover` launches two isolated app processes to check an Explorer-style file handover and shutdown after the final window closes; it uses a generated `.tforge` fixture.

Adding a test: write it here, register it in `SelfTestFullSuite.cs`, give it an area, run `TabForge.exe --feature-map` and commit `docs/feature-map/tests.md`.
