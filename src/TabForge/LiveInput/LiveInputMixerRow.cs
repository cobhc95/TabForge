using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TabForge.Models;
using TabForge.Views;

namespace TabForge.LiveInput;

// Owns: the Live input row of the mixer, a child of the Master row: its name column (the arm toggle with the microphone, the name and
//   the input line), the FX chain button, pan, volume and mute; the Master row's collapse arrow; and the row's refresh from changes.
// Does not own: the mixer's grid, the Master row's own controls and the group chrome (MixerWindow and its shared helpers), the
//   stand-in's level and mute (LiveInputArm, through LiveInputController), the engine sync and the FX chain window (LiveInputController).
// Tests: TestLiveInputMixerRow.
internal sealed class LiveInputMixerRow
{
    private const string Title = "Live input";
    private readonly LiveInputController _controller;
    private readonly Action _rebuild;
    private readonly Button _arm;
    private readonly System.Windows.Shapes.Path _glyph;
    private readonly TextBlock _status;
    private readonly FxSplitButton _fx;
    private readonly Slider _pan, _volume;
    private readonly StackPanel _muteHost = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
    private bool _syncing;
    private bool? _muteShown;

    /// <summary>The row: a child of the Master row's panel, hidden while Master is collapsed.</summary>
    public Border Row { get; }

    /// <summary>True while the row shows live input as on.</summary>
    public bool ShowsOn { get; private set; }

    /// <summary>The input line under the name: "Input 1 + MIDI", "MIDI", or "off".</summary>
    public string StatusText => _status.Text;

    /// <summary>The live input row for the mixer's window, or null when that window has no live input (no main window).</summary>
    public static LiveInputMixerRow? For(Window mixer, Action rebuild)
    {
        if (LiveInputFeatureModule.ControllerFor(mixer) is not { } controller) return null;
        controller.ApplySavedLevel();   // after Reset all settings the saved level is the stand-in's again
        return new LiveInputMixerRow(controller, rebuild);
    }

    internal LiveInputMixerRow(LiveInputController controller, Action rebuild)
    {
        _controller = controller;
        _rebuild = rebuild;
        var grid = MixerWindow.RowGrid();

        // The microphone is the arm toggle: the same command as the track list's microphone button.
        _glyph = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(LiveInputButton.MicGlyph), Style = (Style)Application.Current.FindResource("IconPath"),
            Width = 16, Height = 16, IsHitTestVisible = false
        };
        _arm = new Button
        {
            Content = _glyph, Width = 16, Height = 16, Padding = new Thickness(0), Margin = new Thickness(22, 0, 6, 0), VerticalAlignment = VerticalAlignment.Top,
            Background = Brushes.Transparent, BorderThickness = new Thickness(0)
        };
        TooltipShortcuts.Bind(_arm, "Live input on or off: hear the chosen input through the live FX chain (the microphone button in the track list does the same)", LiveInputFeatureModule.CommandId);
        _arm.Click += (_, _) => _controller.Toggle();
        var name = MixerWindow.NameBlock(Title, "off", accent: false);
        _status = (TextBlock)name.Children[1];
        var nameRow = new DockPanel();
        DockPanel.SetDock(_arm, Dock.Left);
        nameRow.Children.Add(_arm);
        nameRow.Children.Add(name);
        MixerWindow.Put(grid, nameRow, 0);

        _fx = new FxSplitButton { Width = 54, Height = 22, HorizontalAlignment = HorizontalAlignment.Left, PowerEnabled = false, ChainOn = true };
        System.Windows.Automation.AutomationProperties.SetName(_fx, "FX chain for live input");
        TooltipShortcuts.Bind(_fx, "Live input FX chain: FX opens the chain you hear while live input is on", null);
        _fx.OpenChain += (_, _) => _controller.OpenFx();
        MixerWindow.Put(grid, _fx, 1);

        _pan = Level("Live input pan", "Live input pan (−64 left … 0 centre … +63 right; mouse wheel: 1 step, Ctrl+wheel: 8; double-click: centre)",
            -64, 63, 0, () => _controller.Pan - 64, v => _controller.SetLevel(_controller.Volume, v + 64));
        MixerWindow.Put(grid, _pan, 4);
        _volume = Level("Live input volume", "Live input volume (0-127; mouse wheel: 1 step, Ctrl+wheel: 8; double-click: 100)",
            0, 127, 100, () => _controller.Volume, v => _controller.SetLevel(v, _controller.Pan));
        MixerWindow.Put(grid, _volume, 5);
        MixerWindow.Put(grid, _muteHost, 6);

