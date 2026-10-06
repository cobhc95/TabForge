using System.Linq;

namespace TabForge.Models;

// Owns: one project's cached bar timing measurement and the light key that keeps it current.
// Does not own: timeline compilation, clip positions or invalidation policy.
// Tests: TestSongExtent.
internal sealed class SongExtentMeasureCache
{
    private bool _valid;
    private int _timelineRevision;
    private int _tempo;
    private int _timeSignatureNumerator;
    private int _timeSignatureDenominator;
    private string? _importedFrom;
    private TrackModel? _masterBarTrack;
    private List<TrackModel>? _tracksList;
    private TrackState[] _tracks = Array.Empty<TrackState>();
    private (double EndSec, double BarSec) _value;

    internal int BuildCount { get; private set; }

    internal bool TryGet(SongProject project, out (double EndSec, double BarSec) value)
    {
        value = default;
        if (!_valid || !CanCache(project) || project.TimelineRevision != _timelineRevision
            || project.Tempo != _tempo
            || project.TimeSignatureNumerator != _timeSignatureNumerator
            || project.TimeSignatureDenominator != _timeSignatureDenominator
            || !string.Equals(project.ImportedFrom, _importedFrom, StringComparison.Ordinal)
            || !ReferenceEquals(project.MasterBarTrack, _masterBarTrack)
            || !ReferenceEquals(project.Tracks, _tracksList)
            || project.Tracks.Count != _tracks.Length)
            return false;

        for (var i = 0; i < _tracks.Length; i++)
            if (!_tracks[i].Matches(project.Tracks[i])) return false;

        value = _value;
        return true;
    }

    internal void Store(SongProject project, (double EndSec, double BarSec) value)
    {
        if (!CanCache(project))
        {
            _valid = false;
            return;
        }

        _timelineRevision = project.TimelineRevision;
        _tempo = project.Tempo;
        _timeSignatureNumerator = project.TimeSignatureNumerator;
        _timeSignatureDenominator = project.TimeSignatureDenominator;
        _importedFrom = project.ImportedFrom;
        _masterBarTrack = project.MasterBarTrack;
        _tracksList = project.Tracks;
        _tracks = new TrackState[project.Tracks.Count];
        for (var i = 0; i < _tracks.Length; i++) _tracks[i] = new TrackState(project.Tracks[i]);
        _value = value;
        _valid = true;
        BuildCount++;
    }

    private static bool CanCache(SongProject project)
    {
        const long maxTimingCells = 131_072;
        long cells = 0;
        foreach (var track in project.Tracks)
            foreach (var measure in track.Measures)
            {
                cells += measure.Cells.Count + measure.Voice2Cells.Count;
                if (cells > maxTimingCells) return false;
            }
        return true;
    }

    private sealed class TrackState
    {
        private readonly TrackModel _track;
        private readonly List<MeasureModel> _measures;
        private readonly MeasureTiming[] _timing;

        public TrackState(TrackModel track)
        {
            _track = track;
            _measures = track.Measures;
            _timing = new MeasureTiming[_measures.Count];
            for (var i = 0; i < _timing.Length; i++) _timing[i] = new MeasureTiming(_measures[i]);
        }

        public bool Matches(TrackModel track)
        {
            if (!ReferenceEquals(track, _track) || !ReferenceEquals(track.Measures, _measures) || track.Measures.Count != _timing.Length)
                return false;
            for (var i = 0; i < _timing.Length; i++)
                if (!_timing[i].Matches(_measures[i])) return false;
            return true;
        }
    }

