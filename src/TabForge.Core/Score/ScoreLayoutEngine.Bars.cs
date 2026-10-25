using System.Globalization;
using System.Linq;
using TabForge.Diagnostics;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Views.Score;

// Owns: per-bar warp, facts and state lookups.
// Does not own: the rest of this type (see the main file).
// Tests: no dedicated test class.
internal sealed partial class ScoreLayoutEngine
{
    /// <summary>Room (sixteenth-equivalents) for an engraved key/time signature so the first note clears it.</summary>
    private double HeaderLeadWeight(TrackModel track, int measureIndex)
    {
        var pixels = 0.0;
        if (ScoreClefKey.ClefChanges(track, measureIndex)) pixels += ScoreClefKey.ClefChangeWidth;
        if (KeySignatureChanges(track, measureIndex)) pixels += KeySignatureWidth(track, measureIndex);
        if (TimeSignatureShown(track, measureIndex)) pixels += TimeSignatureWidth(track.Measures[measureIndex]) + 6;
        if (pixels > 0) pixels += 12;   // the signatures start 28 px in (NaturalMeasureWidth's lead), 12 px beyond the plain 16 px lead the first-note spacing already has
        return pixels <= 0 ? 0 : pixels / Math.Max(1, RhythmicPixelsPerSlot * _host.Appearance.ScoreSpacing);
    }

    internal MeasureWarp WarpFor(TrackModel track, int measureIndex)
    {
        var measure = track.Measures[measureIndex];
        var slots = SlotsFor(measureIndex);
        if (_warps.TryGetValue(measure, out var hit) && hit.Generation == _scoreGeneration && hit.Slots == slots) return hit.Warp;
        static IEnumerable<double> Onsets(List<TabCell> cells) => MusicTime.BeatOnsets(cells).Select(b => b.Start);
        // Repeat signs get their own room so the first/last notes never touch the dots.
        // The first note always clears the barline (the reference spacing); grace notes before the first beat need extra room.
        var lead = (measure.RepeatStart ? 1.8 : 0) + HeaderLeadWeight(track, measureIndex);
        var firstContent = measure.Cells.FirstOrDefault(MusicTime.IsBeat);
        lead = Math.Max(lead, 1.6);
        // The grace room adds to the plain lead once: after a clef, key or time signature it does not stack a second lead.
        if (firstContent is not null && firstContent.Notes.Any(n => n.IsGraceNote) && firstContent.Notes.Any(n => !n.IsGraceNote)) lead += 1.4;
        // An overfull bar (more music than its time signature) squeezes everything inside the bar,
        // instead of letting the last beats spill into the next bar.
        static double ContentEnd(List<TabCell> cells)
            => MusicTime.BeatOnsets(cells).Where(b => !MusicTime.IsGraceOnly(b.Cell)).Select(b => b.Start + MusicTime.CellSlots(b.Cell)).DefaultIfEmpty(0).Max();
        var contentEnd = Math.Max(ContentEnd(measure.Cells), Voice2HasContent(measure) ? ContentEnd(measure.Voice2Cells) : 0);
        var warp = MeasureWarp.Build(Onsets(measure.Cells).Concat(Voice2HasContent(measure) ? Onsets(measure.Voice2Cells) : Enumerable.Empty<double>()),
            contentEnd > slots + 0.05 ? contentEnd : slots,
            leadWeight: lead, trailWeight: measure.RepeatEnd ? 1.4 : 0);
        _warps[measure] = (_scoreGeneration, slots, warp);
        return warp;
    }

    internal ScoreFacts EnsureScoreFacts(TrackModel track, SongProject project)
    {
        if (_scoreFactsGeneration == _scoreGeneration && ReferenceEquals(_scoreFactsTrack, track) &&
            ReferenceEquals(_scoreFactsProject, project))
            return new ScoreFacts(_palmMutePassages, _fadePassages);

        _palmMutePassages = _host.Notation == NotationMode.StaffOnly
            ? Array.Empty<PalmMutePassage>()
            : ScorePassages.BuildPalmMutePassages(track, project);
        _fadePassages = ScorePassages.BuildFadePassages(track, project);
        DynamicMarks = _host.Appearance.ShowDynamics ? ScorePassages.BuildDynamicMarks(track) : new Dictionary<TabCell, string>(ReferenceEqualityComparer.Instance);
        _scoreFactsTrack = track;
        _scoreFactsProject = project;
        _scoreFactsGeneration = _scoreGeneration;
        return new ScoreFacts(_palmMutePassages, _fadePassages);
    }

