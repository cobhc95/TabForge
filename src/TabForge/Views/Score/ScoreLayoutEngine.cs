using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using TabForge.Diagnostics;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Views.Score;

/// <summary>What the layout engine reads from the editor that owns it.</summary>
internal interface IScoreLayoutHost
{
    SongProject? Project { get; }
    TrackModel? Track { get; }
    NotationMode Notation { get; }
    ScoreAppearance Appearance { get; }
    bool HorizontalScroll { get; }
    double GridLeft { get; }
    double GridWidth { get; }
    double FretFontSize { get; }

    /// <summary>The score layout or its system height changed: the host measures again.</summary>
    void LayoutChanged();
}

/// <summary>
/// Score layout: bar widths from the notation and its marks, system breaks, the duration-based spacing of each bar, the per-bar staff
/// layouts the drawing reuses, and the facts derived once per score (passages, dynamics, markers, bar states). Everything is cached
/// against a generation that <see cref="Invalidate"/> advances, so a repaint never lays anything out again.
/// </summary>
internal sealed partial class ScoreLayoutEngine
{
    internal const double PagePad = 44;
    internal const double RhythmicPixelsPerSlot = 7.5;

    private readonly IScoreLayoutHost _host;
    private readonly StaffNotationRenderer _staff;
    private readonly ScoreLayoutIncrementalState _incremental = new();

    private ScorePageLayout? _scoreLayout;
    private TrackModel? _scoreLayoutTrack;
    private SongProject? _scoreLayoutProject;
    private double _scoreLayoutGridWidth = double.NaN;
    private bool _scoreLayoutHorizontal;
    private double _scoreLayoutSpacing = double.NaN;
    private double _scoreLayoutSystemSpacing = double.NaN;
    private double _scoreLayoutMeasureSpacing = double.NaN;
    private int _scoreGeneration;
    private TrackModel? _scoreFactsTrack;
    private SongProject? _scoreFactsProject;
    private int _scoreFactsGeneration = -1;
    private IReadOnlyList<PalmMutePassage> _palmMutePassages = Array.Empty<PalmMutePassage>();
    private IReadOnlyList<FadePassage> _fadePassages = Array.Empty<FadePassage>();
    private BarState[]? _barStateCache;
    private bool[]? _barStateComputed;
    private MarkerModel?[]? _markerByMeasure;
    private SongProject? _markerCacheProject;
    private int _markerCacheGeneration = -1;
    private StaffNotationMeasureLayout?[,]? _staffLayoutCache;
    private StaffLayoutCacheKey[,]? _staffLayoutKeys;
    // ---- vertical room for the stacked markings: measured once per layout from the track's own content ----
    private double _extraAbove, _extraBelow, _extraTabBelow;
    private double _scanTabBelow;
    private double _scanTop, _scanBottom, _scanRows;
    private bool _scanVolta;
    internal ScoreLayoutEngine(IScoreLayoutHost host, StaffNotationRenderer staff)
    {
        _host = host;
        _staff = staff;
    }

    /// <summary>The beats that carry a dynamics marking (empty when dynamics are hidden).</summary>
    internal Dictionary<TabCell, string> DynamicMarks { get; private set; } = new(ReferenceEqualityComparer.Instance);

    /// <summary>Extra room above the staff, below the staff and below the tab that the marks of the track need.</summary>
    internal double ExtraAbove => _extraAbove;
    internal double ExtraBelow => _extraBelow;
    internal double ExtraTabBelow => _extraTabBelow;

    /// <summary>Page width of the one-line (horizontal scroll) layout; 0 until it has been laid out.</summary>
    internal double HorizontalPageWidth { get; set; }

    /// <summary>Fade passages of the shown track (current after <see cref="EnsureScoreFacts"/>).</summary>
    internal IReadOnlyList<FadePassage> FadePassages => _fadePassages;

    /// <summary>True once the drawing has laid out at least one bar since the last invalidation.</summary>
    internal bool HasStaffLayouts => _staffLayoutCache is not null;

