using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using TabForge.Avalonia.Platform;
using TabForge.Presenters;
using TabForge.Services;

// Owns: the manager's Import… and Export… buttons: the file pickers, reading and writing the file off the UI thread, and the enabled state and
//   tooltip of the two buttons.
// Does not own: turning bytes into a preset or a preset into bytes (PresetManagerActions, the stores) or the other toolbar buttons (the window).
// Tests: PresetUndoTests.
namespace TabForge.Avalonia.Views.Presets;

public sealed partial class PresetManagerWindow
{
    private const long MaxImportBytes = 32L * 1024 * 1024;
    private const string ImportTip = "Add presets from files to the category that is shown";
    private const string ImportNeedsPlaceTip = "Open one plug-in or processor first: presets are imported into the place that is shown";
    private const string ImportSearchTip = "Leave the search first: presets are imported into the category that is shown";
    private const string ExportTip = "Write the selected preset to a file";
    private const string ExportNeedsOneTip = "Select one preset first";
    private const string NothingToExportText = "This preset has no file to export.";

    // Import is for the shown place (not while a search lists many places); Export is for exactly one selected preset.
    private void UpdateFileTools(IReadOnlyList<PresetEntry> chosen)
    {
        var searching = SearchText.Length > 0;
        var canImport = !searching && !_node.IsGroup && _actions.ExtensionOf(_node) is not null;
        SetTool(_import, canImport, ImportTip, searching ? ImportSearchTip : ImportNeedsPlaceTip);
        SetTool(_export, chosen.Count == 1 && _actions.ExtensionOf(chosen[0].Node) is not null, ExportTip, ExportNeedsOneTip);
    }

    /// <summary>The app's remembered dialog folders for the import and export pickers (null: none).</summary>
    public TabForge.Services.DialogFolderMemory? Folders { get; set; }

    private AvaloniaFileDialogs FileDialogs => new(Folders);

    /// <summary>Reads each chosen file off the UI thread and adds it to the shown category under the file's name; says what was refused.</summary>
    public async Task ImportAsync()
    {
        if (!_import.IsEnabled || _actions.ExtensionOf(_node) is not { } extension) return;
        var node = _node;
        var paths = FileDialogs.OpenFiles(new TabForge.Platform.FileDialogOptions
        {
            Key = TabForge.Services.DialogKeys.PresetsImport, Title = "Import preset", Multiselect = true, Filter = "Preset (*" + extension + ")|*" + extension,
        }, this);
        if (paths is not { Length: > 0 }) return;
        var reserved = _catalogue.BuiltIns(node);
        var made = new List<PresetEntry>();
        string? error = null;
        foreach (var path in paths)
        {
            var bytes = await ReadAsync(path);
            var result = bytes is null ? new PresetActionResult(false, $"\"{Path.GetFileName(path)}\" could not be read.") : _actions.Import(node, path, bytes, reserved);
            error ??= result.Error;
            if (result.Ok && result.Select is { } name) made.Add(new PresetEntry(node, name, false, null));
        }
        Say(error);
        if (made.Count > 0) FillList(made);
    }

    /// <summary>Asks for a file and writes the one selected preset to it off the UI thread.</summary>
    public async Task ExportAsync()
    {
        if (SelectedEntries is not [{ } entry] || _actions.ExtensionOf(entry.Node) is not { } extension) return;
        if (_actions.ExportBytes(entry) is not { } bytes) { Say(NothingToExportText); return; }
        var path = FileDialogs.SaveFile(new TabForge.Platform.FileDialogOptions
        {
            Key = TabForge.Services.DialogKeys.PresetsExport, Title = "Export preset", FileName = entry.Name, DefaultExt = extension.TrimStart('.'), OverwritePrompt = true,
            Filter = "Preset (*" + extension + ")|*" + extension,
        }, this);
        if (path is null) return;
        try
        {
            await Task.Run(() => FilePathPolicy.WriteAtomically(path, s => s.Write(bytes)));
            Say("Exported to " + path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Services.Trace.Error(Services.Trace.Ui, "presets: export: " + ex.Message);
            Say("The preset could not be exported: " + ex.Message);
        }
    }

    private static async Task<byte[]?> ReadAsync(string path)
    {
        try { return await Task.Run(() => InputLimits.ReadBoundedBytes(path, MaxImportBytes, "preset")); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Services.Trace.Error(Services.Trace.Ui, "presets: import read: " + ex.Message);
            return null;
        }
    }
}
