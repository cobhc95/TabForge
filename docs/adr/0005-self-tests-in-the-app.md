# 0005. The self-tests ship inside the application

Status: accepted

## Context

Most behaviour lives in internal types, in the app's windows and in the executable's own start-up modes. A separate test project would need those types made public or visible, and could not exercise the real executable, the real windows or the engine and worker modes. A user or a packager should also be able to check a build.

## Decision

The suite lives in the app assembly as `SelfTests/<Area>/SelfTest*.cs` (a `SelfTest` partial class) and runs headlessly: `TabForge.exe --selftest <log> [--areas <list>] [--require ci]`. Tests are static methods registered in `SelfTest.Run`. `AreaOf` maps tests to areas for fast focused runs, and `GuardGroup` names groups with a minimum check count (`Source/src/TabForge.Avalonia/SelfTests/SelfTestRequirements.cs`). `--require ci` makes the groups mandatory, so a deleted or emptied test fails the run. The packaging script re-runs the suite on the exact files it packages.

## Consequences

- Tests reach internals directly and drive real windows and processes.
- The test code ships in the executable. This is accepted for the access it gives.
- The result is a log line (`N passed, M failed, K skipped`), not an exit code alone.
- Tests must not depend on the screen, local files or the clock; hygiene tests enforce part of that, and skips are budgeted (`Source/docs/TESTING.md`).
