# Menus and commands

Index: [Feature map](../FEATURE_MAP.md). Generated test tables: [tests.md](tests.md). The feature modules that add rows are in [feature-modules-and-hosts.md](feature-modules-and-hosts.md).

- **Main code:** `MainWindow.Commands.cs` (id-to-handler lines for plain commands), `MainWindow.Menus.cs` (plain main-menu rows), `Views/MainMenu/MenuTable.cs` (row and group shape and the one-time build), `MainWindow.SettingsApply.cs` (one applier per settings area).
- **Owner folders:** `src/TabForge/Views/MainMenu/` (the menu table, with its rows in the window partial). The complex items (checkable, named, dynamic) stay in `MainWindow.xaml`.
- **Pathway to use:** a plain menu entry is one row in `MainWindow.Menus.cs` with its command id; a new command is one line in `MainWindow.Commands.cs` (or a module's command list). Then add the hotkey row to `HotkeyCatalog` and the entry to `TOOLS_AND_HOTKEYS.md`. Run `--only TestMainMenuTreeGolden` after a menu change and update the golden file only when the new tree is intended.
- **Dock panes and techniques:** `src/TabForge/Docking/DockPaneTable.cs` (the Panels menu and layout ids, in menu order) and `src/TabForge/Models/TechniqueInfo.cs` (one row per note technique).
- **Engine message pairs:** `src/TabForge.Audio.Contracts/EngineMessages.cs` and `EngineMessageLists.cs` hold one Write/Read pair per engine command; both the UI client and the engine use them.
- **Tests:** `TestMainMenuTable`, `TestMainMenuTreeGolden`, `TestCommandRegistryRouting`, `TestEveryHotkeyIdHasHandler`, `TestEverySettingIsWired`, `TestDockPaneTable`, `TestTechniqueInfoTable`, `TestTechniqueCoverage`, `TestEngineMessagesRoundTrip`, `TestEngineMessagesGolden`. Golden files: `tests/full-suite/Menus/main-menu-tree.golden.txt` and `code-menus.golden.txt`.

## Classes and files

| File | Purpose |
| --- | --- |
| `src/TabForge/MainWindow.Commands.cs` | Id-to-handler lines for plain commands (File, Edit, Bar, Section, Track, Mixer, View, Help, ...) |
| `src/TabForge/MainWindow.Menus.cs` | The table of plain main-menu rows: header, command id, click handler |
| `src/TabForge/Views/MainMenu/MenuTable.cs` | `MenuRow` and `MenuGroup`; inserts the rows into the XAML menu skeleton once at startup |
| `src/TabForge/MainWindow.SettingsApply.cs` | The named appliers that `MainWindow.SyncFromSettings` calls in order |
| `src/TabForge/Docking/DockPaneTable.cs` | The dock panes: id, titles, default placement, side-panel membership |
| `src/TabForge/Models/TechniqueInfo.cs` | The note technique table: mark text, playback numbers, file-format names, unsupported formats |
| `src/TabForge.Audio.Contracts/EngineMessages.cs` | Fixed-shape engine command records and their Write/Read pairs |
| `src/TabForge.Audio.Contracts/EngineMessageLists.cs` | Engine command records with lists, optional tails or nested records |
