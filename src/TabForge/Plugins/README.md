# Plugins

Plug-in discovery, trust, chain state and rigs. Plug-ins run only in isolated processes.

## Key types
- `PluginTrust`: records size, time, SHA-256 and signer; a changed file needs re-approval.
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
