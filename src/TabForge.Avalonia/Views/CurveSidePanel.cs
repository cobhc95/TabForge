using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Avalonia.Views.Presets;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Services;
using TabForge.Views.EffectEditors;

namespace TabForge.Avalonia.Views;

/// <summary>
/// The side of the bend and tremolo-bar editor: the list of curve types (Bend, Release, Dive ...; the ones that need a bend on the note before are greyed
/// when there is none), then the person's saved presets with Save as preset and Delete preset. Choosing a row sets the curve through <c>pick</c>.
/// Owns: the two lists, the two buttons and their rules. Does not own: the curve (CurveEffectWindow), the type table (EffectCurveTypes) or the preset storage (EffectPresetStore).
/// Tests: TabForge.Avalonia.Tests.EffectCurveEditorTests.
/// </summary>
internal sealed class CurveSidePanel : StackPanel
{
    private const string SavePromptTitle = "Save as preset", SavePromptLabel = "Preset name:", SavePromptDefault = "My preset";

    private readonly EffectEditorKind _kind;
    private readonly EffectPresetStore _presets;
    private readonly double _previous;
    private readonly Window _owner;
    private readonly Func<List<BendPointModel>> _snapshot;
    private readonly Action<IReadOnlyList<BendPointModel>> _pick;
    private readonly Action _presetsChanged;
    private readonly ListBox _types = new() { Name = "TypeList" };
    private readonly ListBox _list = new() { MinHeight = DialogMetrics.CurvePresetListMinHeight, MaxHeight = DialogMetrics.CurvePresetListMaxHeight, Name = "PresetList" };
    private readonly Button _delete = new() { Content = "Delete preset", Name = "DeletePresetButton" };
    private readonly Presets.PresetRows _rows;
    // The entries behind the virtualized preset list's ItemsSource; the rows read these, never the containers.
    private IReadOnlyList<EffectPresetEntry> _entries = Array.Empty<EffectPresetEntry>();
    private bool _syncing;

