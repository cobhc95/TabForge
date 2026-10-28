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
using TabForge.Avalonia.Views.Presets;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Plugins;
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
    private readonly PluginSlot _slot;
    private readonly Action<Action> _edit;
    private readonly Window _owner;
    private readonly Func<FxChainDialogs> _dialogs;
    private readonly List<ArticulationRow> _rows = ArticulationTechniques.All.Select(t => new ArticulationRow(t)).ToList();
    private readonly ComboBox _presets = new() { Width = DialogMetrics.ArticulationPresetWidth, Name = "ArticulationPresetBox" };
    private readonly Button _saveAs = Small("Save as…", "Save the table as a preset of your own", "ArticulationSaveAsButton");
    private readonly Button _import = Small("Import…", "Read a preset from a .json file", "ArticulationImportButton");
    private readonly Button _export = Small("Export…", "Write the table to a .json file", "ArticulationExportButton");
    private readonly Button _delete = Small("Delete", "Delete the selected preset of your own", "ArticulationDeleteButton");
    private readonly TextBox _lead = new() { Width = DialogMetrics.ArticulationLeadWidth, Name = "ArticulationLeadBox", VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _description = new TextBlock { TextWrapping = TextWrapping.Wrap, Name = "ArticulationDescription" }.Spaced(DialogMetrics.ArticulationTextGap);
    private readonly TextBlock _status = new TextBlock { TextWrapping = TextWrapping.Wrap, Name = "ArticulationStatus" }.Spaced(DialogMetrics.ArticulationPaneGap);
    private readonly PresetFolderStore _store = ArticulationPresets.UserStore();
    private readonly PresetRows _presetRows;
    private bool _filling, _testing, _custom;

    /// <summary>One row of the preset drop-down: its text, and the preset name it loads (null: the Custom row).</summary>
    private sealed record Entry(string Text, string? Name, bool User, bool Manage = false)
    {
        public override string ToString() => Text;
    }

    /// <param name="host">The song and engine (Test sends through it).</param>
    /// <param name="track">The track that holds the instrument.</param>
    /// <param name="slot">The instrument whose articulation map the tab edits.</param>
    /// <param name="edit">Runs a change of the slot as one undo step.</param>
    /// <param name="owner">The window, for dialogs and file pickers.</param>
    /// <param name="dialogs">The window questions (a test replaces them).</param>
    public ArticulationsPane(IFxChainHost host, TrackModel track, PluginSlot slot, Action<Action> edit, Window owner, Func<FxChainDialogs> dialogs)
    {
        (_host, _track, _slot, _edit, _owner, _dialogs) = (host, track, slot, edit, owner, dialogs);
        this.Spaced(DialogMetrics.ArticulationPaneGap);
        Name = "ArticulationsPane";
        var top = new StackPanel { Orientation = Orientation.Horizontal, Children = { Bold("Preset"), _presets, _saveAs, _import, _export, _delete, Bold("  Lead (ms)"), _lead } };
        ToolTip.SetTip(_lead, "How long before the note the keyswitch or controller is sent (0 to 200, default 10)");
        ToolTip.SetTip(_presets, "Pick a preset: the table below is filled from it. Rename, delete or reorder your own with the icons or Manage presets…");
        var header = new StackPanel { Children = { top, _description } };
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(_status, Dock.Bottom);
        Children.Add(header);
        Children.Add(_status);
        Children.Add(new ScrollViewer { Content = BuildTable(), HorizontalScrollBarVisibility = ScrollBarVisibility.Auto });
        _description[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("MutedBrush");
        _status[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("MutedBrush");
        _presetRows = PresetRows.Attach(_presets, () => _store, item => item is Entry { User: true, Manage: false, Name: { } name } ? name : null,
            () => PresetCatalogKind.Articulation, () => null, PresetsChanged, item => item is Entry { Manage: true },
            isBuiltIn: item => item is Entry { User: false, Manage: false, Name: not null });
        _presets.SelectionChanged += (_, _) => PresetPicked();
        _lead.LostFocus += (_, _) => { if (!_filling) Commit(); };
        _lead.KeyDown += (_, e) => { if (e.Key == global::Avalonia.Input.Key.Enter && !_filling) Commit(); };
        _saveAs.Click += async (_, _) => await SaveAsAsync();
        _import.Click += async (_, _) => await ImportAsync();
        _export.Click += async (_, _) => await ExportAsync();
        _delete.Click += (_, _) => Delete();
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
        ToolTip.SetTip(button, tip);
        return button;
    }

    private static TextBlock Bold(string text) => new TextBlock { Text = text, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center }.Spaced(DialogMetrics.ArticulationLabelEnd);

    private Grid BuildTable()
    {
        var grid = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("Auto,Auto,Auto,Auto,Auto,Auto") }.Spaced(DialogMetrics.ArticulationTableGap);
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

    /// <summary>The map the plug-in slot holds now.</summary>
    private ArticulationMap CurrentMap() => ArticulationMap.FromBindings(_slot.ArticulationBindings);

    /// <summary>Shows the slot map: the rows, the lead and the preset drop-down.</summary>
    public void Reload()
    {
        var map = CurrentMap();
        _custom = false;
        var name = _track.Rig.ArticulationMap ?? "";
        var known = map.IsEmpty ? ArticulationPresets.GenericName : ArticulationPresets.Find(name) is { } p && SameMap(p.ToMap(), map) ? p.Name : null;
        _custom = known is null;
        Show(map);
        FillPresets(known);
    }

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

    private void FillPresets(string? select)
    {
        _filling = true;
        try
        {
            var items = new List<Entry>();
            if (_custom) items.Add(new Entry(CustomText, null, false));
            items.AddRange(ArticulationPresets.BuiltIn.Select(p => new Entry(p.Name, p.Name, false)));
            try { items.AddRange(_store.Names().Select(n => new Entry("★ " + n, n, true))); }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { _status.Text = "Your presets could not be listed: " + ex.Message; }
            var chosen = items.FirstOrDefault(i => select is not null && string.Equals(i.Name, select, StringComparison.OrdinalIgnoreCase)) ?? items[0];
            items.Add(new Entry(PresetRowText.Manage, null, false, Manage: true));
            _presets.ItemsSource = items;
            _presets.SelectedItem = chosen;
            _delete.IsEnabled = chosen.User;
            _description.Text = select is null ? "Edited by hand: Save as keeps it as a preset." : ArticulationPresets.Find(select)?.Description ?? "";
        }
        finally { _filling = false; }
    }

    private void PresetPicked()
    {
        if (_filling || _presets.SelectedItem is not Entry { Manage: false } entry) return;   // the Manage row is reverted by PresetRows
        _delete.IsEnabled = entry.User;
        if (entry.Name is null) return;   // the Custom row: nothing to load
        if (ArticulationPresets.Find(entry.Name) is not { } preset) { _status.Text = "That preset could not be read."; return; }
        _custom = false;
        Apply(preset.ToMap(), preset.Name);
        FillPresets(preset.Name);
        _status.Text = "";
    }

    /// <summary>Puts a map into the slot (one undo step) and into the table.</summary>
    private void Apply(ArticulationMap map, string presetName)
    {
        Show(map);
        _edit(() =>
        {
            _slot.ArticulationBindings = map.ToBindings();   // a new dictionary: the compile reading the old one is never disturbed
            _track.Rig.ArticulationMap = map.IsEmpty ? ArticulationPresets.GenericName : presetName;
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
        Apply(map, CustomText);
        FillPresets(map.IsEmpty ? ArticulationPresets.GenericName : null);
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