    /// <summary>Number of bars the cached staff layouts cover.</summary>
    internal int StaffLayoutBars => _staffLayoutCache?.GetLength(0) ?? 0;

    /// <summary>The engraved layout of a bar and voice, or null when it has not been laid out for drawing yet.</summary>
    internal StaffNotationMeasureLayout? CachedStaffLayout(int measureIndex, int voice) =>
        _staffLayoutCache is not null && measureIndex >= 0 && measureIndex < _staffLayoutCache.GetLength(0) ? _staffLayoutCache[measureIndex, voice] : null;

    /// <summary>Forgets every cached layout and fact after the score content or a layout setting changed.</summary>
    internal void Invalidate()
    {
        SlowTrace.Mark("score relayout requested");
        _scoreLayout = null;
        _scoreGeneration++;
        _scoreFactsGeneration = -1;
        _scoreFactsTrack = null;
        _scoreFactsProject = null;
        _palmMutePassages = Array.Empty<PalmMutePassage>();
        _fadePassages = Array.Empty<FadePassage>();
        DynamicMarks = new Dictionary<TabCell, string>(ReferenceEqualityComparer.Instance);
        _barStateCache = null;
        _barStateComputed = null;
        _markerByMeasure = null;
        _markerCacheProject = null;
        _markerCacheGeneration = -1;
        _staffLayoutCache = null;
        _staffLayoutKeys = null;
        ClearIncrementalCaches();
        _host.LayoutChanged();
    }

    /// <summary>The window that hosted the editor closed for good: forgets the song and every cache that points into it.</summary>
    internal void Release()
    {
        _scoreLayoutTrack = null;
        _scoreLayoutProject = null;
        _warps.Clear();
        Invalidate();
    }

    /// <summary>True for a track whose strings have tunings to list (not drums or keys).</summary>
    internal static bool HasStringTuning(TrackModel? track)
        => track is not null && track.Kind is not TrackKind.Drums and not TrackKind.Keys && track.StringTunings.Count > 0;

    private int SlotsFor(int measure) => _host.Project is { } project ? MusicTime.BarSlots(project, measure) : 16;

