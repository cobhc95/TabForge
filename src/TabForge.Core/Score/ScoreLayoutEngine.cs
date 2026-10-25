using System.Globalization;
using System.Linq;
using TabForge.Diagnostics;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Views.Score;

// Owns: the page layout pass: bar widths, packing into system rows, and the host interface the layout reads (IScoreLayoutHost).
// Does not own: the incremental pass (ScoreLayoutEngine.Incremental.cs), the natural measure widths (ScoreLayoutEngine.MeasureWidths.cs) or the key, time and tempo widths (ScoreLayoutEngine.Signatures.cs).
// Tests: no dedicated test class.
// Length: over 300 lines because the full layout pass is one ordered sequence (bar widths, packing, system rows) over the engine shared state.

/// <summary>What the layout engine reads from the editor that owns it.</summary>
internal interface IScoreLayoutHost
{
    SongProject? Project { get; }
    TrackModel? Track { get; }
    NotationMode Notation { get; }
    IScoreLayoutSettings Appearance { get; }
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
    private readonly IScoreTextWidths _text;
    private readonly ScoreLayoutIncrementalState _incremental = new();

    private ScorePageLayout? _scoreLayout;
    private TrackModel? _scoreLayoutTrack;
    private SongProject? _scoreLayoutProject;
    private double[]? _scoreLayoutFloor;
    private double[]? _minBarWidths;
    /// <summary>Bar widths the layout may not go under (layout units, by bar), or null; the Band view lanes share one set so a bar sits at the same place in every lane. A new array lays the score out again.</summary>
    internal double[]? MinBarWidths { get => _minBarWidths; set { if (SameWidths(_minBarWidths, value)) return; _minBarWidths = value; Invalidate(); } }

