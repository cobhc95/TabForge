using System.Linq;
using TabForge.Models;

namespace TabForge.Views.Score;

internal sealed partial class ScoreLayoutEngine
{
    /// <summary>Number of natural bar widths measured by the last rebuilt page layout.</summary>
    internal int LastNaturalMeasureCount => _incremental.LastNaturalMeasureCount;

    internal bool LastRelayoutWasPartial => _incremental.LastRelayoutWasPartial;

    internal string LastRelayoutReason => _incremental.LastRelayoutReason;

    internal void CaptureMeasureRange((int FirstMeasure, int LastMeasure) range) =>
        _incremental.CapturedMeasureRange = (range.FirstMeasure, range.LastMeasure);

    internal void InvalidatePendingMeasures(bool changed = true, bool useCapturedRange = true)
    {
        var range = _incremental.CapturedMeasureRange;
        _incremental.CapturedMeasureRange = null;
        if (!changed) return;
        InvalidateMeasures(useCapturedRange ? range?.First ?? -1 : -1,
            useCapturedRange ? range?.Last ?? -1 : -1);
    }

    /// <summary>Retains page-width inputs when a known edit changes only a stable range.</summary>
    internal void InvalidateMeasures(int firstMeasure, int lastMeasure)
    {
        var track = _scoreLayoutTrack;
        SlowTrace.Mark(firstMeasure >= 0 && lastMeasure >= firstMeasure
            ? $"score relayout requested for bars {firstMeasure + 1}-{lastMeasure + 1}"
            : "score relayout requested");
        if (_scoreLayout is null || track is null || _incremental.NaturalMeasureWidths is null ||
            firstMeasure < 0 || lastMeasure < firstMeasure || lastMeasure >= track.Measures.Count ||
            _incremental.NaturalMeasureWidths.Length != track.Measures.Count ||
            !ReferenceEquals(track, _host.Track) || !ReferenceEquals(_scoreLayoutProject, _host.Project) ||
            _scoreLayoutHorizontal != _host.HorizontalScroll ||
            (!_host.HorizontalScroll && Math.Abs(_scoreLayoutGridWidth - _host.GridWidth) >= 0.1) ||
            Math.Abs(_scoreLayoutSpacing - _host.Appearance.ScoreSpacing) >= 0.001 ||
            Math.Abs(_scoreLayoutSystemSpacing - _host.Appearance.SystemVerticalSpacing) >= 0.001 ||
            Math.Abs(_scoreLayoutMeasureSpacing - _host.Appearance.MeasureHorizontalSpacing) >= 0.001)
        {
            _incremental.PendingMeasureRange = null;
            Invalidate();
            return;
        }

        _incremental.PendingMeasureRange = (firstMeasure, lastMeasure);
        _incremental.PartialFactsAreLocal = _palmMutePassages.Count == 0 && _fadePassages.Count == 0 &&
            DynamicMarks.Count == 0 && (_host.Project?.Markers.Count ?? 0) == 0 &&
            !track.Measures.Any(measure => measure.KeySignature.HasValue || measure.KeySignatureMinor.HasValue ||
                measure.TimeSigNum.HasValue || measure.TimeSigDenom.HasValue);
        _scoreLayout = null;
        _scoreGeneration++;
        _scoreFactsGeneration = -1;
        _scoreFactsTrack = null;
        _scoreFactsProject = null;
        _barStateCache = null;
        _markerByMeasure = null;
        _markerCacheProject = null;
        _markerCacheGeneration = -1;
        if (_staffLayoutCache is not null && _staffLayoutCache.GetLength(0) == track.Measures.Count)
        {
            for (var measure = firstMeasure; measure <= lastMeasure; measure++)
            for (var voice = 0; voice < 2; voice++)
            {
                _staffLayoutCache[measure, voice] = null;
                _staffLayoutKeys![measure, voice] = default;
            }
        }
        _host.LayoutChanged();
    }

