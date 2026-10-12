using System.Diagnostics;
using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using TabForge.Plugins;
using TabForge.Views.Presets;

namespace TabForge.Views.NoteMaps;

// Owns: the note map processor's two map rows in the MIDI processing window: the "Song plays notes of" and "Instrument expects"
//   drop-downs (shipped maps, then the user's), the buttons under the second one (Edit map…, New map…, Manage), and the summary of
//   the note changes that result.
// Does not own: storing a parameter (the window does it as one edit, through the action it passes), the Overrides box, the editor
//   window (DrumMapEditorWindow) or the maps (DrumMapLibrary, DrumMapStore).
// Tests: TestDrumMapRemap (the summary text); capture steps "midi-add" with "noteMap" and "note-map-editor".
internal sealed class NoteMapPane
{
    private const string UnverifiedTip = "Written from the instrument's documentation and not checked against the instrument: compare it with the instrument's own MIDI map before relying on it.";
    private readonly Window _owner;
    private readonly JsonObject _values;
    private readonly Action<string, string> _set;
    private readonly DrumMapStore _store = new();
    private readonly Dictionary<string, ComboBox> _combos = new();
    private readonly Dictionary<string, PresetRows> _rows = new();
    private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(140, 8, 0, 0) };
    private bool _filling;

    /// <param name="values">The selected processor's parameter values (From, To, Overrides).</param>
    /// <param name="set">Stores one parameter (key, map name) as an undoable edit.</param>
    public NoteMapPane(Window owner, JsonObject values, Action<string, string> set)
    {
        _owner = owner; _values = values; _set = set;
        _store.Changed += Fill;
        System.Windows.Automation.AutomationProperties.SetLiveSetting(_summary, System.Windows.Automation.AutomationLiveSetting.Polite);
    }

    private string Value(string key) => _values[key]?.GetValue<string>() ?? "";

    /// <summary>One map parameter's row; the "Instrument expects" row also carries the map buttons and the summary.</summary>
    public FrameworkElement Row(MidiParamInfo p)
    {
        var combo = UiIds.Id(new ComboBox { Width = 200 }, "Midi.NoteMap." + p.Key, p.Label);
        TooltipShortcuts.Bind(combo, p.Tip.Length > 0 ? p.Tip : p.Label, null);
        _combos[p.Key] = combo;
        _rows[p.Key] = PresetRows.Attach(combo, PresetKind.DrumMap, () => _store, o => o is ComboBoxItem { Tag: string name } && DrumMapLibrary.Find(name) is { IsUser: true } map ? map.Name : null);
        combo.SelectionChanged += (_, _) => { if (!_filling && combo.SelectedItem is ComboBoxItem { Tag: string name }) _set(p.Key, name); };
        var label = new TextBlock { Text = p.Label, Width = 140, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = p.Tip.Length > 0 ? p.Tip : p.Label };
        label.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(label); line.Children.Add(combo);
        var stack = new StackPanel { Width = 460, Margin = new Thickness(0, 3, 12, 3) };
        stack.Children.Add(line);
        if (p.Key == "To")
        {
            var edit = Push("Edit map…", "See or change which note each drum of the song sends to the instrument");
            var create = Push("New map…", "Make your own map: type the note your instrument needs for each drum");
            var manage = Push("More ▾", "Open your drum maps folder, or read the map files again");
            edit.Click += (_, _) => Edit(DrumMapLibrary.Find(Value("To")));
            create.Click += (_, _) => Edit(null);
            manage.Click += (_, _) => { manage.ContextMenu = ManageMenu(); manage.ContextMenu.PlacementTarget = manage; manage.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom; manage.ContextMenu.IsOpen = true; };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(140, 6, 0, 0) };
            buttons.Children.Add(edit); buttons.Children.Add(create); buttons.Children.Add(manage);
            stack.Children.Add(buttons);
            stack.Children.Add(_summary);
            var overrides = new TextBlock { Text = "For a single note, Overrides (under Advanced) also works, e.g. 40>38, or 42>x to drop a note.", TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(140, 6, 0, 0) };
            overrides.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            stack.Children.Add(overrides);
        }
        Fill();
        return stack;
    }

    private static Button Push(string text, string tip)
    {
        var button = new Button { Content = text, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 6, 0) };
        TooltipShortcuts.Bind(button, tip, null);
        return button;
    }

    /// <summary>The overflow of the map buttons: the folder and reading the files again.</summary>
    private ContextMenu ManageMenu()
    {
        var menu = new ContextMenu();
        MenuItem Item(string header, string tip, Action run) { var item = new MenuItem { Header = header, ToolTip = tip }; item.Click += (_, _) => run(); menu.Items.Add(item); return item; }
        Item("Open my drum maps folder", "The folder that holds your maps as .json files", () =>
        {
            try { Directory.CreateDirectory(DrumMapLibrary.UserFolder); Process.Start(new ProcessStartInfo(DrumMapLibrary.UserFolder) { UseShellExecute = true }); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { Services.Trace.Error(Services.Trace.Ui, "note map: open the drum maps folder: " + ex.Message); }
        });
        Item("Reload maps", "Read the drum map files again (after changing a file by hand)", () => { DrumMapLibrary.Reload(); Fill(); });
        return menu;
    }

    private void Edit(DrumMap? map)
    {
        var source = DrumMapLibrary.Find(Value("From")) ?? DrumMapLibrary.Find("General MIDI");
        var saved = DrumMapEditorWindow.Edit(_owner, map, source);
        if (saved is null) { Fill(); return; }   // maps may have been renamed or deleted from the editor's Manage
        _values["To"] = saved;   // shown at once; the edit below stores it and sends the new table to the engine
        Fill();
        _set("To", saved);
    }

    /// <summary>Lists the maps again in both drop-downs and writes the summary.</summary>
    private void Fill()
    {
        _filling = true;
        try
        {
            var unreadable = _store.Unreadable();
            foreach (var (key, combo) in _combos)
            {
                var current = Value(key);
                combo.Items.Clear();
                combo.Items.Add(new ComboBoxItem { Content = "(none: notes unchanged)", Tag = DrumMapLibrary.None });
                foreach (var map in DrumMapLibrary.All())
                    combo.Items.Add(_rows[key].Item(map.IsUser ? $"★ {map.Name}" : map.Name, map.Name, map.IsUser ? "Your own map" : map.Verified ? null : UnverifiedTip));
                foreach (var file in unreadable)
                    combo.Items.Add(new ComboBoxItem { Content = $"★ {file} (unreadable)", IsEnabled = false, ToolTip = "This file in your drum maps folder is not a readable map" });
                var name = DrumMapLibrary.Find(current)?.Name ?? current;
                var selected = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag is string tag && string.Equals(tag, name, StringComparison.OrdinalIgnoreCase));
                if (selected is null) combo.Items.Add(selected = new ComboBoxItem { Content = $"{current} (missing: notes unchanged)", Tag = current, ToolTip = "No map of this name is installed" });
                _rows[key].AddManage(combo.Items);
                combo.SelectedItem = selected;
            }
        }
        finally { _filling = false; }
        Refresh();
    }

    /// <summary>Writes the summary of the note changes again (a parameter changed).</summary>
    public void Refresh() => _summary.Text = DrumMapLibrary.Describe(Value("From"), Value("To"), Value("Overrides"));
}