    /// <summary>The bar's fill state in this track's own view (another track's short bar is not marked here): the shared red-bar check (BarTiming.Status, spec R1-R9).</summary>
    internal BarState BarStateFor(TrackModel track, int measureIndex)
    {
        var project = _host.Project;
        if (project is null) return default;
        _barStateCache ??= new();
        if (!_barStateCache.TryGetValue((track, measureIndex), out var state))
        {
            var bar = MusicTime.BarOf(project, measureIndex);
            var numerator = bar?.TimeSigNum ?? project.TimeSignatureNumerator;
            var denominator = bar?.TimeSigDenom ?? project.TimeSignatureDenominator;
            var slots = MusicTime.BarSlots(numerator, denominator);
            if (measureIndex < 0 || measureIndex >= track.Measures.Count)
                state = new BarState(slots, 0, false, false, false);
            else
            {
                var measure = track.Measures[measureIndex];
                var status = BarTiming.Status(measure, numerator, denominator);
                var usedTicks = BarTiming.VoiceUsedTicks(measure, 1);
                if (measure.Voice2Cells.Count > 0)
                {
                    var second = BarTiming.VoiceUsedTicks(measure, 2);
                    if (second > usedTicks) usedTicks = second;
                }
                var used = usedTicks.Value / 240.0;
                state = new BarState(slots, used, status == BarTimingStatus.Complete, status == BarTimingStatus.Overfull, status == BarTimingStatus.Underfull);
            }
            _barStateCache[(track, measureIndex)] = state;
        }
        return state;
    }

    internal MarkerModel? MarkerForMeasure(int measureIndex)
    {
        var project = _host.Project;
        if (project is null || measureIndex < 0) return null;
        var track = _host.Track;
        var count = track?.Measures.Count ?? 0;
        if (_markerByMeasure is null || _markerCacheGeneration != _scoreGeneration ||
            !ReferenceEquals(_markerCacheProject, project) || _markerByMeasure.Length != count)
        {
            _markerByMeasure = new MarkerModel?[count];
            foreach (var marker in project.Markers)
                if (marker.MeasureIndex >= 0 && marker.MeasureIndex < count && _markerByMeasure[marker.MeasureIndex] is null)
                    _markerByMeasure[marker.MeasureIndex] = marker;
            _markerCacheProject = project;
            _markerCacheGeneration = _scoreGeneration;
        }
        return measureIndex < _markerByMeasure.Length ? _markerByMeasure[measureIndex] : null;
    }

    internal StaffNotationMeasureLayout StaffLayoutFor(TrackModel track, MeasureModel measure, int measureIndex,
        int slots, double x, double staffTop, double slotWidth, int numerator, int denominator,
        int keySignature, IReadOnlyList<TabCell> cells)
    {
        var voice = ReferenceEquals(cells, measure.Voice2Cells) ? 1 : 0;
        if (_staffLayoutCache is null || _staffLayoutCache.GetLength(0) != track.Measures.Count)
        {
            _staffLayoutCache = new StaffNotationMeasureLayout?[track.Measures.Count, 2];
            _staffLayoutKeys = new StaffLayoutCacheKey[track.Measures.Count, 2];
        }

        var warp = WarpFor(track, measureIndex);
        var key = new StaffLayoutCacheKey(track, measure, cells, measureIndex, slots, numerator,
            denominator, keySignature, x, staffTop, slotWidth, _host.Appearance.ScoreSpacing, warp);
        if (_staffLayoutCache[measureIndex, voice] is { } cached && _staffLayoutKeys![measureIndex, voice] == key)
            return cached;

        var measureWidth = slotWidth * Math.Max(1, slots);
        var layout = StaffNotationLayoutBuilder.CreateLayout(track, measure, measureIndex, slots, x, staffTop, slotWidth,
            numerator, denominator, keySignature, _host.Appearance.ScoreSpacing, cells, start => x + warp.CenterFraction(start) * measureWidth);
        _staffLayoutCache[measureIndex, voice] = layout;
        _staffLayoutKeys![measureIndex, voice] = key;
        return layout;
    }

    /// <summary>Voice 2 is engraved (notes and rests) only when it holds real content in the bar.</summary>
    internal static bool Voice2HasContent(MeasureModel measure)
    {
        var cells = measure.Voice2Cells;
        for (var i = 0; i < cells.Count; i++)
            if (cells[i].Notes.Count > 0 || cells[i].HasAnnotation) return true;
        return false;
    }

}
