using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TabForge.Avalonia.Platform;
using TabForge.KeyboardMode;

namespace TabForge.Avalonia.Views;

/// <summary>
/// The Keyboard mode control bar of the pop-out: track, play / stop, speed, wait for me, skip, loop, hands, look-ahead - / +, note names, fingers and the MIDI input with its connected dot, in a wrap panel
/// (a narrow window wraps them onto another row). An on toggle shows a check mark as well as the accent. The bar holds the choices and raises <see cref="Changed"/>; what they do is the owner's.
/// Owns: the controls, their texts, automation names and the state they show. Does not own: playback, the judge, MIDI devices (the owner reads the choices and fills the track and device lists), the full screen button (added by the window).
/// Tests: TabForge.Avalonia.Tests.KeyboardModePopoutWindowTests.
/// </summary>
public sealed class KeyboardModeBar
{
    /// <summary>The speeds the Speed box offers, as shares of the song's tempo.</summary>
    public static readonly double[] SpeedPresets = { 0.5, 0.6, 0.7, 0.75, 0.8, 0.9, 1.0, 1.1, 1.25 };
    public const string NoDeviceText = "No device connected", AnyDeviceText = "Any available device", OffDeviceText = "Off";
    private const double RowGap = 12, ButtonGap = 2, ButtonMinHeight = 26;

    private readonly WrapPanel _row;
    private readonly ComboBox _track = new() { MinWidth = 150, MaxWidth = 150 };
    private readonly ComboBox _speed = new() { MinWidth = 76 };
    private readonly ComboBox _hands = new() { MinWidth = 128 };
    private readonly ComboBox _midi = new() { MinWidth = 168, MaxWidth = 260 };
    private readonly Button _play, _wait, _loop, _names, _fingers;
    private readonly TextBlock _look = new() { MinWidth = 30, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly global::Avalonia.Controls.Shapes.Ellipse _dot = new() { Width = 10, Height = 10, StrokeThickness = 1.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) };
    private readonly TextBlock _heard = new() { VerticalAlignment = VerticalAlignment.Center, MaxWidth = 220, TextTrimming = TextTrimming.CharacterEllipsis };
    private bool _syncing, _playing, _waitOn, _loopOn, _namesOn = true, _fingersOn = true;
    private int _lookSeconds = KeyboardModeSettings.DefaultLookAheadSeconds;

    public KeyboardModeBar(WrapPanel row)
    {
        _row = row;
        Group("Track", _track, "Track the keyboard follows and practises");
        _track.SelectionChanged += (_, _) => Raise();
        _play = Add(Make("Play", "Play or pause the song", () => { _playing = !_playing; Show(); Raise(); }));
        Add(Make("■ Stop", "Stop the song", () => { _playing = false; Show(); Raise(); }), "Stop");
        Group("Speed", _speed, "Playback speed: slower to learn, faster to challenge yourself (the play bar's speed)");
        foreach (var s in SpeedPresets) _speed.Items.Add(new ComboBoxItem { Content = Percent(s), Tag = s });
        _speed.SelectedIndex = Array.IndexOf(SpeedPresets, 1.0);
        _speed.SelectionChanged += (_, _) => Raise();
        _wait = Add(Make("Wait for me", "Wait for me: the song stops at each chord until you play its notes", () => { _waitOn = !_waitOn; Show(); Raise(); }), gap: RowGap);
        Add(Make("Skip", "Skip the chord the song is waiting for (a miss) and carry on", SkipAwaited), "Skip the awaited chord");
        _loop = Add(Make("Loop", "Loop the selected bars, or the section at the cursor", () => { _loopOn = !_loopOn; Show(); Raise(); }));
        Group("Hands", _hands, "Which hand you practise: its notes are judged; the other hand's are faded, or hidden with \"only\"");
        foreach (var name in KeyboardHands.FilterNames) _hands.Items.Add(name);
        _hands.SelectedIndex = 0;
        _hands.SelectionChanged += (_, _) => Raise();
        Label("Look-ahead", RowGap);
        Add(Make("−", "Fewer seconds on screen: longer, slower notes", () => StepLook(-1)), "Look-ahead shorter: longer notes", gap: 0);
        _row.Children.Add(_look);
        AutomationProperties.SetName(_look, "Look-ahead seconds");
        Add(Make("+", "More seconds on screen: shorter notes, see further ahead", () => StepLook(1)), "Look-ahead longer: shorter notes", gap: 0);
        _names = Add(Make("Note names", "Note names on the falling notes", () => { _namesOn = !_namesOn; Show(); Raise(); }), gap: RowGap);
        _fingers = Add(Make("Fingers", "Finger numbers on the falling notes (when the score has fingering)", () => { _fingersOn = !_fingersOn; Show(); Raise(); }));
        Group("MIDI in", _midi, "The MIDI keyboard to play along with: any available device (new ones are picked up), one device, or off");
        _midi.Items.Add(new ComboBoxItem { Content = OffDeviceText, Tag = KeyboardModeSettings.MidiOff });
        _midi.Items.Add(new ComboBoxItem { Content = AnyDeviceText, Tag = KeyboardModeSettings.MidiAny });
        _midi.SelectedIndex = 1;
        _midi.SelectionChanged += (_, _) => Raise();
        _row.Children.Add(_dot);
        _row.Children.Add(_heard);
        _heard.Text = NoDeviceText;
        Show();
    }

