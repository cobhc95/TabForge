using System.Diagnostics;
using TabForge.Audio.Contracts;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Playback;

namespace TabForge.Audio;

/// <summary>
/// Song time for audio clips: seconds from the song's start as it is performed (repeats included), worked out from
/// the playhead (bar + position) with a map of the whole song. Tells the audio engine where the song is only when
/// playback starts, stops, jumps (seek, loop) or drifts, so the engine can follow smoothly between updates.
/// </summary>
public sealed class SongClock
{
    private readonly AudioEngineClient _engine;
    /// <summary>The document this clock tells the engine about; the engine keeps one song transport per document. Null: the shared transport (owner 0).</summary>
    public object? OwnerKey { get; set; }
    private double _lastSongMs;
    private double _sentSongMs = double.NaN;
    private long _sentStamp;
    private bool _playing;
    // Set by Stopped, cleared by PrepareForPlayback: a playhead the engine thread reports after a stop must not restart the song.
    private volatile bool _halted;

    // The song map. The thread that owns the song (the one that created the clock: it edits the song) builds it from the live song, always current.
    // The playback thread never reads the live song: it reads the newest map built from a snapshot the owner took at the last timeline mark.
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private readonly SynchronizationContext? _ownerContext = SynchronizationContext.Current;
    private readonly ProjectStateEncoder _encoder = new();
    private readonly object _publishGate = new();
    private readonly Action<SongProject> _onMarked;
    private BarMap? _published;
    private SongProject? _watched;
    private SnapshotJob? _job;
    private SongProject? _requested;
    private SongProject? _snapshotProject;   // owner thread only: the song and revision of the newest snapshot
    private int _snapshotRevision = -1;
    private int _compiling, _requestPosted, _followMarks;

    public SongClock(AudioEngineClient engine)
    {
        _engine = engine;
        _onMarked = OnTimelineMarked;
    }

    /// <summary>The whole song's bars in performed order for one timeline revision of one song.</summary>
    private sealed record BarMap(SongProject Project, (int Revision, int Measures) Key, ScoreBar[] Bars);

    private sealed record SnapshotJob(SongProject Project, (int Revision, int Measures) Key, SongSnapshot Snapshot);

    private bool OnOwnerThread()
        => Environment.CurrentManagedThreadId == _ownerThread || (_ownerContext is not null && ReferenceEquals(SynchronizationContext.Current, _ownerContext));

    private static ScoreBar[] CompileBars(SongProject song)
        => MidiTimelineBuilder.Build(song, new PlaybackOptions { RepeatExpansion = true, RespectMuteSolo = false, SkipClips = true }).Bars.ToArray();

    private void Publish(BarMap map)
    {
        lock (_publishGate)
        {
            // A map from an older snapshot never replaces a newer one of the same song.
            if (_published is { } current && ReferenceEquals(current.Project, map.Project) && current.Key.Revision > map.Key.Revision) return;
            _published = map;
        }
    }

    /// <summary>
    /// The song's bars. On the owner thread: built from the live song when its timeline changed. On another thread: the newest map built
    /// from a snapshot (possibly one edit behind), or null while the owner has not provided the first one; the live song is not read.
    /// A clock created on a thread without a synchronization context has no owner thread to ask: every thread then builds from the live song.
    /// </summary>
    private ScoreBar[]? Bars(SongProject project)
    {
        if (_ownerContext is null || OnOwnerThread())
        {
            Watch(project);
            var key = TimelineKey(project);
            if (Volatile.Read(ref _published) is { } current && ReferenceEquals(current.Project, project) && current.Key == key) return current.Bars;
            var bars = CompileBars(project);
            Publish(new BarMap(project, key, bars));
            return bars;
        }
        var map = Volatile.Read(ref _published);
        if (map is not null && ReferenceEquals(map.Project, project))
        {
            if (map.Key.Revision != project.TimelineRevision) RequestSnapshot(project);   // a newer map follows; this one is complete, only older
            return map.Bars;
        }
        RequestSnapshot(project);
        return null;
    }

