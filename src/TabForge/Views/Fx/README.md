# Views/Fx

Helpers of the FX chain window (`Views/FxChainWindow.cs`). The window keeps the undo step, the engine calls and the plug-in pane; everything about the chain list and containers lives here.

## Files
| File | Purpose |
| --- | --- |
| `FxChainList.cs` | The list's rows as a selection (click, Ctrl / Shift+click, rubber band, Esc, the focused row) and the drag of the selected rows (the lifted rows sit in a gap where they would land); reports a drop, changes no chain |
| `FxChainDropTarget.cs` | Where a drag would drop for a pointer position (middle 60% of a container row: into it; its top or bottom 20%: in front of or behind it; at an open container's end the pointer's horizontal position chooses inside or behind), the insertion line (indented inside a container, full width outside) and the "Drop into …" badge |
| `FxChainRows.cs` | What a plug-in row and a container row show; rows under a collapsed container are left out |
| `FxChainContainers.cs` | The container pane, the container actions with their keys and right-click menu, and what a drop does to the chain's tree |
| `FxContainerPresets.cs` | Container presets: the drop-down and Save preset… in the container pane, a container written as a preset file (`Containers/*.tfcontainer`, a packed plug-in list like an FX chain) and loaded into the shown container in place as one undo step; plug-ins that are not trusted here are left out and listed |
| `FxPresetMenus.cs` | The "+" preset menu and the Chains menu (saved chains as organisable rows, save, export for other hosts, Manage presets…) |
| `FxListSplitter.cs` | The resizable width of the list |
| `PluginEditorFit.cs` | Sizing and scrolling of the docked plug-in editor |
| `PluginPresetDialog.cs` | The name question of Save preset |

## Pathway
A chain is the flat `RigPreset.Plugins` list plus `RigPreset.Containers` (`Plugins/ChainContainers.cs`). Every change goes through `FxChainWindow.EditTree`, which calls `ChainTree.Edit`: the change edits the tree, the flat list, or both, and the containers are written for the result. A row's tag is its `PluginSlot` or `ChainContainer`.

## Keys in the window
Ctrl+G: put the selected rows in a new container. Ctrl+Shift+G: dissolve the container. F2: rename the focused container. Delete: remove the selection (a container goes with its plug-ins, after one question).

## Tests
`TestFxChainListSelection`, `TestFxChainContainerRows`, `TestFxContainerPresets`, `TestChainContainers`, `TestRfxChainExport`. Capture step: `fx-list`.
