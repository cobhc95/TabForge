using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Avalonia.Views.Fx;
using TabForge.Avalonia.Views.Presets;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Views;

// Owns: the selected plug-in's bar of the FX chain window: the preset box (the plug-in's programs, the user's saved presets, the preset name it came
//   with, Manage), + save preset, Wiring…, MIDI… (lit while a processor is on), the Volume knob and the On switch; and the Wiring and MIDI windows it opens.
// Does not own: the engine calls (IFxChainEngine, through the host), the preset files (PluginLibrary, PresetFolderStore), the windows (WiringWindow,
//   MidiProcessingWindow, the preset manager), the undo step (the host).
// Tests: TabForge.Avalonia.Tests.FxEngineCallTests.
public sealed partial class FxChainWindow
{
    public const string SavePresetCaption = "Save preset", NoPresetsText = "No presets";
    private const double PresetBoxMinWidth = 220, SaveButtonWidth = 28, BarGap = 8, KnobLabelGap = 4;
    private const string WiringTip = "Audio pins and MIDI routing (source track, channel) of this plug-in";
    private const string MidiTip = "MIDI processors in front of this plug-in: filter, transpose, drum map, velocity, humanize, delay, program / CC, log… (lit when any is on)";

    /// <summary>One row of the preset box: a program of the plug-in, a saved preset of the user, or the name the plug-in came with.</summary>
    internal sealed record PresetItem(string Name, int Program, PresetKind Kind)
    {
        public override string ToString() => Kind == PresetKind.User ? $"★ {Name}" : Name;
    }

    internal enum PresetKind { Program, User, CameWith, Manage }