    /// <summary>A thread other than the owner reads the map from now on: every timeline mark of the watched song copies it, until <see cref="Stopped"/>.</summary>
    internal void FollowTimelineMarks() => Volatile.Write(ref _followMarks, 1);

    /// <summary>
    /// Owner thread, as playback starts: the playback thread will read the map from now on. When no map of the song's current timeline
    /// exists yet, the copy is taken now and built on a pool thread while the engine starts, so the first playhead finds it (or waits only for
    /// the remainder of that build) instead of first asking the owner thread through the dispatcher.
    /// </summary>
    public void PrepareForPlayback(SongProject project)
    {
        _halted = false;
        if (_ownerContext is null || !OnOwnerThread()) return;
        Watch(project);
        FollowTimelineMarks();
        if (Volatile.Read(ref _published) is { } current && ReferenceEquals(current.Project, project) && current.Key == TimelineKey(project)) return;
        TakeSnapshot(project);
    }

    /// <summary>The newest published map of <paramref name="project"/> (any thread; never reads the live song), with the revision it was built for.</summary>
    internal bool TryReadMap(SongProject project, out int revision, out ScoreBar[] bars)
    {
        if (Volatile.Read(ref _published) is { } map && ReferenceEquals(map.Project, project)) { revision = map.Key.Revision; bars = map.Bars; return true; }
        revision = -1; bars = Array.Empty<ScoreBar>();
        return false;
    }

    /// <summary>Owner thread: from now on a timeline mark of <paramref name="project"/> refreshes the snapshot map (while another thread reads the clock).</summary>
    private void Watch(SongProject project)
    {
        if (ReferenceEquals(_watched, project)) return;
        if (_watched is not null) _watched.TimelineMarked -= _onMarked;
        _watched = project;
        project.TimelineMarked += _onMarked;
    }

    private void OnTimelineMarked(SongProject project)
    {
        if (Volatile.Read(ref _followMarks) == 0) return;   // nobody but the owner reads the map: it rebuilds on demand
        TakeSnapshot(project);
    }

    /// <summary>Another thread needs a map of <paramref name="project"/>: asks the owner thread to snapshot it.</summary>
    private void RequestSnapshot(SongProject project)
    {
        FollowTimelineMarks();
        Volatile.Write(ref _requested, project);
        if (Interlocked.Exchange(ref _requestPosted, 1) == 0)
            _ownerContext!.Post(static state => ((SongClock)state!).OnSnapshotRequested(), this);
    }

    private void OnSnapshotRequested()
    {
        Volatile.Write(ref _requestPosted, 0);
        if (Volatile.Read(ref _requested) is not { } project) return;
        Watch(project);
        TakeSnapshot(project);
    }

    /// <summary>Owner thread: copies the live song and has the map built from the copy on a pool thread (coalesced: only the newest copy is built).</summary>
    private void TakeSnapshot(SongProject project)
    {
        if (ReferenceEquals(_snapshotProject, project) && _snapshotRevision == project.TimelineRevision) return;   // this revision is already copied
        SnapshotJob job;
        try { job = new SnapshotJob(project, TimelineKey(project), SongSnapshot.Take(project, _encoder)); }
        catch { return; }   // the previous map stays in use
        _snapshotProject = project;
        _snapshotRevision = job.Snapshot.Revision;
        Volatile.Write(ref _job, job);
        if (Interlocked.CompareExchange(ref _compiling, 1, 0) == 0) _ = Task.Run(BuildMapsFromSnapshots);
    }

    private void BuildMapsFromSnapshots()
    {
        while (true)
        {
            var job = Interlocked.Exchange(ref _job, null);
            if (job is null)
            {
                Volatile.Write(ref _compiling, 0);
                if (Volatile.Read(ref _job) is null || Interlocked.CompareExchange(ref _compiling, 1, 0) != 0) return;
                continue;
            }
            try { Publish(new BarMap(job.Project, job.Key, CompileBars(job.Snapshot.Song))); }
            catch { /* the previous map stays in use */ }
        }
    }

