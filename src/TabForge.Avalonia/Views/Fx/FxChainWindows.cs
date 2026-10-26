using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using TabForge.Avalonia.Transport;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Services;

namespace TabForge.Avalonia.Views.Fx;

// Owns: the open FX chain windows of one main window: one window per track (a second open brings the first forward), the stand-ins of the master, the group
//   buses and the monitor, closing the window of a track that left the song, and refreshing a window when its track changed elsewhere (the FX power switch, an undo).
// Does not own: the windows (FxChainWindow), the host a window edits through (SongFxChainHost), the entry points (the FX cell, the key, the mixer, the track properties).
// Tests: TabForge.Avalonia.Tests.FxChainWindowsTests.
// Threads: UI thread only. Key: the document and the track's id (a bus stand-in: its engine slot), never the TrackModel object, which an undo replaces; ceiling: one window per track of the shown songs.
public sealed class FxChainWindows
{
    /// <summary>The registry of a main window; the windows it opens (the mixer, the track properties) find it by walking up their owners.</summary>
    public static readonly AttachedProperty<FxChainWindows?> RegistryProperty = AvaloniaProperty.RegisterAttached<FxChainWindows, Window, FxChainWindows?>("Registry");

    private readonly Window _owner;
    private readonly Func<SongDocument> _active;
    private readonly AvaloniaPlaybackSessions? _playback;
    private readonly MainWindowSettings? _settings;
    private readonly PluginTrust _trust;
    private readonly Dictionary<(SongDocument Document, string Track), FxChainWindow> _open = new();
    private readonly Dictionary<SongDocument, EventHandler> _pruning = new();   // per song: floating editors of removed plug-ins and tracks close even with no chain window open

    /// <param name="owner">The main window the chain windows open over.</param>
    /// <param name="active">The song that is shown now; a chain window edits the song it was opened on.</param>
    /// <param name="playback">The playback sessions the engine sync goes through; null: no engine.</param>
    /// <param name="settings">The shared settings; null uses defaults that are not saved.</param>
    /// <param name="trust">The app's one plug-in trust state (required; a test passes its own).</param>
    public FxChainWindows(Window owner, Func<SongDocument> active, AvaloniaPlaybackSessions? playback, MainWindowSettings? settings, PluginTrust trust)
    {
        (_owner, _active, _playback, _settings) = (owner, active, playback, settings);
        _trust = trust ?? throw new ArgumentNullException(nameof(trust));
        owner.SetValue(RegistryProperty, this);
    }

    /// <summary>The tool windows beside the chain windows (the live input's chain window), brought forward the same way.</summary>
    public ToolWindows Tools { get; } = new();

    /// <summary>Shows the live input's chain window made by <paramref name="create"/> over <paramref name="over"/>, or brings the open one forward.</summary>
    public FxChainWindow OpenLive(Func<FxChainWindow> create, Window over) => Tools.OpenOrActivate("Live FX", "", create, over, window => window.ShowOver(over));

    /// <summary>The app's one plug-in trust state, for the hosts this registry makes and for windows that open beside it.</summary>
    public PluginTrust Trust => _trust;

    /// <summary>Opens the live input's FX chain window (set by the live input; the mixer's live input row uses it); null before live input is set up.</summary>
    public Action? OpenLiveChain { get; set; }

    /// <summary>The track list repaints its FX cells after a chain edit (set by the arrangement hook).</summary>
    public Action? TrackListChanged { get; set; }

    /// <summary>The registry of the window or of the nearest owner of it that has one; null when there is none.</summary>
    public static FxChainWindows? For(Window? window)
    {
        for (var w = window; w is not null; w = w.Owner as Window)
            if (w.GetValue(RegistryProperty) is { } registry) return registry;
        return null;
    }

    /// <summary>The open chain window of <paramref name="track"/>, or null.</summary>
    public FxChainWindow? WindowOf(TrackModel track) => _open.TryGetValue((_active(), KeyOf(track)), out var window) ? window : null;