    private double[] MeasureNaturalWidths(TrackModel track, IReadOnlyList<PalmMutePassage> palmMutePassages)
    {
        var pending = _incremental.PendingMeasureRange;
        var oldWidths = _incremental.NaturalMeasureWidths;
        var oldForced = _incremental.ForceLineBreaks;
        var oldPrevented = _incremental.PreventLineBreaks;
        var oldRisk = _incremental.LayoutRisk;
        var oldExtents = _incremental.MeasureExtents;
        var currentFactsAreLocal = palmMutePassages.Count == 0 && _fadePassages.Count == 0 &&
            DynamicMarks.Count == 0 && (_host.Project?.Markers.Count ?? 0) == 0 &&
            !track.Measures.Any(measure => measure.KeySignature.HasValue || measure.KeySignatureMinor.HasValue ||
                measure.TimeSigNum.HasValue || measure.TimeSigDenom.HasValue);
        var canStartPartial = pending is { } range && _incremental.PartialFactsAreLocal && oldWidths is not null &&
            oldWidths.Length == track.Measures.Count && oldForced?.Length == track.Measures.Count &&
            oldPrevented?.Length == track.Measures.Count && oldRisk?.Length == track.Measures.Count &&
            oldExtents?.Length == track.Measures.Count &&
            currentFactsAreLocal &&
            ReferenceEquals(_scoreLayoutTrack, track) && ReferenceEquals(_scoreLayoutProject, _host.Project) &&
            !Enumerable.Range(range.First, range.Last - range.First + 1).Any(index => oldRisk![index]);
        var signatures = MeasureSignatures(track);
        if (canStartPartial && !PartialRangeIsIsolated(track, pending!.Value, signatures))
        {
            canStartPartial = false;
            _incremental.LastRelayoutReason = "bars changed outside edit range or cross-bar context";
            _incremental.BlockedReason = true;
        }
        var widths = canStartPartial ? (double[])oldWidths!.Clone() : new double[track.Measures.Count];
        if (!canStartPartial) _incremental.MeasureExtents = new MeasureMarkExtents[track.Measures.Count];
        var scanOnlyChangedMeasures = canStartPartial;
        var extraBefore = (_extraAbove, _extraBelow, _extraTabBelow);
        BeginMarkExtentScan();
        var measured = Measure(Enumerable.Range(0, track.Measures.Count));
        EndMarkExtentScan();

        var currentExtra = (_extraAbove, _extraBelow, _extraTabBelow);
        var riskUnchanged = canStartPartial && pending is { } riskRange && oldRisk is not null &&
            Enumerable.Range(riskRange.First, riskRange.Last - riskRange.First + 1)
                .All(index => oldRisk[index] == MeasureHasLayoutRisk(track.Measures[index]));
        var widthsUnchanged = canStartPartial && pending is { } widthRange && oldWidths is not null &&
            Enumerable.Range(widthRange.First, widthRange.Last - widthRange.First + 1)
                .All(index => Math.Abs(widths[index] - oldWidths[index]) < 0.01);
        var breaksUnchanged = canStartPartial && oldForced is not null && oldPrevented is not null &&
            Enumerable.Range(0, track.Measures.Count).All(index =>
                oldForced[index] == track.Measures[index].ForceLineBreak &&
                oldPrevented[index] == track.Measures[index].PreventLineBreak);
        var extentsUnchanged = extraBefore == currentExtra;
        _incremental.LastRelayoutWasPartial = canStartPartial && riskUnchanged && widthsUnchanged && breaksUnchanged && extentsUnchanged;
        _incremental.LastRelayoutReason = _incremental.BlockedReason && !canStartPartial ? _incremental.LastRelayoutReason : !canStartPartial ? !_incremental.PartialFactsAreLocal || !currentFactsAreLocal ? "cross-bar layout facts" : "no stable range cache" :
            !riskUnchanged ? "tie or tuplet context changed" : !widthsUnchanged ? "bar width changed" :
            !breaksUnchanged ? "system break changed" : !extentsUnchanged ? "vertical extents changed" : "stable range";
        if (canStartPartial && !_incremental.LastRelayoutWasPartial)
        {
            _warps.Clear();
            widths = new double[track.Measures.Count];
            _incremental.MeasureExtents = new MeasureMarkExtents[track.Measures.Count];
            scanOnlyChangedMeasures = false;
            BeginMarkExtentScan();
            measured = Measure(Enumerable.Range(0, track.Measures.Count));
            EndMarkExtentScan();
        }
        else if (_incremental.LastRelayoutWasPartial && pending is { } retainedRange)
        {
            PreserveUnchangedWarps(track, retainedRange);
        }

        if (pending is not null && !_incremental.LastRelayoutWasPartial)
        {
            // Cached staff layouts are keyed by reference, so any bar outside the edit range may hold stale content.
            _staffLayoutCache = null;
            _staffLayoutKeys = null;
        }
        _incremental.BlockedReason = false;
        _incremental.PendingSignatures = signatures;
        _incremental.LastNaturalMeasureCount = measured;
        _incremental.PendingMeasureRange = null;
        _incremental.CapturedMeasureRange = null;
        return widths;

        int Measure(IEnumerable<int> measureIndexes)
        {
            var count = 0;
            foreach (var index in measureIndexes)
            {
                if (scanOnlyChangedMeasures && pending is { } range && (index < range.First || index > range.Last))
                {
                    MergeMeasureExtent(oldExtents![index]);
                    continue;
                }
                BeginMeasureExtentScan(index);
                widths[index] = NaturalMeasureWidth(track, track.Measures[index], index, palmMutePassages) * _host.Appearance.MeasureHorizontalSpacing;
                if (!_host.HorizontalScroll) widths[index] = Math.Min(widths[index], _host.GridWidth);
                _incremental.MeasureExtents![index] = SaveMeasureExtent();
                count++;
            }
            return count;
        }
    }

