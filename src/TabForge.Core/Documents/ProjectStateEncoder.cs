using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Documents;

// Owns: capturing a song as a ProjectState and restoring one into a song, reusing every unchanged chunk of the previous state.
// Does not own: the undo stack (UndoHistory) and the bar bytes (BarCodec, BarWriter, BarReader).
// Tests: TestUndoDeltaStates.
// Long method: EncodeLocked is about 75 lines because its bar loop is one local function (EncodeBars) that reads the previous state
//   and writes only its own slots; the chunk reuse rules stay beside that loop instead of being passed through fields.
/// <summary>
/// Builds <see cref="ProjectState"/>s for one document and restores them. UI thread only: it remembers the last state (and the live
/// bars it was taken from) so the next capture compares bars against it and reuses every unchanged chunk.
/// </summary>
internal sealed class ProjectStateEncoder
{
    private readonly BarWriter _writer = new();
    private ProjectState? _last;
    // The bars of the song _last was taken from (or restored into), per chunk slot; weak on the song so a replaced song is not kept alive.
    private readonly ConditionalWeakTable<SongProject, MeasureModel?[][]> _lastBars = new();
    private Dictionary<ulong, StateChunk>? _index;
    private readonly object _indexGate = new();
    /// <summary>Undo states: songs with at least this many bars (all tracks) encode their tracks in parallel; below it the pool hand-off costs more than it saves.</summary>
    public const int DefaultParallelEncodeBars = 1500;   // undo runs on the UI thread per gesture: a mid-size song is faster serial than fanned out (128 was not measured faster)
    /// <summary>Playback copies (a seek or a start waits on them on the UI thread): every song with this many bars encodes its tracks in parallel.</summary>
    public const int PlaybackParallelEncodeBars = 64;
    private readonly int _parallelEncodeBars;
    /// <summary>A parallel encode hands out this many bar ranges per processor, so one long track does not leave the other workers idle.</summary>
    private const int RangesPerProcessor = 4;

    public ProjectStateEncoder(int parallelEncodeBars = DefaultParallelEncodeBars) => _parallelEncodeBars = Math.Max(1, parallelEncodeBars);
    // Only guards the caches above if a caller ever runs off the UI thread; reading a song while it is edited is still the caller's problem.
    private readonly object _gate = new();

    public ProjectState Encode(SongProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        lock (_gate) return EncodeLocked(project);
    }

    /// <param name="validate">False: the autosave writer, which must rescue a model that fails validation as a raw copy.</param>
    public SongProject Restore(ProjectState state, SongProject? live, bool validate = true)
    {
        ArgumentNullException.ThrowIfNull(state);
        lock (_gate) return RestoreLocked(state, live, validate);
    }

