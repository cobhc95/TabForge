using System.Windows;
using System.Windows.Controls;
using TabForge.Models;
using TabForge.Plugins;

namespace TabForge.Views;

/// <summary>
/// One plug-in's wiring, opened from the FX chain window's Wiring… button: audio pins (how the chain's stereo signal
/// enters the plug-in), sidechain 3/4 (another track's post-fader audio), MIDI input (this track / another track / none, with a
/// channel filter) and MIDI output forwarding to another track. Links that would loop are refused. Every change goes through the FX window's Edit (undo + engine update).
/// </summary>
public sealed class WiringWindow : Window
{
    private readonly IFxChainHost _host;
    private readonly TrackModel _track;
    private readonly PluginSlot _slot;
    private readonly Action<Action> _edit;
    private readonly ComboBox _input = new() { Width = 200 };
    private readonly ComboBox _midiSource = new() { Width = 200 };
    private readonly ComboBox _midiTrack = new() { Width = 200 };
    private readonly ComboBox _channel = new() { Width = 200 };
    private readonly ComboBox _instrumentAudio = new() { Width = 200 };
    private readonly ComboBox _sidechain = new() { Width = 200 };
    private readonly ComboBox _midiOut = new() { Width = 200 };
    private bool _building;

    private sealed record Choice(string Label, string Value) { public override string ToString() => Label; }
    private sealed record TrackChoice(string Id, string Label) { public override string ToString() => Label; }

    private static readonly Choice[] Pins =
    {
        new("L+R stereo", PluginPins.Stereo), new("Mono sum (L+R)", PluginPins.Mono), new("Left only", PluginPins.Left),
        new("Right only", PluginPins.Right), new("Swap L/R", PluginPins.Swap),
    };
    private static readonly Choice[] Sources =
    {
        new("This track's MIDI", PluginMidiIn.Own), new("Another track", PluginMidiIn.OtherTrack), new("None", PluginMidiIn.None),
    };

    public WiringWindow(IFxChainHost host, TrackModel track, PluginSlot slot, Window? owner, Action<Action> edit, Action? configureMidi = null)
    {
        _host = host; _track = track; _slot = slot; _edit = edit;
        Owner = owner;
        OwnerActivation.Attach(this);
        Title = $"Wiring: {slot.Name}";
        Width = 420; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "WindowBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");

        var root = new StackPanel { Margin = new Thickness(14) };

        root.Children.Add(Header("Audio"));
        _input.ItemsSource = Pins;
        root.Children.Add(Row("Input 1/2", _input, "How the track's stereo signal enters this plug-in"));
        root.Children.Add(Row("Output", new TextBlock { Text = "Stereo → track", VerticalAlignment = VerticalAlignment.Center }, "The plug-in's stereo output continues along the chain"));
        var sidechain = _sidechain;
        // Sidechain: another track's post-fader audio on inputs 3/4. VST2 only (the plug-in must declare at least four inputs);
        // the VST3 bridge has no aux input bus, and plug-ins in their own process get stereo only.
        var linkTargets = new[] { new TrackChoice("", "Not connected") }
            .Concat(_host.Tracks.Where(t => !ReferenceEquals(t, _track)).Select(t => new TrackChoice(t.Id.ToString("N"), t.Name))).ToList();
        _sidechain.ItemsSource = linkTargets;
        string? sideOff = _track.IsBus ? "Sidechain is for track plug-ins (not group buses or the master)"
            : _slot.Format == "VST3" || _slot.Path.EndsWith(".vst3", StringComparison.OrdinalIgnoreCase) ? "Not available for VST3 plug-ins (the VST3 bridge has no sidechain input bus yet)"
            : _host.PluginSettings.SeparateProcessPerPlugin ? "Not available while plug-ins run in their own process (Settings > Audio & Plug-ins)"
            : _slot.Type == PluginSlotType.Instrument ? "Instruments take no audio input" : null;
        _sidechain.IsEnabled = sideOff is null;
        root.Children.Add(Row("Sidechain 3/4", _sidechain,
            "Another track's post-fader audio feeds this plug-in's inputs 3/4 (VST2 plug-ins with four or more inputs; the source track must play through the audio engine)", sideOff));

