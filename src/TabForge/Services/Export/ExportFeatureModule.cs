using TabForge.Services.Features;

namespace TabForge.Services.Export;

/// <summary>What the main window offers the export commands.</summary>
public interface IExportCommandHost : IFeatureHost
{
    void SaveAsTemplate();
    void ExportPdf();
    void ExportMusicXml();
    void ExportMidi();
    void ExportAscii();
    void ExportGuitarPro();
}

// Owns: what the export formats add to the central tables: the hotkey rows and commands of Save as template, PDF, MusicXML, MIDI, ASCII tab
//   and compatible .gp, and their File menu rows except MIDI (which stays in MainWindow.Menus.cs: the Video row is anchored on it).
// Does not own: the exporters (ScoreExportController, the services), the handlers (MainWindow.File.cs) or the window's host methods (MainWindow.ExportCommands.cs).
// Tests: TestFeatureModuleContributions, TestEveryHotkeyIdHasHandler, TestMainMenuTreeGolden, TestMainMenuTable.
public sealed class ExportFeatureModule : IFeatureModule
{
    public string Name => "Export";

    public IReadOnlyList<FeatureHotkey> Hotkeys { get; } = new FeatureHotkey[]
    {
        new(new("File.SaveAsTemplate", HotkeyCatalog.CategoryFile, "Save as template", "", "Save a copy of the active score as a template in the templates folder."), "File.NewFromTemplate"),
        new(new("File.ExportPdf", HotkeyCatalog.CategoryFile, "Export PDF", "", "Export the engraved score (notation and tab) as a PDF file."), "File.CancelImport"),
        new(new("File.ExportMusicXml", HotkeyCatalog.CategoryFile, "Export MusicXML", "", "Export the song as uncompressed MusicXML (.musicxml): a part per track with notation and a tab staff."), "File.ExportPdf"),
        new(new("File.ExportMidi", HotkeyCatalog.CategoryFile, "Export MIDI", "", "Export the song as a standard MIDI file (File > Export MIDI)."), "File.ExportMusicXml"),
        new(new("File.ExportAscii", HotkeyCatalog.CategoryFile, "Export ASCII tab", "", "Export the tablature as a plain-text ASCII tab file (File > Export ASCII tab)."), "File.ExportMidi"),
        new(new("File.ExportGuitarPro", HotkeyCatalog.CategoryFile, "Export compatible .gp file", "", "Write a compatible .gp copy for other programs. If the song uses something that file cannot hold, you choose first: keep a full TabForge copy, export the compatible file only, or cancel."), "File.ExportAscii"),
    };

    public IReadOnlyList<FeatureMenuRow> MenuRows { get; } = new FeatureMenuRow[]
    {
        new("_File", "File.SaveAs", "Save as temp_late…", "File.SaveAsTemplate"),
        new("_File", "File.Render", "Export _ASCII tab…", "File.ExportAscii"),
        new("_File", "File.ExportAscii", "Export compatible .gp file…", "File.ExportGuitarPro"),
        new("_File", "File.ExportGuitarPro", "Export P_DF…", "File.ExportPdf"),
        new("_File", "File.ExportPdf", "Export Music_XML…", "File.ExportMusicXml"),
    };

    public IEnumerable<FeatureCommand> Commands(IFeatureHost host)
    {
        var export = host as IExportCommandHost ?? throw new InvalidOperationException("The host does not implement IExportCommandHost.");
        yield return new("File.SaveAsTemplate", export.SaveAsTemplate);
        yield return new("File.ExportPdf", export.ExportPdf);
        yield return new("File.ExportMusicXml", export.ExportMusicXml);
        yield return new("File.ExportMidi", export.ExportMidi);
        yield return new("File.ExportAscii", export.ExportAscii);
        yield return new("File.ExportGuitarPro", export.ExportGuitarPro);
    }
}
