# Services/Export

Export formats as feature-module rows: Save as template, Export PDF, MusicXML, ASCII tab and compatible .gp (hotkey rows, commands and File menu rows), plus the host interface the window implements.

- `ExportFeatureModule.cs`: the module (hotkeys follow the File rows in the catalogue, menu rows follow their File menu anchors) and `IExportCommandHost`.
- The exporters are: `Controllers/ScoreExportController.cs`, `Source/src/TabForge.Core/Services/Export/MidiExportService.cs`, `Source/src/TabForge.Core/Services/Export/AsciiExportService.cs`, `Source/src/TabForge.Core/Services/Export/MusicXmlExportService.cs`.
- Export MIDI's menu row stays in `MainWindow.Menus.cs` because the Video module's row is anchored on it.

Recipe: "Add an export option". Tests: `TestFeatureModuleContributions`.
