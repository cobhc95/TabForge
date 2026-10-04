# Documents

One `DocumentSession` per open song: edits, undo, save, close and where a song opens.

## How to change me
1. Entry files: `DocumentEdits.cs`, `DocumentSaveFlow.cs`.
2. Owner class: `DocumentSession`; edits via `DocumentEdits.Run`, saves via `DocumentSaveFlow`.
3. Tests to run: `--areas document-operations,document-context` (full-suite build), plus `--areas architecture,hygiene` (see the test box in `docs/TESTING.md`).
4. Docs to update: this README, `CHANGELOG.md`.

## Key types
- `DocumentSession`: the open song, its undo history and its `MediaContext`.
- `DocumentEdits`: `DocumentEdits.Run` is the only way to change a song.
- `DocumentSaveFlow`, `DocumentCloseFlow`: the save and close steps and their questions.
- `DocumentPlacement`: new tab, replace, or beside an unsaved one.
- `UndoHistory`, `UndoController`: undo and redo.
- `DocumentManager`: the open sessions of a window.
- `ScoreImportQueue`: background import of songs.

## Pathway
- Edit: `DocumentEdits.Run(session, mutate)` gives one undo step, one dirty change and one timeline invalidation.
- Save and close: a window answers the questions through dialogs, a test through `ISaveInteractions`.
- Audio files of a song are reached through its session, never through a window or the focused tab.

## Must not depend on
WPF assemblies, except the listed `TabItemModel` exception (`TestArchitectureLayering`). Must not find a document through a window or a global (`TestArchitectureDocumentOperations`).

## Tests
`TestDocumentOperations`, `TestDocumentContext`, `TestDocuments`, `TestUndoController`, `TestUndoDeltaStates`, `TestSaveTransactions`, `TestAsyncSaveSequencing`, `TestClipAndSectionEdits`, `TestClosedDocumentChainsReleased`.
