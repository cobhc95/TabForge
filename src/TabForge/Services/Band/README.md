# Services/Band

The Band view's feature module: what Band adds to the central tables, merged by `Services/Features/FeatureRegistry.cs`. No WPF. The view is in `Views/Band/`.

## Files
| File | Purpose |
| --- | --- |
| `BandFeatureModule.cs` | `IBandCommandHost` and the module: the eight Band hotkey rows (each after the one before, from `View.BandView`), their commands, and the "Band view" Preferences group on the Timeline page |
| `BandSettingsRows.cs` | The "Band view" Preferences rows (a `SettingsCatalog` partial) |

## Not module-driven
The View menu's "Band view" item (a checkable XAML item), the Band menu in `Views/ContextMenuSpecs.cs`, the Band settings bounds in `Services/SettingsValidator.cs`, and the `Band.*` case labels of `RunHotkey` in `MainWindow.Settings.cs`, which run before the module's commands and reach the same handler.

## Tests
`docs/feature-map/band-view.md`.