    /// <summary>Raised when any choice changed.</summary>
    public event Action? Changed;

    public bool IsPlaying { get => _playing; set { _playing = value; Show(); } }
    public bool WaitOn { get => _waitOn; set { _waitOn = value; Show(); } }
    public bool LoopOn { get => _loopOn; set { _loopOn = value; Show(); } }
    public bool ShowNames { get => _namesOn; set { _namesOn = value; Show(); } }
    public bool ShowFingers { get => _fingersOn; set { _fingersOn = value; Show(); } }
    /// <summary>False when the track has no fingering written: the Fingers toggle is disabled.</summary>
    public bool HasFingers { get => _fingers.IsEnabled; set => _fingers.IsEnabled = value; }
    public int LookAheadSeconds { get => _lookSeconds; set { _lookSeconds = KeyboardModeSettings.NormalizeLookAhead(value); Show(); } }
    public KeyboardHandsFilter Hands { get => (KeyboardHandsFilter)Math.Max(0, _hands.SelectedIndex); set { _syncing = true; _hands.SelectedIndex = (int)value; _syncing = false; } }
    public double Speed
    {
        get => _speed.SelectedItem is ComboBoxItem { Tag: double s } ? s : 1.0;
        set { _syncing = true; _speed.SelectedIndex = Array.FindIndex(SpeedPresets, p => Math.Abs(p - value) < 0.001); _syncing = false; }
    }

    /// <summary>The index of the chosen track, or -1 when none is chosen.</summary>
    public int TrackIndex => _track.SelectedIndex;

    /// <summary>The track names the track box lists now.</summary>
    public IReadOnlyList<string> TrackNames => _track.Items.OfType<string>().ToList();

    /// <summary>The MIDI input choice: Off, Any or one device's name.</summary>
    public string MidiInput
    {
        get => _midi.SelectedItem is ComboBoxItem { Tag: string name } ? name
            : _midi.SelectedItem is ComboBoxItem item ? (string?)item.Content ?? KeyboardModeSettings.MidiAny : KeyboardModeSettings.MidiAny;
        set
        {
            _syncing = true;
            var want = KeyboardModeSettings.NormalizeMidiInput(value);
            var at = -1;
            for (var i = 0; i < _midi.Items.Count; i++)
                if (_midi.Items[i] is ComboBoxItem item && (item.Tag as string ?? (string?)item.Content) == want) { at = i; break; }
            if (at >= 0) _midi.SelectedIndex = at;
            _syncing = false;
        }
    }

    /// <summary>The MIDI input devices the bar lists, with <paramref name="selected"/> (Off, Any or a device name) chosen; a chosen device that is gone shows as not connected.</summary>
    public void SetMidiDevices(IEnumerable<string> devices, string selected)
    {
        _syncing = true;
        var want = KeyboardModeSettings.NormalizeMidiInput(selected);
        _midi.Items.Clear();
        _midi.Items.Add(new ComboBoxItem { Content = OffDeviceText, Tag = KeyboardModeSettings.MidiOff });
        _midi.Items.Add(new ComboBoxItem { Content = AnyDeviceText, Tag = KeyboardModeSettings.MidiAny });
        foreach (var name in devices) _midi.Items.Add(new ComboBoxItem { Content = name, Tag = name });
        var at = -1;
        for (var i = 0; i < _midi.Items.Count; i++)
            if (_midi.Items[i] is ComboBoxItem item && (item.Tag as string ?? (string?)item.Content) == want) { at = i; break; }
        if (at < 0 && want != KeyboardModeSettings.MidiOff && want != KeyboardModeSettings.MidiAny)
        {
            _midi.Items.Add(new ComboBoxItem { Content = want + " (not connected)", Tag = want });
            at = _midi.Items.Count - 1;
        }
        _midi.SelectedIndex = Math.Max(0, at);
        _syncing = false;
    }