    /// <summary>Song milliseconds of a playhead; the occurrence of the bar at or after the last known time wins. Null while the map is not available yet.</summary>
    private double? SongMs(SongProject project, int bar, double fraction)
    {
        if (Bars(project) is not { } bars) return null;
        double? first = null;
        foreach (var scoreBar in bars)
        {
            if (scoreBar.Bar != bar) continue;
            var at = scoreBar.MsAtFraction(Math.Clamp(fraction, 0, 1));   // the playhead fraction is in slots; a fermata hold stretches time
            first ??= at;
            if (at >= _lastSongMs - 400) return at;
        }
        return first ?? _lastSongMs;
    }

    /// <summary>Song seconds of a source bar's start (first time it plays), for placing clips on the timeline.</summary>
    public double BarStartSec(SongProject project, int bar)
    {
        foreach (var b in Bars(project) ?? Array.Empty<ScoreBar>()) if (b.Bar == bar) return b.StartMs / 1000;
        return 0;
    }

    /// <summary>The source bar and position at a song time (first bar that covers it), for drawing clips.</summary>
    public (int Bar, double Fraction) BarAt(SongProject project, double songSec)
    {
        var bars = Bars(project) ?? Array.Empty<ScoreBar>();
        var ms = songSec * 1000;
        foreach (var b in bars)
            if (ms < b.EndMs) return (b.Bar, b.EndMs > b.StartMs ? b.SlotFraction(ms) : 0);
        return bars.Length == 0 ? (0, 0) : (bars[^1].Bar, 1);
    }

    /// <summary>Tells the engine this song's position. A document that has been released has no id any more and sends nothing.</summary>
    private void SendPosition(bool playing, double songSec, long stamp)
    {
        var id = OwnerKey is null ? 0 : _engine.ExistingOwnerId(OwnerKey);
        if (id < 0 || (id == 0 && !playing && OwnerKey is not null && _engine.SharesOwnerZero(OwnerKey))) return;   // a song sharing transport 0 never stops the one that owns it
        _engine.SetPosition(playing, songSec, stamp, id);
    }

    /// <summary>Playback thread: a new playhead.</summary>
    public void Report(SongProject project, PlaybackPosition position, bool playing)
    {
        if (_halted) return;
        var now = Stopwatch.GetTimestamp();
        if (SongMs(project, position.Bar, position.BarFraction) is not { } ms) return;   // the owner is still copying the song: the next playhead follows
        _lastSongMs = ms;
        var expected = _playing && !double.IsNaN(_sentSongMs) ? _sentSongMs + (now - _sentStamp) * 1000.0 / Stopwatch.Frequency : double.NaN;
        if (playing != _playing || double.IsNaN(expected) || Math.Abs(expected - ms) > 25)
        {
            // A new anchor (start, jump, loop wrap or drift): the engine follows it, and so does SecAt.
            SendPosition(playing, ms / 1000, now);
            Volatile.Write(ref _anchor, new Anchor(ms, now, playing));
            _sentSongMs = ms;
            _sentStamp = now;
        }
        _playing = playing;
    }

    private sealed record Anchor(double SongMs, long Stamp, bool Playing);
    private Anchor? _anchor;

    /// <summary>Song seconds at a <see cref="Stopwatch"/> timestamp (any thread), from the last anchor; NaN when stopped.</summary>
    public double SecAt(long stamp)
    {
        var a = Volatile.Read(ref _anchor);
        if (a is null || !a.Playing) return double.NaN;
        return (a.SongMs + (stamp - a.Stamp) * 1000.0 / Stopwatch.Frequency) / 1000;
    }

    /// <summary>Song seconds at the end of a source bar's first performance.</summary>
    public double BarEndSec(SongProject project, int bar)
    {
        var bars = Bars(project) ?? Array.Empty<ScoreBar>();
        foreach (var b in bars) if (b.Bar == bar) return b.EndMs / 1000;
        return bars.Length == 0 ? 0 : bars[^1].EndMs / 1000;
    }

