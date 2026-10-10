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
- `AutoChains`, `RigPreset`: default chains and saved rigs.
- `ReaperChainImporter`: imports chain files.

## Pathway
Untrusted plug-ins reach the engine as skipped. Trust is decided in `PluginTrust` at scan or approval, not at load. State files are written through `FilePathPolicy.WriteAtomically`.

## Must not depend on
WPF windows; the engine project directly (use `AudioEngineClient`).

## Tests
`TestPluginStateCollection`, `TestNightPluginApproval`, `TestQuarantineAllowAgain`, `TestReaperChainImport`, `TestSettingsWithInlinePluginStates`, `TestIsolatedPluginGenerations`, `TestPluginFactorySingleSource`.
