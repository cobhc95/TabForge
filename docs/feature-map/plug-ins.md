# Plug-ins

Index: [Feature map](../FEATURE_MAP.md). Generated test tables: [tests.md](tests.md).

- **Main code:** `src/TabForge/Plugins/` (`PluginTrust`, `MidiProcessorCatalog`); engine side `Vst2Plugin`, `RemotePlugin`
- **Pathway to use:** A song only names plug-in paths; `PluginTrust` decides what loads. Isolation is optional crash isolation, not a sandbox.
- **Tests:** `--areas engine`, `--areas midi`; `TestIsolatedPluginGenerations`, `TestPluginFactorySingleSource`, `TestMidiProcessors`, `TestNightPluginApproval`
