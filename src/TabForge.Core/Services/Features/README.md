# Services/Features

Plain data the features (Band, Video, Keyboard mode, Live input) add to the central settings and hotkey catalogues. The rows themselves are merged by `SettingsCatalog` and `HotkeyCatalog.Features.cs`.

## Files
| File | Owns |
| --- | --- |
| `FeatureHotkeyIds.cs` | the ids of the hotkey rows the feature modules add to the catalogue (the rows are in HotkeyCatalog.Features.cs), and the ids of the FX chain window's commands. |
| `FeatureSettingsSources.cs` | the plain data a feature adds to the settings catalogue (IFeatureSettingsSource), the app-side   calls those catalogues make (CatalogPlatform), and the one composition of both (CatalogComposition). |

Tests: see the `// Tests:` line at the top of each file.
