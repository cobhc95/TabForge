# Services/Export

Export formats as feature-module rows: Save as template, Export PDF, MusicXML, ASCII tab and compatible .gp (hotkey rows, commands and File menu rows), plus the host interface the window implements.

- `ExportFeatureModule.cs`: the module (hotkeys follow the File rows in the catalogue, menu rows follow their File menu anchors) and `IExportCommandHost`.
- The exporters stay where they are: `Controllers/ScoreExportController.cs`, `Services/MidiExportService.cs`, `Services/AsciiExportService.cs`, `Services/MusicXmlExportService.cs`.
- Export MIDI's menu row stays in `MainWindow.Menus.cs` because the Video module's row is anchored on it.

Recipe: "Add an export option" in `docs/RECIPES.md`. Tests: `TestFeatureModuleContributions`.
