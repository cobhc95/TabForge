# Services/Features

Feature modules: one object per feature that adds its rows to the central tables.

- `IFeatureModule.cs`: the contract (settings rows, preferences layout, hotkey rows, commands, menu rows, bounds) and the row records.
- `FeatureRegistry.cs`: the list of modules and the merge into `SettingsCatalog`, `HotkeyCatalog`, the command table, the main menu and `SettingsValidator`.

Pilots: `Services/Video/VideoFeatureModule.cs` (Video), `Services/Band/BandFeatureModule.cs` (Band) and `Services/Export/ExportFeatureModule.cs` (export formats). Recipe: "Add a feature module" in `docs/RECIPES.md`.
