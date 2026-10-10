# Feature modules and hosts

Index: [Feature map](../FEATURE_MAP.md). Generated test tables: [tests.md](tests.md). The command and menu tables are in [menus-and-commands.md](menus-and-commands.md).

- **Main code:** `IFeatureModule` and `FeatureRegistry` (`src/TabForge/Services/Features/`); the modules `BandFeatureModule` (`Services/Band/`) and `VideoFeatureModule` (`Services/Video/`); the hosts `MixerHost` (`Views/MixerHost.cs`), `DockHost` (`Views/DockHost.cs`) and `BandHost` (`Views/Band/BandHost.cs`).
- **Owner folders:** `src/TabForge/Services/Features/` (the module shape and the merge, with its README). A feature folder holds its own module and its own classes; the window only sees the module and the host interface.
- **Pathway to use:** add a feature as one module class in its own folder, list it in `FeatureRegistry.Modules`, and give it its hotkeys, layout group, settings rows, commands and menu rows through the module members. A controller that needs the window declares a narrow interface, and a host class implements it. Do not grow `MainWindow` or `TabEditorControl`.
- **Central tables fed by modules:** hotkeys (`HotkeyCatalog`), the Preferences layout (`SettingsCatalog`), settings rows and bounds (`SettingsValidator`), commands (`CommandRegistry`) and the main menu (`MenuTable`).
- **Tests:** `TestFeatureModuleContributions`, `TestPreferencesCatalog`, `TestBandHostForwards`, `TestDockPaneTable`, `TestMixerHost`. Full-suite tests: `tests/full-suite/Features/`, `tests/full-suite/Views/`. Run `--areas architecture,hygiene` for the boundaries.

## Classes

| File | Purpose |
| --- | --- |
| `src/TabForge/Services/Features/IFeatureModule.cs` | The module shape: hotkeys, menu rows, Preferences groups, settings rows, commands and settings bounds; each member has a no-op default |
| `src/TabForge/Services/Features/FeatureRegistry.cs` | The module list and the merge of their rows into the central tables |
| `src/TabForge/Services/Band/BandFeatureModule.cs` | The Band view hotkeys, commands and Preferences group |
| `src/TabForge/Services/Video/VideoFeatureModule.cs` | File > Export video and Sound > Record video: hotkeys, commands, menu entries and settings bounds |
| `src/TabForge/Views/MixerHost.cs` | The host side of the Mixer and FX chain windows (`IMixerHost`, `IFxChainHost`, `IMixerWindowsHost`) |
| `src/TabForge/Views/DockHost.cs` | The window-side answers the dock layout controller asks for (`IDockLayoutHost`); holds no state |
| `src/TabForge/Views/Band/BandHost.cs` | The Band view's window side (`IBandViewHost`): pane basics, active song, instrument options, lane cursor |
| `src/TabForge/Services/CommandRegistry.cs` | The id-to-action table for plain commands, with a duplicate check |
