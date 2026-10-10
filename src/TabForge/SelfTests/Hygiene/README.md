# SelfTests/Hygiene

Repository hygiene checks: documents agree with the code, feature map is current, no stray control characters, no unused screenshots. Does not own the documents themselves.

## Files
| File | Purpose |
| --- | --- |
| `SelfTestDebuggingDoc.cs` | The window options documented for debugging are registered in `App.xaml.cs` |
| `SelfTestDocImages.cs` | Every screenshot and animation is named by some Markdown file; no unused images |
| `SelfTestDocsConsistency.cs` | Public documents: repository links, version claims and folder README links stay consistent |
| `SelfTestFeatureMap.cs` | The feature map agrees with the code: the index links every page and the generated tests page is current |
| `SelfTestGuardrails.cs` | The mojibake allow list (files that may hold a mojibake-looking sequence; empty today) |
| `SelfTestSourceHygiene.cs` | Sources hold no control characters (a scripted edit once turned a Windows path's escape into a form feed) |
| `SelfTestStartHere.cs` | `START_HERE.md` and `docs/RECIPES.md` name only paths and types that exist; folder READMEs exist, are short and are linked |

## Pathway
`START_HERE.md`, Guard rules (Docs and Text) and Run one test. Regenerate the feature map with `--feature-map` after a test change.

## Tests
`--areas hygiene` (basic set); list in `docs/feature-map/tests.md`.
