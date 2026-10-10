# SelfTests/Architecture

Layering and size guards: no WPF in the model, playback and services layers, and the size budgets of `src/TabForge/ArchitectureBudget.json`. Does not own the runner (`../SelfTest.cs`).

## Files
| File | Purpose |
| --- | --- |
| `SelfTestArchitecture.cs` | The WPF assemblies that the model, playback and services layers must not use |
| `SelfTestArchitectureBudget.cs` | Reads and compares `ArchitectureBudget.json`: a value above its budget fails; budgets only go down |
| `SelfTestArchitectureMetrics.cs` | The measurements behind the guards: reflection over the compiled assemblies and source scans |

## Pathway
`START_HERE.md`, Guard rules (layering and size budgets) and Run one test.

## Tests
`--areas architecture` (full-suite build for the rest); list in `docs/feature-map/tests.md`.
