# PresetManagement

The preset manager's rows in the central tables.

## Files
| File | Purpose |
| --- | --- |
| `PresetsFeatureModule.cs` | The Preset manager command (no default key) and the Tools > Preset manager… menu row; the command calls `PresetManager.Open` |

The window, its catalogue and the drop-down rows are in `src/TabForge/Views/Presets/`. The module sits here, outside Views, so the feature registry in Services names no view.

## Tests
`TestPresetManagerWindow`, `TestFeatureModuleContributions`, `TestMainMenuTreeGolden`.
