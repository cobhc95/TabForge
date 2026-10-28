using System;
using System.IO;
using System.Text.Json;
using TabForge.Avalonia.Platform;
using TabForge.Platform;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Views;

// Owns: Import settings… and Export settings… in the Settings window's Manage settings menu: the file pickers, and the staging of an
//       imported file as the window's working settings (Apply or OK keeps it) or the write of the working settings to a file.
// Does not own: the file format, bounds and validation (SettingsFileService, SettingsValidator), or the saving of the live settings (Apply and OK).
// Tests: TabForge.Avalonia.Tests.PreferencesSettingsTransferTests.
public sealed partial class PreferencesWindow
{
    /// <summary>The app's remembered dialog folders (null: none, as in tests).</summary>
    public TabForge.Services.DialogFolderMemory? DialogFolders { get; set; }

    private AvaloniaFileDialogs _dialogs => new(DialogFolders);
    private const string SettingsFileFilter = "TabForge settings (*.json)|*.json";

    /// <summary>Import settings…: picks a settings file and stages it.</summary>
    private void ImportSettingsFromDialog()
    {
        var chosen = _dialogs.OpenFile(new FileDialogOptions { Key = TabForge.Services.DialogKeys.SettingsImport, Title = "Import TabForge settings", Filter = SettingsFileFilter, CheckFileExists = true }, this);
        if (chosen is not null) ImportSettingsFrom(chosen);
    }

    /// <summary>
    /// Stages a settings file as the working settings. SettingsFileService.Load bounds, migrates and validates the file (SettingsValidator), so
    /// a malformed or oversized file is refused with its reason on the status line and nothing changes. Returns true when it was staged.
    /// </summary>
    public bool ImportSettingsFrom(string path)
    {
        AppSettings loaded;
        try { loaded = SettingsFileService.Load(path); }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException or IOException or UnauthorizedAccessException or JsonException)
        {
            _status.Text = "Import failed: " + ex.Message;
            return false;
        }
        Working = loaded;
        DropRows();
        MarkChanged();
        Rebuild();
        _status.Text = "Settings imported · review, then Apply";
        return true;
    }

    /// <summary>Export settings…: picks a file and writes the working settings to it.</summary>
    private void ExportSettingsToDialog()
    {
        var chosen = _dialogs.SaveFile(new FileDialogOptions
        {
            Key = TabForge.Services.DialogKeys.SettingsExport, Title = "Export TabForge settings", Filter = SettingsFileFilter, DefaultExt = ".json", AddExtension = true, FileName = "TabForge-settings.json",
        }, this);
        if (chosen is not null) ExportSettingsTo(chosen);
    }

    /// <summary>Writes the working settings to <paramref name="path"/> (atomic, size-checked); a failure is shown on the status line.</summary>
    public void ExportSettingsTo(string path)
    {
        try
        {
            SettingsFileService.SaveAtomic(path, Working);
            _status.Text = $"Settings exported to {Path.GetFileName(path)}";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            _status.Text = "Export failed: " + ex.GetBaseException().Message;
        }
    }
}