    /// <summary>Opens the chain window of a track of the shown song (or brings the open one forward); <paramref name="over"/> is the window it opens over (the main window by default).</summary>
    public FxChainWindow Open(TrackModel track, Window? over = null) => Open(_active(), track, over);

    private FxChainWindow Open(SongDocument document, TrackModel track, Window? over)
    {
        var key = (document, KeyOf(track));
        if (_open.TryGetValue(key, out var existing) && existing.IsVisible) { Tools.Bring(existing); return existing; }
        var host = new SongFxChainHost(document, _playback, _settings, () => _owner, () => TrackListChanged?.Invoke(), _trust);
        var window = new FxChainWindow(host, track);
        _open[key] = window;
        Watch(document);
        EventHandler changed = (_, _) => Follow(document, key.Item2, window);
        document.DisplayStateChanged += changed;
        window.Closed += (_, _) =>
        {
            document.DisplayStateChanged -= changed;
            if (_open.TryGetValue(key, out var current) && ReferenceEquals(current, window)) _open.Remove(key);
        };
        window.ShowOver(over ?? _owner);
        return window;
    }

    /// <summary>The window of a track that is no longer in the song closes; one whose track an undo or redo replaced reopens on the song's current track; the others read their track again.</summary>
    private void Follow(SongDocument document, string key, FxChainWindow window)
    {
        var current = Resolve(document.Project, key);
        if (current is null) window.CloseReplaced();
        else if (ReferenceEquals(current, window.Track)) window.RefreshFromTrack();
        else
        {
            var over = window.Owner as Window;
            window.CloseReplaced();
            Open(document, current, over);
        }
    }

    /// <summary>The identity of a chain window's track: a song track by its id, a bus stand-in by its engine slot (the stand-in object is rebuilt with each song).</summary>
    private static string KeyOf(TrackModel track) => track.Bus is null ? track.Id.ToString("N") : $"bus{track.BusSlot}";

    private TrackModel? Resolve(SongProject project, string key)
    {
        if (!key.StartsWith("bus", StringComparison.Ordinal))
            return project.Tracks.FirstOrDefault(t => t.Id.ToString("N") == key);
        var slot = int.Parse(key.AsSpan(3), System.Globalization.CultureInfo.InvariantCulture);
        if (slot == MixerBuses.MasterSlot) return MixerBuses.MasterTrack(project);
        if (slot == MixerBuses.MonitorSlot) return MixerBuses.MonitorTrack(project, GlobalMonitorChain);
        return project.Mixer.Buses.Keys.FirstOrDefault(group => MixerBuses.SlotOf(group) == slot) is { } name ? MixerBuses.BusTrack(project, name) : null;
    }

    /// <summary>Reads the open window of the track again (its power switch or chain changed elsewhere).</summary>
    public void Refresh(TrackModel track) => WindowOf(track)?.RefreshFromTrack();

    /// <summary>Track.Wiring: the chain window of the track with the wiring window of its focused (else first) plug-in on top.</summary>
    public void OpenWiring(TrackModel track) => Open(track).OpenWiring();

    /// <summary>Track.MidiProcessing: the chain window of the track with the MIDI processing window of its focused (else first) plug-in on top.</summary>
    public void OpenMidiProcessing(TrackModel track) => Open(track).OpenMidiProcessing();

    /// <summary>Mixer.MasterFx.</summary>
    public FxChainWindow OpenMaster() => Open(MixerBuses.MasterTrack(_active().Project));

    /// <summary>Mixer.GroupFx: the effects bus of a group.</summary>
    public FxChainWindow OpenGroup(string group) => Open(MixerBuses.BusTrack(_active().Project, group));

    /// <summary>The plug-in settings the windows work with.</summary>
    public PluginSettings Plugins => _settings?.Settings.Plugins ?? _fallback;

    /// <summary>A new track without plug-ins takes the default chain saved for its instrument (the "Auto-load for this instrument" option).</summary>
    public void ApplyDefaultChain(TrackModel track) => AutoChains.Apply(Plugins, track);

