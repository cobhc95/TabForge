using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Avalonia.Views.Fx;
using TabForge.Avalonia.Views.Midi;
using TabForge.Avalonia.Views.Presets;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Plugins;
using TabForge.Presets;
using TabForge.Presenters;

namespace TabForge.Avalonia.Views.Articulations;

/// <summary>
/// The Articulations tab of the MIDI processing window: which MIDI a note technique (palm mute, harmonic, slide ...) sends to this instrument. A preset drop-down (built in and your own),
/// Save as, Import, Export, the keyswitch lead, and a table of Technique, Action, Value, Mode with a Test button per row.
/// Owns: the layout, reading the rows into the plug-in's articulation map as one edit, the preset drop-down. Does not own: the file dialogs (ArticulationsPane.Files), the rows, the Test send, or the compile.
/// Tests: no dedicated test class (the compile is covered by TestArticulationKeyswitches).
/// </summary>
public sealed partial class ArticulationsPane : DockPanel
{
    private const string CustomText = "Custom";
    private readonly IFxChainHost _host;
    private readonly TrackModel _track;
    private readonly PluginSlot? _slot;
    private readonly Action<Action> _edit;
    private readonly Window _owner;
    private readonly Func<FxChainDialogs> _dialogs;
    private readonly List<ArticulationRow> _rows = ArticulationTechniques.All.Select(t => new ArticulationRow(t)).ToList();
    private readonly ArticulationPresetSelector _presets = new() { CustomText = CustomText };
    private readonly Button _saveAs = Small("Save preset…", "Save the table as a preset of your own", "ArticulationSaveAsButton");
    private readonly Button _import = Small("Import…", "Read a preset from a .json file", "ArticulationImportButton");
    private readonly Button _export = Small("Export…", "Write the table to a .json file", "ArticulationExportButton");
    private readonly Button _delete = Small("Delete", "Delete the selected preset of your own", "ArticulationDeleteButton");
    private readonly Button _manage = Small("Manage…", "Rename, delete or reorder your articulation presets", "ArticulationManageButton");
    private readonly TextBox _lead = new() { Width = DialogMetrics.ArticulationLeadWidth, Name = "ArticulationLeadBox", VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _description = new TextBlock { TextWrapping = TextWrapping.Wrap, Name = "ArticulationDescription" }.Spaced(DialogMetrics.ArticulationTextGap);
    private readonly TextBlock _status = new TextBlock { TextWrapping = TextWrapping.Wrap, Name = "ArticulationStatus" }.Spaced(DialogMetrics.ArticulationPaneGap);
    private readonly PresetFolderStore _store = ArticulationPresets.UserStore();
    private bool _filling, _testing, _custom;

    /// <param name="host">The song and engine (Test sends through it).</param>
    /// <param name="track">The track that holds the instrument.</param>
    /// <param name="slot">The instrument whose articulation map the tab edits; null edits the map kept on the track itself.</param>
    /// <param name="edit">Runs a change of the slot (or track) as one undo step.</param>
    /// <param name="owner">The window, for dialogs and file pickers.</param>
    /// <param name="dialogs">The window questions (a test replaces them).</param>
    public ArticulationsPane(IFxChainHost host, TrackModel track, PluginSlot? slot, Action<Action> edit, Window owner, Func<FxChainDialogs> dialogs)
    {
        (_host, _track, _slot, _edit, _owner, _dialogs) = (host, track, slot, edit, owner, dialogs);
        this.Spaced(DialogMetrics.ArticulationPaneGap);
        Name = "ArticulationsPane";
        ClipToBounds = true;
        var top = new WrapPanel { Orientation = Orientation.Horizontal, Name = "ArticulationTopRow", ClipToBounds = true };
        top.Children.Add(Bold("Presets")); top.Children.Add(_presets); top.Children.Add(_saveAs); top.Children.Add(_delete); top.Children.Add(_manage); top.Children.Add(_import); top.Children.Add(_export); top.Children.Add(Bold("  Lead (ms)")); top.Children.Add(_lead);
        MidiSharedRows.Tip(_lead, "How long before the note the keyswitch or controller is sent (0 to 200, default 10)");
        MidiSharedRows.Tip(_presets, "Pick a preset: the table below is filled from it. Rename, delete or reorder your own with the icons or Manage presets…");
        var header = new StackPanel { Children = { top } };
        if (slot is null)
        {
            var hint = new TextBlock
            {
                Text = "Playback keeps the built-in sound; this map is used by MIDI exports that pick it and by an instrument added later.",
                TextWrapping = TextWrapping.Wrap, Name = "ArticulationTrackHint",
            }.Spaced(DialogMetrics.ArticulationTextGap);
            hint[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("MutedBrush");
            header.Children.Add(hint);
        }
        header.Children.Add(_description);
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(_status, Dock.Bottom);
        Children.Add(header);
        Children.Add(_status);
        Children.Add(new ScrollViewer { Content = BuildTable(), Name = "ArticulationViewer", ClipToBounds = true, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto });
        _description[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("MutedBrush");
        _status[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("MutedBrush");
        _presets.PresetSelected += preset =>
        {
            _custom = false;
            Apply(preset.ToMap(), preset.Name, PresetCombo.Remember(preset.Name, "Articulation", Snapshot(preset.ToMap())));
            FillPresets(preset.Name);
            _status.Text = "";
        };
        _presets.CustomPicked += () => _delete.IsEnabled = false;
        _presets.EntryChanged += UpdatePresetDetails;
        _presets.Changed += PresetsChanged;
        _lead.LostFocus += (_, _) => { if (!_filling) Commit(); };
        _lead.KeyDown += (_, e) => { if (e.Key == global::Avalonia.Input.Key.Enter && !_filling) Commit(); };
        _saveAs.Click += async (_, _) => await SaveAsAsync();
        _import.Click += async (_, _) => await ImportAsync();
        _export.Click += async (_, _) => await ExportAsync();
        _delete.Click += (_, _) => Delete();
        _manage.Click += (_, _) => PresetManager.Open(_owner, PresetCatalogKind.Articulation, null);
        foreach (var row in _rows)
        {
            row.Edited += Commit;
            row.TestButton.Click += async (_, _) => await TestAsync(row);
        }
        Reload();
    }

    private static Button Small(string text, string tip, string name)
    {
        var button = new Button { Content = text, Name = name }.WithPadding(DialogMetrics.ArticulationButtonPadding).Spaced(DialogMetrics.ArticulationButtonGap);
        MidiSharedRows.Tip(button, tip);
        return button;
    }

    private static TextBlock Bold(string text) => new TextBlock { Text = text, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center }.Spaced(DialogMetrics.ArticulationLabelEnd);

    private Grid BuildTable()
    {
        var grid = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("Auto,Auto,Auto,Auto,Auto,Auto"), Name = "ArticulationTable", ClipToBounds = true }.Spaced(DialogMetrics.ArticulationTableGap);
        var heads = new[] { "Technique", "Action", "Value", "Value 2 (CC)", "Mode", "" };
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (var column = 0; column < heads.Length; column++)
        {
            var head = Bold(heads[column]);
            head.Spaced(column == 0 ? DialogMetrics.ArticulationHeadFirst : DialogMetrics.ArticulationHead);
            head[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("AccentBrush");
            Grid.SetColumn(head, column);
            grid.Children.Add(head);
        }
        for (var i = 0; i < _rows.Count; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto) { MinHeight = DialogMetrics.ArticulationRowMinHeight });
            _rows[i].AddTo(grid, i + 1);
        }
        return grid;
    }

    /// <summary>The map the plug-in slot (or the track, when it has no instrument) holds now.</summary>
    private ArticulationMap CurrentMap() => ArticulationMapStore.MapOf(_track, _slot);

    /// <summary>Shows the slot map: the rows, the lead and the preset drop-down.</summary>
    public void Reload()
    {
        var map = CurrentMap();
        _custom = false;
        var applied = _track.Rig.AppliedArticulationPreset;
        var name = applied?.Name ?? _track.Rig.ArticulationMap ?? "";
        var known = map.IsEmpty ? ArticulationPresets.GenericName : ArticulationPresets.Find(name) is { } p && SameMap(p.ToMap(), map) ? p.Name : null;
        _custom = known is null;
        Show(map);
        FillPresets(known ?? (applied is not null ? applied.Name : null), applied, Snapshot(map));
    }

    /// <summary>The map in one stable text form, for telling an edit from the map a preset applied.</summary>
    private static string Snapshot(ArticulationMap map) =>
        System.Text.Json.JsonSerializer.Serialize(ArticulationPreset.From("", "", map));

    private static bool SameMap(ArticulationMap a, ArticulationMap b) =>
        a.LeadMs == b.LeadMs && a.Rules.Count == b.Rules.Count && a.Rules.All(pair => b.Rules.TryGetValue(pair.Key, out var other) && other == pair.Value);

    private void Show(ArticulationMap map)
    {
        _filling = true;
        try
        {
            foreach (var row in _rows) row.Show(map.Rules.TryGetValue(row.Technique.Id, out var rule) ? rule : null);
            _lead.Text = map.LeadMs.ToString();
        }
        finally { _filling = false; }
    }

    private void FillPresets(string? select, AppliedPreset? applied = null, string? current = null)
    {
        _presets.CustomVisible = _custom;
        _presets.Reload(select, applied, current);
        UpdatePresetDetails();
    }

    /// <summary>The pane's parts that follow the selected row: its description and whether Delete applies.</summary>
    private void UpdatePresetDetails()
    {
        _delete.IsEnabled = _presets.SelectedEntry?.User == true;
        _description.Text = _presets.SelectedEntry is { Name: { } name }
            ? ArticulationPresets.Find(name)?.Description ?? ""
            : _custom ? "Edited by hand: Save preset keeps it as a preset." : "";
    }

    /// <summary>Puts a map into the slot (one undo step) and into the table.</summary>
    private void Apply(ArticulationMap map, string presetName, AppliedPreset? applied = null)
    {
        Show(map);
        _edit(() =>
        {
            ArticulationMapStore.Store(_track, _slot, map);
            _track.Rig.ArticulationMap = map.IsEmpty ? ArticulationPresets.GenericName : presetName;
            if (applied is not null) _track.Rig.AppliedArticulationPreset = applied;
        });
    }

    /// <summary>The table as a map; null (with the reason in the status line) when a box holds something unusable.</summary>
    private ArticulationMap? ReadTable()
    {
        var map = new ArticulationMap();
        foreach (var row in _rows)
        {
            if (!row.TryRule(out var rule, out var problem)) { _status.Text = problem ?? ""; return null; }
            if (rule is { } r) map.Rules[row.Technique.Id] = r;
        }
        if (!int.TryParse(_lead.Text?.Trim(), out var lead) || lead is < 0 or > ArticulationMap.MaxLeadMs) { _status.Text = $"Lead: type a number of milliseconds, 0 to {ArticulationMap.MaxLeadMs}."; return null; }
        map.LeadMs = lead;
        return map;
    }

    /// <summary>A row or the lead changed: the table becomes the slot map (preset name Custom).</summary>
    private void Commit()
    {
        if (ReadTable() is not { } map) return;
        if (SameMap(map, CurrentMap())) { _status.Text = ""; return; }
        _status.Text = "";
        _custom = !map.IsEmpty;
        var applied = _track.Rig.AppliedArticulationPreset;
        Apply(map, applied?.Name ?? CustomText, applied);   // a hand edit keeps the preset's name and gets the " *" mark
        FillPresets(applied?.Name ?? (map.IsEmpty ? ArticulationPresets.GenericName : null), applied, Snapshot(map));
    }

    private async System.Threading.Tasks.Task TestAsync(ArticulationRow row)
    {
        if (_testing) return;
        if (!row.TryRule(out var rule, out var problem)) { _status.Text = problem ?? ""; return; }
        if (rule is not { } r) return;
        _testing = true;
        try
        {
            var lead = int.TryParse(_lead.Text?.Trim(), out var typed) ? Math.Clamp(typed, 0, ArticulationMap.MaxLeadMs) : ArticulationMap.DefaultLeadMs;
            _status.Text = await ArticulationTester.RunAsync(_host, _track, r, lead);
        }
        finally { _testing = false; }
    }
}