        Row = MixerWindow.RowBorder(grid, accent: false);
        Row.Visibility = _controller.MixerCollapsed ? Visibility.Collapsed : Visibility.Visible;
        // Follows every change of the live input, in every window; subscribed only while the row is in the mixer.
        Row.Loaded += (_, _) => { _controller.Changed -= Refresh; _controller.Changed += Refresh; _controller.LevelChanged -= Refresh; _controller.LevelChanged += Refresh; Refresh(); };
        Row.Unloaded += (_, _) => { _controller.Changed -= Refresh; _controller.LevelChanged -= Refresh; };
        Refresh();
    }

    /// <summary>The Master row's name column: its collapse arrow (collapsing Master hides the live input row) and its name.</summary>
    public FrameworkElement MasterHead(FrameworkElement name)
    {
        var collapsed = _controller.MixerCollapsed;
        var chevron = MixerWindow.ChevronButton(collapsed, "Master",
            collapsed ? "Expand Master (shows the live input row)" : "Collapse Master (hides the live input row)",
            () => { _controller.MixerCollapsed = !_controller.MixerCollapsed; _rebuild(); });
        chevron.Focusable = true;   // reached by Tab, unlike a group's arrow (the group row takes the keys there)
        var head = new DockPanel();
        DockPanel.SetDock(chevron, Dock.Left);
        head.Children.Add(chevron);
        head.Children.Add(name);
        return head;
    }

    /// <summary>A pan or volume slider in the track row's style: the same drag, wheel and double-click behaviour, no song edit.</summary>
    private Slider Level(string automationName, string tip, int min, int max, int defaultValue, Func<int> get, Action<int> set)
    {
        var slider = new Slider
        {
            Minimum = min, Maximum = max, Value = get(), ToolTip = tip, SmallChange = 1, LargeChange = Math.Max(1, (max - min) / 10.0),
            TickFrequency = 1, VerticalAlignment = VerticalAlignment.Center, Style = (Style)Application.Current.FindResource("ArrangementSlider")
        };
        System.Windows.Automation.AutomationProperties.SetName(slider, automationName);
        TrackControlWidgets.AttachSmoothDrag(slider, defaultValue);
        TrackControlWidgets.AttachWheelStep(slider);
        slider.ValueChanged += (_, e) =>
        {
            var v = (int)Math.Round(e.NewValue);
            if (!_syncing && v != get()) set(v);
        };
        return slider;
    }

    /// <summary>The arm's click, as a press of the microphone (tests and the capture use it).</summary>
    internal void Press() => _arm.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

    /// <summary>Shows the live input's state: the arm and its input line, the FX button, the sliders and the mute button.</summary>
    internal void Refresh()
    {
        ShowsOn = _controller.IsOn;
        var audio = _controller.Input.Length > 0 ? AudioInputs.Label(_controller.Input) : null;
        _status.Text = !ShowsOn ? "off" : _controller.IsMidi ? (audio is null ? "MIDI" : audio + " + MIDI") : audio ?? "off";
        System.Windows.Automation.AutomationProperties.SetName(_arm, ShowsOn ? "Live input, on" : "Live input, off");
        var panel = (Application.Current.TryFindResource("Panel2Brush") as SolidColorBrush)?.Color ?? Colors.Black;
        var light = panel.R + panel.G + panel.B > 384;
        if (ShowsOn) _glyph.Stroke = new SolidColorBrush(light ? Color.FromRgb(0xC4, 0x12, 0x1A) : (Application.Current.TryFindResource("DangerBrush") as SolidColorBrush)?.Color ?? Colors.Red);
        else _glyph.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "MutedBrush");
        _fx.PluginCount = _controller.PluginCount;
        _syncing = true;
        try
        {
            if (!_pan.IsMouseCaptured) _pan.Value = _controller.Pan - 64;
            if (!_volume.IsMouseCaptured) _volume.Value = _controller.Volume;
        }
        finally { _syncing = false; }
        if (_muteShown != _controller.Muted) ShowMute();
    }

    private void ShowMute()
    {
        _muteShown = _controller.Muted;
        var button = TrackControlWidgets.ToggleIconButton("IconMute", _controller.Muted, () => _controller.SetMuted(!_controller.Muted),
            "Mute live input: silences it without turning it off (its volume is kept)");
        button.Width = 34; button.Height = 26; button.Margin = new Thickness(2, 0, 2, 0);
        System.Windows.Automation.AutomationProperties.SetName(button, "Live input mute");
        _muteHost.Children.Clear();
        _muteHost.Children.Add(button);
    }
}
