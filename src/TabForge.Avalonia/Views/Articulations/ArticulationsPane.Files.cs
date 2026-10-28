using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using TabForge.Avalonia.Platform;
using TabForge.Playback;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Views.Articulations;

// Owns: the preset buttons of the Articulations tab: Save as, Import, Export and Delete (async, so the window never waits on a picker or a disk).
// Does not own: the table or the edit (ArticulationsPane) or the file format (ArticulationPresets).
// Tests: no dedicated test class.
public sealed partial class ArticulationsPane
{
    private const string Caption = "Articulations";
    private const string JsonFilter = "Articulation preset (*.json)|*.json";

    private async Task SaveAsAsync()
    {
        if (ReadTable() is not { } map) return;
        var name = (await _dialogs().Prompt(_owner, "Save articulation preset", "Preset name", ""))?.Trim();
        if (string.IsNullOrEmpty(name)) return;
        if (PresetRules.Error(name) is { } error) { await _dialogs().Tell(_owner, error, Caption); return; }
        if (ArticulationPresets.FindBuiltIn(name) is not null) { await _dialogs().Tell(_owner, "That name belongs to a preset that comes with the app. Pick another.", Caption); return; }
        var store = _store;
        if (store.Names().Contains(name, StringComparer.OrdinalIgnoreCase)
            && await _dialogs().Ask(_owner, $"Replace the preset \"{name}\"?", Caption, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != MessageBoxAnswer.Yes) return;
        if (!await WriteAsync(store.PathOf(name), ArticulationPreset.From(name, "", map))) return;
        store.RaiseChanged();
        _custom = false;
        Apply(map, name);
        FillPresets(name);
        _status.Text = $"Saved the preset \"{name}\".";
    }

    private async Task ExportAsync()
    {
        if (ReadTable() is not { } map) return;
        var current = (_presets.SelectedItem as Entry)?.Name ?? "Articulations";
        var path = _dialogs().Files.SaveFile(new TabForge.Platform.FileDialogOptions
        {
            Key = TabForge.Services.DialogKeys.ArticulationsExport, Title = "Export articulation preset", FileName = current, DefaultExt = "json", Filter = JsonFilter, OverwritePrompt = true,
        }, _owner);
        if (path is null) return;
        if (await WriteAsync(path, ArticulationPreset.From(current, ArticulationPresets.Find(current)?.Description ?? "", map))) _status.Text = "Exported to " + path;
    }

    private async Task ImportAsync()
    {
        var picked = _dialogs().Files.OpenFile(new TabForge.Platform.FileDialogOptions { Key = TabForge.Services.DialogKeys.ArticulationsImport, Title = "Import articulation preset", Filter = JsonFilter }, _owner);
        if (picked is null) return;
        var preset = await Task.Run(() => ArticulationPresets.ReadFile(picked));
        if (preset is null || preset.ToMap().IsEmpty && preset.Rules.Count > 0) { await _dialogs().Tell(_owner, "That file is not an articulation preset this version can read.", Caption); return; }
        var name = PresetRules.Error(preset.Name) is null ? preset.Name : Path.GetFileNameWithoutExtension(picked).Trim();
        if (ArticulationPresets.FindBuiltIn(name) is not null) name += " (imported)";
        var store = _store;
        if (PresetRules.Error(name) is not null) { await _dialogs().Tell(_owner, "The preset has no usable name.", Caption); return; }
        if (store.Names().Contains(name, StringComparer.OrdinalIgnoreCase)
            && await _dialogs().Ask(_owner, $"Replace your preset \"{name}\"?", Caption, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != MessageBoxAnswer.Yes) return;
        preset.Name = name;
        if (!await WriteAsync(store.PathOf(name), preset)) return;
        store.RaiseChanged();
        _custom = false;
        Apply(preset.ToMap(), name);
        FillPresets(name);
        _status.Text = $"Imported the preset \"{name}\".";
    }

    /// <summary>Deletes the selected preset of the person's at once; the message offers Undo (the shared row layer does it, as the pencil and bin of a row do).</summary>
    private void Delete()
    {
        if (_presets.SelectedItem is Entry { User: true, Name: { } name }) _presetRows.Delete(name);
    }

    /// <summary>The list changed (a delete, rename or reorder from a row, the manager or an Undo): list it again, and leave the applied preset when it is gone.</summary>
    private void PresetsChanged()
    {
        var selected = (_presets.SelectedItem as Entry)?.Name;
        if (selected is not null && ArticulationPresets.Find(selected) is null)
        {
            _custom = !CurrentMap().IsEmpty;
            FillPresets(_custom ? null : ArticulationPresets.GenericName);
        }
        else FillPresets(selected);
    }

    /// <summary>Writes the preset file off the UI thread; false (with a message) when the disk refuses.</summary>
    private async Task<bool> WriteAsync(string path, ArticulationPreset preset)
    {
        try { await Task.Run(() => ArticulationPresets.WriteFile(path, preset)); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Trace.Error(Trace.Ui, "articulations: write: " + ex.Message);
            await _dialogs().Tell(_owner, "The preset could not be saved: " + ex.Message, Caption);
            return false;
        }
    }
}