    internal ScorePageLayout GetLayout(TrackModel? track = null)
    {
        track ??= _host.Track;
        if (track is null) return ScorePageLayout.Create(_host.GridLeft, _host.GridWidth, Array.Empty<double>());
        if (_scoreLayout is not null && ReferenceEquals(_scoreLayoutTrack, track) &&
            ReferenceEquals(_scoreLayoutProject, _host.Project) && _scoreLayoutHorizontal == _host.HorizontalScroll &&
            (_host.HorizontalScroll || Math.Abs(_scoreLayoutGridWidth - _host.GridWidth) < 0.1) &&
            Math.Abs(_scoreLayoutSpacing - _host.Appearance.ScoreSpacing) < 0.001 &&
            Math.Abs(_scoreLayoutSystemSpacing - _host.Appearance.SystemVerticalSpacing) < 0.001 &&
            Math.Abs(_scoreLayoutMeasureSpacing - _host.Appearance.MeasureHorizontalSpacing) < 0.001)
            return _scoreLayout;

#if DEBUG
        using var performance = RenderPerformance.Measure(RenderPerformance.PerformanceCategory.ScoreLayout);
#endif
        using var relayoutTrace = SlowTrace.Measure("score relayout", 0);
        var extraBefore = (_extraAbove, _extraBelow, _extraTabBelow);
        var palmMutePassages = _host.Project is null
            ? Array.Empty<PalmMutePassage>()
            : EnsureScoreFacts(track, _host.Project).PalmMutePassages;
        var widths = MeasureNaturalWidths(track, palmMutePassages);
        if ((_extraAbove, _extraBelow, _extraTabBelow) != extraBefore) _host.LayoutChanged();   // the system height follows the content
        var forceLineBreaks = new bool[track.Measures.Count];
        var preventLineBreaks = new bool[track.Measures.Count];
        for (var measure = 0; measure < track.Measures.Count; measure++)
        {
            forceLineBreaks[measure] = track.Measures[measure].ForceLineBreak;
            preventLineBreaks[measure] = track.Measures[measure].PreventLineBreak;
        }
        if (_host.HorizontalScroll)
        {
            // One line: no width limit and no line breaks; the page is then sized to fit that line.
            _scoreLayout = ScorePageLayout.Create(_host.GridLeft, 1e7, widths);
            var line = _scoreLayout.Systems.FirstOrDefault();
            var lineRight = line is null || line.Measures.Count == 0 ? _host.GridLeft + 200
                : line.Measures[^1].X + line.Measures[^1].Width;
            HorizontalPageWidth = lineRight + PagePad;
        }
        else
            _scoreLayout = ScorePageLayout.Create(_host.GridLeft, _host.GridWidth, widths,
                forceLineBreaks, preventLineBreaks, centerRows: _host.Appearance.CenterSystems);
        _scoreLayoutHorizontal = _host.HorizontalScroll;
        _scoreLayoutTrack = track;
        _scoreLayoutProject = _host.Project;
        // (horizontal mode sizes the page from the line, so it never compares the grid width)
        _scoreLayoutGridWidth = _host.GridWidth;
        _scoreLayoutSpacing = _host.Appearance.ScoreSpacing;
        _scoreLayoutSystemSpacing = _host.Appearance.SystemVerticalSpacing;
        _scoreLayoutMeasureSpacing = _host.Appearance.MeasureHorizontalSpacing;
        SaveIncrementalWidths(track, widths, forceLineBreaks, preventLineBreaks);
        SlowTrace.Mark($"score relayout {(_incremental.LastRelayoutWasPartial ? "partial" : "full")}: measured {_incremental.LastNaturalMeasureCount}/{track.Measures.Count} bars");
        return _scoreLayout;
    }

    private void BeginMarkExtentScan() { _scanTop = 0; _scanBottom = 4 * StaffNotationRenderer.StaffGap; _scanRows = 0; _scanVolta = false; _scanTabBelow = 0; }

    private void EndMarkExtentScan()
    {
        // Above: the ink over the staff plus the rows its marks stack into (the first row fits the default margin); below: how far low notes reach under the staff.
        var above = Math.Max(0, -_scanTop) + Math.Max(0, _scanRows * 11 - 14) + (_scanVolta ? 14 : 0);
        var below = Math.Max(0, _scanBottom - 4 * StaffNotationRenderer.StaffGap);
        var newAbove = above > 0 ? Math.Ceiling(above) : 0;
        var newBelow = below > 0 ? Math.Ceiling(below + 6) : 0;
        _extraTabBelow = _scanTabBelow > 28 ? Math.Ceiling(_scanTabBelow) : 0;   // lyric rows (and the fingering above them) under the TAB
        if (Math.Abs(newAbove - _extraAbove) > 0.5 || Math.Abs(newBelow - _extraBelow) > 0.5) { _extraAbove = newAbove; _extraBelow = newBelow; }
    }