        root.Children.Add(Header("MIDI input"));
        _midiSource.ItemsSource = Sources;
        var others = _host.Tracks.Where(t => !ReferenceEquals(t, _track)).Select(t => new TrackChoice(t.Id.ToString("N"), t.Name)).ToList();
        _midiTrack.ItemsSource = others;
        _channel.ItemsSource = new[] { "All" }.Concat(Enumerable.Range(1, 16).Select(i => i.ToString())).ToList();
        root.Children.Add(Row("Source", _midiSource, "Where this plug-in takes its MIDI from"));
        root.Children.Add(Row("Track", _midiTrack, "The track whose MIDI notes this plug-in plays (stays linked if the track is renamed or moved)"));
        root.Children.Add(Row("Channel", _channel, "Only this MIDI channel gets through"));

        root.Children.Add(new CheckBox { Content = "Pass incoming MIDI to next plug-in", IsChecked = _slot.PassMidiThrough, Margin = new Thickness(0, 6, 0, 0),
            ToolTip = "The MIDI arriving here also continues to the next plug-in in the chain" });
        root.Children.Add(new CheckBox { Content = "Send this plug-in's MIDI output to next plug-in", IsChecked = _slot.MidiOutToNext, Margin = new Thickness(0, 4, 0, 0),
            ToolTip = "Notes and messages this plug-in generates (e.g. a chord plug-in) go on to the next plug-in" });
        var pass = (CheckBox)root.Children[^2]; var send = (CheckBox)root.Children[^1];
        _midiOut.ItemsSource = linkTargets.Select(t => t.Id.Length == 0 ? new TrackChoice("", "Off") : t).ToList();
        _midiOut.IsEnabled = !_track.IsBus;
        root.Children.Add(Header("MIDI output"));
        root.Children.Add(Row("Forward to", _midiOut,
            "Forward this plug-in's MIDI output (VST2) to another track's chain input (heard from the next audio block)",
            _track.IsBus ? "MIDI forwarding is for track plug-ins" : null));
        pass.Click += (_, _) => { var on = pass.IsChecked == true; _edit(() => _slot.PassMidiThrough = on); };
        send.Click += (_, _) => { var on = send.IsChecked == true; _edit(() => _slot.MidiOutToNext = on); };
        if (_slot.Type == PluginSlotType.Instrument)
        {
            _instrumentAudio.ItemsSource = new[] { "Add to chain audio", "Replace chain audio" };
            _instrumentAudio.SelectedIndex = _slot.InstrumentAudio == "Replace" ? 1 : 0;
            _instrumentAudio.SelectionChanged += (_, _) => { if (_instrumentAudio.SelectedIndex >= 0) { var replace = _instrumentAudio.SelectedIndex == 1; _edit(() => _slot.InstrumentAudio = replace ? "Replace" : "Add"); } };
            root.Children.Insert(root.Children.IndexOf(sidechain.Parent as UIElement ?? sidechain) + 1,
                Row("Instrument output", _instrumentAudio, "Instrument output: add to / replace the audio arriving from earlier plug-ins"));
        }

        var close = new Button { Content = "Close", IsCancel = true, IsDefault = true, Padding = new Thickness(16, 3, 16, 3), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        close.Click += (_, _) => Close();
        root.Children.Add(close);
        Content = root;

        Load();
        _input.SelectionChanged += (_, _) => { if (!_building && _input.SelectedItem is Choice c) _edit(() => _slot.Pins = c.Value); };
        _midiSource.SelectionChanged += (_, _) => { if (!_building && _midiSource.SelectedItem is Choice c) { _edit(() => MidiIn.Source = c.Value); UpdateEnabled(); } };
        _midiTrack.SelectionChanged += (_, _) => { if (!_building && _midiTrack.SelectedItem is TrackChoice t) _edit(() => MidiIn.TrackId = t.Id); };
        _channel.SelectionChanged += (_, _) => { if (!_building && _channel.SelectedIndex >= 0) _edit(() => MidiIn.Channel = _channel.SelectedIndex); };
        _sidechain.SelectionChanged += (_, _) =>
        {
            if (_building || _sidechain.SelectedItem is not TrackChoice t) return;
            if (t.Id.Length > 0 && RoutingLinks.WouldCycle(Others(sidechain: true), t.Id, SelfId)) { Refuse(t.Label); return; }
            _edit(() => _slot.SidechainTrackId = t.Id);
        };
        _midiOut.SelectionChanged += (_, _) =>
        {
            if (_building || _midiOut.SelectedItem is not TrackChoice t) return;
            if (t.Id.Length > 0 && RoutingLinks.WouldCycle(Others(sidechain: false), SelfId, t.Id)) { Refuse(t.Label); return; }
            _edit(() => _slot.MidiOutTrackId = t.Id);
        };
    }

