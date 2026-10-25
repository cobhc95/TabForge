# Views/Presets: the preset drop-downs and the preset manager

The preset drop-downs are six `ComboBox`es in five windows (the FX chain bar, the FX container, the two in MIDI processing, note maps, and the
drum map editor). `PresetRows` attaches the organisable rows to any of them; the manager window is the one app-wide preset organiser. Both use
the Core `PresetRules` / `PresetManagePresenter` rules and an `IPresetStore`; neither owns where presets live.

| Class | Job |
|---|---|
| `PresetRows` | The organisable rows of one preset `ComboBox`: the two icon buttons of a user's row, the inline rename box with the name rules, the replace and delete questions, Alt+Up / Alt+Down reorder, F2 / Delete, and the trailing Manage row's selection guard. Attached with `PresetRows.Attach`; built-in and hint rows stay plain text. |
| `PresetRowIcons` | The Rename and Delete icon buttons of a user row: 30 DIP squares 8 DIP apart, the pencil and trash vector paths, the rest / hover / pressed looks from theme keys, the tooltip and the automation name. |
| `PresetRowText` | The shared row words and the trailing Manage row (one marker object, never a preset name). |
| `PresetManagerWindow`, `.Rows.cs`, `.Keys.cs` | The manager window: the tree of preset kinds and their children with counts, the toolbar, the list, the search, extended selection, inline rename and the keys. |
| `PresetManager` | The one place every Manage row opens the manager from: the single app-wide window, the kind and key it jumps to, and the delete setting. The opener is replaceable for tests. |
| `PresetManagerActions`, `PresetManagerDialogs`, `PresetDeleteQuestion` | The four changes (rename, duplicate, delete, move) through the store and Core rules, the questions behind settable members, and the one delete decision with the "Don't ask again" tick. |

Rules: built-in, hint and Manage rows never carry actions; one command change is one undoable store change; colours come from theme keys and
sizes from named constants.

Tests: `PresetRowsTests`, `PresetManagerWindowTests`, `PresetManagerOpenTests`, `NoteMapEditEntryTests`, `DrumMapEditorWindowTests`.
Run the Avalonia test project locally with `--filter "FullyQualifiedName~PresetRowsTests"` for the drop-down
rows, or `--filter "FullyQualifiedName~PresetManagerWindowTests"` for the manager.
