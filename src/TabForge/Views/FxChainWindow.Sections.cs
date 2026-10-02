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

// The FX chain window's layout, one builder per section (options row, chain list, selected plug-in pane).
public sealed partial class FxChainWindow
{
    /// <summary>The options row above the chain: chain on/off, GM sound, auto-load, startup track, pitch matching, monitor scope.</summary>
    private WrapPanel BuildOptionsRow()
    {
        // Wraps onto further lines when the window is too narrow, so every option stays readable.
        var top = new WrapPanel { Margin = new Thickness(0, 0, 0, 4), Orientation = Orientation.Horizontal };
        DockPanel.SetDock(top, Dock.Top);
        _useChain.IsChecked = _track.SoundSource == SoundSources.Plugins;
        _useChain.Click += (_, _) => Edit(() => _track.SoundSource = _useChain.IsChecked == true ? SoundSources.Plugins : SoundSources.Midi);
        _midiSound.IsChecked = _track.MidiSound;
        _useChain.Margin = new Thickness(0, 0, 16, 0);
        _midiSound.Margin = new Thickness(0, 0, 16, 0);
        _autoLoad.IsChecked = AutoChains.Has(_host.PluginSettings, _track);
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
        _startup.IsChecked = StartupTracks.Has(_host.PluginSettings, _track);
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
        if (_track.IsBus)
        {
            _startup.Visibility = Visibility.Collapsed;
            _autoPitch.Visibility = Visibility.Collapsed; _autoPitchLabel.Visibility = Visibility.Collapsed; _remeasure.Visibility = Visibility.Collapsed;
            // Group bus / master: effects on summed audio, no MIDI sound or per-instrument defaults.
            _midiSound.Visibility = Visibility.Collapsed;
            _autoGm.Visibility = Visibility.Collapsed;
            _autoLoad.Visibility = Visibility.Collapsed;
            _useChain.Content = "Bus on";
            _useChain.ToolTip = "Untick to bypass this chain: the summed audio passes straight on to the master";
            if (MixerBuses.IsMonitor(_track))
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
        return top;
    }

    /// <summary>The chain list on the left with its Add / Remove buttons and drag-to-reorder.</summary>
    private DockPanel BuildChainPane()
    {
        // Left: the chain.
        var left = new DockPanel { Width = 290, Margin = new Thickness(0, 0, 10, 0) };
        DockPanel.SetDock(left, Dock.Left);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        buttons.Children.Add(UiIds.Id(MakeButton("Add…", AddPlugin, "Add a plug-in to the end of the chain"), "Fx.Add"));
        buttons.Children.Add(UiIds.Id(MakeButton("Remove", RemoveSelected, "Remove the selected plug-in (Delete)"), "Fx.Remove"));
        buttons.Children.Add(_status);
        _status.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        left.Children.Add(buttons);
        var listFrame = new Border { BorderThickness = new Thickness(1), Child = _list };
        listFrame.SetResourceReference(Border.BorderBrushProperty, "BorderSoftBrush");
        _list.SetResourceReference(BackgroundProperty, "PanelBrush");
        left.Children.Add(listFrame);
        _list.SelectionChanged += (_, _) => { if (!_building) ShowSelected(); };
        _list.KeyDown += (_, e) => { if (e.Key == Key.Delete) RemoveSelected(); };
        _list.MouseDoubleClick += (_, _) => { if (Selected is { } slot) Float(slot); };
        AttachDragReorder();
        return left;
    }

    /// <summary>The selected plug-in on the right: its bar above the docked editor area.</summary>
    private DockPanel BuildPluginPane()
    {
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
            if (_dockHere.Tag is "allow-again")
            {
                // Quarantine only: trust and approval records stay as they are (an untrusted file is still blocked by the trust check).
                if (TabForge.Plugins.PluginQuarantine.AllowAgain(_host.PluginSettings.Quarantined, s.Path) > 0)
                {
                    _host.SaveSettings();
                    _host.ChainChanged(_track);   // the next engine Sync loads it again (its chain key no longer says Skip)
                }
                ShowSelected();
                return;
            }
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
        BuildBar();
        return right;
    }

    /// <summary>Hooks the engine events, the window-move/placement handlers, the CPU timer and the close clean-up.</summary>
    private void AttachWindowEvents()
    {
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
    }
}
