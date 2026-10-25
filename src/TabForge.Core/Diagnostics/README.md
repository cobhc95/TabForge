# Diagnostics

Opt-in timing probes for expensive paths. Silent unless asked.

## How to change me
1. Entry files: `SlowTrace.cs`, `RenderPerformance.cs`.
2. Owner class: `SlowTrace` (timed UI-thread funnels to the `ui` trace); `RenderPerformance` (DEBUG-only latest-frame probes).
3. Tests to run: `TestTraceSwitchAreas`, `PlayheadTraceTests`.
4. Docs to update: `CHANGELOG.md` (only if an area name changes).

## Key types
- `SlowTrace`: full renders, row rebuilds, engine sync, score relayout timings.
- `RenderPerformance`: arrangement/score/drag/layout ticks and bytes, debugger-readable.

## Must not depend on
Anything measured (probes read, never own); nothing may log slowly on a realtime path.
