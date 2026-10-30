# Plugins

Plug-in discovery, trust, chain state and rigs. Plug-ins run only in isolated processes.

## How to change me
1. Entry files: `PluginTrust.cs`, `ChainStateStore.cs`.
2. Owner class: `PluginTrust` (trust), `ChainStateStore` (states); plug-ins stay in isolated processes. `PluginTrustLoadGate` takes its file-trust port (`IPluginFileTrust`) explicitly: the WPF build and the Avalonia app pass the same Windows adapter.
3. Tests to run: `--areas persistence,settings`; plug-in trust tests via `--areas release` (full-suite build), plus `--areas architecture,hygiene` (see the test box in `Source/docs/guides/TESTING.md`).
4. Docs to update: `ARCHITECTURE.md` guarantees, `CHANGELOG.md`.

## Key types
- `PluginTrust`: records size, time, SHA-256 and signer; a changed file needs re-approval.
- `MidiProcessorCatalog` (lookup, search, defaults) with `MidiProcessorCatalog.Entries.cs` (the processor table).
- `VstScannerService`, `PluginCatalog`, `PluginLibrary`: scan and list plug-ins.
- `VstPluginInfo`: one plug-in's row data (name, path, format, vendor, role), shared by the scanner and the Add plug-in list.
- `ChainStateStore`: plug-in states kept in checked files and cleaned up when unused.
- `PluginQuarantine`: plug-ins set aside after a failure.
- `PresetFolderStore` (`IPresetStore`, `PresetRules`): one folder of user preset files (plug-in presets, FX chains, MIDI processing presets, MIDI processor presets) in the user's order, with rename, duplicate, delete and move; the order is `preset-order.txt` beside the files. The preset manage panel (`PresetManagePanel`, in the Presets folder under Views) works through it.
- `DrumMapLibrary` (drum maps and the note table between two of them; a map may carry its own note-to-note table) with `DrumMapFiles.cs`: `DrumMapDraft` (the map editor's checks and the atomic write) and `DrumMapStore` (the user's drum maps folder as an `IPresetStore`). The editor itself is in the NoteMaps folder under Views.
- `AutoChains`, `RigPreset`: default chains and saved rigs.
- `ChainContainer`, `ChainTree` (`ChainContainers.cs`): containers of a chain as runs over the flat plug-in list (`RigPreset.Containers`; in a bare plug-in list they ride on the first plug-in as a "ChainContainers" property), bypass inherited by the plug-ins inside (`PluginSlot.Plays`), and the tree an exporter walks (`ChainTree.Tree`).
- `ActiveOnRule`, `ActiveOnRules` (`ActiveOn/`): the optional "Active on" rule of a plug-in (`PluginSlot.ActiveOn`) or container (`ChainContainer.ActiveOn`): driver kind (ASIO / WASAPI) and output device by name, both must match. A container's rule ANDs with every child's; `ActiveOnRules.Enabled` is the one bypass decision the engine receives (`AudioEngineClient.SyncBypass`). A null rule is every output, so old files load unchanged. Part of a saved chain and container preset, never of a plug-in state preset.
- `ReaperChainImporter`: reads `.RfxChain` chain files from another host (header, state and footer of each block; VST2 chunk or parameter list, VST3 component and controller state) and maps them onto the known plug-ins by file name, VST3 class id or name without a "_x64" suffix. `ReaperPluginFolders` reads the plug-in folders from Reaper's own reaper.ini. The FX chain window calls it through `RfxChainImportFlow`.
- `KnownPluginList`: every plug-in the settings know (remembered list, last scan, identified, approved, in use), for matching a chain entry to its file.
- `PluginCatalog`: the role and vendor of a plug-in (VST3 module info, else one throwaway `--plugin-info` process once it is approved); `PluginQuarantine`: the crash list and Allow again. Both take the file checks as a port, so each app passes its own.
- `RfxChainExporter`: the inverse: writes a chain as the text of an `.RfxChain` file (FX chain for other hosts) from the slots, their states and what each loaded plug-in reports about itself (`LoadedPluginInfo`, asked through `AudioEngineClient`); what the file cannot hold comes back as grouped notes. `RfxChainExportFlow` (Views) is the dialog-and-write step.

## Pathway
Untrusted plug-ins reach the engine as skipped. Trust is decided in `PluginTrust` at scan or approval, not at load. State files are written through `FilePathPolicy.WriteAtomically`.

## Must not depend on
WPF windows; the engine project directly (use `AudioEngineClient`).

## Tests
`TestPluginStateCollection`, `TestNightPluginApproval`, `TestQuarantineAllowAgain`, `TestReaperChainImport`, `TestChainContainers`, `TestPresetStoresOnDisk`, `TestRfxChainExport`, `TestDrumMapRemap`, `TestDrumMapEditorStore`, `TestSettingsWithInlinePluginStates`, `TestIsolatedPluginGenerations`, `TestPluginFactorySingleSource`.
