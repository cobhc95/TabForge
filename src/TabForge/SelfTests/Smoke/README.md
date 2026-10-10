# SelfTests/Smoke

Smoke checks of the essential actions, run in the basic set: editing, files, windows and playback, plus the model round trip. The `SelfTestEssential*` files are partial files of `SelfTest`, and `SelfTestEssentialKit.cs` holds their shared helpers. Does not own the features they check.

## Files
| File | Purpose |
| --- | --- |
| `SelfTestEssentialEditing.cs` | Notes, duration, undo and redo, delete, copy and paste, tracks, bar ranges, sections and clips |
| `SelfTestEssentialFiles.cs` | Start with or without a file, a second file, tabs, close with unsaved changes, save, reopen, autosave |
| `SelfTestEssentialKit.cs` | The `Sm*` helpers: a hidden main window and real waits on conditions |
| `SelfTestEssentialWindows.cs` | Playback on a silent engine, the windows and prompts, and the exports |
| `SelfTestSmoke.cs` | Basic-set smoke tests (model round trip, project save and load, one editor entry) and helpers the full suite shares |

## Pathway
`START_HERE.md`, Run one test. A new essential check goes in the matching file here and gets an area in the AreaOf table in `../SelfTest.cs`.

## Tests
`--areas smoke`; list in `docs/feature-map/tests.md`.
