using TabForge.Controllers;
using TabForge.Documents;
using System.Windows;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

// MainWindow: the Mixer window (groups, levels, sound source) and per-track FX chain windows. The windows
// only see IMixerHost / IFxChainHost; plug-in hosting itself lives behind the audio engine client.
public partial class MainWindow : IMixerHost, IFxChainHost, IMixerWindowsHost
{
    private MixerWindowsController? _mixerWindows;
    private MixerWindowsController MixerWindows => _mixerWindows ??= new MixerWindowsController(this);
    IMixerHost IMixerWindowsHost.MixerHost => this;
    IFxChainHost IMixerWindowsHost.FxHost => this;
    void IMixerWindowsHost.MixerSliderDragEnded() => _engineSync.MixerSliderDragEnded();

    /// <summary>Mixer button / hotkey: opens the mixer, or brings it forward.</summary>
    private void OpenMixer() => MixerWindows.OpenMixer();

    /// <summary>The song or its tracks changed: keep an open mixer in step (cheap: only on structure changes).</summary>
    private void RefreshMixerWindow() => MixerWindows.Mixer?.Rebuild();

    SongProject IMixerHost.Project => _project;

    void IMixerHost.BeginMixerEdit() => _engineSync.BeginMixerEdit();

    void IMixerHost.MixerChanged(bool recompile)
    {
        _project.IsDirty = true;
        if (recompile) _midi.Rebuild(_project); else _midi.RefreshMix(_project);   // the MIDI sound follows at once
        if (!recompile && MixerWindows.Mixer is { IsDraggingSlider: true })
        {
            // A slider drag: as light as a track-list drag. The track list's matching slider moves in place, the plug-in
            // engine sync is coalesced to once per frame, and the full list / timeline refresh waits for the drag's end.
            Arrangement.SyncMixValues();
            _engineSync.MixerSliderDragged();
        }
        else
        {
            SyncAudioEngine(); // live level/pan/mute/solo for plug-in tracks (the engine's track mix; only changed values are sent)
            _engineSync.ScheduleMixerFollowUp();
        }
        UpdateTitle();
    }

    void IMixerHost.MuteSoloChanged() => ApplyMuteSolo();

