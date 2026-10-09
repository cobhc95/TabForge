using System.Globalization;
using TabForge.Models;

namespace TabForge.Playback;

/// <summary>
/// Owns immutable checks for reusing a compiled playback schedule and resolving a seek target.
/// Does not own the live timeline, scheduler thread, or MIDI output. Tests: TestPlaybackScheduleReuse.
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

    private PlaybackScheduleReuse(SongProject project, PlaybackOptions options, ScoreTimeline timeline)
    {
        _project = project;
        _timeline = timeline;
        _contentRevision = project.ContentRevision;
        _optionsStamp = OptionsStamp(options);
        _traversal = timeline.Bars.Select(bar => bar.Bar).ToArray();
        _trackRoutes = RouteStamps(project);
        (_loopStart, _loopEnd) = options.Loop ? PlaybackOrder.LoopRange(project, options) : (-1, -1);
        var ambiguousTraversal = options.SkipClips || project.Tracks
            .SelectMany(track => track.Measures)
            .Any(measure => measure.RepeatStart || measure.RepeatEnd || measure.EndingPasses != 0 || !string.IsNullOrWhiteSpace(measure.Directions));
        _simpleAscending = !ambiguousTraversal && _traversal.Length > 0 &&
            _traversal.Select((bar, index) => bar == _traversal[0] + index).All(matches => matches) &&
            PlaybackOrder.Build(project, options).SequenceEqual(_traversal);
        _barsBySource = _simpleAscending
            ? timeline.Bars.ToDictionary(bar => bar.Bar)
            : new Dictionary<int, ScoreBar>();
    }

    public static PlaybackScheduleReuse Capture(SongProject project, PlaybackOptions options, ScoreTimeline timeline) =>
        new(project, options, timeline);

    public bool TryResolve(SongProject project, PlaybackOptions options, ScoreTimeline timeline,
        int requestedBar, int requestedCell, out double targetMs)
    {
        targetMs = 0;
        if (!ReferenceEquals(project, _project) || !ReferenceEquals(timeline, _timeline) || project.ContentRevision != _contentRevision ||
            !string.Equals(_optionsStamp, OptionsStamp(options), StringComparison.Ordinal) ||
            !RoutesMatch(project)) return false;
        if (!_simpleAscending || !_barsBySource.TryGetValue(requestedBar, out var first)) return false;
        if (options.Loop)
        {
            if (requestedBar < _loopStart || requestedBar > _loopEnd ||
                _loopStart != _traversal[0] || _loopEnd > _traversal[^1]) return false;
        }
        else if (requestedBar < _traversal[0] || requestedBar > _traversal[^1]) return false;

        var cell = Math.Clamp(requestedCell, 0, Math.Max(1, first.Slots) - 1);
        targetMs = first.MsAtFraction((double)cell / Math.Max(1, first.Slots));
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

    // Group volume/pan/transpose and the channel assignment decide the compiled setup events, so they belong to the stamp.
    private static string[] RouteStamps(SongProject project)
    {
        var channels = ChannelAllocator.Assign(project);
        return project.Tracks.Select((track, i) => TrackRouteStamp(track) + "," + string.Join(",",
            MixerGroups.Volume(project, track), MixerGroups.Pan(project, track), MixerGroups.Transpose(project, track),
            i < channels.Length ? channels[i] : -1)).ToArray();
    }

    private static string TrackRouteStamp(TrackModel track) => string.Join(",",
        track.Kind, track.MidiChannel, track.MidiProgram, track.MidiOutputDeviceId,
        track.Volume, track.Pan, track.Chorus, track.Reverb, track.Mute, track.Solo,
        track.MidiSound, track.SoundSource,
        string.Join("/", track.Rig.Plugins.Select(plugin => $"{plugin.Type}:{plugin.Enabled}:{plugin.Unavailable}")));

    private static string OptionsStamp(PlaybackOptions options) => string.Join("|",
        options.Speed.ToString("R", CultureInfo.InvariantCulture), options.Loop, options.LoopStartBar,
        options.LoopEndBar, options.LoopStartCell, options.LoopEndCell, options.Metronome,
        options.CountIn, options.RespectMuteSolo, options.CountInBars, options.RepeatExpansion,
        options.SkipClips, options.MetronomeAccentNote, options.MetronomeClickNote,
        options.MetronomeVolume, options.MetronomeAccentVolume, options.MetronomeClickVolume,
        options.MetronomeSubdivision, options.LiveMetronomeEvents,
        options.LetRingCapMs.ToString("R", CultureInfo.InvariantCulture));

}
