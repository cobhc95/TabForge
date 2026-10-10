# Views/Preferences

The pages of the Settings window: setting rows, shortcut rows, the colour picker and the Common and Advanced pages. Each row writes through the host to the one shared settings store; no page keeps its own copy. Does not own the bounds (`SettingsValidator`) or the file (`AppSettingsStore`).

## Files
| File | Purpose |
| --- | --- |
| `ColourPickerWindow.cs` | The full colour picker opened from a colour row: hue, shade, alpha, hex code, recent colours |
| `CommonPages.cs` | The Common page (shortcuts to the same rows) and the Advanced page (version, files, reset) |
| `HotkeyPage.cs` | The Shortcuts page and its search rows: two slots per command, capture, conflicts, per-row reset |
| `IPreferencesHost.cs` | `IPreferencesHost` and `PageRow`: one built row of the open page, with the element to scroll to and highlight |
| `PreferencesCards.cs` | Card, note and brush helpers shared by the page builders |
| `SettingsCopy.cs` | The sections Reset all and Import copy beyond the core ones: video, keyboard mode, render and the plug-in rows (never approvals or the quarantine list) |
| `SettingEditors.cs` | The editor control of each setting row (switch, choice, number, text, colour, button); changes go to the host |

## Pathway
`START_HERE.md`, Golden pathways: **Settings** (rows in `PreferencesWindow`, bounds in `SettingsValidator`, old files in `SettingsMigration`) and **Commands** (bindings through `HotkeyCatalog`).

## Tests
`docs/feature-map/settings-and-preferences.md`.
