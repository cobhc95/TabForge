using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace TabForge.KeyboardMode;

// Owns: Keyboard mode's control bar (the practice hub), the same in the pane and in the pop-out: play / pause, stop, speed, wait for me, skip, loop, hands, look-ahead - / +, note names, finger
//   numbers and the MIDI input (Off, any available device, or one device, listed as the menu opens) with a connected dot and the device heard. Themed buttons and combo boxes in a wrap panel
//   (a narrow window wraps them onto another row); every control has an automation name and is reached by Tab; an on toggle shows a check mark as well as the accent.
// Does not own: what the controls do (KeyboardModeControls) or the hosting pane / window.
// Tests: TestKeyboardModeControlBar.
internal sealed class KeyboardModeControlBar : Border
{
    private const string AnyLabel = "Any available device", OffLabel = "Off";

    private readonly KeyboardModeControls _controls;
    private readonly WrapPanel _row = new() { Margin = new Thickness(6, 3, 6, 3) };
    private readonly Button _play, _wait, _loop, _names, _fingers;
    private readonly ComboBox _speed = new() { MinWidth = 76 };
    private readonly ComboBox _hands = new() { MinWidth = 128 };
    private readonly ComboBox _midi = new() { MinWidth = 168, MaxWidth = 260 };
    private readonly TextBlock _look = new() { MinWidth = 30, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Ellipse _dot = new() { Width = 10, Height = 10, StrokeThickness = 1.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) };
    private readonly TextBlock _heard = new() { VerticalAlignment = VerticalAlignment.Center, MaxWidth = 220, TextTrimming = TextTrimming.CharacterEllipsis };
    private bool _syncing;

    public KeyboardModeControlBar(KeyboardModeControls controls)
    {
        _controls = controls;
        SetResourceReference(BackgroundProperty, "PanelBrush");
        SetResourceReference(BorderBrushProperty, "BorderBrush");
        BorderThickness = new Thickness(0, 0, 0, 1);
        Child = _row;
        AutomationProperties.SetName(this, "Keyboard mode controls");

        _play = Add(Make("Play", "Play or pause the song (Space)", controls.PlayPause));
        Add(Make("■ Stop", "Stop the song", controls.Stop), "Stop");
        Group("Speed", _speed, "Playback speed: slower to learn, faster to challenge yourself (the play bar's speed)");
        foreach (var s in KeyboardModeControls.SpeedPresets) _speed.Items.Add(new ComboBoxItem { Content = Percent(s), Tag = s });
        _speed.SelectionChanged += (_, _) => { if (!_syncing && _speed.SelectedItem is ComboBoxItem { Tag: double s }) controls.SetSpeed(s); };
        _wait = Add(Make("Wait for me", "Wait for me: the song stops at each chord until you play its notes (Ctrl+Alt+W)", controls.ToggleWait), gap: 12);
        Add(Make("Skip", "Skip the chord the song is waiting for (a miss) and carry on (Ctrl+Alt+Q)", controls.Skip), "Skip the awaited chord");
        _loop = Add(Make("Loop", "Loop the selected bars, or the section at the cursor (F9)", controls.ToggleLoop));
        Group("Hands", _hands, "Which hand you practise: its notes are judged; the other hand's are faded, or hidden with \"only\"");
        foreach (var name in KeyboardHands.FilterNames) _hands.Items.Add(name);
        _hands.SelectionChanged += (_, _) => { if (!_syncing && _hands.SelectedIndex >= 0) controls.SetHands((KeyboardHandsFilter)_hands.SelectedIndex); };
        Label("Look-ahead", 12);
        Add(Make("−", "Fewer seconds on screen: longer, slower notes (minus, Ctrl+wheel up)", () => controls.StepLookAhead(-1)), "Look-ahead shorter: longer notes", gap: 0);
        _row.Children.Add(_look);
        AutomationProperties.SetName(_look, "Look-ahead seconds");
        Add(Make("+", "More seconds on screen: shorter notes, see further ahead (plus, Ctrl+wheel down)", () => controls.StepLookAhead(1)), "Look-ahead longer: shorter notes", gap: 0);
        _names = Add(Make("Note names", "Note names on the falling notes", () => controls.SetShowNames(!controls.ShowNames)), gap: 12);
        _fingers = Add(Make("Fingers", "Finger numbers on the falling notes (when the score has fingering)", () => controls.SetShowFingers(!controls.ShowFingers)));
        Group("MIDI in", _midi, "The MIDI keyboard to play along with: any available device (new ones are picked up), one device, or off");
        _row.Children.Add(_dot);
        _row.Children.Add(_heard);
        _heard.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
        _midi.DropDownOpened += (_, _) => FillMidi();
        _midi.SelectionChanged += (_, _) => { if (!_syncing && _midi.SelectedItem is ComboBoxItem { Tag: string v }) controls.SetMidiInput(v); };
        FillMidi();
        controls.Changed += Sync;
        Loaded += (_, _) => { controls.Changed -= Sync; controls.Changed += Sync; Sync(); };
        Unloaded += (_, _) => controls.Changed -= Sync;
        Sync();
    }

    /// <summary>The MIDI input list (off-screen captures render its drop-down).</summary>
    internal ComboBox MidiBox => _midi;

