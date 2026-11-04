# Testing

TabForge has two test suites. Neither needs an audio device, an installed plug-in or song files.

| Suite | Where | Use it for |
| --- | --- | --- |
| Self-test, built into the executable | `Source/src/TabForge.Avalonia/SelfTests/` | The quick check on every build and release |
| xUnit project | `Source/tests/TabForge.Avalonia.Tests/` | Behaviour tests while you work on an area |

## Self-test

`TabForge.exe` is a GUI-subsystem program, so it prints nothing. Start it, wait for it to exit, then read the log. The exit code alone is not the result: the last line must say `0 failed`.

```
Start-Process -Wait build\TabForge\TabForge.exe -ArgumentList '--selftest','selftest.log','--areas','basic'
```

Run it from the repository root, because the source-scanning checks read the folder they start in. Add `--profile <folder>` to keep your own settings untouched.

| Option | Effect |
| --- | --- |
| `--areas basic` | Architecture, hygiene and smoke checks, about 5 seconds. This is also what runs with no option, and what every release runs |
| `--areas release` | The basic set plus saving, recovery, import and input-limit checks. Optional: `Source/tools/Package-Release.ps1 -FullGate` runs it |
| `--areas a,b` | Those areas plus the tests with no area. The area of each test is in the area table in `SelfTests/SelfTest.cs` |
| `--only TestA,TestB` | Just the named tests. An unknown name fails before anything runs and lists the closest names |
| `--require ci` | Makes the gate groups mandatory: a group that did not run, threw or ran too few checks fails |
| `--timing <csv>` | Records the time of every test |
| `--group-report <json>`, `--merge-group-reports <a,b>` | Write each group's result, and merge reports into one verdict |
| `--core-shard <n>` | Runs the untagged tests only when `n` is 0 |

Lines starting with FAIL name the failing check and why. Lines starting with SKIP are checks that could not run here (a missing sound bank, no symbolic-link rights, or a slow machine); a skip never counts as a pass.

To add a test, see `Source/src/TabForge.Avalonia/SelfTests/README.md`. Generate songs in code, never depend on the screen or local files, and compare times only against a baseline measured in the same run.

`TABFORGE_SELFTEST_SMALLSCREEN=1` judges test windows against a 1024 x 728 screen, and `TABFORGE_NO_LOCAL_SONGS=1` ignores any local song folder.

## xUnit project

Run one class, not the whole project, while you work. `Source/src/TabForge.Avalonia/AREA-MAP.md` names the class for each area.

```
dotnet test Source\tests\TabForge.Avalonia.Tests --filter "FullyQualifiedName~ArrangementViewTests"
```

The local-only `test-watchdog` script (PowerShell, in `tools`) runs the whole project in parallel and stops a stuck run. Build first.