    // A wrapping row: in a narrow window the volume and the On switch go to a second line instead of out of the pane.
    private readonly WrapPanel _bar = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6), Name = "PluginBar" };
    private readonly ComboBox _presetBox = new() { MinWidth = PresetBoxMinWidth, Name = "PresetBox" };
    private readonly Button _addPreset = Btn("+", "Save the current settings as a preset", "AddPresetButton");
    private readonly Button _wiring = Btn("Wiring…", WiringTip, "WiringButton");
    private readonly Button _midi = Btn("MIDI…", MidiTip, "MidiButton");
    private readonly KnobControl _volume = new()
    {
        Minimum = -60, Maximum = 12, DefaultValue = 0, Origin = 0, Step = 0.1, Label = "Volume", Name = "VolumeSlider", VerticalAlignment = VerticalAlignment.Center,
        Format = v => VolumeText(v),
    };
    private readonly TextBlock _volumeText = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0), Name = "VolumeText" };
    private readonly CheckBox _bypass = new() { Content = FxChainPresenter.BypassContent, VerticalAlignment = VerticalAlignment.Center, Name = "BypassCheck" };
    private WiringWindow? _wiringWindow;
    private MidiProcessingWindow? _midiWindow;
    private UndoSnapshot? _volumeSince;
    private bool _volumeDragging;

    private DockPanel BuildPluginPane()
    {
        var pane = new DockPanel();
        DockPanel.SetDock(_bar, Dock.Top);
        pane.Children.Add(_bar);
        DockPanel.SetDock(_containers.Pane, Dock.Top);
        pane.Children.Add(_containers.Pane);
        var editorHost = new Grid();   // the editor area, and the parameter panel in its place when it shows
        editorHost.Children.Add(_area);
        _parameters.Panel.IsVisible = false;
        editorHost.Children.Add(_parameters.Panel);
        pane.Children.Add(editorHost);
        return pane;
    }

    private void BuildBar()
    {
        ToolTip.SetTip(_presetBox, "Presets: the plug-in's own programs and your saved presets (rename, duplicate, delete or reorder them with Manage presets…)");
        ToolTip.SetTip(_volume, "This plug-in's output volume (double-click or right-click: type a value; Ctrl+click: 0 dB)");
        ToolTip.SetTip(_bypass, FxChainPresenter.BypassToolTip);
        _addPreset.Width = SaveButtonWidth;
        _addPreset.Padding = new Thickness(0, 2, 0, 4);
        _addPreset.Margin = new Thickness(4, 0, BarGap, 0);
        foreach (var control in new Control[] { _presetBox, _addPreset, _wiring, _midi, _parametersButton })
        {
            control.VerticalAlignment = VerticalAlignment.Center;
            _bar.Children.Add(control);
        }
        _bar.Children.Add(new TextBlock { Text = "Volume", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, KnobLabelGap, 0), [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted") });
        _bar.Children.Add(_volume);
        _bar.Children.Add(_volumeText);
                _bar.Children.Add(_bypass);
        _ = PresetRows.Attach(_presetBox,
            () => Selected is { } slot ? PresetFolderStore.Plugin(slot) : null,
            item => item is PresetItem { Kind: PresetKind.User } preset ? preset.Name : null,
            () => PresetCatalogKind.PluginPreset,
            () => Selected is { } slot ? PresetFolderStore.PluginKey(slot) : null,
            LoadPresetList, item => item is PresetItem { Kind: PresetKind.Manage });
        _presetBox.SelectionChanged += (_, _) => { if (!_building) _ = ApplyPresetAsync(); };
        _addPreset.Click += (_, _) => _ = SavePresetAsync();
        _wiring.Click += (_, _) => OpenWiring();
        _midi.Click += (_, _) => OpenMidiProcessing();
        _volume.EditStarted += (_, _) => { _volumeDragging = true; _volumeSince = _host.Checkpoint(); };
        _volume.EditEnded += (_, _) =>
        {
            _volumeDragging = false;
            if (Selected is not null) _host.Edit(_track, () => { }, _volumeSince);
            _volumeSince = null;
        };
        // While dragging only a light gain message goes to the engine; the song edit (undo step, dirty mark, sync) runs once on release.
        _volume.ValueChanged += (_, value) =>
        {
            if (_building || Selected is not { } slot) return;
            var db = FxChainPresenter.OutputDbFromSlider(value);
            _volumeText.Text = VolumeText(db);
            if (_volumeDragging) { slot.OutputDb = db; _host.Engine?.SetPluginGain(_track, slot); }
            else if (db != slot.OutputDb) Edit(() => { slot.OutputDb = db; _host.Engine?.SetPluginGain(_track, slot); }, show: false);
        };
        _bypass.Margin = new Thickness(BarGap, 0, 0, 0);
        _bypass.IsCheckedChanged += (_, _) =>
        {
            if (_building || Selected is not { } slot || _bypass.IsChecked == slot.Enabled) return;
            Edit(() => { slot.Enabled = _bypass.IsChecked == true; _host.Engine?.SetPluginEnabled(_track, slot); }, refresh: true);
        };
    }

    // ---------- presets ----------

    /// <summary>Fills the preset box for the focused plug-in: its programs, the user's saved presets and the name it came with.</summary>
    private void LoadPresetList()
    {
        _building = true;
        try
        {
            var items = new List<PresetItem>();
            if (Selected is { } slot)
            {
                items.AddRange(_editors.Programs.Select((name, index) => new PresetItem(name, index, PresetKind.Program)));
                items.AddRange(PluginLibrary.PresetNames(slot).Select(name => new PresetItem(name, -1, PresetKind.User)));
            }
            _presetBox.IsEnabled = items.Count > 0 || Selected is not null;
            // The preset name an imported chain gave the plug-in: its own row when no program or saved preset has that name.
            var cameWith = Selected?.PresetName is { Length: > 0 } name ? items.FirstOrDefault(i => i.Name == name) ?? new PresetItem(name, -1, PresetKind.CameWith) : null;
            if (cameWith is { Kind: PresetKind.CameWith }) items.Add(cameWith);
            if (Selected is not null) items.Add(new PresetItem(PresetRowText.Manage, -1, PresetKind.Manage));
            _presetBox.ItemsSource = items;
            if (cameWith is not null) _presetBox.SelectedItem = cameWith;
            else if (Selected is { PresetName: null } && _editors.CurrentProgram >= 0 && _editors.CurrentProgram < _editors.Programs.Count) _presetBox.SelectedIndex = _editors.CurrentProgram;
            else _presetBox.SelectedItem = null;
            _presetBox.PlaceholderText = items.Count == 0 ? NoPresetsText : null;
        }
        finally { _building = false; }
    }

    private async Task ApplyPresetAsync()
    {
        if (Selected is not { } slot || _presetBox.SelectedItem is not PresetItem item) return;
        if (item.Kind == PresetKind.Manage) return;   // PresetRows reverts the row and opens the manager
        if (item.Kind == PresetKind.CameWith) return;
        if (item.Kind == PresetKind.Program)
        {
            if (slot.PresetName is not null) Edit(() => slot.PresetName = null, show: false);   // a pick that changes nothing makes no undo step
            _host.Engine?.SetProgram(_track, slot, item.Program);
            return;
        }
        if (PluginLibrary.LoadPreset(slot, item.Name) is not { } preset)
        {
            await Dialogs.Tell(this, "That preset could not be read.", SavePresetCaption);
            return;
        }
        Edit(() =>
        {
            slot.PresetName = null;
            slot.State = preset.State;
            _host.Engine?.SetState(_track, slot, preset.State);
            if (preset.OutputDb is { } db) { slot.OutputDb = db; _host.Engine?.SetPluginGain(_track, slot); }
        }, show: false);
        _building = true;
        try { _volume.Value = slot.OutputDb; _volumeText.Text = VolumeText(slot.OutputDb); }
        finally { _building = false; }
    }

    /// <summary>Save preset: asks for a name, reads the plug-in's current settings from the engine and writes the preset.</summary>
    public async Task SavePresetAsync()
    {
        if (Selected is not { } slot) return;
        var name = await Dialogs.PresetName(this, slot);
        if (string.IsNullOrWhiteSpace(name)) { LoadPresetList(); return; }
        if (_host.Engine is { } engine) await engine.CollectStatesAsync(new[] { _track }, StateCollectMs);
        if (_closed) return;   // the window closed while the states were read
        if (slot.State is not { Length: > 0 } state)
        {
            await Dialogs.Tell(this, "This plug-in did not give its settings (turn the chain on first).", SavePresetCaption);
            return;
        }
        try { PluginLibrary.SavePreset(slot, name.Trim(), state); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Trace.Error(Trace.Ui, "FX preset: save: " + ex.Message);
            await Dialogs.Ask(this, "The preset could not be saved: " + ex.Message, SavePresetCaption, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        LoadPresetList();
    }

    // ---------- wiring and MIDI ----------

    /// <summary>Opens (or brings forward) the wiring window of the focused plug-in, else the first one.</summary>
    public WiringWindow? OpenWiring()
    {
        var slot = FocusedOrFirst;
        if (slot is null) return null;
        if (_wiringWindow is { IsVisible: true } open && ReferenceEquals(open.Tag, slot)) { open.Activate(); return open; }
        _wiringWindow?.Close();
        var window = new WiringWindow(_host.Tracks, _track, slot, change => Edit(change, refresh: false, show: false), _host.PluginSettings.SeparateProcessPerPlugin) { Tag = slot };
        window.Closed += (_, _) => { if (ReferenceEquals(_wiringWindow, window)) _wiringWindow = null; };
        _wiringWindow = window;
        window.Show(this);
        return window;
    }

    /// <summary>Opens (or brings forward) the MIDI processing window of the given plug-in, else the focused one, else the first.</summary>
    public MidiProcessingWindow? OpenMidiProcessing(PluginSlot? plugin = null)
    {
        var slot = plugin ?? FocusedOrFirst;
        if (slot is null) return null;
        if (_midiWindow is { IsVisible: true } open && ReferenceEquals(open.Tag, slot)) { open.Activate(); return open; }
        _midiWindow?.Close();
        var window = new MidiProcessingWindow(_host, _track, slot, change => { Edit(change, refresh: false, show: false); UpdateMidiButton(); }) { Tag = slot };
        window.Closed += (_, _) => { if (ReferenceEquals(_midiWindow, window)) _midiWindow = null; UpdateMidiButton(); };
        _midiWindow = window;
        window.Show(this);
        return window;
    }

    private void UpdateMidiButton()
    {
        var lit = Selected is { } slot && slot.MidiProcessors.Any(p => p.Enabled);
        if (lit) _midi[!Button.BackgroundProperty] = new DynamicResourceExtension("AccentBrush");
        else _midi.ClearValue(Button.BackgroundProperty);
    }

    /// <summary>Shows the focused plug-in's bar values: the volume, the On switch, the presets and the MIDI button.</summary>
    private void ShowBar()
    {
        var selected = Selected;
        _bar.IsEnabled = selected is not null;
        _building = true;
        try
        {
            _bypass.IsChecked = selected?.Enabled ?? true;
            _volume.Value = selected?.OutputDb ?? 0;
            _volumeText.Text = selected is null ? "" : VolumeText(selected.OutputDb);
        }
        finally { _building = false; }
        LoadPresetList();
        UpdateMidiButton();
    }
}