    /// <summary>Adds a control at the end of the bar (the pop-out's full screen button).</summary>
    public void AddExtra(FrameworkElement element) { element.Margin = new Thickness(12, 1, 1, 1); _row.Children.Add(element); }

    /// <summary>The MIDI input list: Off, any device, the devices present now, and the saved device when it is not connected.</summary>
    internal void FillMidi()
    {
        var was = _syncing;
        _syncing = true;
        try
        {
            _midi.Items.Clear();
            _midi.Items.Add(new ComboBoxItem { Content = AnyLabel, Tag = KeyboardModeSettings.MidiAny });
            var current = _controls.MidiInput;
            var devices = _controls.MidiDevices();
            foreach (var d in devices.Distinct()) _midi.Items.Add(new ComboBoxItem { Content = d, Tag = d });
            if (current is not (KeyboardModeSettings.MidiAny or KeyboardModeSettings.MidiOff) && !devices.Contains(current))
                _midi.Items.Add(new ComboBoxItem { Content = current + " (not connected)", Tag = current });
            _midi.Items.Add(new ComboBoxItem { Content = OffLabel, Tag = KeyboardModeSettings.MidiOff });
            _midi.SelectedItem = _midi.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == current);
        }
        finally { _syncing = was; }
    }

    /// <summary>Shows the current state of every control.</summary>
    public void Sync()
    {
        _syncing = true;
        try
        {
            var playing = _controls.IsPlaying;
            _play.Content = playing ? "❚❚ Pause" : "▶ Play";
            AutomationProperties.SetName(_play, playing ? "Pause" : "Play");
            var speed = _controls.Speed;
            _speed.SelectedItem = _speed.Items.OfType<ComboBoxItem>().FirstOrDefault(i => Math.Abs((double)i.Tag - speed) < 0.001);
            if (_speed.SelectedItem is null) _speed.Text = Percent(speed);
            SetOn(_wait, "Wait for me", _controls.WaitOn);
            SetOn(_loop, "Loop", _controls.LoopOn);
            SetOn(_names, "Note names", _controls.ShowNames);
            SetOn(_fingers, "Fingers", _controls.ShowFingers);
            _fingers.IsEnabled = _controls.HasFingers;
            _fingers.ToolTip = _controls.HasFingers ? "Finger numbers on the falling notes" : "This track has no fingering written, so there are no finger numbers to show";
            ToolTipService.SetShowOnDisabled(_fingers, true);
            _hands.SelectedIndex = (int)_controls.Hands;
            _look.Text = _controls.LookAheadSeconds.ToString(CultureInfo.InvariantCulture) + " s";
            var input = _controls.MidiInput;
            if (_midi.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == input) is { } item) _midi.SelectedItem = item; else FillMidi();
            var heard = _controls.HeardDevices;
            var off = input == KeyboardModeSettings.MidiOff;
            _dot.Visibility = _heard.Visibility = off ? Visibility.Collapsed : Visibility.Visible;
            _dot.SetResourceReference(Shape.StrokeProperty, heard.Count > 0 ? "PlayBrush" : "MutedBrush");
            if (heard.Count > 0) _dot.SetResourceReference(Shape.FillProperty, "PlayBrush"); else _dot.Fill = Brushes.Transparent;
            _heard.Text = heard.Count switch { 0 => "No device connected", 1 => heard[0], _ => heard.Count + " devices" };
            AutomationProperties.SetItemStatus(_midi, heard.Count == 0 ? _heard.Text : "Connected: " + _heard.Text);
            _heard.ToolTip = heard.Count > 1 ? string.Join("\n", heard) : null;
        }
        finally { _syncing = false; }
    }

    private static string Percent(double speed) => Math.Round(speed * 100).ToString(CultureInfo.InvariantCulture) + " %";

    private static Button Make(string text, string tip, Action run)
    {
        var b = new Button { Content = text, ToolTip = tip, Padding = new Thickness(8, 1, 8, 1), MinHeight = 26 };
        b.Click += (_, _) => run();
        return b;
    }

    private Button Add(Button b, string? name = null, double gap = 2)
    {
        AutomationProperties.SetName(b, name ?? (string)b.Content);
        b.Margin = new Thickness(gap, 1, 1, 1);
        _row.Children.Add(b);
        return b;
    }

    private void Label(string text, double gap)
    {
        var t = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(gap, 0, 4, 0) };
        t.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        _row.Children.Add(t);
    }

    private void Group(string label, ComboBox box, string tip)
    {
        Label(label, 12);
        box.ToolTip = tip;
        box.Margin = new Thickness(0, 1, 1, 1);
        box.MinHeight = 26;
        box.VerticalContentAlignment = VerticalAlignment.Center;
        AutomationProperties.SetName(box, label);
        _row.Children.Add(box);
    }

    /// <summary>A toggle: on shows a check mark and the accent fill, off the plain button (not told by colour alone).</summary>
    private static void SetOn(Button b, string text, bool on)
    {
        b.Content = on ? "✓ " + text : text;
        b.SetResourceReference(BackgroundProperty, on ? "AccentSoftBrush" : "Panel2Brush");
        b.SetResourceReference(BorderBrushProperty, on ? "AccentBrush" : "BorderBrush");
        AutomationProperties.SetItemStatus(b, on ? "on" : "off");
    }
}
