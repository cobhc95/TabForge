# SelfTests

The **basic set** of the self-tests, run with `TabForge.exe --selftest <log>`: the architecture and hygiene checks and a few smoke tests and the essential-action smoke checks (`Smoke/SelfTestEssential*.cs`, helpers `Sm*` in `SelfTestEssentialKit.cs`), partial files of `SelfTest`. The big full suite lives in `tests/full-suite/` and is compiled in only with `-p:TabForgeFullSuite=true`.

## How to change me
1. Entry files: `SelfTest.cs` (the AreaOf table), `tests/full-suite/SelfTestFullSuite.cs`.
2. Owner class: `SelfTest` runner; new tests go to `tests/full-suite/<topic>/`.
3. Tests to run: `--areas basic`; then your test with `--only` (full-suite build), plus `--areas architecture,hygiene` (see the test box in `docs/TESTING.md`).
4. Docs to update: `docs/feature-map/tests.md` via `--feature-map`, `docs/TESTING.md`.

## Key types
- `SelfTest`: the runner; Guard wraps each test so one failure does not stop the run.
- `SelfTestOnly.cs`: `--only` and `--areas` selection.
- `SelfTestRequirements.cs`: `--require` groups such as `ci` and `document-context`.
- The AreaOf table in `src/TabForge/SelfTests/SelfTest.cs` gives every test an area.

## Pathway
- Add a narrow test of a feature to `tests/full-suite/<topic>/`, register it in `tests/full-suite/SelfTestFullSuite.cs` and give it an area there. Only tests every rebuild must run belong here, registered in `SelfTest.cs` under the areas `smoke`, `hygiene` or `architecture`.
- After adding or renaming tests run `TabForge.exe --feature-map` and commit `docs/feature-map/tests.md`.
- Run one test with `--only <TestName>`; the log's last line is the result.

## Must not depend on
Nothing depends on the tests. Architecture budgets live in `src/TabForge/ArchitectureBudget.json`.

## Tests
`TestEveryTestHasAnArea`, `TestFeatureMapInSync`, `TestOnlyOption`, `TestRequiredGroupGate`, `TestArchitectureGuards`, `TestSourceControlCharacters`, `TestStartHereAndRecipesInSync`.
