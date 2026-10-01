using System.Windows;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

// MainWindow: the Mixer window (groups, levels, sound source) and per-track FX chain windows. The windows
// only see IMixerHost / IFxChainHost; plug-in hosting itself lives behind the audio engine client.
public partial class MainWindow : IMixerHost, IFxChainHost
{
    private MixerWindow? _mixerWindow;
    private readonly Dictionary<TrackModel, FxChainWindow> _fxWindows = new(ReferenceEqualityComparer.Instance);
    private bool _mixerUndoCaptured;

    /// <summary>Mixer button / hotkey: opens the mixer, or brings it forward.</summary>
    private void OpenMixer()
    {
        if (_mixerWindow is { IsLoaded: true })
        {
            _mixerWindow.Rebuild();
            _mixerWindow.Activate();
            return;
        }
        _mixerWindow = new MixerWindow(this, this);
        _mixerWindow.Closed += (_, _) => _mixerWindow = null;
        _mixerWindow.SliderDragEnded += () => { if (_mixerDragRefreshHeld) { _mixerDragRefreshHeld = false; ScheduleMixerFollowUp(); } };
        _mixerWindow.Show();
    }

    /// <summary>The song or its tracks changed: keep an open mixer in step (cheap: only on structure changes).</summary>
    private void RefreshMixerWindow() => _mixerWindow?.Rebuild();

    SongProject IMixerHost.Project => _project;

