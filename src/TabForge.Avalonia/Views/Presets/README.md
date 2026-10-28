# Views/Presets: the preset lists and the preset manager

Every preset list (the FX chain bar, the FX container, the two in MIDI processing, note maps, the drum map editor, the Articulations tab and the
bend / tremolo editors' "Your presets") gets its rows from `PresetRows`; the manager window is the one app-wide organiser. Both use the Core
`PresetRules` / `PresetManagePresenter` rules and an `IPresetStore`; neither owns where presets live.

| Class | Job |
|---|---|
| `PresetRows`, `.View.cs`, `.Drag.cs` | The rows of one preset `ComboBox` or `ListBox`: presets that come with the app under a "Built-in" header and the user's under "Yours", a drag grip (drop on another row to reorder), the pencil and bin (only under the pointer or on the selected row), the inline rename box with the name rules, the replace question, the delete done at once with an Undo message, Alt+Up / Alt+Down reorder, F2 / Delete, and the trailing Manage row's selection guard. Attached with `PresetRows.Attach`. |
| `PresetRowIcons` | The 14 DIP pencil, bin and grip: theme ink, no box behind them, accent (pencil) or danger colour (bin) under the pointer; the tooltip and the automation name. Sizes come from `DialogMetrics`. |
| `PresetUndoToast` | The timed "Deleted ... Undo" message over the bottom of a window (one at a time). The restore itself is Core `PresetDeleteUndo`. |
| `PresetRowText` | The shared row words and the trailing Manage row (one marker object, never a preset name). |
| `PresetManagerWindow`, `.Rows.cs`, `.Keys.cs`, `.Drag.cs`, `.Portable.cs` | The manager: the category tree (FX chains ... Articulations, Bend / tremolo) with counts, the toolbar (Rename, Duplicate, Move, Delete, Import…, Export…), the list (built-in rows greyed and tagged, a grip on the user's rows), the search, extended selection, inline rename, drag reorder, the keys and the Undo message. |
| `PresetManager` | The one place every Manage row opens the manager from: the single app-wide window, the kind and key it jumps to, and the stores it was configured with. The opener is replaceable for tests. |
| `PresetManagerActions`, `PresetManagerDialogs` | The changes (rename, duplicate, delete with undo, move, drop, import, export) through the store and Core rules, and the one question (replace). |

Rules: built-in, hint and Manage rows never carry delete or rename; one command change is one store change; deleting asks nothing and offers
Undo; the saved order is the store's (`preset-order.txt` beside a folder's presets, the list order of the settings for effect presets), so a folder
without one still lists alphabetically; colours come from theme keys and sizes from named constants; the pointer shape never changes.

Tests: `PresetRowsTests`, `PresetUndoTests`, `PresetManagerWindowTests`, `PresetManagerOpenTests`, `NoteMapEditEntryTests`, `DrumMapEditorWindowTests`.
Run the Avalonia test project locally with `--filter "FullyQualifiedName~PresetRowsTests"` for the rows, or `--filter "FullyQualifiedName~PresetManagerWindowTests"`
for the manager.