    /// <summary>
    /// A mute / solo toggle (track list, group row or Mixer): mixer state, not score content. The sound changes first (the MIDI gate and one
    /// engine level per track); the song is marked dirty without invalidating the timeline, so playback is never recompiled or spliced.
    /// The visuals follow in place; only a song with audio clips or monitored input reconciles the engine afterwards, once, when idle.
    /// </summary>
    private void ApplyMuteSolo()
    {
        _midi.SetMuteSolo(_project);
        _engineSync.SyncLevelsNow();
        var wasDirty = _project.IsDirty;
        DocumentEdits.MarkChanged(Doc, invalidatesTimeline: false);
        Arrangement.ApplyMuteVisualsNow();
        if (_muteSoloFollowUp) return;
        _muteSoloFollowUp = true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
        {
            _muteSoloFollowUp = false;
            if (_project.Tracks.Any(t => t.AudioClips.Count > 0 || t.MonitorInput || t.RecordArm)) SyncAudioEngine();   // clips and monitoring follow mute and solo
            MixerWindows.Mixer?.SyncValues();
            if (!wasDirty) { UpdateTitle(); RefreshTabs(); }
        }));
    }

    string? IMixerHost.HotkeyAction(string gesture) => _hotkeys.Global.TryGetValue(gesture, out var id) ? id : null;

    /// <summary>Everything that shows the track order, captured before an order change (see <see cref="PlayOrderAnimation"/>).</summary>
    private (Dictionary<object, double> List, Dictionary<object, double>? Mixer) CaptureOrderLayout() =>
        (Arrangement.CaptureRowTops(), MixerWindows.Mixer?.CaptureLayout());

    /// <summary>
    /// The track list and the open mixer show the same new order: the mixer is rebuilt in it and both start their movement
    /// animation in this one UI turn (same duration), so they always show the same thing.
    /// </summary>
    private void PlayOrderAnimation((Dictionary<object, double> List, Dictionary<object, double>? Mixer) before)
    {
        MixerWindows.Mixer?.Rebuild();
        Arrangement.AnimateReorder(before.List);
        MixerWindows.Mixer?.AnimateReorder(before.Mixer);
    }

    bool IMixerHost.ReorderFromMixer(Func<SongProject, bool> apply, string status)
    {
        var before = CaptureOrderLayout();
        if (!DocumentEdits.Run(Doc, apply).Changed) return false;   // one undo step per drop / nudge
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
        MixerWindows.CloseFxWindows(MixerBuses.IsMonitor);
        DocumentEdits.Run(Doc, p => { p.Mixer.MonitorUseGlobal = useGlobal; return true; }, invalidatesTimeline: false);
        SyncAudioEngine();
        RefreshMixerWindow();
        UpdateTitle();
        StatusText.Text = useGlobal ? "Monitor FX: the app-wide chain is used for every project" : "Monitor FX: this project uses its own chain";
        OpenMonitorFx();
    }

    /// <summary>Group header power part: bypasses / enables the group's bus chain (one undo step).</summary>
    internal void ToggleBus(string group)
    {
        var bus = default(Models.BusChain)!;   // the bus entry is created inside the edit, so the undo state is taken before it exists (as before)
        DocumentEdits.Run(Doc, p => { bus = p.Mixer.Bus(group); MixerBuses.SetOn(bus, !bus.On); return true; }, invalidatesTimeline: false);
        SyncAudioEngine();
        RefreshTracks();
        RefreshArrangement();
        MixerWindows.Mixer?.SyncValues();
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
        if (DocumentEdits.Run(Doc, p => Services.TrackColouring.Group(p, group, hex) > 0, invalidatesTimeline: false).Changed) TrackColoursChanged();
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
            DocumentEdits.Run(Doc, p => { p.Mixer.ShowGroupsInTrackList = on; return true; }, invalidatesTimeline: false);
            RefreshTracks();
            RefreshArrangement();
            ScheduleFitTimelineToTracks(); // group headers add rows: grow / shrink the arrangement dock to fit
            UpdateTitle();
            MixerWindows.Mixer?.SyncValues();    // the mixer's "Groups in track list" box follows (one setting, two places)
            return;
        }
        var hidden = _settings.Timeline.HiddenTrackColumns;
        hidden.RemoveAll(h => string.Equals(h, what, StringComparison.OrdinalIgnoreCase));
        if (!on) hidden.Add(what);
        Arrangement.HiddenColumns = hidden;
        SaveSettings();
    }

    /// <summary>
    /// Right-click on + Track: "Add track…" and "Audio track", nothing else (owner decision 2026-09-30; the Mixer has its own button). The colour items moved to the
    /// track list's empty-area menu (Colours), and the colour of each group to Settings > Appearance &amp; colours.
    /// </summary>
    private void ShowAddTrackMenu(FrameworkElement target) => BuildAddTrackMenu(target).IsOpen = true;

    /// <summary>The + Track button's right-click menu: "Add track…" and "Audio track" (a test reads and clicks its items).</summary>
    internal System.Windows.Controls.ContextMenu BuildAddTrackMenu(FrameworkElement target)
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
        Item("Audio track", AddAudioTrack);
        return menu;
    }

    /// <summary>Track list menu "Colours > Colour tracks by group" (the group colours are in Settings > Appearance &amp; colours).</summary>
    private void ColourTracksByGroup()
    {
        var changed = 0;
        DocumentEdits.Run(Doc, p => { changed = Services.TrackColouring.ByGroup(p, _settings.Appearance.GroupColours); return true; }, invalidatesTimeline: false);
        TrackColoursChanged();
        StatusText.Text = changed == 0 ? "Tracks already have their group colours" : $"Coloured {changed} track{(changed == 1 ? "" : "s")} by group";
    }

    /// <summary>Track list menu "Colours > Colour tracks...".</summary>
    private void ColourTracksWithDialog()
    {
        var captured = false;
        Views.TrackColoursDialog.Show(this, _project, () => { if (!captured) { CheckpointUndo(); captured = true; } }, TrackColoursChanged);
    }

    void IMixerHost.OpenAudioSettings() => OpenSettingsCategory(SettingsCatalog.AudioVst);

    /// <summary>FX button on a track row / mixer strip / hotkey: that track's chain window.</summary>
    internal void OpenFxChain(TrackModel? track) => MixerWindows.OpenFxChain(track);

    /// <summary>The FX power switch: plays the track through its chain, or back on Windows MIDI.</summary>
    internal void ToggleTrackChain(TrackModel track)
    {
        DocumentEdits.Run(Doc, _ => { track.SoundSource = track.SoundSource == SoundSources.Plugins ? SoundSources.Midi : SoundSources.Plugins; return true; }, invalidatesTimeline: false);
        ((IFxChainHost)this).ChainChanged(track); // only switches: the chain window opens from the FX part
        StatusText.Text = track.SoundSource == SoundSources.Plugins ? $"{track.Name}: plays through its FX chain" : $"{track.Name}: plays on Windows MIDI";
    }

    /// <summary>Track.Wiring / Track.MidiProcessing: the FX window of the track, with the wiring window of its selected (else first) plug-in on top.</summary>
    internal void OpenWiring(TrackModel? track)
    {
        if (track is null) return;
        OpenFxChain(track);
        MixerWindows.FxWindowOf(track)?.OpenWiring();
    }

    /// <summary>Track.MidiProcessing: the FX window of the track, with the MIDI processing window of its selected (else first) plug-in on top.</summary>
    internal void OpenMidiProcessing(TrackModel? track)
    {
        if (track is null) return;
        OpenFxChain(track);
        MixerWindows.FxWindowOf(track)?.OpenMidiProcessing();
    }

    IReadOnlyList<TrackModel> IFxChainHost.Tracks => _project.Tracks;
    PluginSettings IFxChainHost.PluginSettings => _settings.Plugins;

    void IFxChainHost.BeginChainEdit() => ((IMixerHost)this).BeginMixerEdit();

    void IFxChainHost.ChainChanged(TrackModel track)
    {
        if (track.IsBus) MixerBuses.SyncBack(track);   // the FX window's power / plug-ins belong to the bus
        if (MixerBuses.IsMonitor(track) && _project.Mixer.MonitorUseGlobal) SaveSettings();   // the app-wide monitor chain lives in the settings, not the song
        else _project.IsDirty = true;
        SyncAudioEngine();
        _midi.Rebuild(_project);
        RefreshTracks();
        RefreshArrangement();
        RefreshMixerWindow();
        UpdateTitle();
    }

    void IFxChainHost.PluginSwitched(TrackModel track)
    {
        // An instrument that is switched can change what plays the track's notes (the plug-in or the GM sound): that is a full chain change.
        if (track.Rig.Plugins.Any(p => p.Type == PluginSlotType.Instrument)) { ((IFxChainHost)this).ChainChanged(track); return; }
        if (track.IsBus) MixerBuses.SyncBack(track);
        if (MixerBuses.IsMonitor(track) && _project.Mixer.MonitorUseGlobal) SaveSettings();
        else _project.IsDirty = true;
        SyncAudioEngine();   // the engine already has the switch; this keeps its track mix in step
        RefreshTracks();
        RefreshMixerWindow();
        UpdateTitle();
    }

    void IFxChainHost.OpenAudioSettings() => OpenSettingsCategory(SettingsCatalog.AudioVst);
    IntPtr IFxChainHost.OwnerWindow => new System.Windows.Interop.WindowInteropHelper(this).Handle;
    bool IFxChainHost.DarkTheme => !Visualization.VisualTheme.IsLight;
    Audio.AudioEngineClient IFxChainHost.Engine => _engine;

    void IFxChainHost.SaveSettings() => SaveSettings();

    /// <summary>Song time for audio clips: the clock of the active song (it belongs to the document, so it stays with the song when its tab moves to another window).</summary>
    internal Audio.SongClock SongClock => Doc.Playback.Clock;

    private void SyncAudioEngine() => _engineSync.Sync();

    /// <summary>Song switched or restored (undo): rebuild the mixer, close chain windows of tracks no longer shown.</summary>
    private void SyncMixerWindows()
    {
        SyncAudioEngine();
        RefreshMixerWindow();
        MixerWindows.CloseFxWindows(track => !_project.Tracks.Contains(track) && !MixerBuses.IsCurrent(_project, track)
            && !(MixerBuses.IsMonitor(track) && ReferenceEquals(track.Bus, ((IMixerHost)this).MonitorChain)));
    }

    /// <summary>The window as the host of its <see cref="EngineSyncController"/>.</summary>
    private sealed class EngineSyncHost : IEngineSyncHost
    {
        private readonly MainWindow _window;
        public EngineSyncHost(MainWindow window) => _window = window;

        public DocumentSession ActiveDocument => _window.Doc;
        public IReadOnlyList<DocumentSession> Documents => _window._documents.Documents;
        public AppSettings Settings => _window._settings;
        public bool IsInitialized => _window._mainWindowInitialized;
        /// <summary>The one "plug-in loading slowly" question the application shows: the engine is shared, so every window hears the same slow load, but only one prompt (in the window whose song uses the plug-in) may be open at a time.</summary>
        private static Views.ThemedConfirmDialog? _slowLoadPrompt;

        public bool ShowSlowLoadPrompt(string pluginName, Action<bool> answered)
        {
            if (_slowLoadPrompt is not null) return false;
            var prompt = _slowLoadPrompt = new Views.ThemedConfirmDialog(
                "Plug-in loading slowly",
                $"{pluginName} is taking a long time to load.\n\nDisable it: the song plays without it while this song is open. " +
                "This is not a quarantine: it is not saved and the plug-in loads normally the next time the song is opened.",
                yesToolTip: "Keep waiting for the plug-in to load",
                noToolTip: "Skip this plug-in for this song and reload its chain without it",
                showCancel: false, yesText: "Keep waiting", noText: "Disable it") { Owner = _window };
            prompt.ShowModeless(result =>
            {
                if (ReferenceEquals(_slowLoadPrompt, prompt)) _slowLoadPrompt = null;
                answered(result == MessageBoxResult.No);
            });
            return true;
        }

        public void CloseSlowLoadPrompt() => _slowLoadPrompt?.Close();
        public void Post(Action work, System.Windows.Threading.DispatcherPriority priority) => _window.PostIfOpen(work, priority);
        public void RebuildMidi() => _window._midi.Rebuild(_window._project);
        public void RearmChannelSetup() => _window._midi.RearmChannelSetup();
        public void SetStatus(string text) => _window.StatusText.Text = text;
        public void CheckpointUndo() => _window.CheckpointUndo();
        public void SaveSettings() => _window.SaveSettings();
        public void UpdateTitle() => _window.UpdateTitle();
        public void RefreshTracks() => _window.RefreshTracks();
        public void RefreshArrangement() => _window.RefreshArrangement();
        public void RefreshArrangementAll() => _window.Arrangement.RefreshAll();
        public void RefreshMixer() => _window.RefreshMixerWindow();
        public void UpdateAudioDeviceStatus() => _window.UpdateAudioDeviceStatus();
        public void UpdatePluginTrustBar() => _window.UpdatePluginTrustBar();
        public void UpdateMediaApprovalBar() => _window.UpdateMediaApprovalBar();
    }
}
