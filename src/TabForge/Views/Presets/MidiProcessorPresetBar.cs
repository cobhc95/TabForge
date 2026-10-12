using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using TabForge.Plugins;
using TabForge.Services;

namespace TabForge.Views.Presets;

// Owns: the preset row in a MIDI processor's settings pane: a drop-down of the user's presets of that processor type (its parameter
//   values) after the presets that come with the app for that type, with the user's rows organisable in the open list (PresetRows),
//   and Save preset…; the closed box names the preset whose values the processor has; reading a preset file into checked values and writing one.
// Does not own: applying the values (the MIDI processing window does it as one edit, through the action it passes), the parameter
//   controls, or presets of the whole processor list.
// Tests: TestPresetStoresOnDisk.
internal sealed class MidiProcessorPresetBar
{
    private const string Hint = "Load preset…";
    private readonly Window _owner;
    private readonly Func<string?> _askName;
    private readonly Action<PluginMidiProcessor, string> _apply;
    private readonly ComboBox _presets = new() { Width = 200 };
    private readonly Button _save = new() { Content = "Save preset…", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(6, 0, 0, 0) };
    private readonly PresetRows _rows;
    private sealed record BuiltIn(string Params);
    private PluginMidiProcessor? _processor;
    private PresetFolderStore? _store;
    private bool _filling;

