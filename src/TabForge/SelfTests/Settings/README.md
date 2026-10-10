# SelfTests/Settings

Checks of the settings: the note marker size setting, and the audit that every row of the Settings window writes its own value and keeps it through save and load. Does not own the settings store (`src/TabForge/Services/`).

## Files
| File | Purpose |
| --- | --- |
| `SelfTestFretMarkerSize.cs` | The note marker size setting: default 80%, 150% scales radius and number (clamped), value round-trips |
| `SelfTestSettingsAudit.cs` | Settings wiring audit: every row in the Settings window writes its own value and keeps it through save and load |

## Pathway
`START_HERE.md`, Golden pathways: **Settings** (one `AppSettingsStore`; bounds in `SettingsValidator`; rows in `PreferencesWindow`).

## Tests
`--areas settings` (run with the full-suite build for the wider set); list in `docs/feature-map/tests.md`.