    /// <summary>Shows what the engine hears: the lit dot and names when something is connected, the dim dot and <see cref="NoDeviceText"/> otherwise.</summary>
    public void SetHeard(bool heard, string text)
    {
        _heard.Text = text;
        _dot.Bind(global::Avalonia.Controls.Shapes.Shape.FillProperty,
            _dot.GetResourceObservable(heard ? "AccentBrush" : "Panel2Brush"));
    }

    /// <summary>Makes the strip show this bar's hands, note names, fingers and look-ahead now and after every change.</summary>
    public void Drive(KeyboardStripView strip)
    {
        void Apply()
        {
            strip.Hands = Hands;
            strip.ShowNames = ShowNames;
            strip.ShowFingers = ShowFingers;
            strip.LookAheadMs = LookAheadSeconds * 1000.0;
            strip.InvalidateVisual();
        }
        Changed += Apply;
        Apply();
    }

    /// <summary>The track names the Track box lists, with <paramref name="selected"/> chosen.</summary>
    public void SetTracks(IEnumerable<string> names, int selected)
    {
        _syncing = true;
        _track.Items.Clear();
        foreach (var n in names) _track.Items.Add(n);
        _track.SelectedIndex = selected;
        _syncing = false;
    }

    private void StepLook(int step) { _lookSeconds = KeyboardModeSettings.NormalizeLookAhead(_lookSeconds + step); Show(); Raise(); }

    /// <summary>Learn.Skip: give up the chord the song is waiting for (a miss) and carry on — the bar's own Skip button.</summary>
    public void SkipAwaited() => Raise();

    /// <summary>Learn.LookAhead: show one more (or fewer) seconds above the keys, as the bar's + and - buttons do.</summary>
    public void BumpLookAhead(int step) => StepLook(step);

    private void Raise() { if (!_syncing) Changed?.Invoke(); }

    /// <summary>Shows the state on every control.</summary>
    private void Show()
    {
        _play.Content = _playing ? "❚❚ Pause" : "▶ Play";
        AutomationProperties.SetName(_play, _playing ? "Pause" : "Play");
        SetOn(_wait, "Wait for me", _waitOn);
        SetOn(_loop, "Loop", _loopOn);
        SetOn(_names, "Note names", _namesOn);
        SetOn(_fingers, "Fingers", _fingersOn);
        _look.Text = _lookSeconds.ToString(CultureInfo.InvariantCulture) + " s";
    }

    private static string Percent(double speed) => Math.Round(speed * 100).ToString(CultureInfo.InvariantCulture) + " %";

    private static Button Make(string text, string tip, Action run)
    {
        var b = new Button { Content = text, Padding = new Thickness(8, 1), MinHeight = ButtonMinHeight, VerticalContentAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(b, tip);
        b.Click += (_, _) => run();
        return b;
    }

    private Button Add(Button b, string? name = null, double gap = ButtonGap)
    {
        AutomationProperties.SetName(b, name ?? (string)b.Content!);
        b.Margin = new Thickness(gap, 1, 1, 1);
        _row.Children.Add(b);
        return b;
    }

    private void Label(string text, double gap)
    {
        var t = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(gap, 0, 4, 0) };
        t.Bind(TextBlock.ForegroundProperty, t.GetResourceObservable("TextBrush"));
        _row.Children.Add(t);
    }

    private void Group(string label, ComboBox box, string tip)
    {
        Label(label, RowGap);
        ToolTip.SetTip(box, tip);
        box.Margin = new Thickness(0, 1, 1, 1);
        box.MinHeight = ButtonMinHeight;
        box.VerticalContentAlignment = VerticalAlignment.Center;
        AutomationProperties.SetName(box, label);
        _row.Children.Add(box);
    }

    /// <summary>A toggle: on shows a check mark and the accent fill, off the plain button (not told by colour alone).</summary>
    private static void SetOn(Button b, string text, bool on)
    {
        b.Content = on ? "✓ " + text : text;
        b.Bind(Button.BackgroundProperty, b.GetResourceObservable(on ? "SelectedTintBrush" : "Panel2Brush"));
        b.Bind(Button.BorderBrushProperty, b.GetResourceObservable(on ? "SelectedOutlineBrush" : "BorderBrush"));
        AutomationProperties.SetItemStatus(b, on ? "on" : "off");
    }
}