    public StackPanel Root { get; } = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10), Visibility = Visibility.Collapsed };

    public MidiProcessorPresetBar(Window owner, Func<string?> askName, Action<PluginMidiProcessor, string> apply)
    {
        _owner = owner; _askName = askName; _apply = apply;
        UiIds.Id(_presets, "Midi.ProcessorPresets", "Presets of this processor");
        TooltipShortcuts.Bind(_presets, "Presets of this processor: picking one sets its parameters (one undo step). Rename, duplicate, delete or drag your own in the open list", null);
        TooltipShortcuts.Bind(_save, "Save this processor's parameter values as a preset", null);
        Root.Children.Add(_presets); Root.Children.Add(_save);
        _rows = PresetRows.Attach(_presets, PresetKind.MidiProcessor, () => _store, o => o is ComboBoxItem { Tag: string name } ? name : null, key: () => _processor?.Type);
        _rows.Reserved = () => BuiltIns().Select(b => b.Name);
        _presets.SelectionChanged += (_, _) => Pick();
        _save.Click += (_, _) => Save();
    }

    /// <summary>Shows the presets of the selected processor's type; hidden when there is none or its type is unknown.</summary>
    public void Show(PluginMidiProcessor? processor)
    {
        var known = processor is not null && MidiProcessorCatalog.Find(processor.Type) is not null;
        Root.Visibility = known ? Visibility.Visible : Visibility.Collapsed;
        _processor = known ? processor : null;
        if (_store is not null) _store.Changed -= Fill;
        _store = known ? PresetFolderStore.MidiProcessor(processor!.Type) : null;
        if (_store is not null) _store.Changed += Fill;
        Fill();
    }

    private IReadOnlyList<(string Name, string Params)> BuiltIns() => _processor is { } p ? MidiProcessorCatalog.BuiltInProcessorPresets(p.Type) : Array.Empty<(string, string)>();

    /// <summary>The rows of the drop-down as (text, is built-in); the closed box shows <see cref="Shown"/>.</summary>
    internal IReadOnlyList<(string Text, bool BuiltIn)> Listed => _presets.Items.OfType<ComboBoxItem>().Skip(1).Where(i => i.Tag is not null).Select(i => ((string)i.Content, i.Tag is BuiltIn)).ToList();
    internal string Shown => (string)((ComboBoxItem)_presets.SelectedItem).Content;
    /// <summary>Picks a row by its text, as a click does.</summary>
    internal void PickRow(string text) => _presets.SelectedItem = _presets.Items.OfType<ComboBoxItem>().First(i => (string)i.Content == text);

    // ponytail: reads every user preset of the type to find the one that matches; cache per store change if a folder ever holds hundreds.
    private void Fill()
    {
        _filling = true;
        try
        {
            var current = _processor is { } p ? Normalize(p.Type, p.Params) : null;
            ComboBoxItem? match = null;
            _presets.Items.Clear();
            _presets.Items.Add(new ComboBoxItem { Content = Hint });
            foreach (var (name, values) in BuiltIns())
            {
                var item = new ComboBoxItem { Content = name, Tag = new BuiltIn(values), ToolTip = "Comes with the app" };
                _presets.Items.Add(item);
                if (current is not null && Normalize(_processor!.Type, values) == current) match ??= item;
            }
            foreach (var name in _store?.Names() ?? Array.Empty<string>())
            {
                var item = _rows.Item(name, name);
                _presets.Items.Add(item);
                if (match is null && current is not null && Read(_store!, _processor!.Type, name) == current) match = item;
            }
            _rows.AddManage(_presets.Items);
            _presets.SelectedItem = match ?? _presets.Items[0];
        }
        finally { _filling = false; }
    }

    private void Pick()
    {
        if (_filling || _processor is not { } processor || _store is not { } store || _presets.SelectedItem is not ComboBoxItem { Tag: { } tag }) return;
        var values = tag is BuiltIn builtIn ? Normalize(processor.Type, builtIn.Params) : Read(store, processor.Type, (string)tag);
        if (values is not null) _apply(processor, values);
        else ThemedMessageBox.Show(_owner, "That preset could not be read.", _owner.Title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void Save()
    {
        if (_processor is not { } processor || _store is not { } store || _askName() is not { } name) return;
        if ((PresetRules.Error(name) ?? (BuiltIns().Any(b => string.Equals(b.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)) ? PresetRows.ReservedText : null)) is { } error) { ThemedMessageBox.Show(_owner, error, _owner.Title, MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        if (store.Names().Contains(name, StringComparer.OrdinalIgnoreCase) && ThemedMessageBox.Show(_owner, $"Replace the preset \"{name}\"?", _owner.Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try { Write(store, name, processor); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { ThemedMessageBox.Show(_owner, "The preset could not be saved: " + ex.Message, _owner.Title, MessageBoxButton.OK, MessageBoxImage.Warning); } // Not logged: the message is shown in a dialog
        Fill();
    }

    /// <summary>Writes the processor's parameter values as a preset of its type (a failed write leaves an older preset intact).</summary>
    internal static void Write(PresetFolderStore store, string name, PluginMidiProcessor processor)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(processor.Params);
        FilePathPolicy.WriteAtomically(store.PathOf(name), s => s.Write(bytes), createDirectory: true);
    }

    /// <summary>A preset's values for a processor of that type: only the parameters the type has, numbers inside their range, missing ones at their default. Null when unreadable.</summary>
    internal static string? Read(PresetFolderStore store, string type, string name)
    {
        try { return Normalize(type, System.Text.Encoding.UTF8.GetString(InputLimits.ReadBoundedBytes(store.PathOf(name), PluginMidiProcessor.MaxParamsChars * 4L, "preset"))); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Services.Trace.Error(Services.Trace.Ui, "MIDI processor preset: read: " + ex.Message);
            return null;
        }
    }

    /// <summary>Parameter text as the checked values of that type, written one way, so two texts with the same values compare equal. Null when unreadable.</summary>
    internal static string? Normalize(string type, string text)
    {
        if (MidiProcessorCatalog.Find(type) is not { } info) return null;
        try
        {
            if (text.Length > PluginMidiProcessor.MaxParamsChars || JsonNode.Parse(text) is not JsonObject) return null;
            var parsed = MidiProcessorCatalog.ParseParams(new PluginMidiProcessor { Type = type, Params = text }, info);
            var values = new JsonObject();
            foreach (var p in info.Params)
                values[p.Key] = p.Kind == MidiParamKind.Number && parsed[p.Key]!.GetValue<double>() is var v ? Math.Clamp(double.IsFinite(v) ? v : p.Default, p.Min, p.Max) : parsed[p.Key]!.DeepClone();
            return values.ToJsonString();
        }
        catch (Exception ex) when (ex is InvalidDataException or System.Text.Json.JsonException or InvalidOperationException or FormatException)
        {
            Services.Trace.Error(Services.Trace.Ui, "MIDI processor preset: values: " + ex.Message);
            return null;
        }
    }
}