    /// <param name="previous">The level the note before ended on (0 when it has no bend): the types that start from it are greyed at 0.</param>
    /// <param name="pick">Sets the curve to the points of a chosen type or preset.</param>
    public CurveSidePanel(EffectEditorKind kind, EffectPresetStore presets, double previous, Window owner, Func<List<BendPointModel>> snapshot,
        Action<IReadOnlyList<BendPointModel>> pick, Action presetsChanged)
    {
        _kind = kind;
        _presets = presets;
        _previous = previous;
        _owner = owner;
        _snapshot = snapshot;
        _pick = pick;
        _presetsChanged = presetsChanged;
        Width = DialogMetrics.CurveSideWidth;
        this.Spaced(DialogMetrics.CurveSideGap);
        AutomationProperties.SetName(_types, "Curve types");
        AutomationProperties.SetName(_list, "Presets");
        AddTypeRows(kind, previous);
        var save = new Button { Content = "Save as preset\u2026", Name = "SavePresetButton" };
        ToolTip.SetTip(save, "Keep this curve as a preset of your own");
        ToolTip.SetTip(_delete, "Delete the selected preset of your own");
        save.Click += async (_, _) => await SaveAsPreset();
        _delete.Click += (_, _) => DeleteSelected();
        _types.SelectionChanged += (_, _) => { if (!_syncing && _types.SelectedItem is ListBoxItem { Tag: CurveType type }) PickType(type); };
        _list.SelectionChanged += (_, _) => { if (!_syncing && Selected is { } p) PickPreset(p); UpdateDelete(); };
        // Perf: the preset list is virtualized (ItemsSource + recycling template over a VirtualizingStackPanel). The rows come from the shared row layer.
        _list.ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel());
        _rows = Presets.PresetRows.Attach(_list, () => _presets.Store(_kind, _presetsChanged), item => (item as EffectPresetEntry)?.Name,
            () => PresetCatalogKind.EffectPreset, () => _kind.ToString(), () => FillList(Selected?.Name), isManage: _ => false, textOf: item => (item as EffectPresetEntry)?.Name ?? "");
        var manage = new Button { Content = PresetRowText.Manage, Name = "ManagePresetsButton" };
        ToolTip.SetTip(manage, "Rename, duplicate, delete, reorder, import or export your presets of this and the other effect editors");
        manage.Click += (_, _) => PresetManager.Open(_owner, PresetCatalogKind.EffectPreset, _kind.ToString());
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = DialogMetrics.CurveSideSpacing, Children = { save, _delete } };
        Children.Add(Heading("Type", DialogMetrics.CurveHeadingGap));
        Children.Add(_types);
        Children.Add(Heading("Your presets", DialogMetrics.CurveSecondHeadingGap));
        Children.Add(_list);
        Children.Add(buttons.Spaced(DialogMetrics.CurveButtonsGap));
        Children.Add(manage.Spaced(DialogMetrics.CurveButtonsGap));
        FillList(null);
    }

    /// <summary>One row per type: its shape icon (drawn from the type's own points, all on one value range) and its name; greyed when it cannot be chosen.</summary>
    private void AddTypeRows(EffectEditorKind kind, double previous)
    {
        var types = EffectCurveTypes.Of(kind);
        var shown = types.Select(type => EffectCurveTypes.Points(type, EffectCurveTypes.DefaultPrevious(kind))).ToList();
        var low = shown.Min(points => points.Min(p => p.Value));
        var high = shown.Max(points => points.Max(p => p.Value));
        for (var i = 0; i < types.Count; i++)
        {
            var icon = new CurveShapeIcon(shown[i], eased: kind == EffectEditorKind.Bend, low, high);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = DialogMetrics.CurveIconSpacing, Children = { icon, new TextBlock { Text = types[i].Name, VerticalAlignment = VerticalAlignment.Center } } };
            _types.Items.Add(new ListBoxItem { Content = row, Tag = types[i], IsEnabled = EffectCurveTypes.Available(types[i], previous) });
        }
    }

    /// <summary>The type rows as shown, with whether each can be chosen.</summary>
    public IReadOnlyList<(string Name, bool Enabled)> TypeRows => _types.Items.OfType<ListBoxItem>().Select(i => (((CurveType)i.Tag!).Name, i.IsEnabled)).ToList();

    /// <summary>How many type rows carry a shape icon.</summary>
    public int TypeIconCount => _types.Items.OfType<ListBoxItem>().Count(i => i.Content is StackPanel p && p.Children.OfType<CurveShapeIcon>().Any());

    /// <summary>The names of the person's presets as listed.</summary>
    public IReadOnlyList<string> PresetRows => _entries.Select(entry => entry.Name).ToList();

    private EffectPresetEntry? Selected => _list.SelectedItem as EffectPresetEntry;

    private static TextBlock Heading(string text, Thickness gap)
    {
        var heading = new TextBlock { Text = text }.Spaced(gap);
        heading[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted");
        return heading;
    }

    /// <summary>Sets the curve to a type, as choosing its row does (a type that needs a bend before it does nothing without one).</summary>
    public void PickType(CurveType type)
    {
        if (!EffectCurveTypes.Available(type, _previous)) return;
        _syncing = true;
        _list.SelectedItem = null;
        _syncing = false;
        _pick(EffectCurveTypes.Points(type, _previous));
        UpdateDelete();
    }

    /// <summary>Sets the curve to one of the person's presets, as choosing its row does.</summary>
    public void PickPreset(EffectPresetEntry preset)
    {
        _syncing = true;
        _types.SelectedItem = null;
        _syncing = false;
        _pick(preset.Points);
    }

    private void FillList(string? select)
    {
        _entries = _presets.List(_kind).Where(row => !row.BuiltIn).Select(row => row.Preset).ToList();
        _syncing = true;
        _list.ItemsSource = _entries;
        _list.SelectedItem = select is null ? null : _entries.FirstOrDefault(entry => entry.Name == select);
        _syncing = false;
        UpdateDelete();
    }

    private void UpdateDelete() => _delete.IsEnabled = Selected is not null;

    private async System.Threading.Tasks.Task SaveAsPreset()
    {
        var name = await DialogLauncher.Ask<TextPromptWindow, string?>(_owner, () => new TextPromptWindow(SavePromptTitle, SavePromptLabel, SavePromptDefault));
        if (string.IsNullOrWhiteSpace(name)) return;
        if (!_presets.Save(_kind, name, _snapshot())) return;
        _presetsChanged();
        FillList(name.Trim());
    }

    private void DeleteSelected()
    {
        if (Selected is { } p) _rows.Delete(p.Name);
    }
}