    /// <summary>Accumulates how far this bar's notation and marks reach above and below the staff (layout time, no drawing).</summary>
    private void ScanMarkExtents(StaffNotationMeasureLayout layout, MeasureModel measure)
    {
        foreach (var beat in layout.Beats)
        {
            if (beat.IsRest) continue;
            foreach (var n in beat.Notes)
            {
                _scanTop = Math.Min(_scanTop, n.Y - 6);
                _scanBottom = Math.Max(_scanBottom, n.Y + 6);
                _incremental.MeasureScanTop = Math.Min(_incremental.MeasureScanTop, n.Y - 6);
                _incremental.MeasureScanBottom = Math.Max(_incremental.MeasureScanBottom, n.Y + 6);
            }
            if (beat.IsDrum)
                foreach (var y in StaffNotationRenderer.DrumHeadYs(beat)) { _scanTop = Math.Min(_scanTop, y - 7); _scanBottom = Math.Max(_scanBottom, y + 7); _incremental.MeasureScanTop = Math.Min(_incremental.MeasureScanTop, y - 7); _incremental.MeasureScanBottom = Math.Max(_incremental.MeasureScanBottom, y + 7); }
            if (beat.HasStem)
            {
                var top = Math.Min(beat.StemStartY, beat.StemEndY) - (beat.Flags > 0 ? 2 : 0);
                var bottom = Math.Max(beat.StemStartY, beat.StemEndY) + 2;
                _scanTop = Math.Min(_scanTop, top);
                _scanBottom = Math.Max(_scanBottom, bottom);
                _incremental.MeasureScanTop = Math.Min(_incremental.MeasureScanTop, top);
                _incremental.MeasureScanBottom = Math.Max(_incremental.MeasureScanBottom, bottom);
            }
            if (beat.LowerStemTopY is not null) { _scanBottom = Math.Max(_scanBottom, beat.LowerStemEndY + 2); _incremental.MeasureScanBottom = Math.Max(_incremental.MeasureScanBottom, beat.LowerStemEndY + 2); }
            foreach (var grace in beat.GraceNotes) { _scanTop = Math.Min(_scanTop, grace.Y - 18); _incremental.MeasureScanTop = Math.Min(_incremental.MeasureScanTop, grace.Y - 18); }
        }
        // Voice 2's marks go below voice 1's at the same beat (the drawing offsets them by voice 1's extent - 8).
        var voiceOneMax = layout.IsSecondVoice ? measure.Cells.Select(c => ScoreMarkText.FingeringExtent(c) > 0 ? ScoreMarkText.FingeringExtent(c) - 8 : 0).DefaultIfEmpty(0).Max() : 0;
        foreach (var b in layout.Beats) { var ext = ScoreMarkText.FingeringExtent(b.Cell); if (ext > 0) { _scanTabBelow = Math.Max(_scanTabBelow, ext + voiceOneMax + 2); _incremental.MeasureScanTabBelow = Math.Max(_incremental.MeasureScanTabBelow, ext + voiceOneMax + 2); } }   // finger rings / letters / harmonic values reach this far under the strings
        var lyricLines = 0; var fingering = 0.0;
        foreach (var b in layout.Beats)
        {
            if (string.IsNullOrWhiteSpace(b.Cell.Lyrics)) continue;
            lyricLines = Math.Max(lyricLines, Math.Min(3, b.Cell.Lyrics.Split('\n').Length));
            fingering = Math.Max(fingering, layout.Beats.Max(o => Math.Abs(o.CenterX - b.CenterX) < 60 ? ScoreMarkText.FingeringHeight(o.Cell) : 0));
        }
        if (lyricLines > 0) { _scanTabBelow = Math.Max(_scanTabBelow, 29 + fingering + (lyricLines - 1) * 14.5); _incremental.MeasureScanTabBelow = Math.Max(_incremental.MeasureScanTabBelow, 29 + fingering + (lyricLines - 1) * 14.5); }   // first row 13 px under the strings + fingering, 14.5 px per row, 14 px of text
        var cell = measure;
        var kinds = 0;
        bool Any(Func<TabCell, bool> test) => layout.Beats.Any(b => test(b.Cell));
        if (Any(c => c.Accent != 0)) kinds++;
        if (Any(c => c.Fermata)) kinds++;
        if (Any(c => c.IsTriplet || c.TupletNumerator > 0)) kinds++;
        if (Any(c => c.Notes.Any(n => n.Techniques.Contains("Trill")))) kinds++;
        if (Any(c => c.OctaveShiftSemitones != 0)) kinds++;
        if (Any(c => c.Notes.Any(n => n.Techniques.Contains("WahOpen") || n.Techniques.Contains("WahClose") || n.Techniques.Contains("Tapping") || n.Techniques.Contains("LeftTap")))) kinds++;
        if (Any(c => c.Notes.Any(n => n.Techniques.Contains("Vibrato") || n.Techniques.Contains("WideVibrato")))) kinds++;
        if (Any(c => !string.IsNullOrWhiteSpace(c.ChordName))) kinds++;
        if (Any(c => !string.IsNullOrWhiteSpace(c.Text))) kinds++;
        _scanRows = Math.Max(_scanRows, kinds);
        _incremental.MeasureScanRows = Math.Max(_incremental.MeasureScanRows, kinds);
        if (cell.AlternateEnding > 0 || cell.AlternateEndingMask != 0) { _scanVolta = true; _incremental.MeasureScanVolta = true; }
    }

