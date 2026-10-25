# Views

Framework-free view helpers living in Core (effect editors today). The Avalonia views live in `Source/src/TabForge.Avalonia/Views/`.

## How to change me
1. Entry files: `EffectEditors/OrnamentEdits.cs`.
2. Owner class: the helper named by the file.
3. Tests to run: `OrnamentEditorFlowTests`.
4. Docs to update: `CHANGELOG.md` (list user-visible changes).

## Key types
- `EffectEditors/OrnamentEdits`: ornament value edits over `Models`.

## Must not depend on
UI frameworks (that is why they can live here); anything outside `Models`/`Services` without a seam.
