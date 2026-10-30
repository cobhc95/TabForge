using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Avalonia.Views.Fx;
using TabForge.Avalonia.Views.Presets;
using TabForge.Plugins;
using TabForge.Presets;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Views.Midi;

// Owns: the two preset rows of the MIDI processing window: the whole list's presets (the ones that come with the app, then the user's, with Save preset…
//   and Delete) and the preset row of the selected processor (its parameter values: the built-in ones of its type, then the user's, with Save preset…), and
//   reading a preset file into checked values and writing one.
// Does not own: applying a preset (the window does it as one edit, through the actions it passes), the files' folder rules (PresetFolderStore), the parameters.
// Tests: no dedicated test class.
public sealed class MidiPresetBars
{
    public const string Hint = "Load preset…", Caption = "MIDI processing";
    private const long MaxListPresetBytes = 512 * 1024;

    /// <summary>One row of a preset drop-down: what it shows, and where its content comes from.</summary>
    internal sealed record Entry(string Text, Func<List<PluginMidiProcessor>>? Build = null, string? Name = null, string? BuiltInParams = null, bool Manage = false, bool Missing = false, string? Key = null)
    {
        public override string ToString() => Text;
    }

    private readonly Window _owner;
    private readonly Func<FxChainDialogs> _dialogs;
    private readonly Func<List<PluginMidiProcessor>> _current;
    private readonly PluginSlot _slot;
    private readonly Action<List<PluginMidiProcessor>, AppliedPreset?> _applyList;
    private readonly Action<PluginMidiProcessor, string, AppliedPreset?> _applyProcessor;
    private readonly PresetFolderStore _listStore = PresetFolderStore.MidiChains();
    private PresetFolderStore? _processorStore;
    private PluginMidiProcessor? _processor;
    private bool _filling;

    /// <summary>The whole list's presets: a drop-down, Save preset… and Delete.</summary>
    public ComboBox ListBox { get; } = new() { MinWidth = DialogMetrics.MidiChoiceMinWidth, HorizontalAlignment = HorizontalAlignment.Stretch, Name = "PresetBox" };
    public Button SaveList { get; } = Small("Save preset…", "Save the whole processor list as a preset", "SavePresetButton");
    public Button DeleteList { get; } = Small("Delete", "Delete the selected saved preset", "DeletePresetButton");
    /// <summary>The selected processor's presets: a drop-down and Save preset….</summary>
    public ComboBox ProcessorBox { get; } = new() { MinWidth = DialogMetrics.MidiChoiceMinWidth, HorizontalAlignment = HorizontalAlignment.Stretch, Name = "ProcessorPresetBox" };
    public Button SaveProcessor { get; } = Small("Save preset…", "Save this processor's parameter values as a preset", "SaveProcessorPresetButton");
    public WrapPanel ProcessorRow { get; } = new() { Orientation = Orientation.Horizontal, IsVisible = false, Name = "ProcessorPresetRow" };

    /// <param name="current">The plug-in's processors as they are now (what Save preset… writes).</param>
    /// <param name="applyList">Replaces the plug-in's whole list with a preset's processors (one undo step).</param>
    /// <param name="applyProcessor">Sets one processor's parameter text (one undo step).</param>
    public MidiPresetBars(Window owner, Func<FxChainDialogs> dialogs, Func<List<PluginMidiProcessor>> current, PluginSlot slot,
        Action<List<PluginMidiProcessor>, AppliedPreset?> applyList, Action<PluginMidiProcessor, string, AppliedPreset?> applyProcessor)
    {
        (_owner, _dialogs, _current, _slot, _applyList, _applyProcessor) = (owner, dialogs, current, slot, applyList, applyProcessor);
        MidiSharedRows.Tip(ListBox, "Load a preset: replaces this plug-in's whole processor list (Undo works in the FX window). Delete or reorder your own with Manage presets…");
        MidiSharedRows.Tip(ProcessorBox, "Presets of this processor: picking one sets its parameters (one undo step). Delete or reorder your own with Manage presets…");
        ProcessorRow.Children.Add(ProcessorBox);
        ProcessorRow.Children.Add(SaveProcessor);
        ProcessorRow.Spaced(DialogMetrics.MidiNoteGap);
        _ = PresetRows.Attach(ListBox, () => _listStore, item => item is Entry { Name: not null, Manage: false } entry ? entry.Name : null,
            () => PresetCatalogKind.MidiChain, () => null, () => FillList(ListSelectedKey()), item => item is Entry { Manage: true }, isBuiltIn: item => item is Entry { Build: not null });
        _ = PresetRows.Attach(ProcessorBox, () => _processorStore, item => item is Entry { Name: not null, Manage: false } entry ? entry.Name : null,
            () => PresetCatalogKind.MidiProcessor, () => _processor?.Type, FillProcessor, item => item is Entry { Manage: true }, isBuiltIn: item => item is Entry { BuiltInParams: not null });
        ListBox.SelectionChanged += async (_, _) => await ListPicked();
        SaveList.Click += async (_, _) => await SaveListAsync();
        DeleteList.Click += async (_, _) => await DeleteListAsync();
        ProcessorBox.SelectionChanged += async (_, _) => await ProcessorPicked();
        SaveProcessor.Click += async (_, _) => await SaveProcessorAsync();
        _listStore.Changed += () => FillList(ListSelectedKey());
        FillList(null);
        PresetCombo.Watch(ListBox, () => _slot.AppliedMidiPreset, () => Snapshot(_current()), () => FillList(ListSelectedKey()));
        PresetCombo.Watch(ProcessorBox, () => _processor?.AppliedPreset, () => _processor is { } p ? Normalize(p.Type, p.Params) : null, FillProcessor);
    }

