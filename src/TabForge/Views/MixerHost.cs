using System.Windows;
using TabForge.Audio;
using TabForge.Controllers;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Plugins;
using TabForge.Services;

namespace TabForge.Views;

/// <summary>What the mixer host needs from the main window beyond the pane basics: the active song, its engines and the window's refreshes.</summary>
internal interface IMixerSurface : IPaneHost
{
    DocumentSession Doc { get; }
    IReadOnlyList<DocumentSession> Documents { get; }
    /// <summary>The Windows MIDI playback engine of the active song.</summary>
    PlaybackEngine Midi { get; }
    EngineSyncController EngineSync { get; }
    ArrangementPanel Arrangement { get; }
    HotkeyMaps Hotkeys { get; }
    AudioEngineClient Engine { get; }
    void SyncAudioEngine();
    void RefreshTracks();
    void RefreshArrangement();
    void RefreshTabs();
    void UpdateTitle();
    void ScheduleFitTimelineToTracks();
    void CheckpointUndo();
    void OpenAudioSettings();
}

// Owns: the host side of the Mixer window, the FX chain windows and the mixer windows controller for one main window: mixer value
//   changes, mute and solo, groups (colour, collapse, rules, order), bus and monitor chains, chain edits and the track-list options.
// Does not own: the windows themselves (MixerWindowsController, MixerWindow, FxChainWindow), plug-in hosting (the engine client),
//   the song's undo (DocumentEdits), the audio engine sync (EngineSyncController).
// Tests: TestMixer, TestMixerSliders, TestMixerDragAndDrop, TestTrackListFollowsMixerInPlace, TestMixerHost.
internal sealed class MixerHost : IMixerHost, IFxChainHost, IMixerWindowsHost
{
    private readonly IMixerSurface _s;
    private bool _muteSoloFollowUp;

    public MixerHost(IMixerSurface surface)
    {
        _s = surface;
        Windows = new MixerWindowsController(this);
    }

    /// <summary>The open Mixer window and FX chain windows.</summary>
    public MixerWindowsController Windows { get; }

    private SongProject Song => _s.Project;
    private DocumentSession Doc => _s.Doc;

    // ---------- IMixerWindowsHost ----------

    Window IMixerWindowsHost.Window => _s.Window;
    IMixerHost IMixerWindowsHost.MixerHost => this;
    IFxChainHost IMixerWindowsHost.FxHost => this;
    void IMixerWindowsHost.MixerSliderDragEnded() => _s.EngineSync.MixerSliderDragEnded();

    /// <summary>Mixer button / hotkey: opens the mixer, or brings it forward.</summary>
    public void OpenMixer() => Windows.OpenMixer();

    /// <summary>The song or its tracks changed: keep an open mixer in step (cheap: only on structure changes).</summary>
    public void RefreshWindow() => Windows.Mixer?.Rebuild();

    /// <summary>Refreshes mixer windows and closes stale FX windows, optionally syncing the restored document once after the frame.</summary>
    public void SyncWindows(bool deferEngineSync)
    {
        _s.EngineSync.SyncOrSchedule(Doc, deferEngineSync);
        RefreshWindow();
        Windows.CloseStaleFxWindows(Song);
    }

    // ---------- IMixerHost ----------

    public SongProject Project => Song;

    public void BeginMixerEdit() => _s.EngineSync.BeginMixerEdit();

    public void MixerChanged(bool recompile)
    {
        Song.IsDirty = true;
        if (recompile) _s.Midi.Rebuild(Song); else _s.Midi.RefreshMix(Song);   // the MIDI sound follows at once
        if (!recompile && Windows.Mixer is { IsDraggingSlider: true })
        {
            // A slider drag: as light as a track-list drag. The track list's matching slider moves in place, the plug-in
            // engine sync is coalesced to once per frame, and the full list / timeline refresh waits for the drag's end.
            _s.Arrangement.SyncMixValues();
            _s.EngineSync.MixerSliderDragged();
        }
        else
        {
            _s.SyncAudioEngine(); // live level/pan/mute/solo for plug-in tracks (the engine's track mix; only changed values are sent)
            _s.EngineSync.ScheduleMixerFollowUp();
        }
        _s.UpdateTitle();
    }

    public void MuteSoloChanged() => ApplyMuteSolo();

