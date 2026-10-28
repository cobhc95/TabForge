using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using TabForge.Audio;
using TabForge.Documents;
using TabForge.Platform;
using TabForge.Playback;
using TabForge.Services;

namespace TabForge.Avalonia.Transport;

// Owns: the playback session of each open document in one window: created on first use, disposed when its tab closes; the one
//     window-wide state event the tab strip listens to (any session's play, pause or loop change); the engine the Sound menu's test note plays through;
//     and the engine sync that runs when a session is made, before it plays, and on live mixer changes (AvaloniaEngineRouting).
// Does not own: the documents (keyed by reference, never changed), the engines' creation (passed in), the transport rules (PlaybackTransport),
//     the master and plug-in settings (AvaloniaEngineRouting).
// Tests: TabForge.Avalonia.Tests.AvaloniaPlaybackSessionTests, TabForge.Avalonia.Tests.AvaloniaEngineRoutingTests,
//     TabForge.Avalonia.Tests.TransportRecordingTests.
// Threads: UI thread only.
public sealed class AvaloniaPlaybackSessions : IDisposable
{
    private readonly IUiThread _ui;
    private readonly Func<IPlaybackEngine>? _createEngine;
    private readonly AvaloniaEngineRouting _routing = new();
    private readonly Dictionary<SongDocument, AvaloniaPlaybackSession> _sessions = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<SongDocument, PlaybackEditWatch> _editWatches = new(ReferenceEqualityComparer.Instance);   // score edits re-schedule the playing song
    private IPlaybackEngine? _testNoteEngine;   // made on the first test note, disposed with the sessions
    private bool _metronomeOn;                  // the choice a session starts with; read on the UI thread only
    private bool _countInOn;                    // the count-in choice a session starts with; read on the UI thread only
    private TabForge.Services.AudioSettings? _clickAudio; // the click settings a session starts with (the shared settings object); UI thread only

    /// <param name="createEngine">Makes one engine per document; null when no engine is available, so every <see cref="For"/> is null.</param>
    public AvaloniaPlaybackSessions(IUiThread ui, Func<IPlaybackEngine>? createEngine)
    {
        _ui = ui;
        _createEngine = createEngine;
    }

    /// <summary>The playhead frame-rate cap (the Follow setting, read live each frame); 0 or 240 and up draws at the screen rate.</summary>
    public Func<int> FrameRateCap { get; set; } = () => 0;

    /// <summary>The window whose refresh drives the playhead frames; null (no window yet) falls back to the frame timer.</summary>
    public Func<global::Avalonia.Controls.TopLevel?> FrameWindow { get; set; } = () => null;

    /// <summary>Raised when any session's play, pause or loop state changes.</summary>
    public event Action? AnyStateChanged;
    /// <summary>The audio engine client Transport Record records through; null without an engine host.</summary>
    public TabForge.Audio.AudioEngineClient? Engine { get; init; }

    /// <summary>The plug-in trust state the engine client's load gate asks; the windows ask the same one (null: no engine, the windows own theirs).</summary>
    public TabForge.Plugins.PluginTrust? Trust { get; init; }

    /// <summary>The master percent the engines play at (0-100), as saved in the audio settings.</summary>
    public int MasterPercent => _routing.MasterPercent;

    /// <summary>Attaches the shared audio and plug-in settings: every engine sync reads the master percent and plug-ins from them.</summary>
    public void AttachSettings(TabForge.Services.AudioSettings audio, TabForge.Services.PluginSettings plugins) => _routing.Attach(audio, plugins);

    /// <summary>Brings the document's engine in line with its tracks (arm state, chains, levels, MIDI routes, transport); nothing without an engine.</summary>
    public void SyncEngine(SongDocument document, TabForge.Services.PluginSettings plugins)
    {
        if (Engine is null) return;
        AvaloniaEngineRouting.Apply(document.Project, Existing(document)?.Routing, Engine, plugins, _routing.MasterPercent, document, document.SkippedPlugins);
        Existing(document)?.ApplyLiveSolo(document.Project, Engine.LiveInput.Solo);   // the live solo (set by the next sync) gates the MIDI tracks as it does the engine's
    }

