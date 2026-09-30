using System.Diagnostics;
using TabForge.Audio.Contracts;
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
    private ScoreBar[] _bars = Array.Empty<ScoreBar>();
    private object? _mappedProject;
    private (int Revision, int Measures) _mappedVersion = (-1, -1);
    private double _lastSongMs;
    private double _sentSongMs = double.NaN;
    private long _sentStamp;
    private bool _playing;

    public SongClock(AudioEngineClient engine) => _engine = engine;

    /// <summary>The whole song's bars in performed order (built once per song change; cheap to keep).</summary>
    private void Map(SongProject project)
    {
        var version = TimelineKey(project);
        if (ReferenceEquals(project, _mappedProject) && version == _mappedVersion) return;
        var timeline = MidiTimelineBuilder.Build(project, new PlaybackOptions { RepeatExpansion = true, RespectMuteSolo = false, SkipClips = true });
        _bars = timeline.Bars.ToArray();
        _mappedProject = project;
        _mappedVersion = version;
    }

    /// <summary>Song milliseconds of a playhead; the occurrence of the bar at or after the last known time wins.</summary>
    public double SongMs(SongProject project, int bar, double fraction)
    {
        Map(project);
        double? first = null;
        foreach (var scoreBar in _bars)
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
        Map(project);
        foreach (var b in _bars) if (b.Bar == bar) return b.StartMs / 1000;
        return 0;
    }

    /// <summary>The source bar and position at a song time (first bar that covers it), for drawing clips.</summary>
    public (int Bar, double Fraction) BarAt(SongProject project, double songSec)
    {
        Map(project);
        var ms = songSec * 1000;
        foreach (var b in _bars)
            if (ms < b.EndMs) return (b.Bar, b.EndMs > b.StartMs ? b.SlotFraction(ms) : 0);
        return _bars.Length == 0 ? (0, 0) : (_bars[^1].Bar, 1);
    }

    /// <summary>Playback thread: a new playhead.</summary>
    public void Report(SongProject project, PlaybackPosition position, bool playing)
    {
        var now = Stopwatch.GetTimestamp();
        var ms = SongMs(project, position.Bar, position.BarFraction);
        _lastSongMs = ms;
        var expected = _playing && !double.IsNaN(_sentSongMs) ? _sentSongMs + (now - _sentStamp) * 1000.0 / Stopwatch.Frequency : double.NaN;
        if (playing != _playing || double.IsNaN(expected) || Math.Abs(expected - ms) > 25)
        {
            // A new anchor (start, jump, loop wrap or drift): the engine follows it, and so does SecAt.
            if (_engine.IsRunning) _engine.SetPosition(playing, ms / 1000, now);
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
        Map(project);
        foreach (var b in _bars) if (b.Bar == bar) return b.EndMs / 1000;
        return _bars.Length == 0 ? 0 : _bars[^1].EndMs / 1000;
    }

    public void Stopped()
    {
        Volatile.Write(ref _anchor, null);
        if (_playing) _engine.SetPosition(false, _lastSongMs / 1000, Stopwatch.GetTimestamp());
        _playing = false;
        _sentSongMs = double.NaN;
    }

    /// <summary>Song time now (for recording and the timeline).</summary>
    public double CurrentSec => _lastSongMs / 1000;

    private static readonly object TransportGate = new();
    private static (object? Project, (int, int) Key, TransportBar[] Bars) _transport = (null, (-1, -1), Array.Empty<TransportBar>());

    /// <summary>
    /// RT-04: the song's bar map for the engine's plug-in transport, on the same song-seconds clock as <see cref="Report"/> (the same
    /// performed timeline, repeats expanded): per bar its start, ppq, tempo and time signature. Rebuilt only when the song's timeline changes
    /// (<see cref="TimelineKey"/>, cached per project).
    /// </summary>
    public static TransportBar[] TransportBars(SongProject project)
    {
        var key = TimelineKey(project);
        lock (TransportGate)
            if (ReferenceEquals(_transport.Project, project) && _transport.Key == key) return _transport.Bars;
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
        lock (TransportGate) _transport = (project, key, bars);
        return bars;
    }

    /// <summary>
    /// A5-08: the key both timing caches use: the project's <see cref="SongProject.TimelineRevision"/> (bumped by every edit ending and by
    /// undo/redo) plus the bar count as a cheap guard for a path that forgot to bump. Compared exactly, so it cannot collide like a hash.
    /// </summary>
    internal static (int Revision, int Measures) TimelineKey(SongProject project)
        => (project.TimelineRevision, project.Tracks.Sum(t => t.Measures.Count));
}