    // ---- duration-based spacing (one warp per bar, rebuilt when the score changes) ----
    private readonly Dictionary<MeasureModel, (int Generation, int Slots, MeasureWarp Warp)> _warps = new();

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
        static IEnumerable<double> Onsets(List<TabCell> cells)
        {
            var cursor = 0.0;
            for (var i = 0; i < cells.Count; i++)
            {
                var c = cells[i];
                if (c.Notes.Count == 0 && !c.IsRest) continue;
                var start = c.RhythmicPosition ?? Math.Max(i, cursor);
                cursor = Math.Max(cursor, start + MusicTime.CellSlots(c));
                yield return start;
            }
        }
        // Repeat signs get their own room so the first/last notes never touch the dots.
        // The first note always clears the barline (the reference spacing); grace notes before the first beat need extra room.
        var lead = (measure.RepeatStart ? 1.8 : 0) + HeaderLeadWeight(track, measureIndex);
        var firstContent = measure.Cells.FirstOrDefault(c => c.Notes.Count > 0 || c.IsRest);
        if (firstContent is not null && firstContent.Notes.Any(n => n.IsGraceNote) && firstContent.Notes.Any(n => !n.IsGraceNote)) lead += 3.0;
        lead = Math.Max(lead, 1.6);
        // An overfull bar (more music than its time signature) squeezes everything inside the bar,
        // instead of letting the last beats spill into the next bar.
        static double ContentEnd(List<TabCell> cells)
        {
            var cursor = 0.0;
            for (var i = 0; i < cells.Count; i++)
            {
                var c = cells[i];
                if (c.Notes.Count == 0 && !c.IsRest) continue;
                var start = c.RhythmicPosition ?? Math.Max(i, cursor);
                cursor = Math.Max(cursor, start + MusicTime.CellSlots(c));
            }
            return cursor;
        }
        var contentEnd = Math.Max(ContentEnd(measure.Cells), Voice2HasContent(measure) ? ContentEnd(measure.Voice2Cells) : 0);
        var warp = MeasureWarp.Build(Onsets(measure.Cells).Concat(Voice2HasContent(measure) ? Onsets(measure.Voice2Cells) : Enumerable.Empty<double>()),
            contentEnd > slots + 0.05 ? contentEnd : slots,
            leadWeight: lead, trailWeight: measure.RepeatEnd ? 1.4 : 0);
        if (_warps.Count > 4096) _warps.Clear();
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

    internal BarState BarStateFor(int measureIndex)
    {
        var project = _host.Project;
        if (project is null || measureIndex < 0)
            return project is null ? default : MusicTime.AnalyzeBar(project, measureIndex);
        if (_barStateCache is null)
        {
            var count = 0;
            for (var track = 0; track < project.Tracks.Count; track++)
                count = Math.Max(count, project.Tracks[track].Measures.Count);
            _barStateCache = new BarState[count];
            _barStateComputed = new bool[count];
        }
        if (measureIndex >= _barStateCache.Length) return MusicTime.AnalyzeBar(project, measureIndex);
        if (!_barStateComputed![measureIndex])
        {
            _barStateCache[measureIndex] = MusicTime.AnalyzeBar(project, measureIndex);
            _barStateComputed[measureIndex] = true;
        }
        return _barStateCache[measureIndex];
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
        var layout = _staff.CreateLayout(track, measure, measureIndex, slots, x, staffTop, slotWidth,
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