    private static Button Small(string text, string tip, string name)
    {
        var button = new Button { Content = text, Name = name, HorizontalAlignment = HorizontalAlignment.Left };
        DialogMetrics.WithPadding(button, DialogMetrics.MidiItemPad);
        button.Spaced(DialogMetrics.MidiButtonGap);
        MidiSharedRows.Tip(button, tip);
        return button;
    }

    private static string PresetName(string key) => Path.GetFileNameWithoutExtension(key);

    // ---------- the whole list ----------

    private void FillList(string? select)
    {
        _filling = true;
        try
        {
            var applied = _slot.AppliedMidiPreset;
            var names = _listStore.Names();
            var builtIns = MidiProcessorCatalog.BuiltInPresets.Select(b => b.Name).ToList();
            var current = Snapshot(_current());
            var items = new List<Entry> { new(Hint) };
            items.AddRange(MidiProcessorCatalog.BuiltInPresets.Select(b =>
                new Entry(Decorate(b.Name, b.Name, applied, current, true), b.Build, Key: b.Name)));
            items.AddRange(names.Select(n => new Entry($"★ {Decorate(n, n, applied, current, true)}", Name: n, Key: n)));
            if (applied is { } missing && !names.Contains(missing.Name, StringComparer.OrdinalIgnoreCase) && !builtIns.Contains(missing.Name, StringComparer.OrdinalIgnoreCase))
                items.Add(new Entry($"★ {Decorate(missing.Name, missing.Name, applied, current, false)}", Name: missing.Name, Missing: true, Key: missing.Name));
            items.Add(new Entry(PresetRowText.Manage, Manage: true));
            ListBox.ItemsSource = items;
            var chosen = select is not null ? select : applied?.Name;
            ListBox.SelectedItem = items.FirstOrDefault(i => chosen is not null && string.Equals(i.Key, chosen, StringComparison.OrdinalIgnoreCase)) ?? items[0];
            DeleteList.IsEnabled = ListBox.SelectedItem is Entry { Name: not null, Missing: false };
        }
        finally { _filling = false; }
    }

    private static string Decorate(string shown, string name, AppliedPreset? applied, string? current, bool exists) =>
        PresetCombo.Decorate(shown, name, applied, current, exists);

    /// <summary>The list in one stable text form, so an edit can be told from the values a preset applied.</summary>
    internal static string Snapshot(IEnumerable<PluginMidiProcessor> list) =>
        System.Text.Json.JsonSerializer.Serialize(list.Select(p => new { p.Type, p.Enabled, p.Params }));

    // The chosen list row by its preset name (its text for a built-in row), so a refresh keeps the choice in the box.
    private string? ListSelectedKey() => ListBox.SelectedItem is Entry entry ? entry.Key ?? entry.Name : null;

