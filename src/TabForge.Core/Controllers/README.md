# Controllers

Small document-level controllers: selection looping and selection sync across views.

## Controllers vs Presenters vs Shell
- Controllers own one document behaviour (no UI types, no window chrome).
- Presenters own a window/dialog's logic (choice lists, texts, rules) for a view to render.
- Shell owns window chrome rules (tabs, drag, hit-testing).

## How to change me
1. Entry files: `SelectionLoopController.cs`, `SelectionSync.cs`.
2. Owner class: the controller named by the file.
3. Tests to run: `SelectionLoopLinkTests`.
4. Docs to update: `CHANGELOG.md` (list user-visible changes).

## Key types
- `SelectionLoopController`, `SelectionSync`: read `Documents`/`Models`, write selection state.

## Must not depend on
UI frameworks; windows and views (talk to `Presenters`/`Shell` through callers, not directly).