    /// <summary>
    /// A track was added, deleted or moved in <paramref name="document"/>: the engine follows when <paramref name="syncEngine"/> (a deleted track stops sounding,
    /// its slot frees; a track added at the end has nothing to stop and is synced by its first play or arm) and a playing song takes the new track list
    /// from the next bar, so the playing schedule never names a track that is gone or in another place.
    /// </summary>
    public void TracksChanged(SongDocument document, TabForge.Services.PluginSettings plugins, bool syncEngine, IReadOnlyCollection<Guid>? removed = null)
    {
        if (removed is { Count: > 0 }) Existing(document)?.SilenceTracks(removed);   // a deleted track falls silent at once, not at the bar line
        if (syncEngine) SyncEngine(document, plugins);
        var bars = Enumerable.Range(0, TabForge.Services.BarRangeEditor.MaxMeasures(document.Project)).ToArray();   // no bar moved: each plays on at its own place
        Existing(document)?.RefreshArrangementLive(document.Project, bars);
    }

    /// <summary>The document's session, created on first use (and its engine synced at once); null when no engine is available.</summary>
    public AvaloniaPlaybackSession? For(SongDocument document)
    {
        if (_createEngine is null) return null;
        // The engine keeps another owner's tracks live only while that owner reports playing:
        // without this, syncing a new tab parks the playing song's tracks and its sound stops.
        if (Engine is { } engine) engine.IsOwnerPlaying ??= owner => owner is SongDocument song && Existing(song) is { IsPlaying: true };
        if (!_sessions.TryGetValue(document, out var session))
        {
            session = new AvaloniaPlaybackSession(_createEngine(), _ui, () => document.Project, new AnimationFrameSource(_ui, () => FrameWindow(), () => FrameRateCap()));
            session.SetMetronome(_metronomeOn);
            session.SetCountIn(_countInOn);
            if (_clickAudio is not null) session.ApplyClickOptions(_clickAudio);
            // The Preferences "Playback speed" is the speed a session starts at (the speed menu changes it per song afterwards).
            if (_clickAudio is { Speed: var startSpeed } && double.IsFinite(startSpeed) && Math.Abs(startSpeed - 1) > 0.001) session.SetSpeed(Math.Clamp(startSpeed, 0.25, 2));
            session.PlayStarting += () => SyncEngine(document, _routing.Plugins);
            // The engine's song transport follows this document's playhead: its audio clips start, follow
            // jumps and stop with the song. Nothing without an engine; the document that owns the engine now is this session's song.
            session.ReportEnginePosition = (playing, sec, speed) =>
            {
                if (Engine is { } engine) engine.SetPosition(playing, sec, Stopwatch.GetTimestamp(), engine.OwnerIdOf(document), speed);
            };
            session.StateChanged += OnSessionStateChanged;
            _sessions[document] = session;
            _editWatches[document] = new PlaybackEditWatch(_ui, document, session);
            SyncEngine(document, _routing.Plugins);
        }
        return session;
    }

    /// <summary>The document's session when one exists; never creates one. Used for reads (the tab strip) that must not start an engine.</summary>
    public AvaloniaPlaybackSession? Existing(SongDocument document) =>
        _sessions.TryGetValue(document, out var session) ? session : null;

    /// <summary>
    /// Tells the engine's song transport where the recorded song is once, just before Transport Record starts, so the audio take
    /// anchors at that second. A song that is rolling (playing, not paused, its start compiled) keeps the engine RUNNING from there at
    /// its speed: the session's own reports only re-anchor on a play-state change or drift, so a held clock would leave the song's
    /// audio clips stopped through the recording. A song not rolling yet is held there until its first frame anchors it. Nothing without an engine.
    /// </summary>
    public void SetPosition(SongDocument document, double songSec)
    {
        if (Engine is not { } engine) return;
        var rolling = Existing(document) is { IsPlaying: true, IsPaused: false } session && !session.Transport.StartPending ? session : null;
        engine.SetPosition(rolling is not null, songSec, Stopwatch.GetTimestamp(), engine.OwnerIdOf(document), rolling?.Speed ?? 1.0);
    }

