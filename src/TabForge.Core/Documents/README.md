# Core/Documents

The answer to the save-changes question, the open song, the edit transaction and the save sequence: no WPF, no app types. The tab strip's settings DTO lives with the other settings in `Services/TabSettings.cs`.

| File | Owns |
|---|---|
| `DiscardAnswer.cs` | The answer for one document with unsaved changes: close without saving, keep it open, or save first (`Presenters/SavePromptPresenter.cs` maps the buttons to it). |
| `DocumentEdits.cs` | `DocumentEdits.Run`: one logical edit of one document is one undo step, one dirty change and one timeline invalidation; undo, redo and checkpoints. |
| `GlobalRetune.cs` | `GlobalRetune.Apply`: the Global tuning retune of every pitched track by a per-string change, as one undo step, and the status line for the new tuning. |
| `IEditableDocument.cs` | What `DocumentEdits` needs of a document (song, undo stack, state-tied tuning and skip memory, clean baseline); `SongDocument` and the app implement it. |
| `SongDocument.cs` | One open song without playback: score, file path, undo, the unsaved-changes baseline, `MediaContext` and per-tab editor state; the app's `AvaloniaPlaybackSessions` adds playback, the Avalonia window uses it as is. |
| `DocumentController.cs`, `DocumentSaveFlow.cs` | Opening a score into a document, and the save and export sequences of one explicit document (questions through `ISaveInteractions`, plug-in states through a collector, the write through `ProjectService` / `FilePathPolicy`). |
| `UndoController.cs`, `UndoHistory.cs` | The per-document undo and redo stacks and their limits. |
| `ProjectState.cs`, `TrackState.cs`, `StateChunk.cs`, `ProjectStateEncoder.cs`, `BarWriter.cs`, `BarReader.cs`, `BarCodec.cs` | The compact song states the undo stacks store: one immutable state (`ProjectState`), its track level, the shared byte chunks, the encoder that captures and restores them, and the binary bar form (`BarCodec`, with `BarWriter` and `BarReader`). |
| `DocumentRoutingDecision.cs` | The WPF-free rules for opening a song among the tabs: the new-tab insert index, whether a shown tab that plays is stopped before it is replaced, and how a placed song is shown (`PlacementActivation`). The window applies the answer (`MainWindow.Documents.cs`, `MainWindow.Tabs.cs`). |
| `PlacementKind.cs` | How an opened song was placed among the tabs (added, replaced, replaced all, opened beside a target to save first, kept). |
| `NewSongDefaults.cs` | The notation a new song starts with (the Preferences choice, else tab and staff) and its track-list grouping, from the settings (`NewSongDefaults.Apply`); used by the WPF new tab and the Avalonia tabs. |
| `ClipClipboard.cs` | The audio or MIDI clip copied on a clip lane: one per process, shared by every window, holding a private copy of the clip (`ClipClipboard.Copy`, `Clone`). |
| `ClipRelocation.cs` | Moves one audio or MIDI clip to another track or lane of the same kind (`ClipRelocation.Move`), used by the timeline clip drag. |
Does not own: the tab strip itself (`Source/src/TabForge.Avalonia/Views/`) or the settings file (`Source/src/TabForge.Core/Services/AppSettingsStore.cs`).
Tests: `TestSettingsFileSplitSnapshots`, `TestPreferencesCatalog`, `TestSaveFlowCharacterization`, `TestUndoDeltaStates`, `TestDocumentOperations`, `TabForge.Avalonia.Tests.MainWindowSaveTests`.
