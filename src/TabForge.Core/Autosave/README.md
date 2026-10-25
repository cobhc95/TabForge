# Autosave

Copies dirty songs to the Recovery folder in the background. UI thread only at the edges.

## How to change me
1. Entry files: `AutosaveRunner.cs`, `RecoveryCopyWriter.cs`, `AutosaveRegistry.cs`, `AutosaveHealth.cs`.
2. Owner class: `AutosaveRunner` (one pass at a time; `RunPassAsync` starts on the UI thread).
3. Tests to run: `MainWindowAutosaveTests`.
4. Docs to update: `CHANGELOG.md` (list user-visible changes).

## Key types
- `AutosaveRunner`: pass orchestration; `Health`/`Busy` are UI-thread state, workers do the copies.
- `RecoveryCopyWriter`: bounded, cancellable copy writes; `AutosaveRegistry`: which songs are dirty.

## Must not depend on
UI frameworks; the audio engine. Cheap `Project.IsDirty` flag on the UI thread, never a content hash.