    /// <summary>
    /// A mute / solo toggle (track list, group row or Mixer): mixer state, not score content. The sound changes first (the MIDI gate and one
    /// engine level per track); the song is marked dirty without invalidating the timeline, so playback is never recompiled or spliced.
    /// The visuals follow in place; only a song with audio clips or monitored input reconciles the engine afterwards, once, when idle.
    /// </summary>
    public void ApplyMuteSolo()
    {
        _s.Midi.SetMuteSolo(Song);
        _s.EngineSync.SyncLevelsNow();
        var wasDirty = Song.IsDirty;
        DocumentEdits.MarkChanged(Doc, invalidatesTimeline: false);
        _s.Arrangement.ApplyMuteVisualsNow();
        if (_muteSoloFollowUp) return;
        _muteSoloFollowUp = true;
        _s.Window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
        {
            _muteSoloFollowUp = false;
            if (Song.Tracks.Any(t => t.AudioClips.Count > 0 || t.MonitorInput || t.RecordArm)) _s.SyncAudioEngine();   // clips and monitoring follow mute and solo
            Windows.Mixer?.SyncValues();
            if (!wasDirty) { _s.UpdateTitle(); _s.RefreshTabs(); }
        }));
    }

    public string? HotkeyAction(string gesture) => _s.Hotkeys.Global.TryGetValue(gesture, out var id) ? id : null;

    /// <summary>Everything that shows the track order, captured before an order change (see <see cref="PlayOrderAnimation"/>).</summary>
    public (Dictionary<object, double> List, Dictionary<object, double>? Mixer) CaptureOrderLayout() =>
        (_s.Arrangement.CaptureRowTops(), Windows.Mixer?.CaptureLayout());

    /// <summary>
    /// The track list and the open mixer show the same new order: the mixer is rebuilt in it and both start their movement
    /// animation in this one UI turn (same duration), so they always show the same thing.
    /// </summary>
    public void PlayOrderAnimation((Dictionary<object, double> List, Dictionary<object, double>? Mixer) before)
    {
        Windows.Mixer?.Rebuild();
        _s.Arrangement.AnimateReorder(before.List);
        Windows.Mixer?.AnimateReorder(before.Mixer);
    }

    public bool ReorderFromMixer(Func<SongProject, bool> apply, string status)
    {
        var before = CaptureOrderLayout();
        if (!DocumentEdits.Run(Doc, apply).Changed) return false;   // one undo step per drop / nudge
        _s.SyncAudioEngine();
        if (_s.Midi.IsPlaying) _s.Midi.RefreshArrangement(Song, Enumerable.Range(0, BarRangeEditor.MaxMeasures(Song)).ToArray());
        _s.RefreshTracks();
        _s.RefreshArrangement();
        _s.Editor.InvalidateScoreLayout();
        _s.UpdateTitle();
        PlayOrderAnimation(before);
        _s.SetStatus(status);
        return true;
    }

    void IMixerHost.OpenFxChain(TrackModel track) => OpenFxChain(track);

    public int MasterVolume
    {
        get => _s.Settings.Audio.MasterVolume;
        set => _s.Arrangement.MasterVolumeKnob.Value = Math.Clamp(value, 0, 100);   // the knob's handler applies it everywhere
    }

    /// <summary>Group header / mixer group row FX part, Mixer.GroupFx / Mixer.MasterFx: the bus chain window (null group: master).</summary>
    public void OpenBusFx(string? group) =>
        OpenFxChain(group is null ? MixerBuses.MasterTrack(Song) : MixerBuses.BusTrack(Song, group));

    /// <summary>The monitoring chain in effect for the open song: the app-wide one, or the song's own after it opted out.</summary>
    public BusChain MonitorChain => MixerBuses.MonitorChain(Song, _s.Settings.Plugins.MonitorFx);

    /// <summary>Mixer "MON" button / Mixer.MonitorFx: the monitoring chain window (live output only, never rendered).</summary>
    public void OpenMonitorFx() => OpenFxChain(MixerBuses.MonitorTrack(Song, _s.Settings.Plugins.MonitorFx));

    public bool MonitorUseGlobal => Song.Mixer.MonitorUseGlobal;

    /// <summary>"Use for all projects" in the monitor FX window: switches this song between the app-wide chain and its own (the window reopens on the other chain).</summary>
    public void SetMonitorUseGlobal(bool useGlobal)
    {
        if (Song.Mixer.MonitorUseGlobal == useGlobal) return;
        Windows.CloseFxWindows(MixerBuses.IsMonitor);
        DocumentEdits.Run(Doc, p => { p.Mixer.MonitorUseGlobal = useGlobal; return true; }, invalidatesTimeline: false);
        _s.SyncAudioEngine();
        RefreshWindow();
        _s.UpdateTitle();
        _s.SetStatus(useGlobal ? "Monitor FX: the app-wide chain is used for every project" : "Monitor FX: this project uses its own chain");
        OpenMonitorFx();
    }

    /// <summary>Group header power part: bypasses / enables the group's bus chain (one undo step).</summary>
    public void ToggleBus(string group)
    {
        var bus = default(BusChain)!;   // the bus entry is created inside the edit, so the undo state is taken before it exists (as before)
        DocumentEdits.Run(Doc, p => { bus = p.Mixer.Bus(group); MixerBuses.SetOn(bus, !bus.On); return true; }, invalidatesTimeline: false);
        _s.SyncAudioEngine();
        _s.RefreshTracks();
        _s.RefreshArrangement();
        Windows.Mixer?.SyncValues();
        _s.UpdateTitle();
        _s.SetStatus($"{group} bus effects {(bus.On ? "on" : "bypassed")}");
    }

    public string GroupColour(string group) => TrackColouring.ColourOf(group, _s.Settings.Appearance.GroupColours);

    /// <summary>Remembers a group's colour and colours its tracks (one undo step).</summary>
    public void SetGroupColour(string group, string hex)
    {
        _s.Settings.Appearance.GroupColours[group] = hex;
        _s.SaveSettings();
        if (DocumentEdits.Run(Doc, p => TrackColouring.Group(p, group, hex) > 0, invalidatesTimeline: false).Changed) TrackColoursChanged();
    }

    public void SetTrackColour(TrackModel track, string hex)
    {
        if (DocumentEdits.Run(Doc, _ => TrackColouring.SetColour(track, hex), invalidatesTimeline: false).Changed) TrackColoursChanged();
    }

    /// <summary>Gives a song the app-wide group rules it follows when it has none of its own.</summary>
    public void AttachAppRules(SongProject project) => project.Mixer.App = _s.Settings.MixerRules;

    public void SetAppGroupRules(GroupRulesResult result)
    {
        DocumentEdits.Run(Doc, p =>
        {
            MixerRules.ApplyAppWide(_s.Settings.MixerRules, p, result.Groups, result.Fallback, result.Renamed);
            return true;
        }, invalidatesTimeline: false);
        foreach (var doc in _s.Documents) AttachAppRules(doc.Project);
        _s.SaveSettings();
        MixerChanged(recompile: true);
        _s.RefreshTracks();
        _s.RefreshArrangement();
        _s.UpdateTitle();
    }

    /// <summary>
    /// Collapses or expands mixer groups (null: toggles each). View state: not an undo step and not an unsaved change; the song
    /// saves it with the rest. The track list and the mixer follow.
    /// </summary>
    public void SetGroupsCollapsed(IReadOnlyCollection<string> groups, bool? collapsed)
    {
        var list = Song.Mixer.CollapsedGroups;
        var changed = false;
        foreach (var group in groups)
        {
            var on = collapsed ?? !list.Contains(group);
            if (on && !list.Contains(group)) { list.Add(group); changed = true; }
            else if (!on && list.Remove(group)) changed = true;
        }
        if (!changed) return;
        _s.RefreshTracks();
        _s.RefreshArrangement();
        _s.ScheduleFitTimelineToTracks();
        _s.UpdateTitle();
        Windows.Mixer?.Rebuild();
    }

    /// <summary>Mixer.CollapseAllGroups / Mixer.ExpandAllGroups: every group of the open song.</summary>
    public void SetAllGroupsCollapsed(bool collapsed) =>
        SetGroupsCollapsed(TrackOrdering.Layout(Song).Select(l => l.Group).ToList(), collapsed);

    /// <summary>Mixer.GroupRules: opens the mixer and its group rules editor.</summary>
    public void OpenGroupRules()
    {
        OpenMixer();
        Windows.Mixer?.EditGroupRules();
    }

    private void TrackColoursChanged()
    {
        Song.IsDirty = true;
        _s.RefreshTracks();
        _s.RefreshArrangement();
        _s.Editor.InvalidateVisual();
        _s.UpdateTitle();
    }

    public bool TrackListShows(string what) => what switch
    {
        "groups" => Song.Mixer.ShowGroupsInTrackList,
        _ => !_s.Settings.Timeline.HiddenTrackColumns.Contains(what, StringComparer.OrdinalIgnoreCase),
    };

    public void SetTrackListShows(string what, bool on)
    {
        if (what == "groups")
        {
            DocumentEdits.Run(Doc, p => { p.Mixer.ShowGroupsInTrackList = on; return true; }, invalidatesTimeline: false);
            _s.RefreshTracks();
            _s.RefreshArrangement();
            _s.ScheduleFitTimelineToTracks(); // group headers add rows: grow / shrink the arrangement dock to fit
            _s.UpdateTitle();
            Windows.Mixer?.SyncValues();    // the mixer's "Groups in track list" box follows (one setting, two places)
            return;
        }
        var hidden = _s.Settings.Timeline.HiddenTrackColumns;
        hidden.RemoveAll(h => string.Equals(h, what, StringComparison.OrdinalIgnoreCase));
        if (!on) hidden.Add(what);
        _s.Arrangement.HiddenColumns = hidden;
        _s.SaveSettings();
    }

    /// <summary>Track list menu "Colours > Colour tracks by group" (the group colours are in Settings > Appearance &amp; colours).</summary>
    public void ColourTracksByGroup()
    {
        var changed = 0;
        DocumentEdits.Run(Doc, p => { changed = TrackColouring.ByGroup(p, _s.Settings.Appearance.GroupColours); return true; }, invalidatesTimeline: false);
        TrackColoursChanged();
        _s.SetStatus(changed == 0 ? "Tracks already have their group colours" : $"Coloured {changed} track{(changed == 1 ? "" : "s")} by group");
    }

    /// <summary>Track list menu "Colours > Colour tracks...".</summary>
    public void ColourTracksWithDialog()
    {
        var captured = false;
        TrackColoursDialog.Show(_s.Window, Song, () => { if (!captured) { _s.CheckpointUndo(); captured = true; } }, TrackColoursChanged);
    }

    // ---------- FX chain windows and IFxChainHost ----------

    /// <summary>FX button on a track row / mixer strip / hotkey: that track's chain window.</summary>
    public void OpenFxChain(TrackModel? track) => Windows.OpenFxChain(track);

    /// <summary>The FX power switch: plays the track through its chain, or back on Windows MIDI.</summary>
    public void ToggleTrackChain(TrackModel track)
    {
        DocumentEdits.Run(Doc, _ => { track.SoundSource = track.SoundSource == SoundSources.Plugins ? SoundSources.Midi : SoundSources.Plugins; return true; }, invalidatesTimeline: false);
        ChainChanged(track); // only switches: the chain window opens from the FX part
        _s.SetStatus(track.SoundSource == SoundSources.Plugins ? $"{track.Name}: plays through its FX chain" : $"{track.Name}: plays on Windows MIDI");
    }

    /// <summary>Track.Wiring: the FX window of the track, with the wiring window of its selected (else first) plug-in on top.</summary>
    public void OpenWiring(TrackModel? track)
    {
        if (track is null) return;
        OpenFxChain(track);
        Windows.FxWindowOf(track)?.OpenWiring();
    }

    /// <summary>Track.MidiProcessing: the FX window of the track, with the MIDI processing window of its selected (else first) plug-in on top.</summary>
    public void OpenMidiProcessing(TrackModel? track)
    {
        if (track is null) return;
        OpenFxChain(track);
        Windows.FxWindowOf(track)?.OpenMidiProcessing();
    }

    public IReadOnlyList<TrackModel> Tracks => Song.Tracks;
    public PluginSettings PluginSettings => _s.Settings.Plugins;

    public void BeginChainEdit() => BeginMixerEdit();

    public void ChainChanged(TrackModel track)
    {
        if (track.IsBus) MixerBuses.SyncBack(track);   // the FX window's power / plug-ins belong to the bus
        if (MixerBuses.IsMonitor(track) && Song.Mixer.MonitorUseGlobal) _s.SaveSettings();   // the app-wide monitor chain lives in the settings, not the song
        else Song.IsDirty = true;
        _s.SyncAudioEngine();
        _s.Midi.Rebuild(Song);
        _s.RefreshTracks();
        _s.RefreshArrangement();
        RefreshWindow();
        _s.UpdateTitle();
    }

    public void PluginSwitched(TrackModel track)
    {
        // An instrument that is switched can change what plays the track's notes (the plug-in or the GM sound): that is a full chain change.
        if (track.Rig.Plugins.Any(p => p.Type == PluginSlotType.Instrument)) { ChainChanged(track); return; }
        if (track.IsBus) MixerBuses.SyncBack(track);
        if (MixerBuses.IsMonitor(track) && Song.Mixer.MonitorUseGlobal) _s.SaveSettings();
        else Song.IsDirty = true;
        _s.SyncAudioEngine();   // the engine already has the switch; this keeps its track mix in step
        _s.RefreshTracks();
        RefreshWindow();
        _s.UpdateTitle();
    }

    public void OpenAudioSettings() => _s.OpenAudioSettings();
    public IntPtr OwnerWindow => new System.Windows.Interop.WindowInteropHelper(_s.Window).Handle;
    public bool DarkTheme => !Visualization.VisualTheme.IsLight;
    public AudioEngineClient Engine => _s.Engine;
    public void SaveSettings() => _s.SaveSettings();
}