    private string SelfId => _track.Id.ToString("N");

    /// <summary>
    /// Every track's links with only the edge being edited left out (it is the one being replaced). This plug-in's
    /// other link stays in the graph: leaving it out let a sidechain from B plus a MIDI forward to B slip through.
    /// </summary>
    private IEnumerable<TrackModel> Others(bool sidechain)
    {
        var side = _slot.SidechainTrackId; var fwd = _slot.MidiOutTrackId;
        if (sidechain) _slot.SidechainTrackId = ""; else _slot.MidiOutTrackId = "";
        try { return _host.Tracks.Select(Snapshot).ToList(); }
        finally { _slot.SidechainTrackId = side; _slot.MidiOutTrackId = fwd; }
    }

    /// <summary>A light copy carrying only the id and the links (the cycle check reads nothing else).</summary>
    private static TrackModel Snapshot(TrackModel t)
    {
        var rig = new RigPreset();
        foreach (var p in t.Rig.Plugins) rig.Plugins.Add(new PluginSlot { SidechainTrackId = p.SidechainTrackId, MidiOutTrackId = p.MidiOutTrackId });
        return new TrackModel { Id = t.Id, Rig = rig };
    }

    private void Refuse(string target)
    {
        MessageBox.Show(this, $"Connecting to \"{target}\" would make a loop (sidechain / MIDI links must not lead back to this track).", "Wiring", MessageBoxButton.OK, MessageBoxImage.Information);
        Load();
    }

    private PluginMidiIn MidiIn => _slot.MidiIn ??= new PluginMidiIn();

    private void Load()
    {
        _building = true;
        try
        {
            _input.SelectedItem = Pins.FirstOrDefault(p => p.Value == _slot.Pins) ?? Pins[0];
            _midiSource.SelectedItem = Sources.FirstOrDefault(s => s.Value == MidiIn.Source) ?? Sources[0];
            _midiTrack.SelectedItem = (_midiTrack.ItemsSource as List<TrackChoice>)?.FirstOrDefault(t => t.Id == MidiIn.TrackId);
            _channel.SelectedIndex = Math.Clamp(MidiIn.Channel, 0, 16);
            _sidechain.SelectedItem = (_sidechain.ItemsSource as List<TrackChoice>)?.FirstOrDefault(t => t.Id == (_slot.SidechainTrackId ?? "")) ?? (_sidechain.ItemsSource as List<TrackChoice>)?[0];
            _midiOut.SelectedItem = (_midiOut.ItemsSource as List<TrackChoice>)?.FirstOrDefault(t => t.Id == (_slot.MidiOutTrackId ?? "")) ?? (_midiOut.ItemsSource as List<TrackChoice>)?[0];
        }
        finally { _building = false; }
        UpdateEnabled();
    }

    private void UpdateEnabled() => _midiTrack.IsEnabled = MidiIn.Source == PluginMidiIn.OtherTrack;

    private static TextBlock Header(string text)
    {
        var block = new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 4) };
        block.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        return block;
    }

    private static FrameworkElement Row(string label, FrameworkElement control, string tip, string? controlTip = null)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        text.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        Grid.SetColumn(control, 1);
        control.HorizontalAlignment = HorizontalAlignment.Left;
        control.ToolTip = controlTip ?? tip;
        if (controlTip is not null) ToolTipService.SetShowOnDisabled(control, true);
        grid.Children.Add(text);
        grid.Children.Add(control);
        return grid;
    }
}
