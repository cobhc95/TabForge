using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using TabForge.Audio;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Services;

namespace TabForge.Views;

/// <summary>What the FX chain window needs from the app.</summary>
public interface IFxChainHost
{
    /// <summary>All tracks of the open song (the wiring window offers them as MIDI sources).</summary>
    IReadOnlyList<TrackModel> Tracks { get; }
    PluginSettings PluginSettings { get; }
    void BeginChainEdit();
    /// <summary>The chain (order, enabled, wet, roles, source) changed; the app updates playback and the engine.</summary>
    void ChainChanged(TrackModel track);
    void SaveSettings();
    /// <summary>Monitor FX window: the open song uses the app-wide monitor chain (true, default) or its own.</summary>
    bool MonitorUseGlobal { get; }
    void SetMonitorUseGlobal(bool useGlobal);
    void OpenAudioSettings();
    /// <summary>TabForge's main window (owner of floating plug-in windows).</summary>
    IntPtr OwnerWindow { get; }
    bool DarkTheme { get; }
    AudioEngineClient Engine { get; }
}

/// <summary>
/// A track's FX chain window: the chain on the left (drag to reorder, tick to enable,
/// double-click to float a plug-in's window), and for the selected plug-in a bar with its presets (+ to save),
/// channel wiring, role, wet/dry and bypass, above the plug-in's own controls docked in the window.
/// FX menu: add / remove, save and load whole chains.
/// </summary>
public sealed class FxChainWindow : Window
{
    private readonly IFxChainHost _host;
    private readonly TrackModel _track;
    private readonly ListBox _list = new() { BorderThickness = new Thickness(0), AllowDrop = true };
    private readonly CheckBox _useChain = new() { Content = "Through chain", ToolTip = "Play this track through the chain. Off: the track plays through Windows MIDI." };
    private readonly CheckBox _midiSound = new() { Content = "GM sound", ToolTip = "On: the track's General MIDI instrument sound plays; with effects and no VST instrument the effects shape it. Off: only a VST instrument sounds." };
    private readonly CheckBox _autoGm = new() { Content = "Auto-switch to GM sound when no VST instrument plays", ToolTip = "On: GM sound is ticked when no VST instrument plays this track (chain off, instrument bypassed or removed) and unticked when one plays again. A GM sound you untick yourself stays off. Same as the option in Settings > Audio & VST." };
    private readonly CheckBox _autoLoad = new() { Content = "Auto-load for this instrument", ToolTip = "Save this chain (plug-ins, their settings, each plug-in's MIDI processors, wiring and MIDI input) as the default for this instrument type. It is then applied automatically to any track of this instrument type without plug-ins, in any song when loaded, and to new tracks of this type. Untick to remove the default." };
    private readonly CheckBox _startup = new() { Content = "Add as a track on startup", ToolTip = "Off by default. On: this chain (plug-ins, their settings, wiring and MIDI configuration, volume, audio input and monitor setting) is added as a track, not armed, to every song you open or create. That track is not saved with the song until you untick this. Manage them in Settings > Audio & VST." };
    private readonly Button _midiButton =new() { Content = "MIDI…", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 8, 0), ToolTip = "MIDI processors in front of this plug-in: filter, transpose, drum map, velocity, humanize, delay, program / CC, log… (lit when any is on)" };
    private BypassOverlay? _overlay;
    private readonly ComboBox _presets = new() { MinWidth = 220, ToolTip = "Presets: the plug-in's own programs and your saved presets" };
    private readonly Button _wiring = new() { Content = "Wiring…", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 8, 0), ToolTip = "Audio pins and MIDI routing (source track, channel) of this plug-in" };
    private WiringWindow? _wiringWindow;
    private readonly KnobControl _volume = new()
    {
        Minimum = -60, Maximum = 12, DefaultValue = 0, Origin = 0, Width = 30, Height = 30, Label = "Volume",
        Format = v => v <= -59.9 ? "-inf dB" : $"{v:+0.0;-0.0;0.0} dB", ToolTip = "This plug-in's output volume (double-click: 0 dB)"
    };
    private readonly CheckBox _bypass = new() { Content = "On", ToolTip = "Untick to bypass this plug-in", VerticalAlignment = VerticalAlignment.Center };
    private readonly PluginDockHost _dock = new();
    private readonly Border _dockFrame = new();
    private readonly TextBlock _dockMessage = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center };
    private readonly Button _dockHere = new() { Content = "Show it here", Padding = new Thickness(10, 3, 10, 3), HorizontalAlignment = HorizontalAlignment.Center, Visibility = Visibility.Collapsed };
    private readonly TextBlock _status = new() { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, FontSize = 11 };
    private readonly StackPanel _bar = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
    private readonly System.Windows.Threading.DispatcherTimer _cpuTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private PluginSlot? _docked;
    private readonly HashSet<PluginSlot> _floating = new(ReferenceEqualityComparer.Instance);
    private List<string> _programs = new();
    private bool _building;
    private Point _dragStart;

    public TrackModel Track => _track;

    private readonly CheckBox _autoPitch = new() { Content = "Match pitch automatically", Margin = new Thickness(16, 0, 0, 0), ToolTip = "On (default, see Settings > Audio & VST): each VST instrument's sounding octave is measured silently when it loads or its preset changes, and it is transposed to match the notes. A Transpose MIDI processor still applies on top. Drum tracks are never transposed." };
    private readonly TextBlock _autoPitchLabel = new() { Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _remeasure = new() { Content = "Re-measure", Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(6, 0, 0, 0), ToolTip = "Measure the instruments' pitch again (silent; waits while the song plays)" };

    private void BuildAutoPitch(Panel top)
    {
        var matcher = _host.Engine.AutoPitch;
        if (matcher is null) return;
        _autoPitch.IsChecked = matcher.Enabled(_track);
        _autoPitch.IsEnabled = !TabForge.Audio.AutoPitchMatcher.IsDrums(_track);
        _autoPitchLabel.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        void Refresh(TrackModel t) { if (ReferenceEquals(t, _track)) _autoPitchLabel.Text = matcher.Describe(_track); }
        _autoPitch.Click += (_, _) =>
        {
            var on = _autoPitch.IsChecked == true;
            _track.Rig.AutoPitchMatch = on == _host.PluginSettings.AutoPitchMatch ? null : on;
            matcher.Apply(_track);
            if (on) foreach (var s in _track.Rig.Plugins) if (s.Type == PluginSlotType.Instrument && s.AutoPitchOffset is null) matcher.Measure(_track, s);
        };
        _remeasure.Click += (_, _) => { foreach (var s in _track.Rig.Plugins) if (s.Type == PluginSlotType.Instrument && s.Enabled) matcher.Measure(_track, s); };
        matcher.Changed += Refresh;
        Closed += (_, _) => matcher.Changed -= Refresh;
        Refresh(_track);
        top.Children.Add(_autoPitch);
        top.Children.Add(_autoPitchLabel);
        top.Children.Add(_remeasure);
    }

    public FxChainWindow(IFxChainHost host, TrackModel track, Window? owner)
    {
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && Keyboard.FocusedElement is not System.Windows.Controls.Primitives.TextBoxBase && Keyboard.FocusedElement is not ComboBox) { e.Handled = true; Close(); } };
        _host = host;
        _track = track;
        Owner = owner;
        Title = $"FX: {track.Name}";
        Width = 980; Height = 620; MinWidth = 640; MinHeight = 380;
        WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "WindowBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");

        var root = new DockPanel();
        root.Children.Add(BuildMenu());
        var body = new DockPanel { Margin = new Thickness(10) };
        root.Children.Add(body);

        // Wraps onto further lines when the window is too narrow, so every option stays readable.
        var top = new WrapPanel { Margin = new Thickness(0, 0, 0, 4), Orientation = Orientation.Horizontal };
        DockPanel.SetDock(top, Dock.Top);
        _useChain.IsChecked = track.SoundSource == SoundSources.Plugins;
        _useChain.Click += (_, _) => Edit(() => _track.SoundSource = _useChain.IsChecked == true ? SoundSources.Plugins : SoundSources.Midi);
        _midiSound.IsChecked = track.MidiSound;
        _useChain.Margin = new Thickness(0, 0, 16, 0);
        _midiSound.Margin = new Thickness(0, 0, 16, 0);
        _autoLoad.IsChecked = AutoChains.Has(_host.PluginSettings, track);
        // Plug-in states are awaited (never a blocking wait on the UI thread); the box is re-read afterwards, so a quick untick wins.
        _autoLoad.Click += async (_, _) =>
        {
            try
            {
                if (_autoLoad.IsChecked == true)
                {
                    await _host.Engine.CollectStatesAsync(new[] { _track }, 2000);
                    if (_autoLoad.IsChecked != true) return;
                    AutoChains.Save(_host.PluginSettings, _track);
                }
                else AutoChains.Remove(_host.PluginSettings, _track);
                _host.SaveSettings();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { _status.Text = "The auto-load chain could not be saved: " + ex.Message; }
        };
        _startup.IsChecked = StartupTracks.Has(_host.PluginSettings, track);
        _startup.Click += async (_, _) =>
        {
            try
            {
                if (_startup.IsChecked == true)
                {
                    await _host.Engine.CollectStatesAsync(new[] { _track }, 2000);
                    if (_startup.IsChecked != true) return;
                    StartupTracks.Save(_host.PluginSettings, _track);
                }
                else StartupTracks.Remove(_host.PluginSettings, _track);
                _host.SaveSettings();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { _status.Text = "The startup track could not be saved: " + ex.Message; }
        };
        Closed += async (_, _) =>
        {
            // Keep the template in step with the chain as it was left (plug-in states included).
            // Awaited normally; when the main window is going away with it (app exit) an await would never resume, so it waits there.
            if (MixerBuses.IsMonitor(_track) && _host.MonitorUseGlobal)
            {
                // The app-wide monitor chain lives in the settings: keep its plug-in states current.
                try
                {
                    if (Owner is { IsVisible: true } && !Dispatcher.HasShutdownStarted) await _host.Engine.CollectStatesAsync(new[] { _track }, 2000);
                    else _host.Engine.CollectStates(new[] { _track }, 2000);
                    _host.SaveSettings();
                }
                catch (Exception) { }
            }
            if (!_track.IsBus && StartupTracks.Has(_host.PluginSettings, _track))
            {
                try
                {
                    if (Owner is { IsVisible: true } && !Dispatcher.HasShutdownStarted) await _host.Engine.CollectStatesAsync(new[] { _track }, 2000);
                    else _host.Engine.CollectStates(new[] { _track }, 2000);
                    StartupTracks.Save(_host.PluginSettings, _track);
                    _host.SaveSettings();
                }
                catch (Exception) { }
            }
        };
        _midiSound.Click += (_, _) => Edit(() =>
        {
            _track.MidiSound = _midiSound.IsChecked == true;
            _track.MidiSoundAuto = false;
            _track.MidiSoundManualOff = !_track.MidiSound;   // a manual untick is respected by the automatic switch
        });
        _autoGm.IsChecked = _host.PluginSettings.AutoGmSound;
        _autoGm.Margin = new Thickness(0, 0, 16, 0);
        _autoGm.Click += (_, _) =>
        {
            _host.PluginSettings.AutoGmSound = _autoGm.IsChecked == true;
            _host.SaveSettings();
            Edit(() => { });   // applies the switch now
        };
        top.Children.Add(_useChain);
        top.Children.Add(_midiSound);
        top.Children.Add(_autoGm);
        top.Children.Add(_autoLoad);
        _startup.Margin = new Thickness(16, 0, 0, 0);
        top.Children.Add(_startup);
        BuildAutoPitch(top);
        foreach (FrameworkElement child in top.Children) { var m = child.Margin; child.Margin = new Thickness(m.Left, m.Top, m.Right, Math.Max(m.Bottom, 4)); }
        if (track.IsBus)
        {
            _startup.Visibility = Visibility.Collapsed;
            _autoPitch.Visibility = Visibility.Collapsed; _autoPitchLabel.Visibility = Visibility.Collapsed; _remeasure.Visibility = Visibility.Collapsed;
            // Group bus / master: effects on summed audio, no MIDI sound or per-instrument defaults.
            _midiSound.Visibility = Visibility.Collapsed;
            _autoGm.Visibility = Visibility.Collapsed;
            _autoLoad.Visibility = Visibility.Collapsed;
            _useChain.Content = "Bus on";
            _useChain.ToolTip = "Untick to bypass this chain: the summed audio passes straight on to the master";
            if (MixerBuses.IsMonitor(track))
            {
                Title = "FX: Monitor (live only, never rendered)";
                _useChain.Content = "Monitor on";
                _useChain.ToolTip = "Untick to bypass the monitoring effects. They play after the master, live only; never included in renders or exports.";
                var useGlobal = new CheckBox
                {
                    Content = "Use for all projects", IsChecked = _host.MonitorUseGlobal, Margin = new Thickness(16, 0, 0, 4),
                    ToolTip = "Ticked (default): this monitoring chain, saved with the app, is used for every project. Unticked: this project uses its own monitoring chain (empty until you add effects), saved with the project. Never rendered or exported either way."
                };
                useGlobal.Click += (_, _) => _host.SetMonitorUseGlobal(useGlobal.IsChecked == true);
                top.Children.Add(useGlobal);
            }
        }
        body.Children.Add(top);

        // Left: the chain.
        var left = new DockPanel { Width = 290, Margin = new Thickness(0, 0, 10, 0) };
        DockPanel.SetDock(left, Dock.Left);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        buttons.Children.Add(MakeButton("Add…", AddPlugin, "Add a plug-in to the end of the chain"));
        buttons.Children.Add(MakeButton("Remove", RemoveSelected, "Remove the selected plug-in (Delete)"));
        buttons.Children.Add(_status);
        _status.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        left.Children.Add(buttons);
        var listFrame = new Border { BorderThickness = new Thickness(1), Child = _list };
        listFrame.SetResourceReference(Border.BorderBrushProperty, "BorderSoftBrush");
        _list.SetResourceReference(BackgroundProperty, "PanelBrush");
        left.Children.Add(listFrame);
        body.Children.Add(left);
        _list.SelectionChanged += (_, _) => { if (!_building) ShowSelected(); };
        _list.KeyDown += (_, e) => { if (e.Key == Key.Delete) RemoveSelected(); };
        _list.MouseDoubleClick += (_, _) => { if (Selected is { } slot) Float(slot); };
        AttachDragReorder();

        // Right: the selected plug-in.
        var right = new DockPanel();
        DockPanel.SetDock(_bar, Dock.Top);
        right.Children.Add(_bar);
        var dockArea = new Grid();
        dockArea.Children.Add(_dockMessage);
        var message = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        dockArea.Children.Clear();
        message.Children.Add(_dockMessage);
        message.Children.Add(_dockHere);
        dockArea.Children.Add(message);
        _dockFrame.Child = _dock;
        _dockFrame.HorizontalAlignment = HorizontalAlignment.Left;
        _dockFrame.VerticalAlignment = VerticalAlignment.Top;
        dockArea.Children.Add(_dockFrame);
        _dockMessage.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        _dockHere.Click += (_, _) =>
        {
            if (Selected is not { } s) return;
            if (!TabForge.Plugins.PluginTrust.IsTrusted(s.Path, _host.PluginSettings))
            {
                TabForge.Plugins.PluginTrust.Approve(_host.PluginSettings, s.Path);
                _host.SaveSettings();
                _host.ChainChanged(_track);
            }
            _floating.Remove(s);
            ShowSelected();
        };
        var scroller = new ScrollViewer { Content = dockArea, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        right.Children.Add(scroller);
        body.Children.Add(right);
        BuildBar();

        Content = root;
        _host.Engine.EditorSized += OnEditorSized;
        _host.Engine.ProgramsReceived += OnPrograms;
        _host.Engine.ChainLoaded += OnChainLoaded;
        _host.Engine.EditorClosed += OnEditorClosed;
        // Moving the window: the docked editor (an engine-owned popup) and the BYPASSED overlay are moved in the same move
        // message, not by the engine's ~30 Hz poll or a dispatcher pass, so they neither lag nor flash while dragging.
        SourceInitialized += (_, _) => (PresentationSource.FromVisual(this) as System.Windows.Interop.HwndSource)?.AddHook(MoveHook);
        SizeChanged += (_, _) => PlaceOverlay();
        StateChanged += (_, _) => PlaceOverlay();
        Activated += (_, _) => PlaceOverlay();
        // The tick only picks up bypass-state changes; placement never raises the overlay above the editor it covers.
        _cpuTimer.Tick += (_, _) => { _status.Text = _host.Engine.IsRunning ? $"CPU {_host.Engine.CpuLoad:P1}" : ""; PlaceOverlay(); };
        _cpuTimer.Start();
        OwnerActivation.Attach(this);
        Closed += (_, _) =>
        {
            try { _cpuTimer.Stop(); } catch (Exception) { }
            try { _overlay?.Close(); _overlay = null; } catch (Exception) { }
            try
            {
                _host.Engine.EditorClosed -= OnEditorClosed;
                if (_docked is { } d) _host.Engine.CloseEditor(_track, d);
            }
            finally
            {
                _host.Engine.EditorSized -= OnEditorSized;
                _host.Engine.ProgramsReceived -= OnPrograms;
                _host.Engine.ChainLoaded -= OnChainLoaded;
            }
        };
        RefreshList(0);
    }

    /// <summary>A floating plug-in window was closed by the user: bring this window back (else Windows activates some other app).</summary>
    private void OnEditorClosed()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (!IsVisible || WindowState == WindowState.Minimized) return;
            Activate();
        });
    }

    /// <summary>Dims the docked editor of a bypassed plug-in and shows a large red BYPASSED label (click-through, follows the editor).</summary>
    private void PlaceOverlay()
    {
        // Bypassed: its own switch, or the whole chain switched off (a global bypass; the plug-ins stay loaded).
        var show = IsVisible && WindowState != WindowState.Minimized && Selected is { } s && (!s.Enabled || _track.SoundSource != SoundSources.Plugins)
            && ReferenceEquals(s, _docked)
            && _dockFrame.Visibility == Visibility.Visible && _dockFrame.ActualWidth > 8 && _dockFrame.ActualHeight > 8 && PresentationSource.FromVisual(_dockFrame) is not null;
        if (!show) { _overlay?.Hide(); return; }
        var source = PresentationSource.FromVisual(_dockFrame)!;
        var px = _dockFrame.PointToScreen(new Point(0, 0));
        var origin = source.CompositionTarget.TransformFromDevice.Transform(px);
        _overlay ??= new BypassOverlay { Owner = this, Topmost = false };
        if (!_overlay.IsVisible)
        {
            _overlay.Left = origin.X; _overlay.Top = origin.Y; _overlay.Width = _dockFrame.ActualWidth; _overlay.Height = _dockFrame.ActualHeight;
            _overlay.Show();
        }
        PlaceOverlayNow();
    }

    [System.Runtime.InteropServices.DllImport("user32")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hh, uint flags);
    [System.Runtime.InteropServices.DllImport("user32")] private static extern bool GetWindowRect(IntPtr h, out NativeRect r);
    [System.Runtime.InteropServices.DllImport("user32")] private static extern bool GetClientRect(IntPtr h, out NativeRect r);
    [System.Runtime.InteropServices.DllImport("user32")] private static extern bool ClientToScreen(IntPtr h, ref NativePoint p);
    [System.Runtime.InteropServices.DllImport("user32")] private static extern bool IsWindow(IntPtr h);
    [System.Runtime.InteropServices.DllImport("user32")] private static extern bool IsWindowVisible(IntPtr h);
    [System.Runtime.InteropServices.DllImport("user32")] private static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [System.Runtime.InteropServices.DllImport("user32", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern int GetClassName(IntPtr h, System.Text.StringBuilder name, int max);
    private delegate bool EnumProc(IntPtr h, IntPtr l);
    [System.Runtime.InteropServices.DllImport("user32")] private static extern bool EnumWindows(EnumProc proc, IntPtr l);
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    private IntPtr _editorPopup;

    /// <summary>WM_MOVE / WM_WINDOWPOSCHANGED of this window: move the docked editor popup and the overlay in the same message.</summary>
    private IntPtr MoveHook(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        if (msg is 0x0003 /*WM_MOVE*/ or 0x0047 /*WM_WINDOWPOSCHANGED*/)
        {
            FollowDockedEditor(hwnd);
            PlaceOverlayNow();
        }
        return IntPtr.Zero;
    }

    /// <summary>The engine's docked editor window: a popup owned by this window, of the engine's editor class.</summary>
    private IntPtr FindEditorPopup(IntPtr self)
    {
        if (_editorPopup != IntPtr.Zero && IsWindow(_editorPopup) && GetWindow(_editorPopup, 4 /*GW_OWNER*/) == self) return _editorPopup;
        var found = IntPtr.Zero;
        var name = new System.Text.StringBuilder(64);
        EnumWindows((h, _) =>
        {
            if (GetWindow(h, 4) != self) return true;
            name.Clear();
            GetClassName(h, name, name.Capacity);
            if (name.ToString() != "TabForgePluginEditor") return true;
            found = h;
            return false;
        }, IntPtr.Zero);
        return _editorPopup = found;
    }

    /// <summary>Places the docked editor exactly over the host area (clipped to this window's client area), as the engine does, but now.</summary>
    private void FollowDockedEditor(IntPtr self)
    {
        if (_docked is null || _dock.Area == IntPtr.Zero || !IsWindowVisible(_dock.Area)) return;
        var popup = FindEditorPopup(self);
        if (popup == IntPtr.Zero || !IsWindowVisible(popup) || !GetWindowRect(_dock.Area, out var r)) return;
        if (GetClientRect(self, out var client))
        {
            var origin = new NativePoint();
            ClientToScreen(self, ref origin);
            r.Left = Math.Max(r.Left, origin.X); r.Top = Math.Max(r.Top, origin.Y);
            r.Right = Math.Min(r.Right, origin.X + client.Right); r.Bottom = Math.Min(r.Bottom, origin.Y + client.Bottom);
            if (r.Right <= r.Left || r.Bottom <= r.Top) return;
        }
        // The popup belongs to the engine process: ASYNCWINDOWPOS never blocks this thread on it.
        SetWindowPos(popup, IntPtr.Zero, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, 0x4 /*NOZORDER*/ | 0x10 /*NOACTIVATE*/ | 0x4000 /*ASYNCWINDOWPOS*/);
    }

    /// <summary>Places a shown overlay in device pixels straight away (no WPF layout pass): over the host area clipped to this
    /// window's client area, and in z-order directly above the docked editor popup (never HWND_TOP, never topmost), so any
    /// other window that is in front of the editor stays in front of the overlay too.</summary>
    private void PlaceOverlayNow()
    {
        if (_overlay is not { IsVisible: true } overlay || PresentationSource.FromVisual(_dockFrame) is not { } source) return;
        var h = new System.Windows.Interop.WindowInteropHelper(overlay).Handle;
        var self = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (h == IntPtr.Zero || self == IntPtr.Zero) return;
        var px = _dockFrame.PointToScreen(new Point(0, 0));
        var scale = source.CompositionTarget.TransformToDevice;
        int left = (int)Math.Round(px.X), top = (int)Math.Round(px.Y);
        int right = left + (int)Math.Round(_dockFrame.ActualWidth * scale.M11), bottom = top + (int)Math.Round(_dockFrame.ActualHeight * scale.M22);
        if (GetClientRect(self, out var client))
        {
            var o = new NativePoint();
            ClientToScreen(self, ref o);
            left = Math.Max(left, o.X); top = Math.Max(top, o.Y);
            right = Math.Min(right, o.X + client.Right); bottom = Math.Min(bottom, o.Y + client.Bottom);
        }
        if (right <= left || bottom <= top) { overlay.Hide(); return; }
        uint flags = 0x10 /*NOACTIVATE*/;
        var after = IntPtr.Zero;
        var popup = FindEditorPopup(self);
        if (popup != IntPtr.Zero && IsWindowVisible(popup))
        {
            after = GetWindow(popup, 3 /*GW_HWNDPREV*/);   // the window directly above the editor (zero: the editor is on top)
            if (after == h) flags |= 0x4 /*NOZORDER: already directly above it*/;
        }
        else flags |= 0x4 /*NOZORDER*/;
        SetWindowPos(h, after, left, top, right - left, bottom - top, flags);
    }

    private PluginSlot? Selected => _list.SelectedItem is ListBoxItem { Tag: PluginSlot slot } ? slot : null;

    private Menu BuildMenu()
    {
        var menu = new Menu();
        DockPanel.SetDock(menu, Dock.Top);
        var fx = new MenuItem { Header = "_FX" };
        fx.Items.Add(Item("Add plug-in…", AddPlugin));
        fx.Items.Add(Item("Remove plug-in", RemoveSelected));
        fx.Items.Add(new Separator());
        fx.Items.Add(Item("Save FX chain…", SaveChain));
        fx.Items.Add(Item("Load FX chain…", () => LoadChain(replace: true)));
        fx.Items.Add(Item("Add FX chain to the end…", () => LoadChain(replace: false)));
        fx.Items.Add(new Separator());
        fx.Items.Add(Item("Clear chain", () => Edit(() => { CloseDocked(); _track.Rig.Plugins.Clear(); }, refresh: true)));
        menu.Items.Add(fx);
        var options = new MenuItem { Header = "_Options" };
        var dock = new MenuItem { Header = "Show plug-in windows in this window", IsCheckable = true, IsChecked = _host.PluginSettings.DockPluginWindows };
        dock.Click += (_, _) => { _host.PluginSettings.DockPluginWindows = dock.IsChecked; _host.SaveSettings(); _floating.Clear(); ShowSelected(); };
        var onTop = new MenuItem { Header = "Keep floating plug-in windows on top", IsCheckable = true, IsChecked = _host.PluginSettings.PluginWindowsOnTop };
        onTop.Click += (_, _) => { _host.PluginSettings.PluginWindowsOnTop = onTop.IsChecked; _host.SaveSettings(); };
        options.Items.Add(dock);
        options.Items.Add(onTop);
        options.Items.Add(new Separator());
        options.Items.Add(Item("Plug-in folders and audio settings…", _host.OpenAudioSettings));
        menu.Items.Add(options);
        return menu;

        static MenuItem Item(string header, Action action)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => action();
            return item;
        }
    }

    private static Button MakeButton(string text, Action click, string? tip = null)
    {
        var b = new Button { Content = text, Margin = new Thickness(0, 0, 4, 0), Padding = new Thickness(10, 3, 10, 3), ToolTip = tip };
        b.Click += (_, _) => click();
        return b;
    }

    private void BuildBar()
    {
        var addPreset = new Button { Content = "+", Width = 28, Margin = new Thickness(4, 0, 8, 0), ToolTip = "Save or delete presets" };
        addPreset.Click += (_, _) =>
        {
            var menu = new ContextMenu();
            var save = new MenuItem { Header = "Save preset…" };
            save.Click += (_, _) => SavePreset();
            menu.Items.Add(save);
            if (_presets.SelectedItem is PresetItem { User: true } user)
            {
                var delete = new MenuItem { Header = $"Delete preset “{user.Name}”" };
                delete.Click += (_, _) => { if (Selected is { } s) { PluginLibrary.DeletePreset(s, user.Name); LoadPresetList(); } };
                menu.Items.Add(delete);
            }
            addPreset.ContextMenu = menu;
            menu.PlacementTarget = addPreset;
            menu.IsOpen = true;
        };
        _presets.SelectionChanged += (_, _) => { if (!_building) ApplyPreset(); };
        _wiring.Click += (_, _) => OpenWiring();
        _midiButton.Click += (_, _) => OpenMidiProcessing();
        var volumeDragging = false;
        _volume.EditStarted += (_, _) => { volumeDragging = true; _host.BeginChainEdit(); };
        // While dragging only a light gain message goes to the engine; the full ChainChanged (dirty flag, rebuilds, refreshes) runs once on release.
        _volume.ValueChanged += (_, e) =>
        {
            if (_building || Selected is not { } s) return;
            s.OutputDb = Math.Round(e.NewValue, 1);
            if (volumeDragging) _host.Engine.SetPluginGain(_track, s); else _host.ChainChanged(_track);
        };
        _volume.EditEnded += (_, _) => { volumeDragging = false; if (Selected is { } s) _host.ChainChanged(_track); };
        _bypass.Click += (_, _) => { if (Selected is { } s) Edit(() => { s.Enabled = _bypass.IsChecked == true; _host.Engine.SetPluginEnabled(_track, s); }, refresh: true); };

        _bar.Children.Add(_presets);
        _bar.Children.Add(addPreset);
        _bar.Children.Add(_wiring);
        _bar.Children.Add(_midiButton);
        var volumeLabel = new TextBlock { Text = "Volume", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
        volumeLabel.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        _bar.Children.Add(volumeLabel);
        _bar.Children.Add(_volume);
        _bar.Children.Add(new Border { Width = 8 });
        _bar.Children.Add(_bypass);
    }

    private void Edit(Action change, bool refresh = false, bool show = true)
    {
        _host.BeginChainEdit();
        change();
        _host.ChainChanged(_track);
        // The app may have switched GM sound automatically (see MixerGroups.ApplyAutoGm): show what is really playing.
        _midiSound.IsChecked = _track.MidiSound;
        _useChain.IsChecked = _track.SoundSource == SoundSources.Plugins;
        UpdateMidiButton();
        if (refresh) RefreshList(_list.SelectedIndex);
        else if (show) Dispatcher.BeginInvoke(ShowSelected, System.Windows.Threading.DispatcherPriority.Background);
    }

    /// <summary>A plug-in just added whose engine load is still running (its editor opens once the engine reports the chain loaded).</summary>
    private PluginSlot? _loadingSlot;

    private void OnChainLoaded()
    {
        if (_loadingSlot is null)
        {
            // A rebuilt chain may have new plug-in instances (their old editors closed): open the docked one again. For an
            // instance whose editor survived, the engine just re-places and repaints it (never a blank host area).
            if (_docked is { } d && _dock.Area != IntPtr.Zero && _dockFrame.Visibility == Visibility.Visible && _track.Rig.Plugins.Contains(d))
                Dispatcher.BeginInvoke(() => { if (ReferenceEquals(_docked, d)) _host.Engine.OpenEditor(_track, d, _dock.Area, _host.DarkTheme, docked: true); },
                    System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            return;
        }
        _loadingSlot = null;
        Dispatcher.BeginInvoke(ShowSelected, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    private void RefreshList(int select)
    {
        _building = true;
        try
        {
            _list.Items.Clear();
            foreach (var slot in _track.Rig.Plugins)
            {
                var check = new CheckBox { IsChecked = slot.Enabled, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), ToolTip = slot.Enabled ? "On (untick to bypass)" : "Bypassed (tick to enable)" };
                System.Windows.Automation.AutomationProperties.SetHelpText(check, slot.Enabled ? "On" : "Bypassed");
                var captured = slot;
                check.Click += (_, _) => Edit(() => { captured.Enabled = check.IsChecked == true; _host.Engine.SetPluginEnabled(_track, captured); });
                var row = new DockPanel { Margin = new Thickness(2) };
                row.Children.Add(check);
                var kind = new TextBlock { Text = slot.Type == PluginSlotType.Instrument ? "INST" : "FX", FontSize = Services.ThemeService.MinFontSize, Width = 32, VerticalAlignment = VerticalAlignment.Center };
                kind.SetResourceReference(TextBlock.ForegroundProperty, slot.Type == PluginSlotType.Instrument ? "AccentBrush" : "MutedBrush");
                row.Children.Add(kind);
                var names = new StackPanel();
                names.Children.Add(new TextBlock { Text = slot.Name, TextTrimming = TextTrimming.CharacterEllipsis });
                if (slot.Vendor.Length > 0)
                {
                    var vendor = new TextBlock { Text = slot.Vendor, FontSize = Services.ThemeService.MinFontSize, TextTrimming = TextTrimming.CharacterEllipsis };
                    vendor.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
                    names.Children.Add(vendor);
                }
                row.Children.Add(names);
                _list.Items.Add(new ListBoxItem { Content = row, Tag = slot, ToolTip = $"{slot.Name}\n{slot.Path}\nDouble-click: float its window · drag: reorder" });
            }
            if (_track.Rig.Plugins.Count > 0) _list.SelectedIndex = Math.Clamp(select, 0, _track.Rig.Plugins.Count - 1);
        }
        finally { _building = false; }
        _useChain.IsChecked = _track.SoundSource == SoundSources.Plugins;
        _midiSound.IsChecked = _track.MidiSound;
        _autoLoad.IsChecked = AutoChains.Has(_host.PluginSettings, _track);
        ShowSelected();
    }

    /// <summary>Shows the selected plug-in's bar and its controls (docked here, unless it floats).</summary>
    private void ShowSelected()
    {
        var slot = Selected;
        _bar.IsEnabled = slot is not null;
        _building = true;
        try
        {
            _volume.Value = slot?.OutputDb ?? 0;
            _bypass.IsChecked = slot?.Enabled ?? true;
        }
        finally { _building = false; }
        LoadPresetList();
        UpdateMidiButton();

        if (slot is not null && !ReferenceEquals(slot, _docked)) CloseDocked();
        _dockHere.Visibility = Visibility.Collapsed;
        _dockHere.Content = "Show it here";
        if (slot is null) { ShowMessage("No plug-ins yet. Use Add… (or the FX menu) to choose a VST instrument or effect."); return; }
        if (!TabForge.Plugins.PluginTrust.IsTrusted(slot.Path, _host.PluginSettings))
        {
            ShowMessage($"Blocked: not approved, or the file changed since you approved it. It is not loaded (use Allow only if you trust it):\n{slot.Path}");
            _dockHere.Content = "Allow";
            _dockHere.Visibility = Visibility.Visible;
            return;
        }
        if (!_host.Engine.IsRunning || _host.Engine.SlotOf(_track) < 0)
        {
            ShowMessage(_track.SoundSource != SoundSources.Plugins
                ? "The chain is off. Tick “Play this track through the chain” (or the FX power switch) to use the plug-in."
                : slot.Enabled ? "Starting the plug-in…" : "This plug-in is bypassed.");
            _docked = null;   // the engine has no chain for this track: its editor is gone, so it must be opened again when the chain is back
            return;
        }
        if (ReferenceEquals(slot, _loadingSlot))
        {
            // Just added: the engine is still creating it. Opening its window now would stall this window's message pump.
            ShowMessage("Starting the plug-in…");
            return;
        }
        if (!_host.PluginSettings.DockPluginWindows || _floating.Contains(slot))
        {
            ShowMessage("This plug-in's window is floating.");
            if (_host.PluginSettings.DockPluginWindows) _dockHere.Visibility = Visibility.Visible;
            if (!_host.PluginSettings.DockPluginWindows) Float(slot);
            return;
        }
        _dockMessage.Text = "";
        _dockFrame.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(PlaceOverlay, System.Windows.Threading.DispatcherPriority.Background);
        _host.Engine.RequestPrograms(_track, slot);
        if (_dock.Area != IntPtr.Zero && !ReferenceEquals(_docked, slot))
        {
            _docked = slot;
            _host.Engine.OpenEditor(_track, slot, _dock.Area, _host.DarkTheme, docked: true);
        }
        else if (_dock.Area == IntPtr.Zero) Dispatcher.BeginInvoke(ShowSelected, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void UpdateMidiButton()
    {
        var lit = Selected is { } s && s.MidiProcessors.Any(p => p.Enabled);
        if (lit) { _midiButton.SetResourceReference(BackgroundProperty, "AccentBrush"); }
        else { _midiButton.ClearValue(BackgroundProperty); }
    }

    private void ShowMessage(string text)
    {
        PlaceOverlay();
        _dockMessage.Text = text;
        _dockFrame.Visibility = Visibility.Collapsed;
    }

    private void CloseDocked()
    {
        if (_docked is { } d) _host.Engine.CloseEditor(_track, d);
        _docked = null;
    }

    private void Float(PluginSlot slot)
    {
        if (ReferenceEquals(_docked, slot)) CloseDocked();
        _floating.Add(slot);
        if (!_host.Engine.OpenEditor(_track, slot, _host.OwnerWindow, _host.DarkTheme, docked: false, onTop: _host.PluginSettings.PluginWindowsOnTop))
            ShowMessage("The plug-in window can be opened when the chain is on.");
        else if (ReferenceEquals(Selected, slot)) { ShowMessage("This plug-in's window is floating."); if (_host.PluginSettings.DockPluginWindows) _dockHere.Visibility = Visibility.Visible; }
    }

    private void OnEditorSized(int engineSlot, int index, int width, int height)
    {
        if (_docked is not { } d || engineSlot != _host.Engine.SlotOf(_track) || index != _track.Rig.Plugins.IndexOf(d)) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        _dock.Width = width / dpi.DpiScaleX;
        _dock.Height = height / dpi.DpiScaleY;
        _lastEditorPx = (width, height);
    }
    private (int W, int H) _lastEditorPx;
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        if (_docked is not null && _lastEditorPx.W > 0) { _dock.Width = _lastEditorPx.W / newDpi.DpiScaleX; _dock.Height = _lastEditorPx.H / newDpi.DpiScaleY; }
    }

    // ---------- presets ----------
    private sealed record PresetItem(string Name, int Program, bool User)
    {
        public override string ToString() => User ? $"★ {Name}" : Name;
    }

    private void LoadPresetList()
    {
        _building = true;
        try
        {
            var items = new List<PresetItem>();
            if (Selected is { } slot)
            {
                items.AddRange(_programs.Select((n, i) => new PresetItem(n, i, false)));
                items.AddRange(PluginLibrary.PresetNames(slot).Select(n => new PresetItem(n, -1, true)));
            }
            _presets.ItemsSource = items;
            _presets.IsEnabled = items.Count > 0;
            if (items.Count == 0) _presets.Text = "No presets";
        }
        finally { _building = false; }
    }

    private void OnPrograms(int engineSlot, int index, int current, IReadOnlyList<string> names)
    {
        if (Selected is not { } slot || engineSlot != _host.Engine.SlotOf(_track) || index != _track.Rig.Plugins.IndexOf(slot)) return;
        _programs = names.ToList();
        LoadPresetList();
        _building = true;
        try { if (current >= 0 && current < names.Count) _presets.SelectedIndex = current; }
        finally { _building = false; }
    }

    private void ApplyPreset()
    {
        if (Selected is not { } slot || _presets.SelectedItem is not PresetItem item) return;
        if (!item.User) { _host.Engine.SetProgram(_track, slot, item.Program); return; }
        if (PluginLibrary.LoadPreset(slot, item.Name) is not { } preset) return;
        _host.BeginChainEdit();
        slot.State = preset.State;
        _host.Engine.SetState(_track, slot, preset.State);
        if (preset.OutputDb is { } db)
        {
            slot.OutputDb = db;
            _host.Engine.SetPluginGain(_track, slot);
            _building = true;
            try { _volume.Value = db; } finally { _building = false; }
        }
        _host.ChainChanged(_track);
    }

    private async void SavePreset()
    {
        if (Selected is not { } slot) return;
        var name = GpDialogs.Prompt("Save preset", $"Preset name for {slot.Name}:", "My preset");
        if (string.IsNullOrWhiteSpace(name)) return;
        await _host.Engine.CollectStatesAsync(new[] { _track }, 2000);
        if (!IsLoaded) return;   // the window closed while the states were read
        if (slot.State is not { Length: > 0 } state)
        {
            MessageBox.Show(this, "This plug-in did not give its settings (turn the chain on first).", "Save preset", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try { PluginLibrary.SavePreset(slot, name.Trim(), state); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            MessageBox.Show(this, "The preset could not be saved: " + ex.Message, "Save preset", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        LoadPresetList();
    }

    // ---------- wiring ----------
    /// <summary>Opens (or brings forward) the wiring window of the selected plug-in, else the first one.</summary>
    public void OpenWiring()
    {
        var slot = Selected ?? _track.Rig.Plugins.FirstOrDefault();
        if (slot is null) return;
        if (_wiringWindow is { IsLoaded: true } open && ReferenceEquals(open.Tag, slot)) { open.Activate(); return; }
        _wiringWindow?.Close();
        var window = new WiringWindow(_host, _track, slot, this, change => Edit(change, refresh: false, show: false), () => OpenMidiProcessing(slot)) { Tag = slot };
        window.Closed += (_, _) => { if (ReferenceEquals(_wiringWindow, window)) _wiringWindow = null; };
        _wiringWindow = window;
        window.Show();
    }

    private MidiProcessingWindow? _midiWindow;

    /// <summary>Opens (or brings forward) the MIDI processing window of the given plug-in, else the selected one, else the first.</summary>
    public void OpenMidiProcessing(PluginSlot? plugin = null)
    {
        var slot = plugin ?? Selected ?? _track.Rig.Plugins.FirstOrDefault();
        if (slot is null) return;
        if (_midiWindow is { IsLoaded: true } open && ReferenceEquals(open.Tag, slot)) { open.Activate(); return; }
        _midiWindow?.Close();
        var window = new MidiProcessingWindow(_host, _track, slot, this, change => Edit(change, refresh: false, show: false)) { Tag = slot };
        window.Closed += (_, _) => { if (ReferenceEquals(_midiWindow, window)) _midiWindow = null; };
        _midiWindow = window;
        window.Show();
    }

    // ---------- chain editing ----------
    private void RemoveSelected()
    {
        if (Selected is not { } slot) return;
        var index = _list.SelectedIndex;
        if (ReferenceEquals(_docked, slot)) CloseDocked();
        Edit(() =>
        {
            _track.Rig.Plugins.Remove(slot);
            if (slot.Type == PluginSlotType.Instrument && !_track.Rig.Plugins.Any(p => p.Type == PluginSlotType.Instrument)) _track.MidiSound = true;
        }, refresh: false, show: false);
        RefreshList(index);
    }

    private async void AddPlugin()
    {
        try { await AddPluginCore(); }
        catch (Exception ex)
        {
            _status.Text = "";
            try { MessageBox.Show(this, "The plug-in could not be added: " + ex.GetBaseException().Message, "Add plug-in", MessageBoxButton.OK, MessageBoxImage.Warning); }
            catch (Exception) { }
        }
    }

    private async Task AddPluginCore()
    {
        if (_track.Rig.Plugins.Count >= InputLimits.MaxPluginsPerTrack) return;
        var chosen = PluginBrowser.Choose(this, _host);
        if (chosen is null) return;
        TabForge.Plugins.PluginTrust.Approve(_host.PluginSettings, chosen.Path);   // picked from the user's own scan / browse: trusted
        var role = chosen.Role;
        var vendor = chosen.Vendor;
        if (role.Length == 0)
        {
            // Find out what the plug-in is by asking it (in a throwaway process, crash-safe).
            _status.Text = $"Checking {chosen.Name}…";
            var settings = _host.PluginSettings;
            var described = await Task.Run(() => PluginCatalog.Describe(chosen.Path, settings));
            _host.SaveSettings();
            role = described.Role;
            if (vendor.Length == 0) vendor = described.Vendor;
            _status.Text = "";
        }
        var isInstrument = role == "Instrument";
        var slot = new PluginSlot
        {
            Name = chosen.Name, Path = chosen.Path, Format = chosen.Format, Vendor = vendor, Enabled = true,
            Type = isInstrument ? PluginSlotType.Instrument : PluginSlotType.Effect, RoleMode = PluginRoles.Auto
        };
        var hadInstrument = _track.Rig.Plugins.Any(p => p.Type == PluginSlotType.Instrument);
        CloseDocked();   // close the old editor before the engine starts creating the new plug-in
        _loadingSlot = _host.Engine.IsRunning ? slot : null;
        Edit(() =>
        {
            // An instrument goes first (it makes the sound the effects process); effects go to the end.
            if (isInstrument && !hadInstrument) _track.Rig.Plugins.Insert(0, slot); else _track.Rig.Plugins.Add(slot);
            if (isInstrument && !hadInstrument) _track.MidiSound = false;
            // A plug-in you add is loaded and heard at once: adding one switches the chain on.
            _track.SoundSource = SoundSources.Plugins;
        }, refresh: false, show: false);
        RefreshList(_track.Rig.Plugins.IndexOf(slot));
    }

    private async void SaveChain()
    {
        await _host.Engine.CollectStatesAsync(new[] { _track }, 2000);
        if (!IsLoaded) return;   // the window closed while the states were read
        Directory.CreateDirectory(PluginLibrary.ChainsFolder);
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save FX chain", Filter = "TabForge FX chain (*.tfchain)|*.tfchain", DefaultExt = ".tfchain",
            InitialDirectory = PluginLibrary.ChainsFolder, FileName = _track.Name,
        };
        if (dialog.ShowDialog(this) != true) return;
        try { PluginLibrary.SaveChain(dialog.FileName, _track.Rig.Plugins); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            MessageBox.Show(this, "The FX chain could not be saved: " + ex.Message, "Save FX chain", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _status.Text = $"Saved {Path.GetFileName(dialog.FileName)}";
    }

    private void LoadChain(bool replace)
    {
        Directory.CreateDirectory(PluginLibrary.ChainsFolder);
        var reaperFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "REAPER", "FXChains");
        var useReaper = Directory.Exists(reaperFolder) && !Directory.EnumerateFiles(PluginLibrary.ChainsFolder, "*.tfchain").Any();
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Load FX chain", Filter = "TabForge FX chain (*.tfchain)|*.tfchain|REAPER FX chain (*.RfxChain)|*.RfxChain",
            InitialDirectory = useReaper ? reaperFolder : PluginLibrary.ChainsFolder, FilterIndex = useReaper ? 2 : 1,
        };
        if (dialog.ShowDialog(this) != true) return;
        List<PluginSlot>? chain;
        if (dialog.FileName.EndsWith(".rfxchain", StringComparison.OrdinalIgnoreCase))
        {
            var catalog = _host.PluginSettings.ScanCache.Select(k => new VstPluginInfo(k.Name, k.Path, k.Format, k.Vendor, k.Role)).Concat(VstScannerService.LastScan).ToList();
            if (catalog.Count == 0) catalog = VstScannerService.Scan(VstScannerService.RootsFor(_host.PluginSettings).ToList(), CancellationToken.None, null).ToList();
            var imported = ReaperChainImporter.Import(dialog.FileName, catalog);
            chain = imported?.Chain;
            if (imported is not null && imported.Value.Report.Length > 0) MessageBox.Show(this, imported.Value.Report, "REAPER FX chain import", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else chain = PluginLibrary.LoadChain(dialog.FileName);
        if (chain is null) { MessageBox.Show(this, "That file is not a valid FX chain.", "Load FX chain", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        if (!replace && _track.Rig.Plugins.Count + chain.Count > InputLimits.MaxPluginsPerTrack) return;
        CloseDocked();
        Edit(() =>
        {
            if (replace) _track.Rig.Plugins.Clear();
            _track.Rig.Plugins.AddRange(chain);
            if (chain.Count > 0) _track.SoundSource = SoundSources.Plugins;
        }, refresh: true);
    }

    // ---------- drag and drop reordering ----------
    private void AttachDragReorder()
    {
        _list.PreviewMouseLeftButtonDown += (_, e) => _dragStart = e.GetPosition(_list);
        _list.PreviewMouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || Selected is not { } slot) return;
            var delta = e.GetPosition(_list) - _dragStart;
            if (Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            if (e.OriginalSource is DependencyObject source && FindParent<CheckBox>(source) is not null) return;
            DragDrop.DoDragDrop(_list, new DataObject(typeof(PluginSlot), slot), DragDropEffects.Move);
        };
        _list.DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(typeof(PluginSlot)) ? DragDropEffects.Move : DragDropEffects.None; e.Handled = true; };
        _list.Drop += (_, e) =>
        {
            if (e.Data.GetData(typeof(PluginSlot)) is not PluginSlot moving) return;
            var target = FindParent<ListBoxItem>(e.OriginalSource as DependencyObject)?.Tag as PluginSlot;
            var plugins = _track.Rig.Plugins;
            var from = plugins.IndexOf(moving);
            var to = target is null ? plugins.Count - 1 : plugins.IndexOf(target);
            if (from < 0 || to < 0 || from == to) return;
            Edit(() => { plugins.RemoveAt(from); plugins.Insert(to, moving); }, refresh: false);
            RefreshList(to);
        };
    }

    private sealed class BypassOverlay : Window
    {
        [System.Runtime.InteropServices.DllImport("user32")] private static extern int GetWindowLong(IntPtr h, int i);
        [System.Runtime.InteropServices.DllImport("user32")] private static extern int SetWindowLong(IntPtr h, int i, int v);
        [System.Runtime.InteropServices.DllImport("user32")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hh, uint flags);

        public BypassOverlay()
        {
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
            ShowInTaskbar = false; ShowActivated = false; Focusable = false; ResizeMode = ResizeMode.NoResize;
            var grid = new Grid { Background = new SolidColorBrush(Color.FromArgb(110, 0, 0, 0)), IsHitTestVisible = false };
            grid.Children.Add(new Viewbox
            {
                Stretch = Stretch.Uniform, Margin = new Thickness(24),
                Child = new TextBlock { Text = "BYPASSED", FontWeight = FontWeights.Bold, FontSize = 96, Foreground = new SolidColorBrush(Color.FromArgb(150, 255, 40, 40)), RenderTransform = new RotateTransform(-12), RenderTransformOrigin = new Point(0.5, 0.5) }
            });
            Content = grid;
            SourceInitialized += (_, _) =>
            {
                var h = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                SetWindowLong(h, -20, GetWindowLong(h, -20) | 0x20 | 0x80 | 0x08000000);   // TRANSPARENT (click-through), TOOLWINDOW, NOACTIVATE
            };
        }
    }

    private static T? FindParent<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node is not null and not T) node = node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
        return node as T;
    }
}