    // P0: ShareBars builds a fresh array every pass; a reference check alone re-laid out (and re-fired LayoutUpdated) forever.
    // Same values (WPF-visible behaviour identical) skip the layout so the retry converges.
    private static bool SameWidths(double[]? a, double[]? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null || a.Length != b.Length) return false;
        for (var i = 0; i < a.Length; i++) if (Math.Abs(a[i] - b[i]) > 1e-9) return false;
        return true;
    }
    private bool _justifyAllSystems;
    /// <summary>True while the page width is reflowed to a viewer (the continuous view): every system fills the page width, the final one included.</summary>
    internal bool JustifyAllSystems { get => _justifyAllSystems; set { if (_justifyAllSystems == value) return; _justifyAllSystems = value; Invalidate(); } }
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
    private Dictionary<(TrackModel Track, int Bar), BarState>? _barStateCache;
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
    internal ScoreLayoutEngine(IScoreLayoutHost host, IScoreTextWidths text)
    {
        _host = host;
        _text = text;
    }

    /// <summary>The beats that carry a dynamics marking (empty when dynamics are hidden).</summary>
    internal Dictionary<TabCell, string> DynamicMarks { get; private set; } = new(ReferenceEqualityComparer.Instance);

    /// <summary>Extra room above the staff, below the staff and below the tab that the marks of the track need.</summary>
    internal double ExtraAbove => _extraAbove;
    internal double ExtraBelow => _extraBelow;
    internal double ExtraTabBelow => _extraTabBelow;

    /// <summary>
    /// The room above a tab-only line that bars <paramref name="first"/> to <paramref name="last"/> need for their own marks (the rows the marks stack into and the volta),
    /// never more than <see cref="ExtraAbove"/>, which covers every bar of the track. The whole <see cref="ExtraAbove"/> before the bars are laid out.
    /// </summary>
    internal double ExtraAboveNeeded(int first, int last)
    {
        if (_incremental.MeasureExtents is not { } extents) return _extraAbove;
        var rows = 0; var volta = false;
        for (var bar = Math.Max(0, first); bar <= last && bar < extents.Length; bar++) { rows = Math.Max(rows, extents[bar].Rows); volta |= extents[bar].Volta; }
        return Math.Min(_extraAbove, Math.Ceiling(Math.Max(0.0, rows * 11 - 14) + (volta ? 14 : 0)));
    }

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

    /// <summary>The bars' own widths of the last layout, before any shared floor (null before the first layout).</summary>
    internal double[]? NaturalWidths => _incremental.NaturalMeasureWidths;

    internal ScorePageLayout GetLayout(TrackModel? track = null)
    {
        track ??= _host.Track;
        if (track is null) return ScorePageLayout.Create(_host.GridLeft, _host.GridWidth, Array.Empty<double>());
        if (_scoreLayout is not null && ReferenceEquals(_scoreLayoutTrack, track) &&
            ReferenceEquals(_scoreLayoutProject, _host.Project) && _scoreLayoutHorizontal == _host.HorizontalScroll && ReferenceEquals(_scoreLayoutFloor, MinBarWidths) &&
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
        var natural = widths;
        var floor = MinBarWidths;
        if (floor is not null) widths = widths.Select((w, i) => i < floor.Length ? Math.Max(w, floor[i]) : w).ToArray();
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
                forceLineBreaks, preventLineBreaks, justifyAllSystems: _justifyAllSystems);
        _scoreLayoutHorizontal = _host.HorizontalScroll;
        _scoreLayoutTrack = track;
        _scoreLayoutProject = _host.Project;
        _scoreLayoutFloor = floor;
        // (horizontal mode sizes the page from the line, so it never compares the grid width)
        _scoreLayoutGridWidth = _host.GridWidth;
        _scoreLayoutSpacing = _host.Appearance.ScoreSpacing;
        _scoreLayoutSystemSpacing = _host.Appearance.SystemVerticalSpacing;
        _scoreLayoutMeasureSpacing = _host.Appearance.MeasureHorizontalSpacing;
        SaveIncrementalWidths(track, natural, forceLineBreaks, preventLineBreaks);
        SlowTrace.Mark($"score relayout {(_incremental.LastRelayoutWasPartial ? "partial" : "full")}: measured {_incremental.LastNaturalMeasureCount}/{track.Measures.Count} bars");
        return _scoreLayout;
    }

    private void BeginMarkExtentScan() { _scanTop = 0; _scanBottom = 4 * StaffNotationGeometry.StaffGap; _scanRows = 0; _scanVolta = false; _scanTabBelow = 0; }

    private void EndMarkExtentScan()
    {
        // Above: the ink over the staff plus the rows its marks stack into (the first row fits the default margin); below: how far low notes reach under the staff.
        var above = Math.Max(0, -_scanTop) + Math.Max(0, _scanRows * 11 - 14) + (_scanVolta ? 14 : 0);
        // Tab only draws no staff, so nothing reaches under one; the lyric rows under the TAB are counted by _extraTabBelow.
        var below = _host.Notation == NotationMode.TabOnly ? 0 : Math.Max(0, _scanBottom - 4 * StaffNotationGeometry.StaffGap);
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
                foreach (var y in StaffNotationGeometry.DrumHeadYs(beat)) { _scanTop = Math.Min(_scanTop, y - 7); _scanBottom = Math.Max(_scanBottom, y + 7); _incremental.MeasureScanTop = Math.Min(_incremental.MeasureScanTop, y - 7); _incremental.MeasureScanBottom = Math.Max(_incremental.MeasureScanBottom, y + 7); }
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
        if (_host.Notation == NotationMode.TabAndStaff)
        {
            // Between the staff and the TAB each effect takes its own row (P.M., vibrato, technique label, let ring; a dynamic two):
            // past three rows the gap grows, so the rows stack instead of overprinting.
            var lanes = (Any(c => c.Notes.Any(ScoreMarkText.ShowsPalmMute)) ? 1 : 0)
                + (Any(c => c.Notes.Any(n => n.Techniques.Contains("Vibrato") || n.Techniques.Contains("WideVibrato"))) ? 1 : 0)
                + (Any(c => ScoreMarkText.DrawnTechniqueLabel(c.Notes).Length > 0) ? 1 : 0)
                + (Any(c => c.Notes.Any(n => n.Techniques.Contains("LetRing"))) ? 1 : 0)
                + (Any(DynamicMarks.ContainsKey) ? 2 : 0);
            var laneBottom = 4 * StaffNotationGeometry.StaffGap + Math.Max(0, lanes - 3) * 13 - 6;
            _scanBottom = Math.Max(_scanBottom, laneBottom);
            _incremental.MeasureScanBottom = Math.Max(_incremental.MeasureScanBottom, laneBottom);
        }
    }

    // ---- duration-based spacing (one warp per bar, rebuilt when the score changes) ----
    private readonly WarpCache _warps = new();

    /// <summary>
    /// Bar warps by bar, least recently used out first. Key: the bar (by reference); ceiling: <see cref="Capacity"/> entries; eviction: the entry
    /// untouched longest goes when one more arrives (a long score keeps the bars it is laying out instead of dropping the whole cache at once).
    /// Threads: layout runs on the UI thread only (nothing lays out off it); each call also takes a lock, so a stray background caller cannot corrupt the map and list.
    /// </summary>
    internal sealed class WarpCache
    {
        internal const int Capacity = 32768;
        private readonly Dictionary<MeasureModel, LinkedListNode<(MeasureModel Key, (int Generation, int Slots, MeasureWarp Warp) Value)>> _map = new();
        private readonly LinkedList<(MeasureModel Key, (int Generation, int Slots, MeasureWarp Warp) Value)> _order = new();

        public int Count { get { lock (_order) return _map.Count; } }

        public bool TryGetValue(MeasureModel measure, out (int Generation, int Slots, MeasureWarp Warp) value)
        {
            lock (_order)
            {
                if (_map.TryGetValue(measure, out var node))
                {
                    _order.Remove(node); _order.AddFirst(node);
                    value = node.Value.Value;
                    return true;
                }
            }
            value = default;
            return false;
        }

        public (int Generation, int Slots, MeasureWarp Warp) this[MeasureModel measure]
        {
            set { lock (_order) Store(measure, value); }
        }

        private void Store(MeasureModel measure, (int Generation, int Slots, MeasureWarp Warp) value)
        {
            if (_map.TryGetValue(measure, out var node))
            {
                node.Value = (measure, value);
                _order.Remove(node); _order.AddFirst(node);
                return;
            }
            if (_map.Count >= Capacity)
            {
                var oldest = _order.Last!;
                _order.RemoveLast();
                _map.Remove(oldest.Value.Key);
            }
            _map[measure] = _order.AddFirst((measure, value));
        }

        public void Clear() { lock (_order) { _map.Clear(); _order.Clear(); } }
    }

}