    private void PreserveUnchangedWarps(TrackModel track, (int First, int Last) changed)
    {
        for (var index = 0; index < track.Measures.Count; index++)
        {
            if (index >= changed.First && index <= changed.Last) continue;
            var measure = track.Measures[index];
            if (_warps.TryGetValue(measure, out var cached))
                _warps[measure] = (_scoreGeneration, cached.Slots, cached.Warp);
        }
    }

    private void SaveIncrementalWidths(TrackModel track, double[] widths,
        IReadOnlyList<bool> forceLineBreaks, IReadOnlyList<bool> preventLineBreaks)
    {
        _incremental.NaturalMeasureWidths = widths;
        _incremental.ForceLineBreaks = forceLineBreaks.ToArray();
        _incremental.PreventLineBreaks = preventLineBreaks.ToArray();
        _incremental.LayoutRisk = track.Measures.Select(MeasureHasLayoutRisk).ToArray();
        _incremental.BoundaryRisk = track.Measures.Select(MeasureCrossesBarLine).ToArray();
        _incremental.MeasureSignatures = _incremental.PendingSignatures ?? MeasureSignatures(track);
        _incremental.PendingSignatures = null;
    }

    /// <summary>Content hash per bar, so an edit that rewrote bars beyond its captured range is noticed.</summary>
    private static long[] MeasureSignatures(TrackModel track)
    {
        var result = new long[track.Measures.Count];
        for (var i = 0; i < result.Length; i++) result[i] = MeasureSignature(track.Measures[i]);
        return result;
    }

    // Allocation-free hash of the fields that shape a bar's layout.
    private static long MeasureSignature(MeasureModel measure)
    {
        long h = 17;
        Mix(ref h, measure.Cells.Count + 31 * measure.Voice2Cells.Count);
        Mix(ref h, (measure.ForceLineBreak ? 1 : 0) | (measure.PreventLineBreak ? 2 : 0) | (measure.TripletFeel ? 4 : 0));
        for (var v = 0; v < 2; v++)
            foreach (var cell in v == 0 ? measure.Cells : measure.Voice2Cells)
            {
                Mix(ref h, cell.DurationDenominator | cell.Dots << 8 | cell.TupletNumerator << 12 | cell.TupletDenominator << 16);
                Mix(ref h, (cell.IsTriplet ? 1 : 0) | (cell.IsRest ? 2 : 0) | (cell.IsTied ? 4 : 0) | (cell.IsGrace ? 8 : 0) |
                    (cell.Fermata ? 16 : 0) | (cell.Staccato ? 32 : 0) | (cell.Tenuto ? 64 : 0) | cell.Accent << 8 |
                    (int)cell.BeamMode << 12 | (int)cell.StemDirection << 16 | cell.OctaveShiftSemitones << 20);
                Mix(ref h, cell.RhythmicPosition?.GetHashCode() ?? 0);
                Mix(ref h, cell.ChordName?.GetHashCode() ?? 0);
                Mix(ref h, cell.Text?.GetHashCode() ?? 0);
                Mix(ref h, cell.Lyrics.GetHashCode());
                Mix(ref h, cell.WhammyPoints.Count + 7 * cell.TremoloPickDenominator);
                Mix(ref h, cell.Mix is null ? 0 : 1);
                foreach (var note in cell.Notes)
                {
                    Mix(ref h, note.StringIndex | note.Fret << 8 | note.MidiValue << 16);
                    Mix(ref h, note.Velocity | (note.Ghost ? 1 << 8 : 0) | (note.Dead ? 1 << 9 : 0) | (note.Tied ? 1 << 10 : 0) |
                        (note.IsGraceNote ? 1 << 11 : 0) | note.Techniques.Count << 12);
                    Mix(ref h, note.BendPoints.Count + 5 * note.SlideTargetMidi + 11 * note.TrillTargetMidi);
                    Mix(ref h, note.HarmonicFret?.GetHashCode() ?? 0);
                    foreach (var technique in note.Techniques) h += technique.GetHashCode();
                }
            }
        return h;
    }