    /// <summary>Owner thread: playback paused. The engine stops the song (its audio clips go quiet) until the next playhead resumes it.</summary>
    public void Paused()
    {
        Volatile.Write(ref _anchor, null);
        if (_playing) SendPosition(false, _lastSongMs / 1000, Stopwatch.GetTimestamp());
        _playing = false;
        _sentSongMs = double.NaN;
    }

    public void Stopped()
    {
        _halted = true;
        Volatile.Write(ref _followMarks, 0);   // no reader off the owner thread any more: marks stop copying the song
        Volatile.Write(ref _anchor, null);
        if (_playing) SendPosition(false, _lastSongMs / 1000, Stopwatch.GetTimestamp());
        _playing = false;
        _sentSongMs = double.NaN;
    }

    /// <summary>Song time now (for recording and the timeline).</summary>
    public double CurrentSec => _lastSongMs / 1000;

    private static readonly object TransportGate = new();
    // Weak in the project: this app-wide one-entry cache must not keep the last song alive after its tab and window are gone.
    private static (WeakReference<SongProject>? Project, (int, int) Key, TransportBar[] Bars) _transport = (null, (-1, -1), Array.Empty<TransportBar>());

    /// <summary>
    /// RT-04: the song's bar map for the engine's plug-in transport, on the same song-seconds clock as <see cref="Report"/> (the same
    /// performed timeline, repeats expanded): per bar its start, ppq, tempo and time signature. Rebuilt only when the song's timeline changes
    /// (<see cref="TimelineKey"/>, cached per project).
    /// </summary>
    public static TransportBar[] TransportBars(SongProject project)
    {
        var key = TimelineKey(project);
        lock (TransportGate)
            if (_transport.Project is { } cached && cached.TryGetTarget(out var cachedProject) && ReferenceEquals(cachedProject, project) && _transport.Key == key) return _transport.Bars;
        var timeline = MidiTimelineBuilder.Build(project, new PlaybackOptions { RepeatExpansion = true, RespectMuteSolo = false, SkipClips = true });
        var count = Math.Min(timeline.Bars.Count, TransportMap.MaxBars);
        var bars = new TransportBar[count];
        var ppq = 0.0;
        for (var i = 0; i < count; i++)
        {
            var bar = timeline.Bars[i];
            var measure = Services.MusicTime.BarOf(project, bar.Bar);
            var num = Math.Clamp(measure?.TimeSigNum ?? project.TimeSignatureNumerator, 1, 64);
            var den = measure?.TimeSigDenom ?? project.TimeSignatureDenominator;
            if (den is < 1 or > 64 || (den & (den - 1)) != 0) den = 4;
            var tempo = Math.Clamp(bar.Tempo > 0 ? bar.Tempo : project.Tempo, 1, 2000);
            bars[i] = new TransportBar(bar.StartMs / 1000, ppq, tempo, num, den);
            // Quarters actually performed (a Guitar Pro bar that ends early is shorter), so ppq stays continuous with song time.
            var endMs = i + 1 < count ? Math.Max(bar.StartMs, timeline.Bars[i + 1].StartMs) : bar.EndMs;
            ppq += Math.Max(0, endMs - bar.StartMs) / 60000.0 * tempo;
        }
        lock (TransportGate) _transport = (new WeakReference<SongProject>(project), key, bars);
        return bars;
    }

    /// <summary>
    /// A5-08: the key both timing caches use: the project's <see cref="SongProject.TimelineRevision"/> (bumped by every edit ending and by
    /// undo/redo) plus the bar count as a cheap guard for a path that forgot to bump. Compared exactly, so it cannot collide like a hash.
    /// </summary>
    internal static (int Revision, int Measures) TimelineKey(SongProject project)
        => (project.TimelineRevision, project.Tracks.Sum(t => t.Measures.Count));
}
