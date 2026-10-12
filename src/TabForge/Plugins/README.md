# Plugins

Plug-in discovery, trust, chain state and rigs. Plug-ins run only in isolated processes.

## How to change me
1. Entry files: `PluginTrust.cs`, `ChainStateStore.cs`.
2. Owner class: `PluginTrust` (trust), `ChainStateStore` (states); plug-ins stay in isolated processes.
3. Tests to run: `--areas persistence,settings`; plug-in trust tests via `--areas release` (full-suite build), plus `--areas architecture,hygiene` (see the test box in `docs/TESTING.md`).
4. Docs to update: `ARCHITECTURE.md` guarantees, `CHANGELOG.md`.

## Key types
- `PluginTrust`: records size, time, SHA-256 and signer; a changed file needs re-approval.
- `MidiProcessorCatalog` (lookup, search, defaults) with `MidiProcessorCatalog.Entries.cs` (the processor table).
- `VstScannerService`, `PluginCatalog`, `PluginLibrary`: scan and list plug-ins.
- `ChainStateStore`: plug-in states kept in checked files and cleaned up when unused.
- `PluginQuarantine`: plug-ins set aside after a failure.
- `PresetFolderStore` (`IPresetStore`, `PresetRules`): one folder of user preset files (plug-in presets, FX chains, MIDI processing presets, MIDI processor presets) in the user's order, with rename, duplicate, delete and move; the order is `preset-order.txt` beside the files. The preset manage panel (`PresetManagePanel`, in the Presets folder under Views) works through it.
- `DrumMapLibrary` (drum maps and the note table between two of them; a map may carry its own note-to-note table) with `DrumMapFiles.cs`: `DrumMapDraft` (the map editor's checks and the atomic write) and `DrumMapStore` (the user's drum maps folder as an `IPresetStore`). The editor itself is in the NoteMaps folder under Views.
- `AutoChains`, `RigPreset`: default chains and saved rigs.
- `ChainContainer`, `ChainTree` (`ChainContainers.cs`): containers of a chain as runs over the flat plug-in list (`RigPreset.Containers`; in a bare plug-in list they ride on the first plug-in as a "ChainContainers" property), bypass inherited by the plug-ins inside (`PluginSlot.Plays`), and the tree an exporter walks (`ChainTree.Tree`).
- `ReaperChainImporter`: reads `.RfxChain` chain files from another host (header, state and footer of each block; VST2 chunk or parameter list, VST3 component and controller state) and maps them onto the known plug-ins by file name. The FX chain window calls it through `RfxChainImportFlow`.
- `RfxChainExporter`: the inverse: writes a chain as the text of an `.RfxChain` file (FX chain for other hosts) from the slots, their states and what each loaded plug-in reports about itself (`LoadedPluginInfo`, asked through `AudioEngineClient`); what the file cannot hold comes back as grouped notes. `RfxChainExportFlow` (Views) is the dialog-and-write step.

## Pathway
Untrusted plug-ins reach the engine as skipped. Trust is decided in `PluginTrust` at scan or approval, not at load. State files are written through `FilePathPolicy.WriteAtomically`.

## Must not depend on
WPF windows; the engine project directly (use `AudioEngineClient`).

## Tests
`TestPluginStateCollection`, `TestNightPluginApproval`, `TestQuarantineAllowAgain`, `TestReaperChainImport`, `TestChainContainers`, `TestPresetStoresOnDisk`, `TestRfxChainExport`, `TestDrumMapRemap`, `TestDrumMapEditorStore`, `TestSettingsWithInlinePluginStates`, `TestIsolatedPluginGenerations`, `TestPluginFactorySingleSource`.