    private ProjectState EncodeLocked(SongProject project)
    {
        var previous = _last;
        long fresh = 0;
        var song = Reuse(ProjectService.UndoSongHeader(project), previous?.Song, ref fresh);
        var sourceTracks = project.Tracks ?? new List<TrackModel>();
        var trackCount = sourceTracks.Count;
        var bars = new StateChunk[trackCount][];
        var numbers = new int[trackCount][];
        var liveBars = new MeasureModel?[trackCount][];
        var totalBars = 0;
        for (var trackIndex = 0; trackIndex < trackCount; trackIndex++)
        {
            var count = sourceTracks[trackIndex]?.Measures?.Count ?? 0;
            bars[trackIndex] = new StateChunk[count];
            numbers[trackIndex] = new int[count];
            liveBars[trackIndex] = new MeasureModel?[count];
            totalBars += count;
        }

        // Encodes bars [from, to) of one track. It reads only the live song and the previous state and writes only its own slots, so ranges
        // can encode side by side (the calling thread waits, so the song cannot change meanwhile). Returns the bytes not shared with the previous state.
        long EncodeBars(int trackIndex, int from, int to, BarWriter writer)
        {
            long rangeFresh = 0;
            var measures = sourceTracks[trackIndex]!.Measures!;
            var prior = previous is not null && trackIndex < previous.Tracks.Length ? previous.Tracks[trackIndex] : null;
            for (var bar = from; bar < to; bar++)
            {
                var measure = measures[bar];
                liveBars[trackIndex][bar] = measure;
                numbers[trackIndex][bar] = measure?.Number ?? 0;
                writer.Reset();
                BarCodec.Write(writer, measure);
                var bytes = writer.Written;
                var slot = prior is not null && bar < prior.Bars.Length ? prior.Bars[bar] : null;
                StateChunk chunk;
                if (slot is not null && slot.Bytes.AsSpan().SequenceEqual(bytes)) chunk = slot;
                else if (FindInPrevious(bytes) is { } moved) chunk = moved;
                else
                {
                    chunk = new StateChunk(bytes.ToArray());
                    rangeFresh += chunk.Bytes.Length;
                }
                bars[trackIndex][bar] = chunk;
            }
            return rangeFresh;
        }

        if (trackCount > 0 && totalBars >= _parallelEncodeBars && Environment.ProcessorCount > 1)
        {
            var ranges = BarRanges(bars, Math.Max(1, totalBars / (Environment.ProcessorCount * RangesPerProcessor)));
            var freshByRange = new long[ranges.Count];
            try { Parallel.For(0, ranges.Count, i => freshByRange[i] = EncodeBars(ranges[i].Track, ranges[i].From, ranges[i].To, new BarWriter())); }
            catch (AggregateException ex) when (ex.InnerExceptions.Count > 0) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerExceptions[0]).Throw(); }
            fresh += freshByRange.Sum();
        }
        else
            for (var trackIndex = 0; trackIndex < trackCount; trackIndex++) fresh += EncodeBars(trackIndex, 0, bars[trackIndex].Length, _writer);

        var tracks = new TrackState[trackCount];
        for (var trackIndex = 0; trackIndex < trackCount; trackIndex++)
        {
            var prior = previous is not null && trackIndex < previous.Tracks.Length ? previous.Tracks[trackIndex] : null;
            var header = Reuse(ProjectService.UndoTrackHeader(sourceTracks[trackIndex]), prior?.Header, ref fresh);
            var same = prior is not null && ReferenceEquals(header, prior.Header) && prior.Bars.Length == bars[trackIndex].Length;
            for (var bar = 0; same && bar < bars[trackIndex].Length; bar++)
                same = ReferenceEquals(bars[trackIndex][bar], prior!.Bars[bar]) && numbers[trackIndex][bar] == prior.Numbers[bar];
            tracks[trackIndex] = same ? prior! : new TrackState(header, bars[trackIndex], numbers[trackIndex]);
        }

        // The first state of a document is its baseline (the song itself is already in memory): only later changes are charged.
        var state = new ProjectState(song, tracks, previous is null ? 0 : fresh);
        Remember(state, project, liveBars);
        return state;
    }

    /// <summary>Splits every track's bars into ranges of at most <paramref name="size"/> bars, the units a parallel encode hands out.</summary>
    private static List<(int Track, int From, int To)> BarRanges(StateChunk[][] bars, int size)
    {
        var ranges = new List<(int, int, int)>();
        for (var trackIndex = 0; trackIndex < bars.Length; trackIndex++)
            for (var from = 0; from < bars[trackIndex].Length; from += size)
                ranges.Add((trackIndex, from, Math.Min(bars[trackIndex].Length, from + size)));
        return ranges;
    }

    /// <summary>
    /// Rebuilds a song from <paramref name="state"/>. Bars of <paramref name="live"/> (the song being replaced, last encoded by
    /// this encoder) whose content is unchanged are moved over after re-checking their bytes; only the other bars are decoded.
    /// </summary>
    private SongProject RestoreLocked(ProjectState state, SongProject? live, bool validate)
    {
        var reusable = live is not null && _lastBars.TryGetValue(live, out var lastBars) ? ReusableBars(lastBars) : null;
        var used = new HashSet<MeasureModel>(ReferenceEqualityComparer.Instance);
        var renumbered = new List<(MeasureModel Measure, int Number)>();
        var project = ProjectService.RestoreUndoSongHeader(state.Song.Bytes);
        project.BandLayout = live?.BandLayout;   // view state: an undo never rewinds it
        project.Mixer.CollapsedGroups = new List<string>(live?.Mixer.CollapsedGroups ?? new List<string>());   // likewise the collapsed groups
        var liveBars = new MeasureModel?[state.Tracks.Length][];
        try
        {
            foreach (var trackState in state.Tracks)
            {
                var track = ProjectService.RestoreUndoTrackHeader(trackState.Header.Bytes);
                var bars = new MeasureModel?[trackState.Bars.Length];
                for (var bar = 0; bar < bars.Length; bar++)
                {
                    var chunk = trackState.Bars[bar];
                    var measure = reusable is not null && reusable.TryGetValue(chunk, out var candidates)
                        ? TakeUnchanged(candidates, chunk, used, renumbered)
                        : null;
                    measure ??= BarCodec.Read(chunk.Bytes);
                    if (measure is not null) measure.Number = trackState.Numbers[bar];
                    bars[bar] = measure;
                }
                var measures = new List<MeasureModel>(bars.Length);
                foreach (var measure in bars) measures.Add(measure!);
                track.Measures = measures;
                project.Tracks.Add(track);
                liveBars[project.Tracks.Count - 1] = bars;
            }
            KeepLiveArmState(project, live);
            if (validate) ProjectValidator.Validate(project);
        }
        catch
        {
            // The song being replaced stays as it was: give its moved-over bars their own numbers back.
            foreach (var (measure, number) in renumbered) measure.Number = number;
            throw;
        }
        Remember(state, project, liveBars);
        return project;
    }

    /// <summary>
    /// Undo states omit the monitoring fields (audio input, input monitoring): the restored tracks take the live song's values by track id.
    /// Record arm is restored from the state itself (it is undoable, one step per toggle); only a track that did not exist in the live song
    /// keeps the state's default, as a track added by redo does.
    /// </summary>
    private static void KeepLiveArmState(SongProject project, SongProject? live)
    {
        if (live is null || live.Tracks.Count == 0) return;
        var liveTracks = live.Tracks;
        var byId = new Dictionary<Guid, TrackModel>(liveTracks.Count);
        foreach (var track in liveTracks) if (track is not null) byId[track.Id] = track;
        foreach (var track in project.Tracks)
        {
            if (track is null || !byId.TryGetValue(track.Id, out var liveTrack)) continue;
            track.AudioInput = liveTrack.AudioInput;
            track.MonitorInput = liveTrack.MonitorInput;
        }
    }

    /// <summary>A live bar recorded with <paramref name="chunk"/> that still encodes to exactly its bytes (each bar is moved once).</summary>
    private MeasureModel? TakeUnchanged(List<MeasureModel> candidates, StateChunk chunk, HashSet<MeasureModel> used,
        List<(MeasureModel Measure, int Number)> renumbered)
    {
        while (candidates.Count > 0)
        {
            var candidate = candidates[^1];
            candidates.RemoveAt(candidates.Count - 1);
            if (!used.Add(candidate)) continue;
            _writer.Reset();
            BarCodec.Write(_writer, candidate);
            if (!_writer.Written.SequenceEqual(chunk.Bytes)) continue;
            renumbered.Add((candidate, candidate.Number));
            return candidate;
        }
        return null;
    }

    private void Remember(ProjectState state, SongProject project, MeasureModel?[][] liveBars)
    {
        if (!ReferenceEquals(state, _last)) _index = null;
        _last = state;
        _lastBars.Clear();
        _lastBars.AddOrUpdate(project, liveBars);
    }

    private Dictionary<StateChunk, List<MeasureModel>>? ReusableBars(MeasureModel?[][] lastBars)
    {
        if (_last is null) return null;
        var map = new Dictionary<StateChunk, List<MeasureModel>>(ReferenceEqualityComparer.Instance);
        for (var trackIndex = 0; trackIndex < _last.Tracks.Length && trackIndex < lastBars.Length; trackIndex++)
        {
            var chunks = _last.Tracks[trackIndex].Bars;
            var bars = lastBars[trackIndex];
            for (var bar = 0; bar < chunks.Length && bar < bars.Length; bar++)
            {
                if (bars[bar] is not { } measure) continue;
                if (!map.TryGetValue(chunks[bar], out var list)) map[chunks[bar]] = list = new List<MeasureModel>(1);
                list.Add(measure);
            }
        }
        return map;
    }

    private static StateChunk Reuse(byte[] bytes, StateChunk? previous, ref long fresh)
    {
        if (previous is not null && previous.Bytes.AsSpan().SequenceEqual(bytes)) return previous;
        fresh += bytes.Length;
        return new StateChunk(bytes);
    }

    /// <summary>A bar that moved (inserted/deleted/reordered bars or tracks): find its chunk anywhere in the previous state.</summary>
    private StateChunk? FindInPrevious(ReadOnlySpan<byte> bytes)
    {
        if (_last is null) return null;
        var index = _index;
        if (index is null)
            lock (_indexGate)   // tracks encode in parallel on a big song: the first miss builds it for all of them
            {
                if (_index is null)
                {
                    var built = new Dictionary<ulong, StateChunk>();
                    foreach (var track in _last.Tracks)
                        foreach (var chunk in track.Bars)
                            built.TryAdd(chunk.Key, chunk);
                    Volatile.Write(ref _index, built);
                }
                index = _index;
            }
        return index.TryGetValue(StateChunk.KeyOf(bytes), out var found) && found.Bytes.AsSpan().SequenceEqual(bytes) ? found : null;
    }
}
