using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Avalonia.Views.Presets;
using TabForge.Plugins;
using TabForge.Presets;
using TabForge.Presenters;
using TabForge.Playback;

namespace TabForge.Avalonia.Views.Articulations;

// Owns: the one articulation preset ComboBox: its built-in and user rows, the Custom / track-map row, the trailing Manage row, and loading the picked preset.
// Does not own: what picking does with the map (ArticulationsPane, MidiExportWindow) or where presets live (ArticulationPresets, PresetFolderStore).
// Tests: MidiTechniqueExportTests (the export dialog lists the same presets); the pane's own behaviour is covered by TestArticulationKeyswitches.
/// <summary>The shared articulation preset selector used by the Articulations pane and the Export MIDI dialog.</summary>
public sealed class ArticulationPresetSelector : ComboBox
{
    public sealed record Entry(string Text, string? Name, bool User, bool Manage = false, bool Default = false, bool File = false)
    {
        public override string ToString() => Text;
    }

    private readonly PresetRows _rows;
    private readonly PresetFolderStore _store = ArticulationPresets.UserStore();
    private bool _filling;

    /// <summary>The text of the "edited by hand" row; null leaves it out.</summary>
    public string? CustomText { get; init; }
    /// <summary>The text of the row that means "the track's own map"; null for the Articulations pane.</summary>
    public string? DefaultText { get; init; }
    /// <summary>The text of the "open a preset file" row; null leaves it out.</summary>
    public string? FileText { get; init; }
    /// <summary>True while the pane shows a hand-edited map: the Custom row is listed.</summary>
    public bool CustomVisible { get; set; }

    public event Action<ArticulationPreset>? PresetSelected;
    public event Action? CustomPicked;
    public event Action? DefaultPicked;
    public event Action? FilePicked;
    /// <summary>Raised after a non-manage row was picked (the pane updates its description and buttons).</summary>
    public event Action? EntryChanged;
    /// <summary>Raised when a row renamed, moved or was deleted: list again.</summary>
    public event Action? Changed;

    public ArticulationPresetSelector()
    {
        Name = "ArticulationPresetBox";
        Width = DialogMetrics.ArticulationPresetWidth;
        _rows = PresetRows.Attach(this, () => _store, item => item is Entry { User: true, Manage: false, Name: { } name } ? name : null,
            () => PresetCatalogKind.Articulation, () => null, () => Changed?.Invoke(), item => item is Entry { Manage: true },
            isBuiltIn: item => item is Entry { User: false, Manage: false, Name: not null });
        SelectionChanged += (_, _) => Picked();
    }

    /// <summary>A subclass has its own style key and would get no ComboBox theme (zero-size, invisible): use the stock ComboBox theme.</summary>
    protected override Type StyleKeyOverride => typeof(ComboBox);

    public Entry? SelectedEntry => SelectedItem as Entry;
    public PresetRows Rows => _rows;

    /// <summary>Lists the presets and selects <paramref name="select"/> (a preset name), the Custom row or the track-map row when it is null.</summary>
    public void Reload(string? select, AppliedPreset? applied = null, string? current = null)
    {
        _filling = true;
        try
        {
            var items = new List<Entry>();
            if (DefaultText is { } defaultText) items.Add(new Entry(defaultText, null, false, Default: true));
            if (CustomText is { } custom && CustomVisible) items.Add(new Entry(custom, null, false));
            var builtIns = ArticulationPresets.BuiltIn.Select(p => p.Name).ToList();
            items.AddRange(ArticulationPresets.BuiltIn.Select(p => new Entry(PresetCombo.Decorate(p.Name, p.Name, applied, current, true), p.Name, false)));
            try
            {
                items.AddRange(_store.Names().Select(n => new Entry("★ " + PresetCombo.Decorate(n, n, applied, current, true), n, true)));
                if (applied is { } missing && !_store.Names().Contains(missing.Name, StringComparer.OrdinalIgnoreCase) && !builtIns.Contains(missing.Name, StringComparer.OrdinalIgnoreCase))
                    items.Add(new Entry("★ " + PresetCombo.Decorate(missing.Name, missing.Name, applied, current, false), missing.Name, true));
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { /* swallow: the pane's own status line reports the preset folder */ }
            if (FileText is { } fileText) items.Add(new Entry(fileText, null, false, File: true));
            var chosen = items.FirstOrDefault(i => !i.Default && select is not null && string.Equals(i.Name, select, StringComparison.OrdinalIgnoreCase))
                ?? items[0];
            items.Add(new Entry(PresetRowText.Manage, null, false, Manage: true));
            ItemsSource = items;
            SelectedItem = chosen;
        }
        finally { _filling = false; }
    }

    private void Picked()
    {
        if (_filling || SelectedEntry is not { Manage: false } entry) return;   // the Manage row is reverted by PresetRows
        if (entry.Default) { DefaultPicked?.Invoke(); EntryChanged?.Invoke(); return; }
        if (entry.File) { FilePicked?.Invoke(); EntryChanged?.Invoke(); return; }
        if (entry.Name is null) { CustomPicked?.Invoke(); EntryChanged?.Invoke(); return; }
        if (ArticulationPresets.Find(entry.Name) is { } preset) PresetSelected?.Invoke(preset);
        EntryChanged?.Invoke();
    }
}
