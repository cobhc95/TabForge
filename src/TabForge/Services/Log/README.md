# Services/Log

The opt-in debug trace (off unless turned on) and the always-on, bounded errors.log of swallowed errors.

## Files
| File | Purpose |
| --- | --- |
| `Trace.cs` | `Trace`: the opt-in debug trace switch and its output to the diagnostics folder; `Trace.Error` also feeds `ErrorLog` |
| `ErrorLog.cs` | `ErrorLog`: bounded `errors.log` (256 KB, rotates once to `errors.1.log`), repeat-suppressed, never throws; fed by `Trace.Error` |

## Pathway
`START_HERE.md`, Find a feature. The debug options are listed in `docs/DEBUGGING.md`.

## Tests
`docs/DEBUGGING.md` (the debug options); list in `docs/feature-map/tests.md`.