    private readonly struct MeasureTiming
    {
        private readonly MeasureModel? _measure;
        private readonly int? _timeSigNum;
        private readonly int? _timeSigDenom;
        private readonly int? _tempoChange;
        private readonly TempoPoint[]? _midBarTempos;
        private readonly bool _repeatStart;
        private readonly bool _repeatEnd;
        private readonly int _repeatCount;
        private readonly int _alternateEnding;
        private readonly int _alternateEndingMask;
        private readonly string _directions;
        private readonly bool _simileOneBar;
        private readonly bool _simileTwoBar;
        private readonly CellTimingList _cells;
        private readonly CellTimingList _voice2Cells;

        public MeasureTiming(MeasureModel? measure)
        {
            _measure = measure;
            _timeSigNum = measure?.TimeSigNum;
            _timeSigDenom = measure?.TimeSigDenom;
            _tempoChange = measure?.TempoChange;
            _midBarTempos = measure?.MidBarTempos?.ToArray();
            _repeatStart = measure?.RepeatStart ?? false;
            _repeatEnd = measure?.RepeatEnd ?? false;
            _repeatCount = measure?.RepeatCount ?? 0;
            _alternateEnding = measure?.AlternateEnding ?? 0;
            _alternateEndingMask = measure?.AlternateEndingMask ?? 0;
            _directions = measure?.Directions ?? "";
            _simileOneBar = measure?.SimileOneBar ?? false;
            _simileTwoBar = measure?.SimileTwoBar ?? false;
            _cells = new CellTimingList(measure?.Cells);
            _voice2Cells = new CellTimingList(measure?.Voice2Cells);
        }

        public bool Matches(MeasureModel? measure)
        {
            if (!ReferenceEquals(measure, _measure)
                || measure?.TimeSigNum != _timeSigNum
                || measure?.TimeSigDenom != _timeSigDenom
                || measure?.TempoChange != _tempoChange
                || (measure?.RepeatStart ?? false) != _repeatStart
                || (measure?.RepeatEnd ?? false) != _repeatEnd
                || (measure?.RepeatCount ?? 0) != _repeatCount
                || (measure?.AlternateEnding ?? 0) != _alternateEnding
                || (measure?.AlternateEndingMask ?? 0) != _alternateEndingMask
                || !string.Equals(measure?.Directions ?? "", _directions, StringComparison.Ordinal)
                || (measure?.SimileOneBar ?? false) != _simileOneBar
                || (measure?.SimileTwoBar ?? false) != _simileTwoBar
                || !_cells.Matches(measure?.Cells)
                || !_voice2Cells.Matches(measure?.Voice2Cells))
                return false;

            var points = measure?.MidBarTempos;
            if (points is null) return _midBarTempos is null;
            if (_midBarTempos is null || points.Count != _midBarTempos.Length) return false;
            for (var i = 0; i < points.Count; i++)
                if (points[i] != _midBarTempos[i]) return false;
            return true;
        }
    }

    private sealed class CellTimingList
    {
        private readonly int _cellCount;
        private readonly CellTiming[] _timedCells;

        public CellTimingList(List<TabCell>? cells)
        {
            _cellCount = cells?.Count ?? 0;
            if (cells is null || cells.Count == 0) { _timedCells = Array.Empty<CellTiming>(); return; }
            List<CellTiming>? timed = null;
            for (var i = 0; i < cells.Count; i++)
                if (CellTiming.IsRelevant(cells[i])) (timed ??= new()).Add(new CellTiming(i, cells[i]));
            _timedCells = timed?.ToArray() ?? Array.Empty<CellTiming>();
        }

        public bool Matches(List<TabCell>? cells)
        {
            if (cells is null) return _cellCount == 0;
            if (cells.Count != _cellCount) return false;
            var timedIndex = 0;
            for (var i = 0; i < cells.Count; i++)
            {
                var cell = cells[i];
                if (!CellTiming.IsRelevant(cell)) continue;
                if (timedIndex >= _timedCells.Length || !_timedCells[timedIndex].Matches(i, cell)) return false;
                timedIndex++;
            }
            return timedIndex == _timedCells.Length;
        }
    }

    private readonly struct CellTiming
    {
        private readonly int _index;
        private readonly bool _hasNotes;
        private readonly bool _isRest;
        private readonly bool _hasAnnotation;
        private readonly int _durationDenominator;
        private readonly int _dots;
        private readonly bool _isTriplet;
        private readonly int _tupletNumerator;
        private readonly int _tupletDenominator;
        private readonly double? _rhythmicPosition;
        private readonly bool _isGrace;
        private readonly bool _hasFermata;

        public CellTiming(int index, TabCell cell)
        {
            _index = index;
            _hasNotes = cell.Notes.Count > 0;
            _isRest = cell.IsRest;
            _hasAnnotation = cell.HasAnnotation;
            _durationDenominator = cell.DurationDenominator;
            _dots = cell.Dots;
            _isTriplet = cell.IsTriplet;
            _tupletNumerator = cell.TupletNumerator;
            _tupletDenominator = cell.TupletDenominator;
            _rhythmicPosition = cell.RhythmicPosition;
            _isGrace = cell.IsGrace;
            _hasFermata = HasFermata(cell);
        }

        public bool Matches(int index, TabCell cell) =>
            index == _index
            && (cell.Notes.Count > 0) == _hasNotes
            && cell.IsRest == _isRest
            && cell.HasAnnotation == _hasAnnotation
            && cell.DurationDenominator == _durationDenominator
            && cell.Dots == _dots
            && cell.IsTriplet == _isTriplet
            && cell.TupletNumerator == _tupletNumerator
            && cell.TupletDenominator == _tupletDenominator
            && cell.RhythmicPosition == _rhythmicPosition
            && cell.IsGrace == _isGrace
            && HasFermata(cell) == _hasFermata;

        public static bool IsRelevant(TabCell cell) =>
            cell.Notes.Count > 0 || cell.IsRest || cell.HasAnnotation || cell.Fermata;

        private static bool HasFermata(TabCell cell)
        {
            if (cell.Fermata) return true;
            foreach (var note in cell.Notes)
                if (note.Techniques.Contains("Fermata")) return true;
            return false;
        }
    }
}
