# Settings and Preferences

Index: [Feature map](../FEATURE_MAP.md). Generated test tables: [tests.md](tests.md).

- **Main code:** `src/TabForge/Services/` (`AppSettings`, `AppSettingsStore`, `SettingsValidator`, `SettingsMigration`, `SettingsCatalog`); `PreferencesWindow`
- **Pathway to use:** One shared `AppSettingsStore`. A setting has a default, a bound in the validator, a migration entry and a Preferences row.
- **Tests:** `--areas settings`; `TestEverySettingIsWired`, `TestPreferencesCatalog`, `TestSettingsStoreSharedAcrossWindows`