    private async Task ListPicked()
    {
        if (_filling || ListBox.SelectedItem is not Entry entry) { DeleteList.IsEnabled = false; return; }
        DeleteList.IsEnabled = entry.Name is not null && !entry.Missing;
        if (entry.Manage || entry.Missing) return;   // PresetRows reverts the Manage row and opens the manager; a missing preset keeps the settings
        List<PluginMidiProcessor>? list = null;
        if (entry.Build is { } build) list = build();
        else if (entry.Name is { } name) list = ReadList(name);
        else return;
        if (list is null) { await _dialogs().Ask(_owner, "That preset could not be read.", Caption, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        var kept = list.Where(p => p is not null && p.Params.Length <= PluginMidiProcessor.MaxParamsChars).Take(MidiProcessingListPresenter.MaxProcessors).ToList();
        var presetName = entry.Key ?? entry.Name ?? entry.Text;
        _applyList(kept, PresetCombo.Remember(presetName, "MidiChain", Snapshot(kept)));
    }

    private List<PluginMidiProcessor>? ReadList(string name)
    {
        try
        {
            var bytes = InputLimits.ReadBoundedBytes(_listStore.PathOf(name), MaxListPresetBytes, "MIDI preset");
            return JsonSerializer.Deserialize<List<PluginMidiProcessor>>(bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            Trace.Error(Trace.Ui, "MIDI processing: read preset: " + ex.Message);
            return null;
        }
    }

    /// <summary>Saves the whole list under a name the user types; false when cancelled or refused.</summary>
    public async Task<bool> SaveListAsync(string? name = null)
    {
        name ??= (await _dialogs().SavePresetName(_owner, new PresetNameRequest("Save preset", "Preset name:",
            "", _listStore.Names(), MidiProcessorCatalog.BuiltInPresets.Select(b => b.Name).ToList(), PresetRules.Error)))?.Trim();
        if (string.IsNullOrEmpty(name)) return false;
        if (PresetRules.Error(name) is { } error) { await _dialogs().Tell(_owner, error, Caption); return false; }
        if (_listStore.Names().Contains(name, StringComparer.OrdinalIgnoreCase)
            && await _dialogs().Ask(_owner, $"Replace the preset \"{name}\"?", Caption, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != MessageBoxAnswer.Yes) return false;
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(_current(), new JsonSerializerOptions { WriteIndented = true });
            FilePathPolicy.WriteAtomically(_listStore.PathOf(name), s => s.Write(bytes), createDirectory: true);   // a failed write leaves the previous preset intact
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Trace.Error(Trace.Ui, "MIDI processing: save preset: " + ex.Message);
            await _dialogs().Ask(_owner, "The preset could not be saved: " + ex.Message, Caption, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        _slot.AppliedMidiPreset = PresetCombo.Remember(name, "MidiChain", Snapshot(_current()));
        FillList(name);
        return true;
    }

    private async Task DeleteListAsync()
    {
        if (ListBox.SelectedItem is not Entry { Name: { } name }) return;
        if (await _dialogs().Ask(_owner, $"Delete the preset \"{name}\"?", Caption, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != MessageBoxAnswer.Yes) return;
        try { _listStore.Delete(name); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Trace.Error(Trace.Ui, "MIDI processing: delete preset: " + ex.Message); }
        FillList(ListSelectedKey());   // a deleted preset keeps its name and values and shows " (missing)" until another is picked
    }

    // ---------- the selected processor ----------

    /// <summary>Shows the presets of the selected processor's type; hidden when there is none or its type is unknown.</summary>
    public void Show(PluginMidiProcessor? processor)
    {
        var known = processor is not null && MidiProcessorCatalog.Find(processor.Type) is not null;
        ProcessorRow.IsVisible = known;
        _processor = known ? processor : null;
        if (_processorStore is not null) _processorStore.Changed -= FillProcessor;
        _processorStore = known ? PresetFolderStore.MidiProcessor(processor!.Type) : null;
        if (_processorStore is not null) _processorStore.Changed += FillProcessor;
        FillProcessor();
    }

    private IReadOnlyList<(string Name, string Params)> BuiltIns() => _processor is { } p ? MidiProcessorCatalog.BuiltInProcessorPresets(p.Type) : Array.Empty<(string, string)>();

    /// <summary>The rows of the processor drop-down as (text, is built-in); the closed box shows <see cref="ProcessorShown"/>.</summary>
    internal IReadOnlyList<(string Text, bool BuiltIn)> Listed => ProcessorBox.Items.OfType<Entry>().Skip(1).Where(i => !i.Manage).Select(i => (i.Text, i.BuiltInParams is not null)).ToList();
    internal string ProcessorShown => (ProcessorBox.SelectedItem as Entry)?.Text ?? "";
    internal void PickProcessorRow(string text) => ProcessorBox.SelectedItem = ProcessorBox.Items.OfType<Entry>().First(i => i.Text == text);

    private void FillProcessor()
    {
        _filling = true;
        try
        {
            var current = _processor is { } p ? Normalize(p.Type, p.Params) : null;
            var applied = _processor?.AppliedPreset;
            var names = _processorStore?.Names() ?? Array.Empty<string>();
            var builtIns = BuiltIns();
            Entry? match = null;
            var items = new List<Entry> { new(Hint) };
            foreach (var (name, values) in builtIns)
            {
                var item = new Entry(PresetCombo.Decorate(name, name, applied, current, true), BuiltInParams: values);
                items.Add(item);
                if (applied is not null && string.Equals(applied.Name, name, StringComparison.OrdinalIgnoreCase)) match ??= item;
                else if (current is not null && Normalize(_processor!.Type, values) == current) match ??= item;
            }
            foreach (var name in names)
            {
                var item = new Entry(PresetCombo.Decorate(name, name, applied, current, true), Name: name);
                items.Add(item);
                if (match is null && applied is not null && string.Equals(applied.Name, name, StringComparison.OrdinalIgnoreCase)) match = item;
                else if (match is null && current is not null && Read(_processorStore!, _processor!.Type, name) == current) match = item;
            }
            if (applied is { } missing && !names.Contains(missing.Name, StringComparer.OrdinalIgnoreCase)
                && !builtIns.Any(b => string.Equals(b.Name, missing.Name, StringComparison.OrdinalIgnoreCase)))
            {
                var item = new Entry(PresetCombo.Decorate(missing.Name, missing.Name, applied, current, false), Name: missing.Name, Missing: true);
                items.Add(item);
                match ??= item;
            }
            items.Add(new Entry(PresetRowText.Manage, Manage: true));
            ProcessorBox.ItemsSource = items;
            ProcessorBox.SelectedItem = match ?? items[0];
        }
        finally { _filling = false; }
    }

    private async Task ProcessorPicked()
    {
        if (_filling || _processor is not { } processor || _processorStore is not { } store || ProcessorBox.SelectedItem is not Entry entry) return;
        if (entry.Manage || entry.Missing) return;   // PresetRows reverts the row; a missing preset keeps the settings
        if (entry.Text == Hint) return;
        var values = entry.BuiltInParams is { } builtIn ? Normalize(processor.Type, builtIn) : Read(store, processor.Type, entry.Name!);
        if (values is not null) _applyProcessor(processor, values, PresetCombo.Remember(entry.Name ?? entry.Text, "MidiProcessor:" + processor.Type, values));
        else await _dialogs().Ask(_owner, "That preset could not be read.", Caption, MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    /// <summary>Saves the selected processor's values under a name the user types (or <paramref name="name"/>); false when cancelled or refused.</summary>
    public async Task<bool> SaveProcessorAsync(string? name = null)
    {
        if (_processor is not { } processor || _processorStore is not { } store) return false;
        var builtIns = BuiltIns().Select(b => b.Name).ToList();
        name ??= (await _dialogs().SavePresetName(_owner, new PresetNameRequest("Save preset", "Preset name:",
            "", store.Names(), builtIns, text => PresetRules.Error(text) ?? (builtIns.Contains(text.Trim(), StringComparer.OrdinalIgnoreCase) ? PluginPresetNamePresenter.BuiltInError : null))))?.Trim();
        if (string.IsNullOrEmpty(name)) return false;
        var reserved = BuiltIns().Any(b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase)) ? "A preset that comes with the app has this name. Choose another name." : null;
        if ((PresetRules.Error(name) ?? reserved) is { } error) { await _dialogs().Ask(_owner, error, Caption, MessageBoxButtons.OK, MessageBoxIcon.Warning); return false; }
        if (store.Names().Contains(name, StringComparer.OrdinalIgnoreCase)
            && await _dialogs().Ask(_owner, $"Replace the preset \"{name}\"?", Caption, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != MessageBoxAnswer.Yes) return false;
        try { Write(store, name, processor); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Trace.Error(Trace.Ui, "MIDI processor preset: write: " + ex.Message);
            await _dialogs().Ask(_owner, "The preset could not be saved: " + ex.Message, Caption, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        processor.AppliedPreset = PresetCombo.Remember(name, "MidiProcessor:" + processor.Type, Normalize(processor.Type, processor.Params));
        FillProcessor();
        return true;
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
            Trace.Error(Trace.Ui, "MIDI processor preset: read: " + ex.Message);
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
        catch (Exception ex) when (ex is InvalidDataException or JsonException or InvalidOperationException or FormatException)
        {
            Trace.Error(Trace.Ui, "MIDI processor preset: values: " + ex.Message);
            return null;
        }
    }
}