    private static void Mix(ref long h, int value) => h = (h ^ (uint)value) * 1099511628211L;

    /// <summary>True when only the captured bars changed and no tie, tuplet, slur or technique crosses its edges.</summary>
    private bool PartialRangeIsIsolated(TrackModel track, (int First, int Last) range, long[] signatures)
    {
        var old = _incremental.MeasureSignatures;
        var oldBoundary = _incremental.BoundaryRisk;
        if (old is null || oldBoundary is null || old.Length != signatures.Length || oldBoundary.Length != signatures.Length) return false;
        for (var index = 0; index < signatures.Length; index++)
            if ((index < range.First || index > range.Last) && old[index] != signatures[index]) return false;
        for (var index = Math.Max(0, range.First - 1); index <= Math.Min(signatures.Length - 1, range.Last + 1); index++)
            if (oldBoundary[index] || MeasureCrossesBarLine(track.Measures[index])) return false;
        return true;
    }

    private static bool MeasureCrossesBarLine(MeasureModel measure) =>
        measure.Cells.Concat(measure.Voice2Cells).Any(cell => cell.IsTied || cell.IsTriplet || cell.TupletNumerator > 0 ||
            cell.Notes.Any(note => note.Tied || note.Techniques.Count > 0));

    private void ClearIncrementalCaches()
    {
        _incremental.NaturalMeasureWidths = null;
        _incremental.ForceLineBreaks = null;
        _incremental.PreventLineBreaks = null;
        _incremental.LayoutRisk = null;
        _incremental.BoundaryRisk = null;
        _incremental.MeasureSignatures = null;
        _incremental.PendingSignatures = null;
        _incremental.MeasureExtents = null;
        _incremental.PendingMeasureRange = null;
        _incremental.CapturedMeasureRange = null;
        _incremental.PartialFactsAreLocal = false;
        _incremental.LastNaturalMeasureCount = 0;
        _incremental.LastRelayoutWasPartial = false;
    }

    private static bool MeasureHasLayoutRisk(MeasureModel measure) =>
        measure.TripletFeel || TripletFeels.Effective(measure) != TripletFeels.None ||
        measure.Cells.Concat(measure.Voice2Cells).Any(cell => cell.IsTied || cell.IsTriplet || cell.TupletNumerator > 0 ||
            cell.Notes.Any(note => note.Tied));

    private void BeginMeasureExtentScan(int measureIndex)
    {
        _incremental.MeasureScanTop = 0;
        _incremental.MeasureScanBottom = 4 * StaffNotationRenderer.StaffGap;
        _incremental.MeasureScanRows = 0;
        _incremental.MeasureScanVolta = false;
        _incremental.MeasureScanTabBelow = 0;
    }

    private MeasureMarkExtents SaveMeasureExtent() => new(_incremental.MeasureScanTop, _incremental.MeasureScanBottom,
        _incremental.MeasureScanRows, _incremental.MeasureScanVolta, _incremental.MeasureScanTabBelow);

    private void MergeMeasureExtent(MeasureMarkExtents extent)
    {
        _scanTop = Math.Min(_scanTop, extent.Top);
        _scanBottom = Math.Max(_scanBottom, extent.Bottom);
        _scanRows = Math.Max(_scanRows, extent.Rows);
        _scanVolta |= extent.Volta;
        _scanTabBelow = Math.Max(_scanTabBelow, extent.TabBelow);
    }

}
