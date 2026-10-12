# Views/Presets

Organising the user's presets inside the preset drop-downs themselves: in the open list every row of a user's preset shows a grip, the name and rename and delete buttons (a copy is made in the preset manager); built-in rows (a plug-in's own programs, shipped presets and maps) stay plain. Every list ends with "Manage presets…", which opens the preset manager.

## Files
| File | Purpose |
| --- | --- |
| `PresetRows.cs` | The organisable rows of a drop-down (`PresetRows.Attach` on the ComboBox or menu, `Item` for a row, `AddManage` for the last row): inline rename with the name rules, the questions, reorder by the grip or Alt+Up / Alt+Down, F2 / Delete on the highlighted row; a button or the grip never picks the preset |
| `PresetManager.cs` | `PresetManager.Open(owner, kind, key)` and `StoreOf(kind, key)`: the store of each `PresetKind` without a window instance, and opening the one preset manager window (an open one comes to the front and jumps to the node) |
| `PresetManagerWindow.cs` | The preset manager: the tree of kinds with counts, the toolbar (Rename, Duplicate, Move up, Move down, Delete), the list of the node's presets with dates and an extended selection (Ctrl or Shift click, Ctrl+A: one delete question for several, the moves as one block, built-ins skipped), the search across every kind with Show in tree, and the keys. Every action runs through a `PresetManagePanel`, so the rules and questions are the drop-downs' own |
| `PresetCatalogue.cs` | What the manager lists: nodes, a node's presets (built-in ones first, marked), counts, search, the date text |
| `../../PresetManagement/PresetsFeatureModule.cs` | (namespace `TabForge.PresetManagement`, beside the other feature modules, so the registry in Services names no view) The Preset manager command (no default key) and the Tools > Preset manager… menu row |
| `PresetDeleteQuestion.cs` | The question before a delete, wherever one is asked: the text for one or several presets, the one decision whether to ask (the "Ask before deleting presets" setting) and storing the "Don't ask again" tick (Yes only) |
| `PresetManagePanel.cs` | The panel: rows, inline rename (F2, double-click, the pencil; Enter applies, Esc cancels), the overwrite and delete questions, reorder by the grip or Alt+Up / Alt+Down, the keys |
| `MidiProcessorPresetBar.cs` | The preset row in a MIDI processor's settings pane (drop-down, Save preset…): the built-in presets of the type (`MidiProcessorCatalog.BuiltInProcessorPresets`) before the user's, the closed box naming the preset whose values the processor has, and reading / writing a processor preset |

The panel reaches presets only through `IPresetStore` (`Plugins/PresetFolderStore.cs`). `PresetFolderStore` is the one implementation: a folder of files of one extension, with the order in `preset-order.txt` beside them (a folder without it lists alphabetically). `PresetRules` holds the name rules and the copy name.

## The four places
| Kind | Folder under the user's settings folder | Entry |
| --- | --- | --- |
| Plug-in presets | `Presets/<plug-in>/*.tfpreset` | FX chain window, the preset drop-down (`PresetFolderStore.Plugin`, `PluginKeys`) |
| FX chains | `FxChains/*.tfchain` | FX chain window, the Chains menu (`Views/Fx/FxPresetMenus.cs`; rename opens the panel, a menu does not take typing) |
| MIDI processing presets | `MidiPresets/*.json` | MIDI processing window, the Presets drop-down |
| MIDI processor presets | `MidiPresets/Processors/<type>/*.json` | MIDI processing window, the row under the processor's description (`MidiProcessorPresetBar`; `MidiProcessorTypes`) |
| Container presets | `Containers/*.tfcontainer` | `PresetFolderStore.Containers` |

The drum maps of the note map processor are a fifth place: `DrumMapStore` (`Plugins/DrumMapFiles.cs`) wraps a `PresetFolderStore` over the `DrumMaps` folder and keeps the name inside each file in step; its entries are the map drop-downs of `Views/NoteMaps`.

## Add another place
Create a `PresetFolderStore` factory for the folder and a `PresetKind`, call `PresetRows.Attach(dropDown, kind, ...)` before the drop-down's own SelectionChanged handler, add user rows with `rows.Item(text, tag)`, end the list with `rows.AddManage(items)`, and subscribe to `store.Changed` to list again.

## Tests
`TestPresetManagerWindow` also covers the multi-select (one question, block moves, built-ins skipped) and the "Don't ask again" setting. `TestPresetManagePanel` (the panel and the drop-down rows over a fake store, the note map's built-in preset), `TestPresetStoresOnDisk` (the four stores over real files in a scratch folder), `TestPresetManagerWindow` (the manager over a scratch profile with every kind). Capture steps: `preset-panel`, `preset-manager`.
