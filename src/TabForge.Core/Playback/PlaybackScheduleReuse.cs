using System.Globalization;
using TabForge.Models;
using TabForge.Plugins;

namespace TabForge.Playback;

// Owns: the checks that decide whether a compiled playback schedule can be reused, and the seek target it resolves.
// Does not own: the compile (ScoreToMidiCompiler) and the engine (PlaybackEngine, app).
// Tests: PlaybackStartOrderTests.

/// <summary>
/// Owns immutable checks for reusing a compiled playback schedule and resolving a seek target.
/// Does not own the live timeline, scheduler thread, or MIDI output. Tests: TestPlaybackScheduleReuse, PlaybackStartOrderTests.
/// </summary>
internal sealed class PlaybackScheduleReuse
{
    private readonly SongProject _project;
    private readonly ScoreTimeline _timeline;
    private readonly int _contentRevision;
    private readonly string _optionsStamp;
    private readonly int[] _traversal;
    private readonly string[] _trackRoutes;
    private readonly Dictionary<int, ScoreBar> _barsBySource;
    private readonly bool _simpleAscending;
    private readonly int _loopStart;
    private readonly int _loopEnd;

    // project is the live song (identity only); the stamps and the order are read from the snapshot's copy, so they describe the song the timeline was compiled from.
    private PlaybackScheduleReuse(SongProject project, SongSnapshot snapshot, PlaybackOptions options, ScoreTimeline timeline)
    {
        var song = snapshot.Song;
        _project = project;
        _timeline = timeline;
        _contentRevision = snapshot.ContentRevision;
        _optionsStamp = OptionsStamp(options);
        _traversal = timeline.Bars.Select(bar => bar.Bar).ToArray();
        _trackRoutes = RouteStamps(song);
        (_loopStart, _loopEnd) = options.Loop ? PlaybackOrder.LoopRange(song, options) : (-1, -1);
        var ambiguousTraversal = options.SkipClips || song.Tracks
            .SelectMany(track => track.Measures)
            .Any(measure => measure.RepeatStart || measure.RepeatEnd || measure.EndingPasses != 0 || !string.IsNullOrWhiteSpace(measure.Directions));
        var startOrder = PlaybackOrder.Build(song, options);
        // Reuse resolves a bar to its timeline start, so it only holds while the start order is a plain ascending run
        // from the start bar. A suffix that jumps back to an earlier written bar needs a fresh compile.
        _simpleAscending = !ambiguousTraversal && _traversal.Length > 0 &&
            startOrder.Count == _traversal.Length &&
            startOrder.SequenceEqual(_traversal) &&
            startOrder.Select((bar, index) => bar == startOrder[0] + index).All(matches => matches) &&
            !startOrder.Any(bar => bar < options.StartBar);
        _barsBySource = _simpleAscending
            ? timeline.Bars.ToDictionary(bar => bar.Bar)
            : new Dictionary<int, ScoreBar>();
    }

    public static PlaybackScheduleReuse Capture(SongProject project, SongSnapshot snapshot, PlaybackOptions options, ScoreTimeline timeline) =>
        new(project, snapshot, options, timeline);

    /// <summary>Captures from a fresh copy of <paramref name="project"/> (a caller with no copy of its own, such as a test).</summary>
    public static PlaybackScheduleReuse Capture(SongProject project, PlaybackOptions options, ScoreTimeline timeline) =>
        Capture(project, SongSnapshot.Take(project), options, timeline);

    public bool TryResolve(SongProject project, PlaybackOptions options, ScoreTimeline timeline,
        int requestedBar, int requestedCell, out double targetMs)
    {
        targetMs = 0;
        // A pass-aware start changes the performed order itself, so the schedule on screen cannot be reused.
        if (options.StartOccurrence is not null) return false;
        if (!ReferenceEquals(project, _project) || !ReferenceEquals(timeline, _timeline) || project.ContentRevision != _contentRevision ||
            !string.Equals(_optionsStamp, OptionsStamp(options), StringComparison.Ordinal)) return false;
        if (!_simpleAscending || !_barsBySource.TryGetValue(requestedBar, out var first)) return false;
        if (options.Loop)
        {
            if (requestedBar < _loopStart || requestedBar > _loopEnd ||
                _loopStart != _traversal[0] || _loopEnd > _traversal[^1]) return false;
        }
        else if (requestedBar < _traversal[0] || requestedBar > _traversal[^1]) return false;
        // The route stamps walk every track, so they are checked only once the bar is reachable (a seek on a non-reusable song never pays for them).
        if (!RoutesMatch(project)) return false;

        var cell = Math.Clamp(requestedCell, 0, Math.Max(1, first.Slots) - 1);
        targetMs = first.MsAtFraction((double)cell / Math.Max(1, first.Slots));
        // The compile of the first performed bar left out the beats before its start cell: a target before them needs a fresh compile.
        if (requestedBar == _traversal[0] && targetMs < timeline.PlayFromMs - 0.001) return false;
        return targetMs >= first.StartMs && targetMs < first.EndMs + 0.001;
    }

