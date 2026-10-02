# SelfTests

The **basic set** of the self-tests, run with `TabForge.exe --selftest <log>`: the architecture and hygiene checks and a few smoke tests (the smoke tests), partial files of `SelfTest`. The big full suite lives in `tests/full-suite/` and is compiled in only with `-p:TabForgeFullSuite=true`.

## Key types
- `SelfTest`: the runner; Guard wraps each test so one failure does not stop the run.
- `SelfTestOnly.cs`: `--only` and `--areas` selection.
- `SelfTestRequirements.cs`: `--require` groups such as `ci` and `document-context`.
- The AreaOf table in `src/TabForge/SelfTests/SelfTest.cs` gives every test an area.

## Pathway
- Add a narrow test of a feature to `tests/full-suite/<topic>/`, register it in `tests/full-suite/SelfTestFullSuite.cs` and give it an area there. Only tests every rebuild must run belong here, registered in `SelfTest.cs` under the areas `smoke`, `hygiene` or `architecture`.
- After adding or renaming tests run `TabForge.exe --feature-map` and commit `docs/FEATURE_MAP.md`.
- Run one test with `--only <TestName>`; the log's last line is the result.

## Must not depend on
Nothing depends on the tests. Architecture budgets live in `src/TabForge/ArchitectureBudget.json`.

## Tests
`TestEveryTestHasAnArea`, `TestFeatureMapInSync`, `TestOnlyOption`, `TestRequiredGroupGate`, `TestArchitectureGuards`, `TestSourceControlCharacters`, `TestStartHereAndRecipesInSync`.