    /// <summary>The app-wide monitoring chain kept in the settings.</summary>
    public BusChain GlobalMonitorChain => (_settings?.Settings.Plugins ?? _fallback).MonitorFx;

    private readonly PluginSettings _fallback = new();

    /// <summary>Mixer.MonitorFx: the monitoring chain (the app-wide one, or the song's own).</summary>
    public FxChainWindow OpenMonitor() => Open(MixerBuses.MonitorTrack(_active().Project, GlobalMonitorChain));

    /// <summary>Closes the windows of the tracks <paramref name="close"/> picks.</summary>
    public void CloseWhere(Func<TrackModel, bool> close)
    {
        foreach (var window in _open.Values.ToList()) if (close(window.Track)) window.Close();
    }

    /// <summary>A song's tab closes: the chain windows of its tracks and buses close with it (the monitor chain stays, it is app-wide).</summary>
    public void CloseSong(SongDocument song)
    {
        foreach (var (key, window) in _open.ToList()) if (ReferenceEquals(key.Document, song)) window.Close();
        FloatingEditors.CloseSong(song);
        if (_pruning.Remove(song, out var prune)) song.DisplayStateChanged -= prune;
    }

    private void Watch(SongDocument document)
    {
        if (_pruning.ContainsKey(document)) return;
        EventHandler prune = (_, _) => FloatingEditors.Prune(document);
        _pruning[document] = prune;
        document.DisplayStateChanged += prune;
    }

    /// <summary>The shown song changed: the other songs' floating plug-in windows hide, the shown song's open again. Call again once the engine took the song (a refused open retries).</summary>
    public void SongShown(IEnumerable<SongDocument> held)
    {
        var handle = _owner.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        FloatingEditors.SongShown(held, _active(), handle, _owner.ActualThemeVariant != global::Avalonia.Styling.ThemeVariant.Light);
    }

    /// <summary>The monitor switched between the app-wide chain and the song's own: its window closes and reopens on the other chain.</summary>
    public void ReopenMonitor()
    {
        var wasOpen = _open.Values.Any(w => MixerBuses.IsMonitor(w.Track) && w.IsVisible);
        CloseWhere(MixerBuses.IsMonitor);
        if (wasOpen) OpenMonitor();
    }

    /// <summary>The monitor chain in effect for the shown song: whether it is on, its plug-in count and whether it is the app-wide one (the title bar's Monitor FX button).</summary>
    public (bool On, int Plugins, bool Global) MonitorState()
    {
        var project = _active().Project;
        var chain = project.Mixer.MonitorUseGlobal ? GlobalMonitorChain : project.Mixer.MonitorFx;
        return (chain?.On ?? false, chain?.Rig.Plugins.Count ?? 0, project.Mixer.MonitorUseGlobal);
    }

    private SongFxChainHost MonitorHost() => new(_active(), _playback, _settings, () => _owner, () => TrackListChanged?.Invoke(), _trust);

    /// <summary>Switches the monitor chain on or off as a chain edit (the app-wide chain saves to the settings, the song's own is an undo step).</summary>
    public void SetMonitorOn(bool on)
    {
        var track = MixerBuses.MonitorTrack(_active().Project, GlobalMonitorChain);
        MonitorHost().Edit(track, () => track.SoundSource = on ? SoundSources.Plugins : SoundSources.Midi);
        Refresh(track);
    }

    /// <summary>Switches the shown song between the app-wide monitor chain and its own.</summary>
    public void SetMonitorUseGlobal(bool useGlobal) => MonitorHost().SetMonitorUseGlobal(useGlobal);

    /// <summary>Opens the chain window of <paramref name="track"/> over <paramref name="owner"/>: through the registry of its main window (the one that holds the app's trust state).</summary>
    public static FxChainWindow OpenFor(Window owner, TrackModel track)
    {
        var registry = For(owner) ?? throw new InvalidOperationException("No FxChainWindows registry on this window or its owners; the app's trust state lives there.");
        return registry.Open(track, owner);
    }
}