    private bool RoutesMatch(SongProject project)
    {
        if (project.Tracks.Count != _trackRoutes.Length) return false;
        var current = RouteStamps(project);
        for (var i = 0; i < _trackRoutes.Length; i++)
            if (!string.Equals(_trackRoutes[i], current[i], StringComparison.Ordinal)) return false;
        return true;
    }

    /// <summary>Everything the mixer sets on the song's tracks and groups (the per-track route stamps) as one string: it differs after any level, mute, solo, sound or channel change, whether or not the edit marked the song.</summary>
    internal static string MixStamp(SongProject project) => string.Join(";", RouteStamps(project));

    // Group volume/pan/transpose and the channel assignment decide the compiled setup events, so they belong to the stamp.
    private static string[] RouteStamps(SongProject project)
    {
        var channels = ChannelAllocator.Assign(project);
        return project.Tracks.Select((track, i) => TrackRouteStamp(track) + "," + string.Join(",",
            MixerGroups.Volume(project, track), MixerGroups.Pan(project, track), MixerGroups.Transpose(project, track),
            i < channels.Length ? channels[i] : -1)).ToArray();
    }

    private static string TrackRouteStamp(TrackModel track) => string.Join(",",
        track.Kind, track.MidiChannel, track.MidiProgram, track.MidiOutputDeviceId, track.MidiOutputDeviceName ?? "",
        track.Volume, track.Pan, track.Chorus, track.Reverb, track.Mute, track.Solo,
        track.MidiSound, track.MidiSoundManualOff, track.SoundSource,
        string.Join("/", track.Rig.Plugins.Select(plugin => $"{plugin.Type}:{plugin.Plays}:{plugin.Unavailable}{ArticulationStamp(plugin)}")),
        ArticulationStamp(track.ArticulationBindings));

    /// <summary>The articulation map of the plug-in (empty without one): editing it makes the next seek compile afresh, like any other route change.</summary>
    private static string ArticulationStamp(PluginSlot plugin) => ArticulationStamp(plugin.ArticulationBindings);

    /// <summary>The track-level fallback map (empty without one), which a plug-in added later uses.</summary>
    private static string ArticulationStamp(IReadOnlyDictionary<string, string>? bindings) =>
        bindings is { Count: > 0 } map ? ":" + string.Join(",", map.Select(pair => pair.Key + "=" + pair.Value)) : "";

    private static string OptionsStamp(PlaybackOptions options) => string.Join("|",
        options.Speed.ToString("R", CultureInfo.InvariantCulture),
        options.StartOccurrence?.ToString(CultureInfo.InvariantCulture) ?? "", options.Loop, options.LoopStartBar,
        options.LoopEndBar, options.LoopStartCell, options.LoopEndCell, options.Metronome,
        options.CountIn, options.RespectMuteSolo, options.CountInBars, options.RepeatExpansion,
        options.SkipClips, options.MetronomeAccentNote, options.MetronomeClickNote,
        options.MetronomeVolume, options.MetronomeAccentVolume, options.MetronomeClickVolume,
        options.MetronomeSubdivision, options.LiveMetronomeEvents,
        options.LetRingCapMs.ToString("R", CultureInfo.InvariantCulture),
        options.NormalVibratoDepth.ToString("R", CultureInfo.InvariantCulture), options.WideVibratoDepth.ToString("R", CultureInfo.InvariantCulture),
        options.VibratoSpeedHz.ToString("R", CultureInfo.InvariantCulture));

}
