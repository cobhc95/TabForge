# Editing and notation

Index: [Feature map](../FEATURE_MAP.md). Generated test tables: [tests.md](tests.md).

- **Main code:** `src/TabForge/Models/` (`SongProject`, `TrackModel`, `MeasureModel`, `TabCell`); `src/TabForge/Services/` (`EditCommands`); `src/TabForge/Views/` (`TabEditorControl`, `StaffNotationRenderer`); undo in `UndoController`
- **Pathway to use:** A model edit is `DocumentEdits.Run` (one undo step, one dirty change, one timeline invalidation). Commands are bound through `HotkeyCatalog`.
- **Tests:** `--areas ui`, `--areas notation`; `TestEditCommands`, `TestEditorEntry`, `TestNotationLayout`, `TestEngravingCollisions`, `TestUndoController`