    /// <summary>Sets the metronome and count-in choices: every open session follows them, and sessions made later start with them.
    /// Unchanged choices push nothing, so a slider drag's change report (which never flips a toggle) costs no session walk.</summary>
    public void SetToggles(bool metronome, bool countIn)
    {
        if (_metronomeOn == metronome && _countInOn == countIn) return;
        _metronomeOn = metronome;
        _countInOn = countIn;
        foreach (var session in _sessions.Values)
        {
            session.SetMetronome(metronome);
            session.SetCountIn(countIn);
        }
    }

    /// <summary>Sets the click settings: every open session's engine takes them now, and sessions made later take them on creation.</summary>
    public void SetClickOptions(TabForge.Services.AudioSettings audio)
    {
        _clickAudio = audio;
        foreach (var session in _sessions.Values) session.ApplyClickOptions(audio);
    }

    /// <summary>Raised with the new master percent when it changes (the transport knobs and the mixer's Master slider follow it).</summary>
    public event Action<int>? MasterPercentChanged;

    /// <summary>Sets the master percent (saved in the audio settings) and pushes it to every open document's engine now.</summary>
    public void SetMasterPercent(int percent)
    {
        var before = _routing.MasterPercent;
        _routing.SetMasterPercent(percent);
        foreach (var (document, session) in _sessions)
        {
            session.ApplyMasterVolume(document.Project, _routing.MasterPercent);   // the one MIDI send: the master change leaves mute and solo as they are
            PushEngineLevels(document);
        }
        if (_routing.MasterPercent != before) MasterPercentChanged?.Invoke(_routing.MasterPercent);
    }

    /// <summary>Pushes the document's track levels, pan, sounds, mute and solo to its running playback now, without a route or chain sync: the MIDI channel state always, the audio engine's track levels when there is one.</summary>
    public void PushLiveMix(SongDocument document)
    {
        PushEngineLevels(document);
        Existing(document)?.ApplyMuteSolo(document.Project, Engine?.LiveInput.Solo ?? false);
    }

    private void PushEngineLevels(SongDocument document)
    {
        if (Engine is { } engine) AvaloniaEngineRouting.ApplyLevelsNow(document.Project, engine, _routing.MasterPercent);
    }

    /// <summary>The length of a test note and of a note audition that does not give one.</summary>
    internal const int TestNoteLengthMs = 350;

    /// <summary>Plays a short test note on a MIDI output through the test engine; completes at once when no engine is available.</summary>
    public Task PlayTestNoteAsync(int deviceId, int channel, int program, int note, int lengthMs = TestNoteLengthMs)
    {
        if (_createEngine is null) return Task.CompletedTask;
        _testNoteEngine ??= _createEngine();
        return _testNoteEngine.PreviewNoteAsync(deviceId, channel, program, note, lengthMs);
    }

    /// <summary>Panic: note-offs, sustain off and all-notes-off on every session's output (also while stopped); each runs on its engine's output thread.</summary>
    public void Panic()
    {
        foreach (var session in _sessions.Values)
            if (session.Engine is PlaybackEngine engine) engine.PanicAsync();
        (_testNoteEngine as PlaybackEngine)?.PanicAsync();
    }

    /// <summary>Raised with a session just before its tab's close stops and disposes it (a recording of that song ends first).</summary>
    public event Action<AvaloniaPlaybackSession>? SessionClosing;

    /// <summary>Stops and disposes the document's session when its tab closes; nothing happens for a document without one.</summary>
    public void Close(SongDocument document)
    {
        if (!_sessions.Remove(document, out var session)) return;
        if (_editWatches.Remove(document, out var watch)) watch.Dispose();
        SessionClosing?.Invoke(session);
        session.StateChanged -= OnSessionStateChanged;
        session.Dispose();
    }

    public void Dispose()
    {
        foreach (var watch in _editWatches.Values) watch.Dispose();
        _editWatches.Clear();
        foreach (var session in _sessions.Values)
        {
            session.StateChanged -= OnSessionStateChanged;
            session.Dispose();
        }
        _sessions.Clear();
        _testNoteEngine?.Dispose();
        _testNoteEngine = null;
    }

    private void OnSessionStateChanged() => AnyStateChanged?.Invoke();
}
