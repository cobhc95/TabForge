using System;
using System.Collections.Generic;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Presenters;
using Texts = TabForge.Presenters.WiringPresenter.Texts;
using TabForge.Avalonia.Views.Dialogs;

namespace TabForge.Avalonia.Views;

/// <summary>
/// The Wiring window (the FX chain window's Wiring… button), the Avalonia wiring window: the same
/// sections, rows, texts, choices and enabled rules, all read from WiringPresenter. It runs on a sample routing graph
/// (<see cref="SampleTracks"/>): the preview has no song, so the tracks and the plug-in are made up here, and Pad already forwards
/// its MIDI to Lead so the loop refusal can be seen. A refused link shows its message on the status line and the choice goes back.
/// Owns: the layout, the rows and the event wiring. Does not own: the choices, rules, texts and loop check (WiringPresenter), the
/// change to the plug-in (the <paramref name="edit"/> callback) and engine routing.
/// The rows carry the tooltips (a disabled row says why). The instrument output row shows for instrument plug-ins only.
/// Tests: TabForge.Avalonia.Tests.WiringWindowTests.
/// </summary>
public sealed class WiringWindow : Window
{
    private readonly IReadOnlyList<TrackModel> _tracks;
    private readonly TrackModel _track;
    private readonly PluginSlot _slot;
    private readonly Action<Action> _edit;
    private readonly IReadOnlyList<WiringTrackChoice> _sidechainTargets;
    private readonly IReadOnlyList<WiringTrackChoice> _midiTrackTargets;
    private readonly IReadOnlyList<WiringTrackChoice> _midiOutTargets;
    private readonly ComboBox _input = new() { Width = 200, Name = "InputCombo" };
    private readonly ComboBox _midiSource = new() { Width = 200, Name = "SourceCombo" };
    private readonly ComboBox _midiTrack = new() { Width = 200, Name = "TrackCombo" };
    private readonly ComboBox _channel = new() { Width = 200, Name = "ChannelCombo" };
    private readonly ComboBox _sidechain = new() { Width = 200, Name = "SidechainCombo" };
    private readonly ComboBox _instrumentAudio = new() { Width = 200, Name = "InstrumentOutputCombo" };
    private readonly ComboBox _midiOut = new() { Width = 200, Name = "ForwardCombo" };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Name = "StatusText", Margin = new Thickness(0, 8, 0, 0) };
    private readonly bool _separateProcessPerPlugin;
    private bool _building;

    /// <summary>Four tracks of the preview's sample song; Pad forwards its MIDI to Lead.</summary>
    public static IReadOnlyList<TrackModel> SampleTracks()
    {
        var lead = new TrackModel { Name = "Lead" };
        var bass = new TrackModel { Name = "Bass" };
        var pad = new TrackModel { Name = "Pad" };
        pad.Rig.Plugins.Add(new PluginSlot { Name = "Chord", Path = "chord.dll", Format = "VST2", MidiOutTrackId = lead.Id.ToString("N") });
        return new[] { lead, bass, pad };
    }

    /// <summary>The plug-in the preview wires: a VST2 effect on Lead, sidechained from Bass.</summary>
    public static PluginSlot SampleSlot() => new() { Name = "Chorus", Path = "chorus.dll", Format = "VST2" };

    /// <param name="separateProcessPerPlugin">The settings' "each plug-in in its own process" choice: a side-chain is not offered while it is on.</param>
    public WiringWindow(IReadOnlyList<TrackModel> tracks, TrackModel track, PluginSlot slot, Action<Action> edit, bool separateProcessPerPlugin = false)
    {
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        _tracks = tracks; _track = track; _slot = slot; _edit = edit; _separateProcessPerPlugin = separateProcessPerPlugin;
        _sidechainTargets = WiringPresenter.SidechainTargets(tracks, track);
        _midiTrackTargets = WiringPresenter.OtherTracks(tracks, track);
        _midiOutTargets = WiringPresenter.MidiOutTargets(tracks, track);
        Title = Texts.Title(slot.Name);
        Width = DialogMetrics.SmallDialogWidth; SizeToContent = SizeToContent.Height; CanResize = false;
        this[!Window.BackgroundProperty] = new DynamicResourceExtension("WindowBackground");
        this[!Window.ForegroundProperty] = new DynamicResourceExtension("TextPrimary");

        var root = new StackPanel { Margin = new Thickness(14) };
        AddAudioRows(root);
        AddMidiInputRows(root);
        AddMidiOutputRows(root);
        root.Children.Add(_status);

        var close = new Button { Content = Texts.Close, Name = "CloseButton", Padding = new Thickness(16, 3, 16, 3), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        close.Click += (_, _) => Close();
        root.Children.Add(close);
        Content = root;

        Load();
        WireChanges();
    }

    /// <summary>The audio section: the input pins, the output text and the sidechain source (disabled when the plug-in cannot take one).</summary>
    private void AddAudioRows(StackPanel root)
    {
        root.Children.Add(Header(Texts.SectionAudio));
        _input.ItemsSource = WiringPresenter.Pins;
        root.Children.Add(Row(Texts.RowInput, _input, Texts.RowInputTip));
        root.Children.Add(Row(Texts.RowOutput, new TextBlock { Text = Texts.OutputText, VerticalAlignment = VerticalAlignment.Center }, Texts.OutputTip));
        _sidechain.ItemsSource = _sidechainTargets;
        var sideOff = WiringPresenter.SidechainDisabledReason(_track, _slot, _separateProcessPerPlugin);
        _sidechain.IsEnabled = sideOff is null;
        root.Children.Add(Row(Texts.RowSidechain, _sidechain, Texts.RowSidechainTip, sideOff));
        _instrumentAudio.ItemsSource = WiringPresenter.InstrumentAudioModes;
        if (WiringPresenter.ShowsInstrumentAudio(_slot)) root.Children.Add(Row(Texts.RowInstrumentOutput, _instrumentAudio, Texts.RowInstrumentOutputTip));
    }

    /// <summary>The MIDI input section: the source, track and channel, then the pass-through and send-on checks.</summary>
    private void AddMidiInputRows(StackPanel root)
    {
        root.Children.Add(Header(Texts.SectionMidiInput));
        _midiSource.ItemsSource = WiringPresenter.MidiSources;
        _midiTrack.ItemsSource = _midiTrackTargets;
        _channel.ItemsSource = WiringPresenter.Channels;
        root.Children.Add(Row(Texts.RowSource, _midiSource, Texts.RowSourceTip));
        root.Children.Add(Row(Texts.RowTrack, _midiTrack, Texts.RowTrackTip));
        root.Children.Add(Row(Texts.RowChannel, _channel, Texts.RowChannelTip));

        var pass = new CheckBox { Content = Texts.PassMidiLabel, Name = "PassMidiCheck", IsChecked = _slot.PassMidiThrough, Margin = new Thickness(0, 6, 0, 0) };
        var send = new CheckBox { Content = Texts.SendMidiLabel, Name = "SendMidiCheck", IsChecked = _slot.MidiOutToNext, Margin = new Thickness(0, 4, 0, 0) };
        pass.IsCheckedChanged += (_, _) => { var on = pass.IsChecked == true; _edit(() => _slot.PassMidiThrough = on); };
        send.IsCheckedChanged += (_, _) => { var on = send.IsChecked == true; _edit(() => _slot.MidiOutToNext = on); };
        ToolTip.SetTip(pass, Texts.PassMidiTip);
        ToolTip.SetTip(send, Texts.SendMidiTip);
        root.Children.Add(pass);
        root.Children.Add(send);
    }

    /// <summary>The MIDI output section: the track the plug-in forwards to (disabled when it cannot forward).</summary>
    private void AddMidiOutputRows(StackPanel root)
    {
        root.Children.Add(Header(Texts.SectionMidiOutput));
        _midiOut.ItemsSource = _midiOutTargets;
        var forwardOff = WiringPresenter.MidiOutDisabledReason(_track);
        _midiOut.IsEnabled = forwardOff is null;
        root.Children.Add(Row(Texts.RowForward, _midiOut, Texts.RowForwardTip, forwardOff));
    }

    /// <summary>Connects each combo box and check box to its change; a refused loop shows its message and goes back.</summary>
    private void WireChanges()
    {
        _input.SelectionChanged += (_, _) => { if (!_building && _input.SelectedItem is WiringChoice c) _edit(() => _slot.Pins = c.Value); };
        _midiSource.SelectionChanged += (_, _) => { if (!_building && _midiSource.SelectedItem is WiringChoice c) { _edit(() => MidiIn.Source = c.Value); UpdateEnabled(); } };
        _midiTrack.SelectionChanged += (_, _) => { if (!_building && _midiTrack.SelectedItem is WiringTrackChoice t) _edit(() => MidiIn.TrackId = t.Id); };
        _channel.SelectionChanged += (_, _) => { if (!_building && _channel.SelectedIndex >= 0) _edit(() => MidiIn.Channel = _channel.SelectedIndex); };
        _instrumentAudio.SelectionChanged += (_, _) =>
        {
            if (_building || _instrumentAudio.SelectedIndex < 0) return;
            var mode = WiringPresenter.InstrumentAudioValue(_instrumentAudio.SelectedIndex);
            _edit(() => _slot.InstrumentAudio = mode);
        };
        _sidechain.SelectionChanged += (_, _) =>
        {
            if (_building || _sidechain.SelectedItem is not WiringTrackChoice t) return;
            if (WiringPresenter.WouldLoop(_tracks, _track, _slot, sidechain: true, t.Id)) { Refuse(t.Label); return; }
            _edit(() => _slot.SidechainTrackId = t.Id);
        };
        _midiOut.SelectionChanged += (_, _) =>
        {
            if (_building || _midiOut.SelectedItem is not WiringTrackChoice t) return;
            if (WiringPresenter.WouldLoop(_tracks, _track, _slot, sidechain: false, t.Id)) { Refuse(t.Label); return; }
            _edit(() => _slot.MidiOutTrackId = t.Id);
        };
    }

    /// <summary>The MIDI input of the plug-in, created when the plug-in has none yet.</summary>
    private PluginMidiIn MidiIn => _slot.MidiIn ??= new PluginMidiIn();

    private void Refuse(string target)
    {
        _status.Text = WiringPresenter.RefusalText(target);
        Load();
    }

    private void Load()
    {
        _building = true;
        try
        {
            _input.SelectedItem = WiringPresenter.PinChoice(_slot);
            _midiSource.SelectedItem = WiringPresenter.SourceChoice(MidiIn);
            _midiTrack.SelectedItem = WiringPresenter.MidiTrackChoice(_midiTrackTargets, MidiIn);
            _channel.SelectedIndex = WiringPresenter.ChannelIndex(MidiIn);
            _sidechain.SelectedItem = WiringPresenter.SidechainChoice(_sidechainTargets, _slot);
            _instrumentAudio.SelectedIndex = WiringPresenter.InstrumentAudioIndex(_slot);
            _midiOut.SelectedItem = WiringPresenter.MidiOutChoice(_midiOutTargets, _slot);
        }
        finally { _building = false; }
        UpdateEnabled();
    }

    private void UpdateEnabled() => _midiTrack.IsEnabled = WiringPresenter.MidiTrackEnabled(MidiIn);

    private static TextBlock Header(string text) => new()
    {
        Text = text, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 10, 0, 4),
        [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("AccentBrush"),
    };

    /// <summary>One label and control row: the label in the muted colour, the control in the first column after it.</summary>
    private static Grid Row(string label, Control control, string tip, string? controlTip = null)
    {
        var grid = new Grid { Margin = DialogMetrics.RowMargin };
        grid.ColumnDefinitions.Add(new ColumnDefinition(110, GridUnitType.Pixel));
        grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        var text = new TextBlock
        {
            Text = label, VerticalAlignment = VerticalAlignment.Center,
            [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted"),
        };
        Grid.SetColumn(control, 1);
        control.HorizontalAlignment = HorizontalAlignment.Left;
        ToolTip.SetTip(control, controlTip ?? tip);
        if (controlTip is not null) ToolTip.SetShowOnDisabled(control, true);
        grid.Children.Add(text);
        grid.Children.Add(control);
        return grid;
    }
}
