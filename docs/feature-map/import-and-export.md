# Import and export (.gp, .gp5, MIDI, MusicXML, ASCII)

Index: [Feature map](../FEATURE_MAP.md). Generated test tables: [tests.md](tests.md).

- **Main code:** `src/TabForge/Services/` (`GuitarProImporter`, `GuitarProExporter`, `AlphaTabBoundary`, `ImportWorker`, `MidiFileImport`, `MidiExportService`, `MusicXmlExportService`, `AsciiExportService`, `AudioDataFile`)
- **Feature module:** `src/TabForge/Services/Export/ExportFeatureModule.cs` (Save as template, Export PDF, MusicXML, ASCII tab and compatible .gp rows and commands); the exporters are unchanged. Recipe: "Add an export option" in `docs/RECIPES.md`.
- **Pathway to use:** Reading goes through the import worker with limits (`ScoreImportQueue`, `InputLimits`). Saving goes through `DocumentSaveFlow`; files are written with `FilePathPolicy.WriteAtomically`.
- **Tests:** `--areas guitarpro`, `--areas persistence`; groups gp-fixtures, gp-fidelity, long-import, fuzz; `TestGuitarProImportWorker`, `TestMidiExport`, `TestMusicXmlExport`, `TestAsciiExport`, `TestPairSaveRecovery`
