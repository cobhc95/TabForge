using System.Windows;
using System.Windows.Controls;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Views.EffectEditors;

// Owns: the dialog of a value-based effect (trill, grace note, harmonic): the editor's fields plus the preset list, Save as preset and Delete preset.
// Does not own: what the values mean (OrnamentEditors), the edit that follows (EffectEditorFlow.OpenOrnament) or the frame (ThemedEditorDialog).
// Tests: TestOrnamentEditors.
internal sealed class ValuesEffectDialog
{
    private readonly EffectEditorKind _kind;
    private readonly EffectPresetStore _presets;
    private readonly Func<Dictionary<string, double>> _read;
    private readonly Action<IReadOnlyDictionary<string, double>> _write;
    private readonly Action? _presetsChanged;
    private readonly ListBox _list = new() { Width = 190, MinHeight = 120, MaxHeight = 220, TabIndex = 40 };
    private readonly Button _delete = new() { Content = "Delete preset", Margin = new Thickness(0, 6, 0, 0), Padding = new Thickness(10, 4, 10, 4), TabIndex = 42 };

    public ValuesEffectDialog(EffectEditorKind kind, string title, UIElement fields, Func<Dictionary<string, double>> read,
        Action<IReadOnlyDictionary<string, double>> write, EffectPresetStore presets, bool canClean, Action? presetsChanged = null)
    {
        _kind = kind; _read = read; _write = write; _presets = presets; _presetsChanged = presetsChanged;
        var side = new StackPanel { Margin = new Thickness(18, 0, 0, 0) };
        side.Children.Add(new TextBlock { Text = "Presets", Margin = new Thickness(0, 0, 0, 4), Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("MutedBrush") });
        side.Children.Add(_list);
        var save = new Button { Content = "Save as preset…", Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(10, 4, 10, 4), TabIndex = 41, ToolTip = "Keep these settings as a preset of your own" };
        save.Click += (_, _) => SaveAsPreset();
        side.Children.Add(save);
        _delete.ToolTip = "Delete the selected preset of your own";
        _delete.Click += (_, _) => DeleteSelected();
        side.Children.Add(_delete);
        _list.SelectionChanged += (_, _) => { if (_list.SelectedItem is ListBoxItem { Tag: EffectPresetEntry p }) Pick(p); UpdateDelete(); };
        System.Windows.Automation.AutomationProperties.SetName(_list, "Presets");
        FillList(null);
        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.Children.Add(fields);
        Grid.SetColumn(side, 1);
        body.Children.Add(side);
        Dialog = new ThemedEditorDialog(title, body, canClean);
    }

    public ThemedEditorDialog Dialog { get; }

    /// <summary>The editor's current settings (the keys of its preset values).</summary>
    public Dictionary<string, double> Values => _read();

    /// <summary>Fills the fields from a preset, as picking it in the list does.</summary>
    public void Pick(EffectPresetEntry preset) => _write(preset.Values);

    internal IReadOnlyList<(string Name, bool BuiltIn)> ListedPresets =>
        _list.Items.OfType<ListBoxItem>().Select(i => (((EffectPresetEntry)i.Tag).Name, EffectPresetStore.BuiltIn(_kind).Contains((EffectPresetEntry)i.Tag))).ToList();

    private void FillList(string? select)
    {
        _list.Items.Clear();
        foreach (var (preset, builtIn) in _presets.List(_kind))
        {
            var item = new ListBoxItem { Content = builtIn ? preset.Name : preset.Name + "  (mine)", Tag = preset };
            _list.Items.Add(item);
            if (select is not null && preset.Name == select) _list.SelectedItem = item;
        }
        UpdateDelete();
    }

    private void UpdateDelete() => _delete.IsEnabled = _list.SelectedItem is ListBoxItem { Tag: EffectPresetEntry p } && !EffectPresetStore.BuiltIn(_kind).Contains(p);

    private void SaveAsPreset()
    {
        var name = GpDialogs.Prompt("Save as preset", "Preset name:", "My preset");
        if (string.IsNullOrWhiteSpace(name)) return;
        if (!_presets.Save(_kind, name, Array.Empty<BendPointModel>(), _read())) { MessageBox.Show(Dialog, "That name is taken by a built-in preset, too long, or the list is full.", "Save as preset"); return; }
        _presetsChanged?.Invoke();
        FillList(name.Trim());
    }

    private void DeleteSelected()
    {
        if (_list.SelectedItem is not ListBoxItem { Tag: EffectPresetEntry p } || EffectPresetStore.BuiltIn(_kind).Contains(p)) return;
        _presets.Delete(_kind, p.Name);
        _presetsChanged?.Invoke();
        FillList(null);
    }
}
