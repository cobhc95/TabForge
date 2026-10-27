using System;
using System.Collections.Generic;
using System.Diagnostics;
using TabForge.Audio;
using TabForge.Audio.Contracts;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Transport;
using TabForge.Documents;
using TabForge.LiveInput;
using TabForge.Models;
using TabForge.Playback;

namespace TabForge.Avalonia;

// Owns: the window's MIDI input monitor: playing the MIDI keyboard through the armed tracks and the selected track of the shown song. It tells the monitor when
//   something it reads changed (the shown tab, the selected track, an edit or undo such as an arm, the "Monitor MIDI input" setting, an engine sync that moved slots),
//   and gives it the engine's slots and note ring. A track with no engine slot (Windows MIDI) is played through the song's playback output on its channel.
// Does not own: the rules of who monitors (MidiInputMonitor), the setting (PluginSettings.MonitorMidiInput), recording.
// Tests: TabForge.Avalonia.Tests.MidiInputMonitorTests (the monitor and its rules, with a fake host).
// Threads: UI thread for the hooks; Write and WriteFallback run on the MIDI driver thread.
internal sealed class MainWindowMidiMonitor : IMidiMonitorHost, IDisposable
{
    private readonly MainWindowTabs _songs;
    private readonly AvaloniaPlaybackSessions _playback;
    private readonly MainWindowSettings _settings;
    private readonly AudioEngineClient _engine;
    private readonly DocumentWatch _watch = new();
    private SongDocument? _track;   // the song whose selected-track change is hooked
    private volatile IPlaybackEngine? _fallback;   // the shown song's playback output, read by the driver thread

    public MainWindowMidiMonitor(MainWindowTabs songs, AvaloniaPlaybackSessions playback, MainWindowSettings settings, AudioEngineClient engine)
    {
        (_songs, _playback, _settings, _engine) = (songs, playback, settings, engine);
        Monitor = new MidiInputMonitor(this, engine.MidiInput, AvaloniaUiThread.ForApplication());
        Monitor.NoteHeard += engine.RaiseMidiNoteHeard;   // the armed rows' meters wake on a note
        Activate();
        _watch.Marked = _ => Refresh();
        _watch.Restored = Refresh;
        songs.ActiveChanged += Rebind;
        engine.Synced += Refresh;
        settings.SettingsChanged += OnSettings;
        Rebind();
    }

    public MidiInputMonitor Monitor { get; }

    /// <summary>This window became the active one: it takes the engine's monitor slot, the window that had it is muted (its held notes let go), this one sounds.</summary>
    public void Activate()
    {
        if (_engine.MidiMonitor is { } previous && !ReferenceEquals(previous, Monitor)) previous.Muted = true;
        _engine.MidiMonitor = Monitor;
        Monitor.Muted = false;
    }

    public IReadOnlyList<TrackModel> Tracks => _songs.Songs.Count == 0 ? Array.Empty<TrackModel>() : _songs.Active.Project.Tracks;

    /// <summary>The selected track of the shown song, read from the document at each call (nothing is held, so an undo that replaces the song is seen at once).</summary>
    public TrackModel? Selected => ReadSelected();

    private TrackModel? ReadSelected()
    {
        if (_songs.Songs.Count == 0) return null;
        var song = _songs.Active;
        return song.TrackIndex >= 0 && song.TrackIndex < song.Project.Tracks.Count ? song.Project.Tracks[song.TrackIndex] : null;
    }

    public string Mode => _settings.Settings.Plugins.MonitorMidiInput;
    public bool StandInListening => _engine.LiveInputMidiOn;
    public int SlotOf(TrackModel track) => _engine.SlotOf(track);

    public void Write(int slot, byte status, byte data1, byte data2, bool panic) =>
        _engine.Write(new TimedMidi
        {
            Timestamp = Stopwatch.GetTimestamp() - _engine.LatencyTicks, Slot = slot, Status = status, Data1 = data1, Data2 = data2,
            Flags = panic ? TimedMidi.PanicFlag : (byte)0
        });

    public void WriteFallback(TrackModel track, byte status, byte data1, byte data2) =>
        _fallback?.SendLive(track.MidiOutputDeviceId, status, data1, data2);

    private void Rebind()
    {
        if (_track is not null) _track.TrackIndexChanged -= Refresh;
        if (_songs.Songs.Count == 0) { _track = null; _watch.Bind(null); }
        else
        {
            _track = _songs.Active;
            _track.TrackIndexChanged += Refresh;
            _watch.Bind(_track);
        }
        Refresh();
    }

    private void OnSettings(TabForge.Services.AppSettings _) => Refresh();

    private void Refresh()
    {
        _fallback = _songs.Songs.Count == 0 ? null : _playback.Existing(_songs.Active)?.Engine;
        Monitor.Refresh();
    }

    public void Dispose()
    {
        _songs.ActiveChanged -= Rebind;
        _engine.Synced -= Refresh;
        _settings.SettingsChanged -= OnSettings;
        if (_track is not null) _track.TrackIndexChanged -= Refresh;
        _watch.Dispose();
        if (ReferenceEquals(_engine.MidiMonitor, Monitor)) _engine.MidiMonitor = null;
        Monitor.Dispose();
    }
}
