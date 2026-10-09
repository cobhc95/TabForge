# Windows, tabs and documents

Index: [Feature map](../FEATURE_MAP.md). Generated test tables: [tests.md](tests.md).

- **Main code:** `src/TabForge/Documents/` (`DocumentSession`, `DocumentController`, `DocumentSaveFlow`, `DocumentCloseFlow`, `DocumentPlacement`); `src/TabForge/Shell/` (`TabWindowRegistry`, `OwnedSubscriptions`); `MediaContext`
- **Pathway to use:** One `DocumentSession` per song. Subscribe through `OwnedSubscriptions`; open songs through `DocumentPlacement`; save through `DocumentSaveFlow`.
- **Tests:** groups window-lifetime, document-context, document-operations (`--areas window-lifetime`, `--areas document-context`, `--areas document-operations`); `TestWindowLifetime`, `TestDocumentContext`, `TestDocumentOperations`, `TestDocuments`