    void IMixerHost.BeginMixerEdit()
    {
        // One undo step per gesture: the first change after a pause captures, later ones in the same drag do not.
        if (_mixerUndoCaptured) return;
        CaptureUndo();
        _mixerUndoCaptured = true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, () => _mixerUndoCaptured = false);
    }

    void IMixerHost.MixerChanged(bool recompile)
    {
        _project.IsDirty = true;
        if (recompile) _midi.Rebuild(_project); else _midi.RefreshMix(_project);   // the MIDI sound follows at once
        if (!recompile && _mixerWindow is { IsDraggingSlider: true })
        {
            // A slider drag: as light as a track-list drag. The track list's matching slider moves in place, the plug-in
            // engine sync is coalesced to once per frame, and the full list / timeline refresh waits for the drag's end.
            Arrangement.SyncMixValues();
            ScheduleEngineSync();
            _mixerDragRefreshHeld = true;
        }
        else
        {
            SyncAudioEngine(); // live level/pan/mute/solo for plug-in tracks (the engine's track mix; only changed values are sent)
            ScheduleMixerFollowUp();
        }
        UpdateTitle();
    }

    private bool _mixerFollowUpPending, _mixerDragRefreshHeld, _engineSyncPending;

    /// <summary>At most one engine sync per frame while a mixer slider is dragged (only changed values are sent).</summary>
    private void ScheduleEngineSync()
    {
        if (_engineSyncPending) return;
        _engineSyncPending = true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() =>
        {
            _engineSyncPending = false;
            SyncAudioEngine();
        }));
    }

    /// <summary>
    /// Root cause of the mixer sliders jumping: every single value step rebuilt the whole track list and timeline
    /// (RefreshTracks + RefreshArrangement) inside the drag, so the UI thread fell behind the pointer and the value
    /// leapt between far apart positions. The sound is updated at once; the list refresh runs once, when input is idle.
    /// </summary>
    private void ScheduleMixerFollowUp()
    {
        if (_mixerFollowUpPending) return;
        _mixerFollowUpPending = true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(() =>
        {
            _mixerFollowUpPending = false;
            RefreshTracks();
            RefreshArrangement();
        }));
    }

    string? IMixerHost.HotkeyAction(string gesture) => _hotkeyMap.TryGetValue(gesture, out var id) ? id : null;

    /// <summary>Everything that shows the track order, captured before an order change (see <see cref="PlayOrderAnimation"/>).</summary>
    private (Dictionary<object, double> List, Dictionary<object, double>? Mixer) CaptureOrderLayout() =>
        (Arrangement.CaptureRowTops(), _mixerWindow?.CaptureLayout());

    /// <summary>
    /// The track list and the open mixer show the same new order: the mixer is rebuilt in it and both start their movement
    /// animation in this one UI turn (same duration), so they always show the same thing.
    /// </summary>
    private void PlayOrderAnimation((Dictionary<object, double> List, Dictionary<object, double>? Mixer) before)
    {
        _mixerWindow?.Rebuild();
        Arrangement.AnimateReorder(before.List);
        _mixerWindow?.AnimateReorder(before.Mixer);
    }

    bool IMixerHost.ReorderFromMixer(Func<SongProject, bool> apply, string status)
    {
        var before = CaptureOrderLayout();
        var capture = CaptureUndo();   // one undo step per drop / nudge
        if (!apply(_project))
        {
            if (capture is { } cancelled) _undo.Discard(cancelled);
            return false;
        }
        _project.IsDirty = true;
        SyncAudioEngine();
        if (_midi.IsPlaying) _midi.RefreshArrangement(_project, Enumerable.Range(0, MaxMeasures()).ToArray());
        RefreshTracks();
        RefreshArrangement();
        Editor.InvalidateScoreLayout();
        UpdateTitle();
        PlayOrderAnimation(before);
        StatusText.Text = status;
        return true;
    }

    void IMixerHost.OpenFxChain(TrackModel track) => OpenFxChain(track);

    void IMixerHost.OpenBusFx(string? group) => OpenBusFx(group);

    int IMixerHost.MasterVolume
    {
        get => _settings.Audio.MasterVolume;
        set => Arrangement.MasterVolumeKnob.Value = Math.Clamp(value, 0, 100);   // the knob's handler applies it everywhere
    }

    /// <summary>Group header / mixer group row FX part, Mixer.GroupFx / Mixer.MasterFx: the bus chain window (null group: master).</summary>
    internal void OpenBusFx(string? group) =>
        OpenFxChain(group is null ? MixerBuses.MasterTrack(_project) : MixerBuses.BusTrack(_project, group));

    /// <summary>The monitoring chain in effect for the open song: the app-wide one, or the song's own after it opted out.</summary>
    BusChain IMixerHost.MonitorChain => MixerBuses.MonitorChain(_project, _settings.Plugins.MonitorFx);

    /// <summary>Mixer "MON" button / Mixer.MonitorFx: the monitoring chain window (live output only, never rendered).</summary>
    internal void OpenMonitorFx() => OpenFxChain(MixerBuses.MonitorTrack(_project, _settings.Plugins.MonitorFx));

    void IMixerHost.OpenMonitorFx() => OpenMonitorFx();

    bool IFxChainHost.MonitorUseGlobal => _project.Mixer.MonitorUseGlobal;

    /// <summary>"Use for all projects" in the monitor FX window: switches this song between the app-wide chain and its own (the window reopens on the other chain).</summary>
    void IFxChainHost.SetMonitorUseGlobal(bool useGlobal)
    {
        if (_project.Mixer.MonitorUseGlobal == useGlobal) return;
        foreach (var (track, window) in _fxWindows.ToList()) if (MixerBuses.IsMonitor(track)) window.Close();
        CaptureUndo();
        _project.Mixer.MonitorUseGlobal = useGlobal;
        _project.IsDirty = true;
        SyncAudioEngine();
        RefreshMixerWindow();
        UpdateTitle();
        StatusText.Text = useGlobal ? "Monitor FX: the app-wide chain is used for every project" : "Monitor FX: this project uses its own chain";
        OpenMonitorFx();
    }

    /// <summary>Group header power part: bypasses / enables the group's bus chain (one undo step).</summary>
    internal void ToggleBus(string group)
    {
        CaptureUndo();
        var bus = _project.Mixer.Bus(group);
        MixerBuses.SetOn(bus, !bus.On);
        _project.IsDirty = true;
        SyncAudioEngine();
        RefreshTracks();
        RefreshArrangement();
        _mixerWindow?.SyncValues();
        UpdateTitle();
        StatusText.Text = $"{group} bus effects {(bus.On ? "on" : "bypassed")}";
    }

    string IMixerHost.GroupColour(string group) => Services.TrackColouring.ColourOf(group, _settings.Appearance.GroupColours);

    void IMixerHost.SetGroupColour(string group, string hex) => ColourGroup(group, hex);

    /// <summary>Remembers a group's colour and colours its tracks (one undo step).</summary>
    private void ColourGroup(string group, string hex)
    {
        _settings.Appearance.GroupColours[group] = hex;
        SaveSettings();
        CaptureUndo();
        if (Services.TrackColouring.Group(_project, group, hex) > 0) TrackColoursChanged();
    }

    private void TrackColoursChanged()
    {
        _project.IsDirty = true;
        RefreshTracks();
        RefreshArrangement();
        Editor.InvalidateVisual();
        UpdateTitle();
    }

    bool IMixerHost.TrackListShows(string what) => what switch
    {
        "groups" => _project.Mixer.ShowGroupsInTrackList,
        _ => !_settings.Timeline.HiddenTrackColumns.Contains(what, StringComparer.OrdinalIgnoreCase),
    };

    void IMixerHost.SetTrackListShows(string what, bool on)
    {
        if (what == "groups")
        {
            CaptureUndo();
            _project.Mixer.ShowGroupsInTrackList = on;
            _project.IsDirty = true;
            RefreshTracks();
            RefreshArrangement();
            ScheduleFitTimelineToTracks(); // group headers add rows: grow / shrink the arrangement dock to fit
            UpdateTitle();
            _mixerWindow?.SyncValues();    // the mixer's "Groups in track list" box follows (one setting, two places)
            return;
        }
        var hidden = _settings.Timeline.HiddenTrackColumns;
        hidden.RemoveAll(h => string.Equals(h, what, StringComparison.OrdinalIgnoreCase));
        if (!on) hidden.Add(what);
        Arrangement.HiddenColumns = hidden;
        SaveSettings();
    }

    /// <summary>
    /// Right-click on + Track: adding a track and the Mixer, nothing else (owner decision 2026-09-30). The colour items moved to the
    /// track list's empty-area menu (Colours), and the colour of each group to Settings > Appearance &amp; colours.
    /// </summary>
    private void ShowAddTrackMenu(FrameworkElement target)
    {
        var menu = new System.Windows.Controls.ContextMenu
        {
            PlacementTarget = target,
            Style = (Style)FindResource(typeof(System.Windows.Controls.ContextMenu))
        };
        System.Windows.Controls.MenuItem Item(string header, Action action)
        {
            var item = new System.Windows.Controls.MenuItem { Header = header, Style = (Style)FindResource(typeof(System.Windows.Controls.MenuItem)) };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
            return item;
        }
        Item("Add track…", AddTrackWithWindow);
        Item("Mixer…", OpenMixer);
        menu.IsOpen = true;
    }

    /// <summary>Track list menu "Colours > Colour tracks by group" (the group colours are in Settings > Appearance &amp; colours).</summary>
    private void ColourTracksByGroup()
    {
        CaptureUndo();
        var changed = Services.TrackColouring.ByGroup(_project, _settings.Appearance.GroupColours);
        TrackColoursChanged();
        StatusText.Text = changed == 0 ? "Tracks already have their group colours" : $"Coloured {changed} track{(changed == 1 ? "" : "s")} by group";
    }

    /// <summary>Track list menu "Colours > Colour tracks...".</summary>
    private void ColourTracksWithDialog()
    {
        var captured = false;
        Views.TrackColoursDialog.Show(this, _project, () => { if (!captured) { CaptureUndo(); captured = true; } }, TrackColoursChanged);
    }

    private static System.Windows.Controls.Border Swatch(string hex)
    {
        System.Windows.Media.Brush brush;
        try { brush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex)); }
        catch (FormatException) { brush = System.Windows.Media.Brushes.Gray; }
        return new System.Windows.Controls.Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(3), Background = brush };
    }

    void IMixerHost.OpenAudioSettings() => OpenSettingsCategory(SettingsCatalog.AudioVst);

    /// <summary>FX button on a track row / mixer strip / hotkey: that track's chain window.</summary>
    internal void OpenFxChain(TrackModel? track)
    {
        if (track is null) return;
        if (_fxWindows.TryGetValue(track, out var open) && open.IsLoaded) { open.Activate(); return; }
        var window = new FxChainWindow(this, track, this);
        window.Closed += (_, _) => _fxWindows.Remove(track);
        _fxWindows[track] = window;
        window.Show();
    }

    /// <summary>The FX power switch: plays the track through its chain, or back on Windows MIDI.</summary>
    internal void ToggleTrackChain(TrackModel track)
    {
        CaptureUndo();
        track.SoundSource = track.SoundSource == SoundSources.Plugins ? SoundSources.Midi : SoundSources.Plugins;
        ((IFxChainHost)this).ChainChanged(track); // only switches: the chain window opens from the FX part
        StatusText.Text = track.SoundSource == SoundSources.Plugins ? $"{track.Name}: plays through its FX chain" : $"{track.Name}: plays on Windows MIDI";
    }

    /// <summary>Track.Wiring / Track.MidiProcessing: the FX window of the track, with the wiring window of its selected (else first) plug-in on top.</summary>
    internal void OpenWiring(TrackModel? track)
    {
        if (track is null) return;
        OpenFxChain(track);
        if (_fxWindows.TryGetValue(track, out var window)) window.OpenWiring();
    }

    /// <summary>Track.MidiProcessing: the FX window of the track, with the MIDI processing window of its selected (else first) plug-in on top.</summary>
    internal void OpenMidiProcessing(TrackModel? track)
    {
        if (track is null) return;
        OpenFxChain(track);
        if (_fxWindows.TryGetValue(track, out var window)) window.OpenMidiProcessing();
    }

    IReadOnlyList<TrackModel> IFxChainHost.Tracks => _project.Tracks;
    PluginSettings IFxChainHost.PluginSettings => _settings.Plugins;

    void IFxChainHost.BeginChainEdit() => ((IMixerHost)this).BeginMixerEdit();

    void IFxChainHost.ChainChanged(TrackModel track)
    {
        if (track.IsBus) MixerBuses.SyncBack(track);   // the FX window's power / plug-ins belong to the bus
        if (MixerBuses.IsMonitor(track) && _project.Mixer.MonitorUseGlobal) SaveSettings();   // the app-wide monitor chain lives in the settings, not the song
        else _project.IsDirty = true;
        var pluginTotal = CountPlugins();
        if (pluginTotal > _pluginTotal) AutoEnablePlayAllThroughEngine(); // a plug-in was added
        else if (pluginTotal == 0 && _pluginTotal > 0) AutoDisablePlayAllThroughEngine(); // the last plug-in was removed
        _pluginTotal = pluginTotal;
        SyncAudioEngine();
        _midi.Rebuild(_project);
        RefreshTracks();
        RefreshArrangement();
        RefreshMixerWindow();
        UpdateTitle();
    }

    void IFxChainHost.OpenAudioSettings() => OpenSettingsCategory(SettingsCatalog.AudioVst);
    IntPtr IFxChainHost.OwnerWindow => new System.Windows.Interop.WindowInteropHelper(this).Handle;
    bool IFxChainHost.DarkTheme => !Visualization.VisualTheme.IsLight;
    Audio.AudioEngineClient IFxChainHost.Engine => Audio.AudioEngineClient.Instance;

    void IFxChainHost.SaveSettings() => SaveSettings();

    private bool _audioEngineHooked;

    /// <summary>Song time for audio clips (one per window: only the active song plays).</summary>
    internal Audio.SongClock SongClock { get; } = new(Audio.AudioEngineClient.Instance);

    /// <summary>
    /// Starts / updates / stops the audio engine for the active song's plug-in tracks and routes their MIDI to it.
    /// Songs without plug-in tracks never start the engine.
    /// </summary>
    private void SyncAudioEngine()
    {
        var engine = Audio.AudioEngineClient.Instance;
        HookAudioEngine(engine);
        TabForge.Models.MixerGroups.AutoGmSound = _settings.Plugins.AutoGmSound;
        var playAll = _settings.Plugins.PlayAllThroughEngine;
        if (TabForge.Models.MixerGroups.PlayAllThroughEngine != playAll)
        {
            TabForge.Models.MixerGroups.PlayAllThroughEngine = playAll;
            if (_mainWindowInitialized) _midi.Rebuild(_project);
        }
        Audio.AudioRouting.Apply(_project, Doc.Playback.Routing, engine, _settings.Plugins, _settings.Audio.MasterVolume, owner: Doc);   // R-10: the active document owns the engine
        UpdateAudioDeviceStatus();
        UpdatePluginTrustBar();
        UpdateMediaApprovalBar();
    }

    private void HookAudioEngine(Audio.AudioEngineClient engine)
    {
        if (_audioEngineHooked) return;
        _audioEngineHooked = true;
        engine.Quarantine = () => _settings.Plugins.Quarantined;
        // Multi-tab playback: a song that still plays in another tab keeps its engine tracks live (not parked) when this tab takes over.
        engine.IsOwnerPlaying = owner => owner is TabForge.Documents.DocumentSession { Playback.Engine.IsPlaying: true };
        Arrangement.QuarantinedPlugins = () => _settings.Plugins.Quarantined;   // faulted FX icon on tracks whose plug-in crashed
        // A synth created while the song plays starts on the default piano: give it its programs again.
        engine.ChainLoaded += () => _midi.RearmChannelSetup();
        engine.ChainAcknowledged += ack =>
        {
            // A plug-in whose file changed since its approval was refused at load: show it in the trust bar and re-send it as Skip.
            if (ack.Plugins.Any(x => x.Status == TabForge.Audio.Contracts.PluginLoadStatus.BlockedChanged)) { UpdatePluginTrustBar(); SyncAudioEngine(); }
            // An instrument that failed to load (or loads fine again) changes whether GM takes over: re-apply the automatic GM sound.
            else if (engine.RefreshAvailability(_project.Tracks, _settings.Plugins)) { SyncAudioEngine(); _midi.Rebuild(_project); RefreshTracks(); RefreshMixerWindow(); }
        };
        engine.AutoPitch ??= new TabForge.Audio.AutoPitchMatcher(engine, () => _settings.Plugins);   // automatic pitch matching of VST instruments
        engine.PluginCrashed += path =>
        {
            SaveSettings();
            // No path: the engine hung outside any plug-in call (or stopped answering) and was restarted; nothing was switched off.
            StatusText.Text = string.IsNullOrEmpty(path)
                ? "The audio engine stopped responding and was restarted; playback continues"
                : $"{System.IO.Path.GetFileNameWithoutExtension(path)} stopped working and was switched off; playback continues";
            if (!engine.TooManyCrashes) SyncAudioEngine();
            else StatusText.Text = "The audio engine stopped several times; plug-in tracks now play on Windows MIDI. Check Settings > Audio & Plug-ins.";
            RefreshMixerWindow();
            Arrangement.RefreshAll();
        };
        engine.PluginFailed += (path, why) => StatusText.Text = $"{System.IO.Path.GetFileNameWithoutExtension(path)} could not be loaded: {why}";
        // RT-02: invalid audio (NaN / infinity) never reaches the mix; the plug-in is skipped until it is switched off and on again.
        engine.PluginMisbehaved += (path, index) => StatusText.Text = index >= 0
            ? $"{System.IO.Path.GetFileNameWithoutExtension(path)} produced invalid audio and was bypassed; switch it off and on in its FX chain to try again"
            : $"A track's sound ({path}) produced invalid audio; it was muted for that moment";
        engine.DeviceError += message => StatusText.Text = $"Audio output: {message}";
        // Slow is not a crash: a big instrument may take a while; it is switched off only if it passes its limit (load 90 s).
        engine.PluginSlow += (path, kind, seconds) =>
        {
            StatusText.Text = $"{System.IO.Path.GetFileNameWithoutExtension(path)} " + kind switch
            {
                TabForge.Audio.Contracts.PluginCallKind.Load => "is still loading",
                TabForge.Audio.Contracts.PluginCallKind.SetState => "is still applying its settings",
                TabForge.Audio.Contracts.PluginCallKind.GetState => "is still saving its settings",
                TabForge.Audio.Contracts.PluginCallKind.Editor => "is still opening its window",
                _ => "is still busy",
            } + $" ({seconds} s)…";
            if (kind == TabForge.Audio.Contracts.PluginCallKind.Load && seconds >= TabForge.Audio.Contracts.EngineWatchdog.LongNoticeSec)
                PromptSlowLoad(engine, path);
        };
        // R-06: "Disable it" on a very slow load skips that plug-in for the active song only (not saved, unlike quarantine).
        engine.SkipForNow = () => Doc.SkippedPlugins;
        engine.ChainLoaded += () => _slowLoadPrompt?.Close();   // the load finished: nothing left to decide
        engine.PluginEdited += () => { if (!_project.IsDirty) { _project.IsDirty = true; UpdateTitle(); } };
    }

    private Views.ThemedConfirmDialog? _slowLoadPrompt;

    /// <summary>R-06: a plug-in load past 30 s asks (non-modal) whether to keep waiting or disable it for this song.</summary>
    private void PromptSlowLoad(Audio.AudioEngineClient engine, string path)
    {
        if (_slowLoadPrompt is not null || string.IsNullOrEmpty(path) || Doc.SkippedPlugins.Contains(path)) return;
        var name = System.IO.Path.GetFileNameWithoutExtension(path);
        var doc = Doc;
        var prompt = _slowLoadPrompt = new Views.ThemedConfirmDialog(
            "Plug-in loading slowly",
            $"{name} is taking a long time to load.\n\nDisable it: the song plays without it while this song is open. " +
            "This is not a quarantine: it is not saved and the plug-in loads normally the next time the song is opened.",
            yesToolTip: "Keep waiting for the plug-in to load",
            noToolTip: "Skip this plug-in for this song and reload its chain without it",
            showCancel: false, yesText: "Keep waiting", noText: "Disable it") { Owner = this };
        prompt.ShowModeless(result =>
        {
            _slowLoadPrompt = null;
            if (result != MessageBoxResult.No) return;
            doc.SkippedPlugins.Add(path);
            StatusText.Text = $"{name} is disabled for this song (not permanently); its chain reloads without it once the engine is free";
            if (ReferenceEquals(doc, Doc)) SyncAudioEngine();
            RefreshMixerWindow();
        });
    }

    /// <summary>Song switched or restored (undo): rebuild the mixer, close chain windows of tracks no longer shown.</summary>
    private void SyncMixerWindows()
    {
        AutoEnableForSong();
        SyncAudioEngine();
        RefreshMixerWindow();
        foreach (var (track, window) in _fxWindows.ToList())
            if (!_project.Tracks.Contains(track) && !MixerBuses.IsCurrent(_project, track)
                && !(MixerBuses.IsMonitor(track) && ReferenceEquals(track.Bus, ((IMixerHost)this).MonitorChain))) window.Close();
    }
}
