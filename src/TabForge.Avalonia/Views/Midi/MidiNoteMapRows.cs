using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Avalonia.Views.NoteMaps;
using TabForge.Avalonia.Views.Presets;
using TabForge.Plugins;
using TabForge.Presenters;

namespace TabForge.Avalonia.Views.Midi;

// Owns: the note map processor's two map rows in the MIDI processing window: the "Song plays notes of" and "Instrument expects" drop-downs (the shipped maps, then
//   the user's), the buttons under the second one (Edit map…, New map…), and the summary of the note changes that result.
// Does not own: storing a parameter (the window does it as one edit, through the action it passes), the Overrides box, the editor window (DrumMapEditorWindow), the maps (DrumMapLibrary).
// Tests: no dedicated test class.
public sealed class MidiNoteMapRows
{
    public const string UnverifiedTip = "Written from the instrument's documentation and not checked against the instrument: compare it with the instrument's own MIDI map before relying on it.";
    private readonly Window _owner;
    private readonly JsonObject _values;
    private readonly Action<string, string> _set;
    private readonly Dictionary<string, ComboBox> _combos = new();
    private readonly DrumMapStore _mapStore = new();
    private readonly TextBlock _summary = new() { TextWrapping = global::Avalonia.Media.TextWrapping.Wrap, Name = "NoteMapSummary" };

    /// <summary>The editor column's scroller: while set, labels stay inside its viewport instead of pushing editors out.</summary>
    public ScrollViewer? Viewer { get; set; }
    private bool _filling;

    /// <param name="values">The selected processor's parameter values (From, To, Overrides).</param>
    /// <param name="set">Stores one parameter (key, map name) as an undoable edit.</param>
    public MidiNoteMapRows(Window owner, JsonObject values, Action<string, string> set)
    {
        (_owner, _values, _set) = (owner, values, set);
    }

    private string Value(string key) => _values[key]?.GetValue<string>() ?? "";

    /// <summary>One map parameter's row; the "Instrument expects" row also carries the map buttons and the summary.</summary>
    public Control Row(MidiParamInfo p)
    {
        var combo = new ComboBox { Name = "NoteMap_" + p.Key, HorizontalAlignment = HorizontalAlignment.Stretch };
        MidiSharedRows.StretchDrop(combo, p.Tip.Length > 0 ? p.Tip : p.Label);
        _combos[p.Key] = combo;
        _ = PresetRows.Attach(combo, () => _mapStore,
            item => item switch
            {
                ComboBoxItem { Tag: string tag } when DrumMapLibrary.Find(tag) is { IsUser: true } map => map.Name,
                string text when DrumMapLibrary.Find(text.TrimStart('★', ' ')) is { IsUser: true } map => map.Name,
                _ => null,
            },
            () => PresetCatalogKind.DrumMap, () => null, Fill, null, item => item is ComboBoxItem combo ? combo.Content?.ToString() ?? "" : item?.ToString() ?? "",
            item => item is ComboBoxItem { Tag: string tag } && DrumMapLibrary.Find(tag) is { IsUser: false });
        combo.SelectionChanged += (_, _) =>
        {
            if (_filling) return;
            if (PresetRowText.IsManage(combo.SelectedItem)) return;   // PresetRows reverts the row and opens the manager
            if (combo.SelectedItem is ComboBoxItem { Tag: string name } && name != Value(p.Key)) _set(p.Key, name);
        };
        var fields = Viewer is null ? MidiSharedRows.ParamRow(p.Label, p.Tip.Length > 0 ? p.Tip : p.Label, combo) : MidiSharedRows.ParamRow(p.Label, p.Tip.Length > 0 ? p.Tip : p.Label, combo, Viewer);
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Stretch };
        stack.Spaced(DialogMetrics.MidiRowGap);
        stack.Children.Add(fields);
        if (p.Key == "To")
        {
            var edit = Push("Edit map…", NoteMapEditEntry.EditButtonTip, "NoteMapEdit");
            var create = Push("New map…", "Make your own map: type the note your instrument needs for each drum", "NoteMapNew");
            edit.Click += (_, _) => Edit(DrumMapLibrary.Find(Value("To")));
            create.Click += (_, _) => Edit(null);
            var buttons = new WrapPanel { Orientation = Orientation.Horizontal };
            buttons.Spaced(DialogMetrics.MidiBarGap);
            buttons.Children.Add(edit); buttons.Children.Add(create);
            stack.Children.Add(buttons);
            _summary.Spaced(DialogMetrics.MidiBarGap);
            stack.Children.Add(_summary);
            var overrides = new TextBlock
            {
                Text = "For a single note, the Overrides setting also works, e.g. 40>38, or 42>x to drop a note.", TextWrapping = global::Avalonia.Media.TextWrapping.Wrap,
                FontSize = 11, [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("MutedBrush"),
            };
            overrides.Spaced(DialogMetrics.MidiBarGap);
            stack.Children.Add(overrides);
        }
        Fill();
        return stack;
    }

    private static Button Push(string text, string tip, string name)
    {
        var button = new Button { Content = text, Name = name, HorizontalAlignment = HorizontalAlignment.Left };
        DialogMetrics.WithPadding(button, DialogMetrics.MidiItemPad);
        button.Spaced(DialogMetrics.MidiInputGap);
        MidiSharedRows.Tip(button, tip);
        return button;
    }

    private async void Edit(DrumMap? map)
    {
        try
        {
            var source = DrumMapLibrary.FindOrGeneralMidi(Value("From"));
            var editor = new DrumMapEditorWindow(map, source);
            await editor.ShowDialog(_owner);
            if (editor.Saved is not { } saved) { Fill(); return; }   // maps may have been renamed or deleted from the editor
            _values["To"] = saved;   // shown at once; the edit below stores it and sends the new table to the engine
            Fill();
            _set("To", saved);
        }
        catch (Exception ex)
        {
            // Reported, not swallowed (UiTasks.Forget pattern): the failure shows in the row summary.
            _summary.Text = "Drum map edit failed: " + ex.Message;
        }
    }

    /// <summary>Lists the maps again in both drop-downs and writes the summary.</summary>
    public void Fill()
    {
        _filling = true;
        try
        {
            foreach (var (key, combo) in _combos)
            {
                var current = Value(key);
                var items = new List<ComboBoxItem> { new() { Content = "(none: notes unchanged)", Tag = DrumMapLibrary.None } };
                foreach (var map in DrumMapLibrary.All())
                {
                    var item = new ComboBoxItem { Content = map.IsUser ? $"★ {map.Name}" : map.Name, Tag = map.Name };
                    if (map.IsUser) MidiSharedRows.Tip(item, "Your own map");
                    else if (!map.Verified) MidiSharedRows.Tip(item, UnverifiedTip);
                    items.Add(item);
                }
                var name = DrumMapLibrary.Find(current)?.Name ?? current;
                var selected = items.FirstOrDefault(i => i.Tag is string tag && string.Equals(tag, name, StringComparison.OrdinalIgnoreCase));
                if (selected is null)
                {
                    selected = new ComboBoxItem { Content = $"{current} (missing: notes unchanged)", Tag = current };
                    MidiSharedRows.Tip(selected, "No map of this name is installed");
                    items.Add(selected);
                }
                PresetRowText.AppendManage(items);
                combo.ItemsSource = items;
                combo.SelectedItem = selected;
            }
        }
        finally { _filling = false; }
        Refresh();
    }

    /// <summary>Writes the summary of the note changes again (a parameter changed).</summary>
    public void Refresh() => _summary.Text = DrumMapLibrary.Describe(Value("From"), Value("To"), Value("Overrides"));
}
