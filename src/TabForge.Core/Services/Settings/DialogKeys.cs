namespace TabForge.Services;

// Owns: the stable keys of every open, save and browse dialog. Each key keeps its own last folder (DialogFolders) and never changes once shipped.
// Does not own: the folders (DialogFolders), the dialogs (AvaloniaFileDialogs) or the settings file (AppSettings).
// Tests: DialogFoldersTests (two keys keep separate folders; the default of each kind).
public static class DialogKeys
{
    public const string ProjectOpen = "project.open";
    public const string ProjectSave = "project.save";
    public const string ExportGp = "export.gp";
    public const string ExportGpLegacy = "export.gpLegacy";
    public const string ExportMidi = "export.midi";
    public const string ExportAscii = "export.ascii";
    public const string ExportMusicXml = "export.musicxml";
    public const string ExportPdf = "export.pdf";
    public const string ExportVideo = "export.video";
    public const string RenderFolder = "render.folder";
    public const string AudioImport = "audio.import";
    public const string AudioInsert = "audio.insert";
    public const string MidiInsert = "midi.insert";
    public const string MediaLocate = "media.locate";
    public const string MediaSearchFolder = "media.searchFolder";
    public const string PluginFolder = "plugin.folder";
    public const string PluginFile = "plugin.file";
    public const string SettingsFolder = "settings.folder";
    public const string SettingsImport = "settings.import";
    public const string SettingsExport = "settings.export";
    public const string FxChainLoad = "fxchain.load";
    public const string FxChainSave = "fxchain.save";
    public const string FxChainExportRfx = "fxchain.exportRfx";
    public const string ArticulationsImport = "articulations.import";
    public const string ArticulationsExport = "articulations.export";
    public const string IrBrowse = "ir.browse";
    public const string PresetsImport = "presets.import";
    public const string PresetsExport = "presets.export";
}
