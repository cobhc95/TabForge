using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TabForge.Diagnostics;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Services;
using RenderDraw = TabForge.Visualization.Draw;

namespace TabForge.Views;

// TabEditorControl: OnRender and the drawing helpers.
public sealed partial class TabEditorControl
{
    // ================= rendering =================

    private Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    protected override void OnRender(DrawingContext dc)
    {
#if DEBUG
        using var performance = RenderPerformance.Measure(RenderPerformance.PerformanceCategory.Score);
#endif
        using var dpiScope = TabForge.Visualization.Draw.UseDpi(this);   // A-03: text shaped for this editor's own monitor
        base.OnRender(dc);
        dc.PushTransform(new ScaleTransform(_zoom, _zoom));
        var bg = DarkPaper ? DarkPaperColor : LightPaperColor;
        var ink = DarkPaper ? DarkInkColor : LightInkColor;
        _normalInk = ink;
        var faint = DarkPaper ? C("#5A636F") : C("#8A8A8A");
        // Staff and string lines should guide the eye without cutting through noteheads or labels.
        var line = DarkPaper ? DarkStaffLineColor : LightStaffLineColor;
        var accent = AccentColor;
        var cursorColor = CursorColor;
        var playColor = PlaybackColor;
        var errorColor = C("#E5484D");

        dc.DrawRectangle(Brush(bg), null, new Rect(0, 0, PageWidth, Math.Max(400, ActualHeight / _zoom)));

        var track = Track;
        if (track is null || _project is null)
        {
            DrawCenteredIn(ScoreTextArea.Header, dc, "TabForge", HeaderCentreX, 60, 30, Brush(ink), FontWeights.SemiBold, "Segoe UI");
            DrawCenteredIn(ScoreTextArea.Header, dc, "Create or open a score", HeaderCentreX, 104, 14, Brush(faint));
            dc.Pop();
            return;
        }

        DrawCenteredIn(ScoreTextArea.Header, dc, _project.Title, HeaderCentreX, 8, 22, Brush(ink), FontWeights.SemiBold, "Segoe UI");
        if (!string.IsNullOrWhiteSpace(_project.Artist))
            DrawCenteredIn(ScoreTextArea.Header, dc, _project.Artist, HeaderCentreX, 34, 12, Brush(faint));
        var wordsBy = string.IsNullOrWhiteSpace(_project.LyricsAuthor) ? "" : $"Words by {_project.LyricsAuthor}";
        var musicBy = string.IsNullOrWhiteSpace(_project.MusicAuthor) ? "" : $"Music by {_project.MusicAuthor}";
        if (wordsBy.Length > 0) DrawIn(ScoreTextArea.Header, dc, wordsBy, GridLeft, 51, 9, Brush(faint));
        if (musicBy.Length > 0)
        {
            var text = MakeTextIn(ScoreTextArea.Header, musicBy, 9, Brush(faint));
            DrawIn(ScoreTextArea.Header, dc, musicBy, GridLeft + GridWidth - text.Width, 51, 9, Brush(faint));
        }
        DrawTuningBlock(dc, track, faint);

        var scoreLayout = GetScoreLayout(track);
        var systems = scoreLayout.SystemCount;
        // One lookup per render for the exact sounding notes.
        RebuildSoundingSets();
        var scoreFacts = EnsureScoreFacts(track, _project);
        var palmMutePassages = scoreFacts.PalmMutePassages;
        var fadePassages = scoreFacts.FadePassages;
        // Engrave only the systems the viewport can show: a long score is dozens of systems, and
        // repainting all of them on every playback tick was the single biggest CPU cost.
        var (firstSystem, lastSystem) = VisibleSystems(systems);
        UpdateHorizontalBand();
        _drawnFirstSystem = firstSystem;
        _drawnLastSystem = lastSystem;
        if (!ReferenceEquals(_systemDrawingsLayout, scoreLayout) || _systemDrawingsDpi != TabForge.Visualization.Draw.DpiKey)
        {
            _systemDrawings.Clear();
            _systemDrawingsLayout = scoreLayout;
            _systemDrawingsDpi = TabForge.Visualization.Draw.DpiKey;
        }
        var activeSystem = PlaybackMeasure >= 0 && PlaybackMeasure < track.Measures.Count ? scoreLayout.SystemForMeasure(PlaybackMeasure) : -1;
        for (var s = firstSystem; s <= lastSystem; s++)
        {
            var system = scoreLayout.Systems[s];
            // One-line mode draws only the visible band live (cheap); a cached drawing would hold a stale band.
            if (s == activeSystem || HorizontalScroll)
            {
                // Reads the continuous playback position (progress fill): always engraved live.
                DrawSystem(dc, track, system, ink, faint, line, accent, cursorColor, playColor, errorColor, palmMutePassages, fadePassages);
                _systemDrawings.Remove(s);
                continue;
            }
            var signature = SystemPlaybackSignature(system);
            if (!_systemDrawings.TryGetValue(s, out var cached) || cached.Signature != signature)
            {
                var group = new DrawingGroup();
                using (var recorder = group.Open())
                    DrawSystem(recorder, track, system, ink, faint, line, accent, cursorColor, playColor, errorColor, palmMutePassages, fadePassages);
                group.Freeze();
                cached = (signature, group);
                _systemDrawings[s] = cached;
            }
            dc.DrawDrawing(cached.Drawing);
        }
        // Forget systems that scrolled well out of view so the cache stays small.
        if (_systemDrawings.Count > 24)
            foreach (var key in _systemDrawings.Keys.Where(k => k < firstSystem - 2 || k > lastSystem + 2).ToList())
                _systemDrawings.Remove(key);
        dc.Pop();
    }

    private ScrollViewer? _viewport;
    private bool _viewportHooked;
    private int _drawnFirstSystem;
    private int _drawnLastSystem = -1;

    /// <summary>The inclusive range of systems overlapping the viewport, with one system of margin.</summary>
    private (int First, int Last) VisibleSystems(int systems)
    {
        var last = systems - 1;
        if (_viewport is null)
        {
            _viewport = FindAncestorScrollViewer(this);
            if (_viewport is not null && !_viewportHooked)
            {
                _viewportHooked = true;
                // Culling only works if a scroll re-runs the render, but scrolling inside the band we
                // already drew needs no repaint at all (WPF just translates the cached visuals) - so
                // only invalidate when systems outside the drawn band come into view.
                _viewport.ScrollChanged += (_, _) =>
                {
                    var (first, final) = VisibleSystems(systems);
                    if (first < _drawnFirstSystem || final > _drawnLastSystem || HorizontalBandLeftBehind()) InvalidateVisual();
                };
            }
        }
        if (_viewport is null || _viewport.ViewportHeight <= 1)
            return (0, last);

        var top = _viewport.VerticalOffset / _zoom - SystemHeight;
        var bottom = (_viewport.VerticalOffset + _viewport.ViewportHeight) / _zoom + SystemHeight;
        var first = (int)Math.Floor((top - HeaderHeight) / SystemHeight);
        var final = (int)Math.Ceiling((bottom - HeaderHeight) / SystemHeight);
        return (Math.Max(0, first), Math.Min(last, Math.Max(0, final)));
    }

    // ---- one-line (horizontal) culling: draw about one screen either side of the view, repaint only
    // when the view scrolls past that band (the same idea as the vertical system culling above).
    private double _bandLeft = double.NegativeInfinity, _bandRight = double.PositiveInfinity;

    private void UpdateHorizontalBand()
    {
        if (!HorizontalScroll || _viewport is null || _viewport.ViewportWidth <= 1)
        {
            _bandLeft = double.NegativeInfinity; _bandRight = double.PositiveInfinity;
            return;
        }
        var view = _viewport.ViewportWidth / _zoom;
        _bandLeft = _viewport.HorizontalOffset / _zoom - view;
        _bandRight = (_viewport.HorizontalOffset + _viewport.ViewportWidth) / _zoom + view;
    }

    private bool InHorizontalBand(ScoreMeasurePosition measure) =>
        measure.X + measure.Width >= _bandLeft && measure.X <= _bandRight;

    private bool HorizontalBandLeftBehind()
    {
        if (!HorizontalScroll || _viewport is null) return false;
        var left = _viewport.HorizontalOffset / _zoom;
        var right = (_viewport.HorizontalOffset + _viewport.ViewportWidth) / _zoom;
        return left < _bandLeft || right > _bandRight;
    }

    private static ScrollViewer? FindAncestorScrollViewer(DependencyObject start)
    {
        var current = VisualTreeHelper.GetParent(start);
        while (current is not null)
        {
            if (current is ScrollViewer viewer) return viewer;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private void DrawTuningBlock(DrawingContext dc, TrackModel track, Color faint)
    {
        if (!HasStringTuning(track)) return;
        var tuning = track.StringTunings;
        var rowCount = Math.Max(1, (tuning.Count + 1) / 2);
        DrawIn(ScoreTextArea.Header, dc, TuningName(tuning), GridLeft, 65, 9, TabEditorControl.Brush(faint), FontWeights.SemiBold);
        for (var stringIndex = 0; stringIndex < tuning.Count; stringIndex++)
        {
            var column = stringIndex / rowCount;
            var row = stringIndex % rowCount;
            DrawIn(ScoreTextArea.Header, dc, $"{stringIndex + 1} = {MusicTheoryService.NoteName(tuning[stringIndex])}",
                GridLeft + column * 72, 77 + row * 11, 9, TabEditorControl.Brush(faint));
        }
    }

    private static string TuningName(IReadOnlyList<int> tuning)
    {
        if (tuning.SequenceEqual(new[] { 62, 57, 53, 48, 43, 36 })) return "Dropped C Tuning";
        if (tuning.SequenceEqual(new[] { 64, 59, 55, 50, 45, 38 })) return "Drop D Tuning";
        if (tuning.SequenceEqual(new[] { 64, 59, 55, 50, 45, 40 })) return "Standard Tuning";
        return "Tuning";
    }

    // Frozen brushes are shared, so a repaint reuses them instead of allocating a brush per glyph.
    // The cache is bounded by the number of distinct colours in the palette (a few dozen).
    internal static SolidColorBrush Brush(Color colour) => (SolidColorBrush)RenderDraw.Solid(colour);

    // ---- playback sounding-note lookup (canonical timeline, binary search) ----
    private HashSet<(int bar, int cell, int s)> _soundingNow = new();
    private HashSet<(int bar, int cell, int s)> _struckNow = new();
    private readonly StaffNotationRenderer _staff = new();

    private void RebuildSoundingSets()
    {
        _soundingNow.Clear();
        _struckNow.Clear();
        var timeline = Timeline;
        if (timeline is null || PlaybackMs <= 0) return;
        var notes = timeline.NotesFor(PlaybackTrackIndex);
        if (notes.Length == 0) return;

        // Shared with the fretboard (NoteTimeline) so the score and the fretboard always agree.
        foreach (var n in NoteTimeline.SoundingAt(notes, PlaybackMs))
            _soundingNow.Add((MapPlaybackBar(n.Bar), n.Cell, n.StringIndex));
        foreach (var n in NoteTimeline.StruckWithin(notes, PlaybackMs, 130))
            _struckNow.Add((MapPlaybackBar(n.Bar), n.Cell, n.StringIndex));
    }

    private int MapPlaybackBar(int sourceBar)
    {
        var remap = PlaybackBarRemap;
        return remap is not null && sourceBar >= 0 && sourceBar < remap.Length ? remap[sourceBar] : sourceBar;
    }

    /// <summary>
    /// True when the sounding-note overlay must be repainted between two musical times, i.e. a note
    /// starts or ends. This keeps the highlight exactly as long as the note sounds without repainting
    /// the whole score page on every tick.
    /// </summary>
    public bool PlaybackNeedsRepaint(double fromMs, double toMs)
    {
        var notes = Timeline?.NotesFor(PlaybackTrackIndex);
        if (notes is null) return true;
        return NoteTimeline.AnyBoundaryBetween(notes, fromMs, toMs);
    }

    private void DrawSystem(DrawingContext dc, TrackModel track, ScoreSystemPosition systemLayout, Color ink, Color faint, Color line, Color accent, Color cursorColor, Color playColor, Color errorColor,
        IReadOnlyList<PalmMutePassage> palmMutePassages, IReadOnlyList<FadePassage> fadePassages)
    {
        var system = systemLayout.Index;
        var staffTop = StaffTop(system);
        var tabTop = TabTop(system);
        var strings = Math.Max(1, track.StringTunings.Count);
        var showStaff = Notation != NotationMode.TabOnly;
        var showTab = Notation != NotationMode.StaffOnly;
        var thin = RenderDraw.Pen(line, 1.0);
        var thick = RenderDraw.Pen(ink, 1.4);
        var systemRight = systemLayout.X + systemLayout.Width;

        if (showStaff) for (var l = 0; l < 5; l++) dc.DrawLine(thin, new Point(systemLayout.X, staffTop + l * StaffGap), new Point(systemRight, staffTop + l * StaffGap));
        if (showTab) for (var s = 0; s < strings; s++) dc.DrawLine(thin, new Point(systemLayout.X, tabTop + s * StringGap), new Point(systemRight, tabTop + s * StringGap));

        if (showStaff) Draw(dc, "𝄞", GridLeft - 26, staffTop - 6, 22, Brush(ink));
        if (showTab)
        {
            if (track.Kind == TrackKind.Drums && DrumMaps.LineNames(track.DrumMapPreset) is { } lineNames)
            {
                // Drum-tab preset: name each line (CC, HH, SD, T1, T2, FT, BD) instead of T-A-B.
                for (var li = 0; li < lineNames.Length; li++)
                    Draw(dc, lineNames[li], GridLeft - 24, tabTop + li * StringGap - 6, 9, Brush(faint), FontWeights.Bold);
            }
            else
            {
                Draw(dc, "T", GridLeft - 22, tabTop + 1, 13, Brush(faint), FontWeights.Bold);
                Draw(dc, "A", GridLeft - 22, tabTop + 17, 13, Brush(faint), FontWeights.Bold);
                Draw(dc, "B", GridLeft - 22, tabTop + 33, 13, Brush(faint), FontWeights.Bold);
            }
        }

        foreach (var measurePosition in systemLayout.Measures)
        {
            if (!InHorizontalBand(measurePosition)) continue;
            var measureIndex = measurePosition.MeasureIndex;
            var state = BarStateFor(measureIndex);
            var barPen = state is { Error: true } ? RenderDraw.Pen(errorColor, 1.6) : thick;

            // Snap vertical bar lines to the pixel grid so they render as crisp 1 px lines.
            var barX = Math.Round(measurePosition.X) + 0.5;
            if (showStaff) dc.DrawLine(barPen, new Point(barX, staffTop - 4), new Point(barX, staffTop + 4 * StaffGap + 4));
            if (showTab) dc.DrawLine(barPen, new Point(barX, tabTop - 4), new Point(barX, tabTop + (strings - 1) * StringGap + 4));
            if (track.Measures[measureIndex].IsDoubleBar)
            {
                var doubleX = Math.Round(measurePosition.X + measurePosition.Width) + 3.5;
                if (showStaff) dc.DrawLine(thick, new Point(doubleX, staffTop - 4), new Point(doubleX, staffTop + 4 * StaffGap + 4));
                if (showTab) dc.DrawLine(thick, new Point(doubleX, tabTop - 4), new Point(doubleX, tabTop + (strings - 1) * StringGap + 4));
            }
        }
        var finalBarX = Math.Round(systemRight) + 0.5;
        // The song's last bar ends on a thin + thick double line; other systems end on one line.
        var endsSong = systemLayout.Measures.Count > 0 && systemLayout.LastMeasure == track.Measures.Count - 1;
        var finalPen = endsSong ? RenderDraw.Pen(ink, 3.0) : thick;
        var finalThin = RenderDraw.Pen(ink, 1.0);
        if (showStaff)
        {
            dc.DrawLine(finalPen, new Point(finalBarX, staffTop - 4), new Point(finalBarX, staffTop + 4 * StaffGap + 4));
            if (endsSong) dc.DrawLine(finalThin, new Point(finalBarX - 5, staffTop - 4), new Point(finalBarX - 5, staffTop + 4 * StaffGap + 4));
        }
        if (showTab)
        {
            dc.DrawLine(finalPen, new Point(finalBarX, tabTop - 4), new Point(finalBarX, tabTop + (strings - 1) * StringGap + 4));
            if (endsSong) dc.DrawLine(finalThin, new Point(finalBarX - 5, tabTop - 4), new Point(finalBarX - 5, tabTop + (strings - 1) * StringGap + 4));
        }

        foreach (var measurePosition in systemLayout.Measures)
        {
            if (!InHorizontalBand(measurePosition)) continue;
            var measureIndex = measurePosition.MeasureIndex;
            var measure = track.Measures[measureIndex];
            var editingCells = CellsFor(measure);
            var x = measurePosition.X;
            var measureWidth = measurePosition.Width;
            var slots = SlotsFor(measureIndex);
            var slotWidth = measureWidth / Math.Max(1, slots);
            var warp = WarpFor(track, measureIndex);
            double SlotX(double s) => x + warp.Fraction(s) * measureWidth;

            // Titles, endings and beat text ride above the tallest stem of the bar (drum chords have long stems).
            _barLift = showStaff ? BarLift(track, measure, measureIndex, x, staffTop, slotWidth, slots) : 0;
            DrawBarAnnotations(dc, track, measure, measureIndex, x, measureWidth, staffTop, tabTop, strings, ink, faint, accent, _barLift);

            // standard repeat barlines on both staves: thick line, thin line and two dots (start ||:,
            // end :||), with the play count above the end repeat.
            if (measure.RepeatStart || measure.RepeatEnd)
            {
                var inkBrush = Brush(ink);
                var heavy = RenderDraw.Pen(ink, 3.2);
                var light = RenderDraw.Pen(ink, 1);
                void RepeatSign(double top, double bottom, bool start)
                {
                    var edge = start ? Math.Round(x) + 1.6 : Math.Round(x + measureWidth) - 1.6;
                    var thin = start ? edge + 4.5 : edge - 4.5;
                    var dots = start ? thin + 4.5 : thin - 4.5;
                    dc.DrawLine(heavy, new Point(edge, top), new Point(edge, bottom));
                    dc.DrawLine(light, new Point(thin, top), new Point(thin, bottom));
                    var mid = (top + bottom) / 2; var gap = Math.Max(4, (bottom - top) / 6);
                    dc.DrawEllipse(inkBrush, null, new Point(dots, mid - gap), 1.9, 1.9);
                    dc.DrawEllipse(inkBrush, null, new Point(dots, mid + gap), 1.9, 1.9);
                }
                foreach (var start in new[] { true, false })
                {
                    if (start ? !measure.RepeatStart : !measure.RepeatEnd) continue;
                    if (showStaff) RepeatSign(staffTop, staffTop + 4 * StaffGap, start);
                    if (showTab) RepeatSign(tabTop, tabTop + (strings - 1) * StringGap, start);
                }
                if (measure.RepeatEnd && measure.RepeatCount > 2)
                    DrawIn(ScoreTextArea.BarInfo, dc, $"x{measure.RepeatCount}", x + measureWidth - 22, (showStaff ? staffTop : tabTop) - 16, 10, inkBrush, FontWeights.Bold);
            }

            var isError = BarStateFor(measureIndex).Error;
            if (isError && measureIndex != SelectedMeasure)
            {
                var tint = Brush(Color.FromArgb(38, errorColor.R, errorColor.G, errorColor.B));
                dc.DrawRectangle(tint, null, new Rect(x + 1, staffTop - 6, measureWidth - 2, (showTab ? tabTop + (strings - 1) * StringGap : staffTop + 4 * StaffGap) - staffTop + 18));
            }

            // Selection range overlay
            if (HasSelection)
            {
                var (m1, c1, m2, c2) = SelectionRange();
                if (m2 < m1 || (m2 == m1 && c2 < c1)) (m1, c1, m2, c2) = (m2, c2, m1, c1);
                if (measureIndex >= m1 && measureIndex <= m2 && editingCells.Count > 0)
                {
                    var from = measureIndex == m1 ? c1 : 0;
                    var to = measureIndex == m2 ? c2 : slots - 1;
                    var startSlots = CellStartSlots(measure, from, editingCells);
                    var endCell = editingCells[Math.Clamp(to, 0, editingCells.Count - 1)];
                    var endSlots = Math.Min(slots, CellStartSlots(measure, to, editingCells) + Math.Max(1, MusicTime.CellSlots(endCell)));
                    var sx = SlotX(startSlots);
                    var sw = Math.Max(4, SlotX(endSlots) - sx);
                    var selected = SelectionColor;
                    var selectionAlpha = (byte)Math.Clamp(Math.Round(255 * Math.Clamp(SelectionHighlightIntensity, 0, 1)), 0, 255);
                    var selBrush = Brush(Color.FromArgb(selectionAlpha, selected.R, selected.G, selected.B));
                    dc.DrawRectangle(selBrush, RenderDraw.Pen(selected, 1), new Rect(sx, staffTop - 8, sw, (showTab ? tabTop + (strings - 1) * StringGap : staffTop + 4 * StaffGap) - staffTop + 22));
                }
            }

            // Playback: retain the configurable beat tint, then draw the active note's remaining
            // duration ahead of the independent, lightweight caret overlay.
            if (measureIndex == PlaybackMeasure && PlaybackCell >= 0 && measure.Cells.Count > 0)
            {
                var beatCell = measure.Cells[Math.Clamp(PlaybackCell, 0, measure.Cells.Count - 1)];
                var beatStart = CellStartSlots(measure, PlaybackCell);
                var beatEnd = Math.Min(slots, beatStart + MusicTime.CellSlots(beatCell));
                var px = SlotX(beatStart);
                var beatWidth = Math.Max(0, SlotX(beatEnd) - px);
                var bandBottom = showTab ? tabTop + (strings - 1) * StringGap : staffTop + 4 * StaffGap;
                var bandRect = new Rect(px, staffTop - 8, beatWidth, bandBottom - staffTop + 22);
                if (HighlightPlayedBeat && beatWidth > 0 && DurationGlowOpacity > 0)
                {
                    // The elapsed beat tint stops at the caret; the overlay shades only the
                    // remaining duration. This prevents the two independently rendered layers
                    // from stacking opacity over the same part of the beat.
                    var playedSlots = Math.Clamp(PlaybackFraction * slots, beatStart, beatEnd);
                    var playedWidth = Math.Max(0, SlotX(playedSlots) - px);
                    var tintAlpha = PlaybackGlowIntensity.ScaleAlpha(HighlightBackground.A, DurationGlowOpacity);
                    if (playedWidth > 0 && tintAlpha > 0)
                    {
                        var tint = Color.FromArgb(tintAlpha, HighlightBackground.R, HighlightBackground.G, HighlightBackground.B);
                        dc.DrawRectangle(Brush(tint), null,
                            new Rect(px, bandRect.Y, Math.Min(beatWidth, playedWidth), bandRect.Height));
                    }
                }

            }

            // Hover: a faint outline shows which beat a click would act on, without looking like the
            // edit cursor, the selection or the playhead.
            if (measureIndex == _hoverMeasure && _hoverCell >= 0 && _hoverCell < editingCells.Count && !PlaybackActive)
            {
                var hoverRect = CellHighlightRect(measure, editingCells, _hoverCell, x, slotWidth, warp, measureWidth,
                    staffTop - 4, (showTab ? tabTop + (strings - 1) * StringGap : staffTop + 4 * StaffGap) - staffTop + 12);
                var hoverAlpha = (byte)Math.Clamp(Math.Round(255 * Math.Clamp(HoverHighlightIntensity, 0, 1)), 0, 255);
                dc.DrawRectangle(null, RenderDraw.Pen(Color.FromArgb(hoverAlpha, HoverColor.R, HoverColor.G, HoverColor.B), 1), hoverRect);
            }

            // Cursor (dimmed while the transport runs so the green playhead is the tracker)
            if (measureIndex == SelectedMeasure && !HideCursor)
            {
                var cy = showTab ? tabTop + SelectedString * StringGap - 8 : staffTop;
                var height = showTab ? 16 : 4 * StaffGap;
                var cursorAlpha = PlaybackActive ? (byte)70 : (byte)255;
                var cursorPen = RenderDraw.Pen(Color.FromArgb(cursorAlpha, cursorColor.R, cursorColor.G, cursorColor.B), PlaybackActive ? 1.1 : 1.6);
                dc.DrawRectangle(null, cursorPen,
                    CellHighlightRect(measure, editingCells, SelectedCell, x, slotWidth, warp, measureWidth, cy, height));
            }

            DrawMeasure(dc, track, measure, measureIndex, x, measureWidth, staffTop, tabTop, slotWidth, slots, strings, ink, faint, line, accent, playColor, showStaff, showTab,
                measure.Cells, inactiveVoice: _project?.GrayInactiveVoice == true && _activeVoiceIndex != 0);
            if (Voice2HasContent(measure))
                DrawMeasure(dc, track, measure, measureIndex, x, measureWidth, staffTop, tabTop, slotWidth, slots, strings, ink, faint, line, accent, playColor, showStaff, showTab,
                    measure.Voice2Cells, inactiveVoice: _project?.GrayInactiveVoice == true && _activeVoiceIndex != 1);
        }

        if (showTab || showStaff) DrawPalmMutePassages(dc, palmMutePassages, track, systemLayout, tabTop, ink, showStaff ? staffTop + 4 * StaffGap : null);
        DrawFadePassages(dc, fadePassages, track, systemLayout, staffTop, tabTop, ink, showTab);
    }

    private double _barLift;

    /// <summary>How far the upper text lanes must rise so the bar's tallest stem, beam or grace note stays clear of them.</summary>
    private double BarLift(TrackModel track, MeasureModel measure, int measureIndex, double x, double staffTop, double slotWidth, int slots)
    {
        var numerator = measure.TimeSigNum ?? _project?.TimeSignatureNumerator ?? 4;
        var denominator = measure.TimeSigDenom ?? _project?.TimeSignatureDenominator ?? 4;
        var keySignature = measure.KeySignature ?? _project?.KeySignature ?? 0;
        var top = double.PositiveInfinity;
        foreach (var cells in Voice2HasContent(measure) ? new[] { measure.Cells, measure.Voice2Cells } : new[] { measure.Cells })
        {
            var layout = StaffLayoutFor(track, measure, measureIndex, slots, x, staffTop, slotWidth, numerator, denominator, keySignature, cells);
            foreach (var beat in layout.Beats)
            {
                if (beat.HasStem) top = Math.Min(top, Math.Min(beat.StemStartY, beat.StemEndY) - (beat.Flags > 0 ? 2 : 0));
                foreach (var note in beat.Notes)
                    top = Math.Min(top, note.Y - 6 - (note.Accidental is not null ? 6 : 0));
                if (beat.IsDrum) top = Math.Min(top, beat.MinY - 6);
                foreach (var grace in beat.GraceNotes) top = Math.Min(top, grace.Y - 18);
            }
        }
        // The lowest text lane (tempo / bar number) ends about 12 px above the staff; titles sit above 32 px.
        return double.IsInfinity(top) ? 0 : Math.Clamp((staffTop - 13) - top, 0, 34);
    }

    private void DrawBarAnnotations(DrawingContext dc, TrackModel track, MeasureModel measure, int measureIndex, double x, double measureWidth, double staffTop, double tabTop, int strings, Color ink, Color faint, Color accent, double lift)
    {
        var marker = MarkerForMeasure(measureIndex);
        var sectionLabel = marker?.Title ?? measure.SectionName;
        var sectionColor = marker is not null && ThemeService.TryParse(marker.ColorHex, out var markerColor)
            ? markerColor : accent;
        if (ShowSectionHeadings && !string.IsNullOrWhiteSpace(sectionLabel))
            DrawIn(ScoreTextArea.BarInfo, dc, sectionLabel!, x + 2, staffTop - 44 - lift, 10, Brush(sectionColor), FontWeights.Bold);
        if (ShowBarNumbers && measureIndex % Math.Max(1, BarNumberFrequency) == 0)
            DrawIn(ScoreTextArea.BarInfo, dc, (measureIndex + 1).ToString(), x + 2, staffTop - 26 - lift, 9, Brush(accent));
        if (_project is null) return;
        var showStaffHere = Notation != NotationMode.TabOnly;
        var noteX = x + 28;
        if (KeySignatureChanges(track, measureIndex))
        {
            var key = measure.KeySignature ?? _project.KeySignature;
            if (showStaffHere) DrawKeySignature(dc, measure.Clef, key, PreviousKeySignature(track, measureIndex), noteX, staffTop, ink);
            noteX += KeySignatureWidth(track, measureIndex);
        }
        if (TimeSignatureShown(track, measureIndex) && showStaffHere)
            DrawTimeSignature(dc, measure, noteX, staffTop, ink);
        var tempoText = TempoText(measure, measureIndex);
        if (tempoText is not null) DrawIn(ScoreTextArea.BarInfo, dc, tempoText, x + 22, staffTop - 26 - lift, 9, Brush(accent), FontWeights.Bold);
        if (measure.AlternateEnding > 0 || measure.AlternateEndingMask != 0)
        {
            // The reference volta bracket: a line over the ending's bars with a hook down at its start and the pass numbers inside.
            var bracketY = staffTop - 56 - lift;
            var pen = RenderDraw.Pen(ink, 1.0);
            dc.DrawLine(pen, new Point(x + 1, bracketY), new Point(x + measureWidth - 1, bracketY));
            dc.DrawLine(pen, new Point(x + 1, bracketY), new Point(x + 1, bracketY + 9));
            DrawIn(ScoreTextArea.BarInfo, dc, measure.EndingLabel, x + 4, bracketY + 1, 9, Brush(ink));
        }
        if (!string.IsNullOrWhiteSpace(measure.Directions)) {
            var titleWidth = ShowSectionHeadings && !string.IsNullOrWhiteSpace(sectionLabel)
                ? MakeTextIn(ScoreTextArea.BarInfo, sectionLabel!, 10, Brush(sectionColor), FontWeights.Bold).Width + 10 : 0;
            DrawDirections(dc, measure.Directions, x + 2 + titleWidth, x + measureWidth - 4, staffTop - 44 - lift, ink);
        }
        if (measure.SimileOneBar) DrawCentered(dc, "𝄌", x + measureWidth / 2, staffTop + 6, 16, Brush(ink));
        if (measure.SimileTwoBar) DrawCentered(dc, "𝄌𝄌", x + measureWidth / 2, staffTop + 6, 16, Brush(ink));
        var feel = TripletFeels.Effective(measure);
        if (feel != TripletFeels.None)
        {
            // The reference prints the swing symbol right after the tempo, on the tempo lane.
            var swingX = x + 22 + (tempoText is null ? 0 : MakeTextIn(ScoreTextArea.BarInfo, tempoText, 9, Brush(accent), FontWeights.Bold).Width + 8);
            DrawIn(ScoreTextArea.BarInfo, dc, SwingSymbol(feel), swingX, staffTop - 26 - lift, 9, Brush(faint));
        }
        if (measure.FreeTime) DrawIn(ScoreTextArea.BarInfo, dc, "free", x + measureWidth - 34, staffTop - 10, 8.5, Brush(faint));
    }

    /// <summary>the standard swing indicator, e.g. "(♫ = ♩♪)".</summary>
    internal static string SwingSymbol(string feel) => feel == TripletFeels.Sixteenth ? "(♬ = ♪♬)" : "(♫ = ♩♪)";

    /// <summary>Navigation directions above a bar: the segno / coda signs and the Fine / D.C. / D.S. texts, right-aligned.</summary>
    /// <summary>the standard wording for a direction name from the file: "TargetSegno" is the sign, "JumpDalSegnoAlFine" is "D.S. al Fine".</summary>
    internal static (string Text, bool IsSign, bool AtLeft) DirectionText(string raw)
    {
        var name = raw.Trim();
        if (name.StartsWith("Target", StringComparison.OrdinalIgnoreCase)) name = name[6..];
        else if (name.StartsWith("Jump", StringComparison.OrdinalIgnoreCase)) name = name[4..];
        return name.ToLowerInvariant() switch
        {
            "segno" => ("\U0001D10B", true, true),
            "segnosegno" => ("\U0001D10B\U0001D10B", true, true),
            "coda" => ("\U0001D10C", true, true),
            "doublecoda" => ("\U0001D10C\U0001D10C", true, true),
            "fine" => ("fine", false, true),
            "dacapo" => ("D.C.", false, false),
            "dacapoalcoda" => ("D.C. al Coda", false, false),
            "dacapoaldoublecoda" => ("D.C. al Double Coda", false, false),
            "dacapoalfine" => ("D.C. al Fine", false, false),
            "dalsegno" => ("D.S.", false, false),
            "dalsegnoalcoda" => ("D.S. al Coda", false, false),
            "dalsegnoaldoublecoda" => ("D.S. al Double Coda", false, false),
            "dalsegnoalfine" => ("D.S. al Fine", false, false),
            "dalsegnosegno" => ("D.S.S.", false, false),
            "dalsegnosegnoalcoda" => ("D.S.S. al Coda", false, false),
            "dalsegnosegnoaldoublecoda" => ("D.S.S. al Double Coda", false, false),
            "dalsegnosegnoalfine" => ("D.S.S. al Fine", false, false),
            "dacoda" => ("To Coda", false, false),
            "dadoublecoda" => ("To Double Coda", false, false),
            _ => (raw.Trim(), false, false)
        };
    }

    /// <summary>Navigation directions above a bar: segno / coda signs after the bar's title, Fine / D.C. / D.S. texts right-aligned.</summary>
    private void DrawDirections(DrawingContext dc, string directions, double leftX, double rightX, double y, Color ink)
    {
        var brush = Brush(ink);
        var x = rightX;
        foreach (var raw in directions.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var (text, isSign, atLeft) = DirectionText(raw);
            var ft = MakeTextIn(ScoreTextArea.BarInfo, text, isSign ? 16 : 10, brush, isSign ? FontWeights.Normal : FontWeights.SemiBold);
            if (atLeft)
            {
                TabForge.Visualization.Draw.DrawText(dc, ft, new Point(leftX, y - 4));
                leftX += ft.Width + 6;
            }
            else
            {
                x -= ft.Width;
                TabForge.Visualization.Draw.DrawText(dc, ft, new Point(x, isSign ? y - 4 : y));
                x -= 8;
            }
        }
    }

    private bool KeySignatureChanges(TrackModel track, int measureIndex)
    {
        if (_project is null) return false;
        if (measureIndex <= 0) return true;
        var previous = track.Measures[measureIndex - 1];
        var current = track.Measures[measureIndex];
        return (current.KeySignature ?? _project.KeySignature) != (previous.KeySignature ?? _project.KeySignature) ||
               (current.KeySignatureMinor ?? _project.KeySignatureMinor) != (previous.KeySignatureMinor ?? _project.KeySignatureMinor);
    }

    private int PreviousKeySignature(TrackModel track, int measureIndex)
        => measureIndex <= 0 || _project is null ? 0
            : track.Measures[measureIndex - 1].KeySignature ?? _project.KeySignature;

    /// <summary>Naturals that cancel the previous signature, then the new accidentals (the reference engraving).</summary>
    internal static (int Naturals, int Accidentals) KeySignatureGlyphs(int previous, int current)
    {
        previous = Math.Clamp(previous, -7, 7);
        current = Math.Clamp(current, -7, 7);
        var naturals = 0;
        if (previous != 0)
            naturals = Math.Sign(previous) != Math.Sign(current) ? Math.Abs(previous)
                : Math.Max(0, Math.Abs(previous) - Math.Abs(current));
        return (naturals, Math.Abs(current));
    }

    private double KeySignatureWidth(TrackModel track, int measureIndex)
    {
        var current = track.Measures[measureIndex].KeySignature ?? _project?.KeySignature ?? 0;
        var (naturals, accidentals) = KeySignatureGlyphs(PreviousKeySignature(track, measureIndex), current);
        var count = naturals + accidentals;
        return count == 0 ? 0 : count * 10.5 + (naturals > 0 && accidentals > 0 ? 3 : 0) + 6;
    }

    private bool TimeSignatureShown(TrackModel track, int measureIndex)
    {
        if (_project is null) return false;
        if (measureIndex <= 0) return true;
        var current = track.Measures[measureIndex];
        var previous = track.Measures[measureIndex - 1];
        return (current.TimeSigNum ?? _project.TimeSignatureNumerator) != (previous.TimeSigNum ?? _project.TimeSignatureNumerator) ||
               (current.TimeSigDenom ?? _project.TimeSignatureDenominator) != (previous.TimeSigDenom ?? _project.TimeSignatureDenominator);
    }

    private (string Num, string Den) TimeSignatureParts(MeasureModel measure)
        => ((measure.TimeSigNum ?? _project?.TimeSignatureNumerator ?? 4).ToString(),
            (measure.TimeSigDenom ?? _project?.TimeSignatureDenominator ?? 4).ToString());

    private double TimeSignatureWidth(MeasureModel measure)
    {
        var (num, den) = TimeSignatureParts(measure);
        return Math.Max(MakeTextIn(ScoreTextArea.BarInfo, num, 22, Brush(Colors.White), FontWeights.Bold).Width,
                        MakeTextIn(ScoreTextArea.BarInfo, den, 22, Brush(Colors.White), FontWeights.Bold).Width);
    }

    /// <summary>Tempo mark at the start of the song and at every tempo change (the reference style "♩ = 120").</summary>
    private string? TempoText(MeasureModel measure, int measureIndex)
    {
        if (measure.TempoChange.HasValue) return $"♩ = {measure.TempoChange}";
        return measureIndex == 0 && _project is not null && _project.Tempo > 0 ? $"♩ = {_project.Tempo}" : null;
    }

    /// <summary>Large engraved numerals: numerator in the upper half of the staff, denominator in the lower half.</summary>
    private void DrawTimeSignature(DrawingContext dc, MeasureModel measure, double x, double staffTop, Color ink)
    {
        var (num, den) = TimeSignatureParts(measure);
        var width = TimeSignatureWidth(measure);
        var brush = Brush(ink);
        DrawCenteredV(dc, num, x + width / 2, staffTop + StaffGap, 19, brush, FontWeights.Bold);
        DrawCenteredV(dc, den, x + width / 2, staffTop + 3 * StaffGap, 19, brush, FontWeights.Bold);
    }

    private static void DrawCenteredV(DrawingContext dc, string text, double cx, double cy, double size, Brush brush, FontWeight? weight = null, string font = "Segoe UI")
    {
        var ft = MakeText(text, size, brush, weight, font);
        TabForge.Visualization.Draw.DrawText(dc, ft, new Point(cx - ft.Width / 2, cy - ft.Height / 2));
    }

    private void DrawKeySignature(DrawingContext dc, string clef, int signature, int previous, double x, double staffTop, Color color)
    {
        var (naturals, count) = KeySignatureGlyphs(previous, signature);
        if (naturals + count == 0) return;
        var isBass = clef.Contains("F", StringComparison.OrdinalIgnoreCase) ||
                     clef.Contains("Bass", StringComparison.OrdinalIgnoreCase);
        var isAlto = clef.Contains("C", StringComparison.OrdinalIgnoreCase) ||
                     clef.Contains("Alto", StringComparison.OrdinalIgnoreCase) ||
                     clef.Contains("Tenor", StringComparison.OrdinalIgnoreCase);
        var sharpOffsets = isBass
            ? new[] { 9.0, 22.5, 4.5, 18.0, 31.5, 13.5, 27.0 }
            : isAlto ? new[] { 18.0, 4.5, 22.5, 9.0, 27.0, 13.5, 31.5 }
            : new[] { 0.0, 13.5, -4.5, 9.0, 22.5, 4.5, 18.0 };
        var flatOffsets = isBass
            ? new[] { 27.0, 13.5, 31.5, 18.0, 36.0, 22.5, 40.5 }
            : isAlto ? new[] { 4.5, 18.0, 0.0, 13.5, 27.0, 9.0, 22.5 }
            : new[] { 18.0, 4.5, 22.5, 9.0, 27.0, 13.5, 31.5 };
        var offsets = signature > 0 ? sharpOffsets : flatOffsets;
        var symbol = signature > 0 ? "♯" : "♭";
        var brush = Brush(color);
        var slot = 0;
        if (naturals > 0)
        {
            var old = previous > 0 ? sharpOffsets : flatOffsets;
            var first = Math.Sign(previous) == Math.Sign(signature) ? Math.Abs(signature) : 0;
            for (var i = first; i < first + naturals; i++, slot++)
                DrawCenteredV(dc, "♮", x + slot * 10.5 + 4, staffTop + old[i], 15, brush, null, "Segoe UI Symbol");
        }
        for (var i = 0; i < count; i++, slot++)
            DrawCenteredV(dc, symbol, x + slot * 10.5 + 4 + (naturals > 0 ? 3 : 0), staffTop + offsets[i], 15, brush, null, "Segoe UI Symbol");
    }

    private static string KeyName(int signature, bool minor)
    {
        var major = Math.Clamp(signature, -7, 7) switch
        {
            -7 => "Cb", -6 => "Gb", -5 => "Db", -4 => "Ab", -3 => "Eb", -2 => "Bb", -1 => "F",
            0 => "C", 1 => "G", 2 => "D", 3 => "A", 4 => "E", 5 => "B", 6 => "F#", _ => "C#"
        };
        if (!minor) return $"{major} major";
        var relativeMinor = Math.Clamp(signature, -7, 7) switch
        {
            -7 => "Ab", -6 => "Eb", -5 => "Bb", -4 => "F", -3 => "C", -2 => "G", -1 => "D",
            0 => "A", 1 => "E", 2 => "B", 3 => "F#", 4 => "C#", 5 => "G#", 6 => "D#", _ => "A#"
        };
        return $"{relativeMinor} minor";
    }

    // Wavy vibrato line (a sawtooth-sine polyline), thicker and taller for wide vibrato.
    private static void DrawVibratoLine(DrawingContext dc, double left, double right, double y, bool wide, Brush brush)
    {
        if (right <= left + 4) return;
        var amplitude = wide ? 3.2 : 2.0;
        var period = wide ? 8.0 : 6.5;
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(new Point(left, y), false, false);
            for (var px = left + 1; px <= right; px += 1)
                g.LineTo(new Point(px, y - Math.Sin((px - left) / period * 2 * Math.PI) * amplitude), true, true);
        }
        geometry.Freeze();
        dc.DrawGeometry(null, new Pen(brush, wide ? 2.2 : 1.5) { LineJoin = PenLineJoin.Round }, geometry);
    }

    private void DrawMeasure(DrawingContext dc, TrackModel track, MeasureModel measure, int measureIndex, double x, double measureWidth, double staffTop, double tabTop, double slotWidth, int slots, int strings, Color ink, Color faint, Color staffLine, Color accent, Color playColor, bool showStaff, bool showTab,
        IReadOnlyList<TabCell> cells, bool inactiveVoice)
    {
        if (inactiveVoice)
        {
            var gray = Color.FromRgb(0x6F, 0x7A, 0x89);
            ink = gray;
            faint = Color.FromRgb(0x5D, 0x67, 0x75);
        }
        _bendLabelBoxes.Clear();
        _pendingBendLabels.Clear();
        var bg = DarkPaper ? DarkPaperColor : LightPaperColor;
        var numerator = measure.TimeSigNum ?? _project?.TimeSignatureNumerator ?? 4;
        var denominator = measure.TimeSigDenom ?? _project?.TimeSignatureDenominator ?? 4;
        var keySignature = measure.KeySignature ?? _project?.KeySignature ?? 0;
        var layout = StaffLayoutFor(track, measure, measureIndex, slots, x, staffTop, slotWidth,
            numerator, denominator, keySignature, cells);
        if (showStaff)
        {
            // 8va / 15ma captions share the lane of the tempo text and bar number: start them to the right of those.
            var octaveMinX = double.NegativeInfinity;
            var tempoHere = TempoText(measure, measureIndex);
            if (tempoHere is not null) octaveMinX = x + 22 + MakeTextIn(ScoreTextArea.BarInfo, tempoHere, 9, Brush(faint), FontWeights.Bold).Width + 6;
            else if (ShowBarNumbers && measureIndex % Math.Max(1, BarNumberFrequency) == 0)
                octaveMinX = x + 2 + MakeTextIn(ScoreTextArea.BarInfo, (measureIndex + 1).ToString(), 9, Brush(faint)).Width + 4;
            layout.OctaveLabelMinX = octaveMinX;
        }
        if (showStaff)
            _staff.DrawMeasure(dc, layout, measureIndex, ink, faint, accent, playColor, bg, staffLine, LedgerLines,
                _soundingNow, _struckNow, LedgerLineOpacity);
        if (showTab)
            DrawTabSlides(dc, track, measureIndex, layout, cells, strings, tabTop, ink);

        foreach (var beat in layout.Beats)
        {
            var cell = beat.Cell;
            var i = beat.CellIndex;
            var cx = beat.CenterX;
            if (cell.IsRest && cell.Notes.Count == 0)
            {
                if (showTab && !showStaff) // The reference shows the rest only on the staff when notation is visible
                    DrawCentered(dc, StaffNotationRenderer.RestGlyph(cell), cx,
                        tabTop + (strings - 1) * StringGap / 2.0 - 9, 14, Brush(faint));
                continue;
            }

            {
                    var techniqueLabel = showTab ? DrawnTechniqueLabel(cell.Notes, !showStaff) : "";
                    if (!string.IsNullOrWhiteSpace(cell.ChordName)) DrawCenteredIn(ScoreTextArea.Chord, dc, cell.ChordName!, cx, staffTop - 28 - _barLift, 10, Brush(accent), FontWeights.SemiBold);
            // The reference vibrato: a wavy line along the note's duration, above the staff and above the TAB.
            var vibratoWide = cell.Notes.Any(n => n.Techniques.Contains("WideVibrato"));
            if (vibratoWide || cell.Notes.Any(n => n.Techniques.Contains("Vibrato")))
            {
                var right = beat.CenterX + Math.Max(18, beat.DurationSlots * (measureWidth / Math.Max(1, slots)) * 0.8);
                // Stack above whatever already sits over the note instead of drawing across it:
                // staff: above the chord-name / text lanes when present; TAB: above the technique label
                // and the P.M. lane.
                var staffLane = staffTop - 16;
                if (!string.IsNullOrWhiteSpace(cell.ChordName)) staffLane = staffTop - 40;
                if (!string.IsNullOrWhiteSpace(cell.Text)) staffLane = Math.Min(staffLane, staffTop - 54);
                var pm = cell.Notes.Any(note => note.Techniques.Any(IsPalmMute));
                var tabLane = tabTop - 12;
                if (pm && !showStaff) tabLane = tabTop - 30;
                if (DrawnTechniqueLabel(cell.Notes, !showStaff).Length > 0) tabLane = tabTop - (pm && !showStaff ? 34 : 20) - 9;
                if (showStaff) DrawVibratoLine(dc, cx - 6, right, staffLane, vibratoWide, Brush(ink));
                if (showTab) DrawVibratoLine(dc, cx - 6, right, tabLane, vibratoWide, Brush(ink));
            }
            // Mix Table point (F10): a red marker with a white core above the beat dot.
            if (cell.Mix is not null)
            {
                dc.DrawEllipse(Brush(Color.FromRgb(0xE0, 0x3B, 0x3B)), null, new Point(cx, staffTop - 48), 4.2, 4.2);
                dc.DrawEllipse(Brush(Colors.White), null, new Point(cx, staffTop - 48), 1.5, 1.5);
            }
                    if (!string.IsNullOrWhiteSpace(cell.Text))
                    {
                        // Beat text shares a lane with the section title: start to the right of the title, never on it.
                        var textFt = MakeTextIn(ScoreTextArea.Lyrics, cell.Text!, 9, Brush(faint));
                        var textLeft = cx - textFt.Width / 2;
                        var sectionTitleHere = MarkerForMeasure(measureIndex)?.Title ?? measure.SectionName;
                        if (ShowSectionHeadings && !string.IsNullOrWhiteSpace(sectionTitleHere))
                            textLeft = Math.Max(textLeft, x + 2 + MakeTextIn(ScoreTextArea.BarInfo, sectionTitleHere!, 10, Brush(faint), FontWeights.Bold).Width + 6);
                        TabForge.Visualization.Draw.DrawText(dc, textFt, new Point(textLeft, staffTop - 42 - _barLift));
                    }
                    if (cell.Fermata) DrawCenteredIn(ScoreTextArea.Technique, dc, "𝄐", cx, staffTop - 14, 12, Brush(ink));
                    if (cell.Accent != 0 && !showStaff)
                    {
                        // With a notation staff the accent is engraved above the note there; tab-only shows it here.
                        var accentY = tabTop - 22;
                        DrawCenteredIn(ScoreTextArea.Technique, dc, cell.Accent == 2 ? "^" : ">", cx, accentY, 11, Brush(ink), FontWeights.Bold);
                    }
                    // With a notation staff the renderer engraves staccato / tenuto beside the note head; tab-only shows them here.
                    if (cell.Staccato && !showStaff) DrawCenteredIn(ScoreTextArea.Technique, dc, "•", cx, tabTop - 26, 10, Brush(ink));
                    if (cell.Tenuto && !showStaff) DrawCenteredIn(ScoreTextArea.Technique, dc, "—", cx, tabTop - (cell.Staccato ? 16 : 26), 10, Brush(ink));
                    if (cell.IsGrace && !cell.Notes.Any(n => n.IsGraceNote)) DrawCenteredIn(ScoreTextArea.Technique, dc, "gr", cx, staffTop - 40, 8, Brush(faint));

                    // Lyrics sit under the TAB staff, one line per row (the reference layout).
                    if (!string.IsNullOrWhiteSpace(cell.Lyrics))
                    {
                        var lines = cell.Lyrics.Split('\n');
                        for (var li = 0; li < Math.Min(lines.Length, 3); li++)
                            DrawCenteredIn(ScoreTextArea.Lyrics, dc, lines[li], cx, tabTop + (strings - 1) * StringGap + 13 + FingeringHeight(cell) + li * 12, 10, Brush(ink));
                    }

                    foreach (var note in cell.Notes)
                    {
                        if (note.StringIndex < 0 || note.StringIndex >= strings) continue;
                        var isSounding = _soundingNow.Contains((measureIndex, i, note.StringIndex));
                        var isStruck = _struckNow.Contains((measureIndex, i, note.StringIndex));
                        if (showTab)
                        {
                            var sy = tabTop + note.StringIndex * StringGap;
                            var label = note.Dead ? "X"
                                : track.Kind == TrackKind.Drums ? DrumMaps.For(track, note.MidiValue > 0 ? note.MidiValue : note.Fret).Label
                                : note.Fret.ToString(CultureInfo.InvariantCulture);
                            if (track.Kind == TrackKind.Drums)
                            {
                                // Several drum sounds on one line at one beat (e.g. two cymbals) share one label instead of overprinting.
                                var sameLine = cell.Notes.Where(o => o.StringIndex == note.StringIndex).ToList();
                                if (sameLine[0] != note) continue;
                                if (sameLine.Count > 1)
                                    label = string.Join("\n", sameLine.Select(o => DrumMaps.For(track, o.MidiValue > 0 ? o.MidiValue : o.Fret).Label));
                            }
                            if (note.Ghost && !note.Dead) label = "(" + label + ")"; // The reference: a ghost note is the fret in brackets
                            var isGraceNote = note.IsGraceNote && cell.Notes.Any(other => !other.IsGraceNote);
                            var brush = Brush(ink);
                            var ft = MakeTextIn(ScoreTextArea.Fret, label, isGraceNote ? FretFontSize - 3 : label.Contains('\n') ? FretFontSize - 4 : FretFontSize, brush, FontWeights.Normal, "Consolas");
                            var gx = isGraceNote ? cx - 13 : cx; // a grace fret sits small, just before the main fret
                            var chipWidth = ft.Width + 2;
                            var chipHeight = Math.Max(14, ft.Height + 2);

                            if (!isGraceNote) DrawTabHopoSlur(dc, note, measureIndex, i, cx, sy, ink);

                            if (isSounding)
                            {
                                // Exact note being played: a bright pill behind the fret number.
                                var glow = Brush(Color.FromArgb(isStruck ? (byte)90 : (byte)46, playColor.R, playColor.G, playColor.B));
                                dc.DrawRoundedRectangle(glow, RenderDraw.Pen(playColor, isStruck ? 1.8 : 1.0),
                                    new Rect(gx - chipWidth / 2 - 2, sy - chipHeight / 2 - 2, chipWidth + 4, chipHeight + 4), 4, 4);
                                if (isStruck)
                                    dc.DrawRoundedRectangle(null, RenderDraw.Pen(playColor, 1.0),
                                        new Rect(gx - chipWidth / 2 - 5, sy - chipHeight / 2 - 3, chipWidth + 10, chipHeight + 6), 5, 5);
                            }
                            else
                            {
                                dc.DrawRectangle(Brush(bg), null,
                                    new Rect(gx - chipWidth / 2, sy - chipHeight / 2, chipWidth, chipHeight));
                            }
                            TabForge.Visualization.Draw.DrawText(dc, ft, new Point(gx - ft.Width / 2, sy - ft.Height / 2));
                        }
                    }
                    if (techniqueLabel.Length > 0)
                    {
                        // One complete, width-reserved annotation per beat prevents chord techniques
                        // from being overprinted and keeps simultaneous marks together.
                        var hasPalmMute = cell.Notes.Any(note => note.Techniques.Any(IsPalmMute));
                        DrawCenteredIn(ScoreTextArea.Technique, dc, techniqueLabel, cx, tabTop - (hasPalmMute && !showStaff ? 34 : 20), 9, Brush(ink),
                            techniqueLabel.Split(' ').Contains("T") ? FontWeights.SemiBold : FontWeights.Normal);
                    }
                    if (showTab) DrawTabBeatMarks(dc, track, layout, beat, x + measureWidth, tabTop, strings, ink, showStaff);
            }
        }
        if (showTab) DrawLetRingSpans(dc, layout, x + measureWidth, tabTop, ink, showStaff);
        FlushBendLabels(dc);
    }


    internal static IReadOnlyList<PalmMutePassage> BuildPalmMutePassages(TrackModel track, SongProject project)
    {
        var events = new List<PalmMuteEvent>();
        var measureStart = 0.0;
        for (var measureIndex = 0; measureIndex < track.Measures.Count; measureIndex++)
        {
            var measure = track.Measures[measureIndex];
            var slots = MusicTime.BarSlots(project, measureIndex);
            for (var cellIndex = 0; cellIndex < measure.Cells.Count && cellIndex < slots; cellIndex++)
            {
                var cell = measure.Cells[cellIndex];
                if (!cell.Notes.Any(note => note.Techniques.Any(IsPalmMute))) continue;
                var rawStart = cell.RhythmicPosition ?? cellIndex;
                var start = Math.Clamp(double.IsFinite(rawStart) ? rawStart : cellIndex, 0, slots);
                var end = Math.Min(slots, start + MusicTime.CellSlots(cell));
                events.Add(new PalmMuteEvent(measureIndex, start, end, measureStart + start,
                    measureStart + end));
            }
            measureStart += slots;
        }

        if (events.Count == 0) return Array.Empty<PalmMutePassage>();
        events.Sort((a, b) => a.AbsoluteStart.CompareTo(b.AbsoluteStart));

        // A chord or overlapping voice at the same onset is one annotation event, not multiple marks.
        var onsets = new List<PalmMuteEvent>();
        foreach (var item in events)
        {
            if (onsets.Count > 0 && Math.Abs(onsets[^1].AbsoluteStart - item.AbsoluteStart) < 0.001)
            {
                if (item.AbsoluteEnd > onsets[^1].AbsoluteEnd)
                    onsets[^1] = item;
            }
            else onsets.Add(item);
        }

        var passages = new List<PalmMutePassage>();
        var first = onsets[0];
        var endEvent = first;
        var passageEnd = first.AbsoluteEnd;
        var eventCount = 1;
        void FinishPassage() => passages.Add(new PalmMutePassage(
            first.Measure, first.StartSlots, endEvent.Measure, endEvent.EndSlots, eventCount));

        foreach (var item in onsets.Skip(1))
        {
            if (item.AbsoluteStart > passageEnd + 0.001)
            {
                FinishPassage();
                first = endEvent = item;
                passageEnd = item.AbsoluteEnd;
                eventCount = 1;
                continue;
            }

            if (item.AbsoluteEnd >= passageEnd)
            {
                passageEnd = item.AbsoluteEnd;
                endEvent = item;
            }
            eventCount++;
        }
        FinishPassage();
        return passages;
    }

    /// <summary>
    /// Resolves imported fade marks to musical spans. If the marked note is tied forward, its hairpin
    /// follows the same pitch/string through contiguous tie destinations, including across barlines.
    /// </summary>
    internal static IReadOnlyList<FadePassage> BuildFadePassages(TrackModel track, SongProject project)
    {
        var notes = new List<FadeNoteEvent>();
        var measureStart = 0.0;
        for (var measureIndex = 0; measureIndex < track.Measures.Count; measureIndex++)
        {
            var measure = track.Measures[measureIndex];
            var slots = MusicTime.BarSlots(project, measureIndex);
            for (var cellIndex = 0; cellIndex < measure.Cells.Count && cellIndex < slots; cellIndex++)
            {
                var cell = measure.Cells[cellIndex];
                if (cell.Notes.Count == 0) continue;
                var rawStart = cell.RhythmicPosition ?? cellIndex;
                var start = Math.Clamp(double.IsFinite(rawStart) ? rawStart : cellIndex, 0, slots);
                var end = Math.Min(slots, start + MusicTime.CellSlots(cell));
                foreach (var note in cell.Notes)
                {
                    var midi = note.MidiValue > 0 ? note.MidiValue
                        : note.StringIndex >= 0 && note.StringIndex < track.StringTunings.Count
                            ? track.PitchOf(note.StringIndex, note.Fret) : note.Fret;
                    notes.Add(new FadeNoteEvent(measureIndex, cellIndex, start, end,
                        measureStart + start, measureStart + end, note, midi,
                        cell.IsTied || note.Tied));
                }
            }
            measureStart += slots;
        }

        var passages = new List<FadePassage>();
        foreach (var origin in notes.Where(item => item.Note.Techniques.Contains("FadeIn") ||
                                                    item.Note.Techniques.Contains("FadeOut")))
        {
            var fadeOut = origin.Note.Techniques.Contains("FadeOut");
            var endMeasure = origin.Measure;
            var endSlots = origin.EndSlots;
            var endAbsolute = origin.AbsoluteEnd;
            while (true)
            {
                var destination = notes.Where(item => item.IsTied &&
                        item.AbsoluteStart > origin.AbsoluteStart + 0.001 &&
                        item.Note.StringIndex == origin.Note.StringIndex && item.Midi == origin.Midi &&
                        Math.Abs(item.AbsoluteStart - endAbsolute) < 0.001)
                    .OrderBy(item => item.AbsoluteStart).FirstOrDefault();
                if (destination is null) break;
                endMeasure = destination.Measure;
                endSlots = destination.EndSlots;
                endAbsolute = destination.AbsoluteEnd;
            }
            passages.Add(new FadePassage(origin.Measure, origin.StartSlots,
                endMeasure, endSlots, fadeOut));
        }

        return passages.Distinct().ToArray();
    }

    private void DrawFadePassages(DrawingContext dc, IReadOnlyList<FadePassage> passages,
        TrackModel track, ScoreSystemPosition systemLayout, double staffTop, double tabTop, Color ink,
        bool showTab)
    {
        if (passages.Count == 0 || systemLayout.Measures.Count == 0) return;
        var layout = GetScoreLayout(track);
        var phraseInk = StaffNotationRenderer.EngravingInkColor(ink,
            DarkPaper ? DarkPaperColor : LightPaperColor);
        var pen = RenderDraw.Pen(phraseInk, 0.9);
        const double halfOpening = 3.5;
        var lineY = showTab ? tabTop - 8 : staffTop + 4 * StaffGap + 20;
        foreach (var passage in passages)
        {
            var firstSystem = layout.SystemForMeasure(passage.FirstMeasure);
            var lastSystem = layout.SystemForMeasure(passage.LastMeasure);
            var system = systemLayout.Index;
            if (system < firstSystem || system > lastSystem) continue;

            var continuesFromPreviousSystem = system > firstSystem;
            var continuesIntoNextSystem = system < lastSystem;
            var startX = continuesFromPreviousSystem
                ? systemLayout.X + 2
                : CellCenterX(passage.FirstMeasure, passage.FirstStartSlots);
            var endX = continuesIntoNextSystem
                ? systemLayout.X + systemLayout.Width - 2
                : CellBoundaryX(passage.LastMeasure, passage.LastEndSlots);
            if (endX <= startX + 1) continue;

            var startHalf = continuesFromPreviousSystem || passage.IsFadeOut ? halfOpening : 0;
            var endHalf = continuesIntoNextSystem || !passage.IsFadeOut ? halfOpening : 0;
            dc.DrawLine(pen, new Point(startX, lineY - startHalf), new Point(endX, lineY - endHalf));
            dc.DrawLine(pen, new Point(startX, lineY + startHalf), new Point(endX, lineY + endHalf));
        }
    }

    private void DrawPalmMutePassages(DrawingContext dc, IReadOnlyList<PalmMutePassage> passages,
        TrackModel track, ScoreSystemPosition systemLayout, double tabTop, Color ink, double? belowStaffBottom = null)
    {
        var system = systemLayout.Index;
        if (systemLayout.Measures.Count == 0) return;

        var phraseInk = StaffNotationRenderer.EngravingInkColor(ink,
            DarkPaper ? DarkPaperColor : LightPaperColor);
        var phraseBrush = Brush(phraseInk);
        const double terminalTickLength = 5.0;
        var pen = RenderDraw.DashedPen(phraseInk, 1.0, 5.0, 2.5);
        var endPen = RenderDraw.Pen(phraseInk, 1.0);
        // The reference puts "P.M. - - |" under the notation staff; without a staff it stays above the TAB.
        var lineY = belowStaffBottom is { } bottom ? bottom + 32 : tabTop - 12;
        var baseLineY = lineY;
        var labelY = lineY - 10;
        foreach (var passage in passages)
        {
            var layout = GetScoreLayout(track);
            var passageFirstSystem = layout.SystemForMeasure(passage.FirstMeasure);
            var passageLastSystem = layout.SystemForMeasure(passage.LastMeasure);
            if (system < passageFirstSystem || system > passageLastSystem) continue;

            var continuesFromPreviousSystem = system > passageFirstSystem;
            var continuesIntoNextSystem = system < passageLastSystem;
            var startX = continuesFromPreviousSystem
                ? systemLayout.X + 2
                : CellCenterX(passage.FirstMeasure, passage.FirstStartSlots);
            var endX = continuesIntoNextSystem
                ? systemLayout.X + systemLayout.Width - 2
                : CellBoundaryX(passage.LastMeasure, passage.LastEndSlots);

            lineY = baseLineY;
            labelY = lineY - 10;
            if (belowStaffBottom is not null && _staffLayoutCache is not null)
            {
                // Keep the dashed line under the lowest notehead of the bars it spans (ledger-line notes).
                var lowest = double.NegativeInfinity;
                foreach (var mp in systemLayout.Measures)
                {
                    if (mp.MeasureIndex < passage.FirstMeasure || mp.MeasureIndex > passage.LastMeasure || mp.MeasureIndex >= _staffLayoutCache.GetLength(0)) continue;
                    for (var v = 0; v < 2; v++)
                        if (_staffLayoutCache[mp.MeasureIndex, v] is { } cachedLayout)
                            foreach (var b in cachedLayout.Beats)
                            {
                                foreach (var n in b.Notes) lowest = Math.Max(lowest, n.Y);
                                if (b.HasStem) lowest = Math.Max(lowest, Math.Max(b.StemStartY, b.StemEndY) + 3);
                                if (b.LowerStemTopY is not null) lowest = Math.Max(lowest, b.LowerStemEndY + 3);
                            }
                }
                if (lowest > double.NegativeInfinity && lowest + 11 > lineY) { lineY = lowest + 11; labelY = lineY - 10; }
            }

            if (!continuesFromPreviousSystem)
            {
                var labelWidth = MakeTextIn(ScoreTextArea.Technique, "P.M.", 9, phraseBrush).Width;
                DrawCenteredIn(ScoreTextArea.Technique, dc, "P.M.", startX - labelWidth / 2 - 2, labelY, 9, phraseBrush);
            }

            var lineStart = continuesFromPreviousSystem ? startX : startX + 2;
            if (endX > lineStart + 1)
                dc.DrawLine(pen, new Point(lineStart, lineY), new Point(endX, lineY));
            // Keep the closing hook tied to the actual final muted-note boundary even for a short
            // passage whose dashed extender is only a few pixels long.
            if (!continuesIntoNextSystem && endX > startX)
                dc.DrawLine(endPen, new Point(endX, lineY - terminalTickLength),
                    new Point(endX, lineY + terminalTickLength));
        }
    }

    private double CellCenterX(int measureIndex, double startSlots)
    {
        var track = Track;
        if (track is null || measureIndex < 0 || measureIndex >= track.Measures.Count) return 0;
        var position = GetScoreLayout(track).Measure(measureIndex);
        return position.X + WarpFor(track, measureIndex).CenterFraction(startSlots) * position.Width;
    }

    private double CellBoundaryX(int measureIndex, double endSlots)
    {
        var track = Track;
        if (track is null || measureIndex < 0 || measureIndex >= track.Measures.Count) return 0;
        var position = GetScoreLayout(track).Measure(measureIndex);
        return position.X + WarpFor(track, measureIndex).Fraction(endSlots) * position.Width;
    }

    private readonly record struct PalmMuteEvent(
        int Measure, double StartSlots, double EndSlots, double AbsoluteStart, double AbsoluteEnd);

    private sealed record FadeNoteEvent(
        int Measure, int Cell, double StartSlots, double EndSlots,
        double AbsoluteStart, double AbsoluteEnd, TabNote Note, int Midi, bool IsTied);

    private static bool IsPalmMute(string technique) => TechniqueNames.IsPalmMute(technique);


    private static string ShortTechnique(string t) => t switch
    {
        "LetRing" => "let ring",
        "Harmonic" => "H",
        "ArtificialHarmonic" => "A.H.",
        "PinchHarmonic" => "P.H.",
        "TapHarmonic" => "T.H.",
        "SemiHarmonic" => "S.H.",
        "FeedbackHarmonic" => "F.B.",
        "Vibrato" => "", // drawn as a wavy line
        "WideVibrato" => "",
        "TremBar" => "T",
        "Bend" => "b",
        "LegatoSlide" => "/",
        "ShiftSlide" => "S",
        "SlideInBelow" => "↗",
        "SlideInAbove" => "↘",
        "SlideOutUp" => "↗",
        "SlideOutDown" => "↘",
        "PickSlideUp" => "P.S.↑",
        "PickSlideDown" => "P.S.↓",
        "DeadSlapped" => "D.S.",
        "HOPO" or "HOPOOrigin" or "HOPODestination" => "",
        "Tapping" or "LeftTap" => "",
        "Slap" => "S",
        "Pop" => "P",
        "Trill" => "tr",
        "TremoloPick" => "𝄆",
        "GraceOnBeat" => "gr",
        "GraceBend" => "grb",
        "Ghost" => "G",
        "Dead" => "X",
        "FadeIn" => "<",
        "FadeOut" => ">",
        "WahOpen" => "wah",
        "WahClose" => "wah",
        "GraceBefore" => "gr",
        "BrushDown" => "↓",
        "BrushUp" => "↑",
        "ArpeggioDown" => "arp↓",
        "ArpeggioUp" => "arp↑",
        _ => ""
    };

    private static bool HasTapTechnique(TabNote note) =>
        note.Techniques.Contains("Tapping") || note.Techniques.Contains("LeftTap");

    internal static string TechniqueLabel(IEnumerable<TabNote> notes, bool includeFade = true)
    {
        var noteList = notes.ToList();
        var labels = noteList.SelectMany(note => note.Techniques.Where(technique => !IsPalmMute(technique) &&
                    !TabSlideNotation.IsRenderedAsGeometry(technique) &&
                    (includeFade || (!technique.Equals("FadeIn", StringComparison.OrdinalIgnoreCase) &&
                                     !technique.Equals("FadeOut", StringComparison.OrdinalIgnoreCase))))
                .Select(technique => technique == "Bend" ? BendLabel(note.BendTypeName) : ShortTechnique(technique)))
            .Where(label => label.Length > 0).Distinct().ToList();
        if (noteList.Any(HasTapTechnique) && !labels.Contains("T")) labels.Add("T");
        return string.Join(" ", labels);
    }

    internal static string BendLabel(string bendType) => bendType switch
    {
        "Prebend" => "P.B.",
        "Release" => "R",
        "BendRelease" => "b/R",
        "PrebendBend" => "P.B./b",
        "PrebendRelease" => "P.B./R",
        "Hold" => "H",
        _ => "b"
    };

    private void DrawTabHopoSlur(DrawingContext dc, TabNote note, int measureIndex, int cellIndex,
        double startX, double y, Color ink)
    {
        var isExplicitOrigin = note.Techniques.Contains("HOPOOrigin");
        var isLegacyHopo = note.Techniques.Contains("HOPO") &&
                           !note.Techniques.Contains("HOPODestination") && !isExplicitOrigin;
        if (!isExplicitOrigin && !isLegacyHopo) return;

        (int Measure, int Cell, TabNote Note)? next;
        if (isExplicitOrigin)
        {
            var previous = FindPreviousTabNoteOnString(measureIndex, cellIndex, note.StringIndex);
            if (previous is { } prior && GetScoreLayout().SystemForMeasure(prior.Measure) == GetScoreLayout().SystemForMeasure(measureIndex) &&
                prior.Note.Techniques.Contains("HOPOOrigin")) return;

            next = FindHopoDestination(measureIndex, cellIndex, note.StringIndex);
            while (next is { } endpoint && endpoint.Note.Techniques.Contains("HOPOOrigin"))
            {
                var chained = FindHopoDestination(endpoint.Measure, endpoint.Cell, note.StringIndex);
                if (chained is null) break;
                next = chained;
            }
            // Some reference variants only encode the origin bit. Preserve a useful short slur for them.
            next ??= FindNextTabNoteOnString(measureIndex, cellIndex, note.StringIndex);
        }
        else
        {
            // Older saved projects only have a generic HOPO bit. Treat each contiguous run as one
            // legato phrase: draw from its first note to its last instead of tiny adjacent arcs.
            var previous = FindPreviousTabNoteOnString(measureIndex, cellIndex, note.StringIndex);
            if (previous is { } prior && GetScoreLayout().SystemForMeasure(prior.Measure) == GetScoreLayout().SystemForMeasure(measureIndex) &&
                prior.Note.Techniques.Contains("HOPO")) return;
            next = FindLegacyHopoPhraseEnd(measureIndex, cellIndex, note.StringIndex);
        }
        if (next is null || GetScoreLayout().SystemForMeasure(next.Value.Measure) != GetScoreLayout().SystemForMeasure(measureIndex)) return;

        var endX = CellCenterX(next.Value.Measure, next.Value.Cell);
        if (endX - startX < 18) return;

        // Hammer-on / pull-off groups as a legato slur in the tablature,
        // not as a repeated "H/P" text label over every note.
        // The reference draws the slur above the numbers.
        var start = new Point(startX + 3, y - 7);
        var end = new Point(endX - 3, y - 7);
        var arc = Math.Clamp((endX - startX) * 0.10, 5, 10);
        var figure = new PathFigure { StartPoint = start, IsClosed = false };
        figure.Segments.Add(new BezierSegment(
            new Point(startX + (endX - startX) * 0.28, y - 7 - arc),
            new Point(startX + (endX - startX) * 0.72, y - 7 - arc),
            end, true));
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        dc.DrawGeometry(null, RenderDraw.Pen(ink, 0.9), geometry);
    }

    private void DrawTabSlides(DrawingContext dc, TrackModel track, int measureIndex,
        StaffNotationMeasureLayout layout, IReadOnlyList<TabCell> cells, int strings, double tabTop, Color ink)
    {
        var voiceIndex = ReferenceEquals(cells, track.Measures[measureIndex].Voice2Cells) ? 1 : 0;
        var scoreLayout = GetScoreLayout(track);
        var systemIndex = scoreLayout.SystemForMeasure(measureIndex);
        var pen = RenderDraw.Pen(ink, 1.05);

        foreach (var mark in TabSlideNotation.ForMeasure(track, measureIndex, voiceIndex))
        {
            var sourceBeat = layout.BeatForCell(mark.SourceCellIndex);
            if (sourceBeat is null || mark.Source.StringIndex < 0 || mark.Source.StringIndex >= strings) continue;

            var sourceX = sourceBeat.CenterX;
            var y = tabTop + mark.Source.StringIndex * StringGap;
            var sourceLabel = mark.Source.Dead ? "X" : mark.Source.Fret.ToString(CultureInfo.InvariantCulture);
            var sourceWidth = MakeTextIn(ScoreTextArea.Fret, sourceLabel, FretFontSize, Brush(ink), FontWeights.Normal, "Consolas").Width;
            switch (mark.Kind)
            {
                case TabSlideMarkKind.IncomingFromBelow:
                    dc.DrawLine(pen, new Point(sourceX - sourceWidth / 2 - 7, y + 3.5),
                        new Point(sourceX - sourceWidth / 2 - 2, y - 2.5));
                    break;
                case TabSlideMarkKind.IncomingFromAbove:
                    dc.DrawLine(pen, new Point(sourceX - sourceWidth / 2 - 7, y - 3.5),
                        new Point(sourceX - sourceWidth / 2 - 2, y + 2.5));
                    break;
                case TabSlideMarkKind.OutgoingUp:
                case TabSlideMarkKind.OutgoingDown:
                    DrawTabSlideStub(dc, pen, sourceX, sourceWidth, y,
                        mark.Kind == TabSlideMarkKind.OutgoingUp);
                    break;
                case TabSlideMarkKind.Connection:
                    if (mark.Target is null || mark.TargetMeasureIndex is not { } targetMeasure ||
                        mark.TargetCellIndex is null || mark.Target.StringIndex < 0 ||
                        mark.Target.StringIndex >= strings || scoreLayout.SystemForMeasure(targetMeasure) != systemIndex)
                    {
                        DrawTabSlideStub(dc, pen, sourceX, sourceWidth, y, goesUp: true);
                        break;
                    }

                    var targetX = CellCenterX(targetMeasure, mark.TargetStartSlots);
                    var targetLabel = mark.Target.Dead ? "X" : mark.Target.Fret.ToString(CultureInfo.InvariantCulture);
                    var targetWidth = MakeTextIn(ScoreTextArea.Fret, targetLabel, FretFontSize, Brush(ink), FontWeights.Normal, "Consolas").Width;
                    var startX = sourceX + sourceWidth / 2 + 1.5;
                    var endX = targetX - targetWidth / 2 - 1.5;
                    if (endX - startX < 4) break;
                    var goesUp = mark.Target.Fret >= mark.Source.Fret;
                    var endY = y + (goesUp ? -3 : 3);
                    dc.DrawLine(pen, new Point(startX, y), new Point(endX, endY));
                    if (mark.Source.Techniques.Contains("LegatoSlide"))
                    {
                        // Legato slide = the slide line plus a slur; a shift slide is the line alone.
                        var arcHeight = Math.Clamp((endX - startX) * 0.12, 4, 9);
                        var slur = new StreamGeometry();
                        using (var c = slur.Open())
                        {
                            c.BeginFigure(new Point(startX, y - 8), false, false);
                            c.QuadraticBezierTo(new Point((startX + endX) / 2, y - 8 - arcHeight * 2), new Point(endX, endY - 8), true, false);
                        }
                        slur.Freeze();
                        dc.DrawGeometry(null, pen, slur);
                    }
                    break;
            }
        }
    }

    private static void DrawTabSlideStub(DrawingContext dc, Pen pen, double centerX, double labelWidth,
        double y, bool goesUp)
    {
        var start = new Point(centerX + labelWidth / 2 + 1.5, y);
        var end = new Point(start.X + 6, y + (goesUp ? -5 : 5));
        dc.DrawLine(pen, start, end);
    }

    private (int Measure, int Cell, TabNote Note)? FindNextTabNoteOnString(int measureIndex, int cellIndex, int stringIndex)
    {
        var track = Track;
        if (track is null) return null;
        for (var measure = measureIndex; measure < track.Measures.Count; measure++)
        {
            var firstCell = measure == measureIndex ? cellIndex + 1 : 0;
            var cells = track.Measures[measure].Cells;
            for (var cell = firstCell; cell < cells.Count; cell++)
            {
                var next = cells[cell].Notes.FirstOrDefault(candidate => candidate.StringIndex == stringIndex);
                if (next is not null) return (measure, cell, next);
            }
        }
        return null;
    }

    private (int Measure, int Cell, TabNote Note)? FindHopoDestination(int measureIndex, int cellIndex, int stringIndex)
    {
        var track = Track;
        if (track is null) return null;
        var system = GetScoreLayout(track).SystemForMeasure(measureIndex);
        for (var measure = measureIndex; measure < track.Measures.Count && GetScoreLayout(track).SystemForMeasure(measure) == system; measure++)
        {
            var firstCell = measure == measureIndex ? cellIndex + 1 : 0;
            var cells = track.Measures[measure].Cells;
            for (var cell = firstCell; cell < cells.Count; cell++)
            {
                var next = cells[cell].Notes.FirstOrDefault(candidate => candidate.StringIndex == stringIndex);
                if (next is null) continue;
                if (next.Techniques.Contains("HOPODestination")) return (measure, cell, next);
            }
        }
        return null;
    }

    private (int Measure, int Cell, TabNote Note)? FindLegacyHopoPhraseEnd(int measureIndex, int cellIndex, int stringIndex)
    {
        var track = Track;
        if (track is null) return null;
        var system = GetScoreLayout(track).SystemForMeasure(measureIndex);
        (int Measure, int Cell, TabNote Note)? last = null;
        for (var measure = measureIndex; measure < track.Measures.Count && GetScoreLayout(track).SystemForMeasure(measure) == system; measure++)
        {
            var firstCell = measure == measureIndex ? cellIndex + 1 : 0;
            var cells = track.Measures[measure].Cells;
            for (var cell = firstCell; cell < cells.Count; cell++)
            {
                var next = cells[cell].Notes.FirstOrDefault(candidate => candidate.StringIndex == stringIndex);
                if (next is null) continue;
                if (!next.Techniques.Contains("HOPO")) return last;
                last = (measure, cell, next);
            }
        }
        return last;
    }

    private (int Measure, int Cell, TabNote Note)? FindPreviousTabNoteOnString(int measureIndex, int cellIndex, int stringIndex)
    {
        var track = Track;
        if (track is null) return null;
        for (var measure = measureIndex; measure >= 0; measure--)
        {
            var firstCell = measure == measureIndex ? cellIndex - 1 : track.Measures[measure].Cells.Count - 1;
            for (var cell = firstCell; cell >= 0; cell--)
            {
                var previous = track.Measures[measure].Cells[cell].Notes.FirstOrDefault(candidate => candidate.StringIndex == stringIndex);
                if (previous is not null) return (measure, cell, previous);
            }
        }
        return null;
    }

    private double CellCenterX(int measureIndex, int cellIndex)
    {
        var track = Track;
        if (track is null || measureIndex < 0 || measureIndex >= track.Measures.Count) return 0;
        var measure = track.Measures[measureIndex];
        var slots = SlotsFor(measureIndex);
        var position = GetScoreLayout(track).Measure(measureIndex);
        var slotWidth = position.Width / Math.Max(1, slots);
        var startSlots = cellIndex >= 0 && cellIndex < measure.Cells.Count
            ? measure.Cells[cellIndex].RhythmicPosition ?? cellIndex
            : Math.Max(0, cellIndex);
        return position.X + WarpFor(track, measureIndex).CenterFraction(Math.Max(0, startSlots)) * position.Width;
    }

    private static double CellStartSlots(MeasureModel measure, int cellIndex, IReadOnlyList<TabCell>? voiceCells = null)
    {
        var cells = voiceCells ?? measure.Cells;
        return cellIndex >= 0 && cellIndex < cells.Count
            ? Math.Max(0, cells[cellIndex].RhythmicPosition ?? cellIndex)
            : Math.Max(0, cellIndex);
    }

    /// <summary>
    /// Shared horizontal bounds for the edit cursor and note hover. A populated cell occupies its
    /// actual rhythmic duration; empty cells retain the one-slot cursor width.
    /// </summary>
    internal static Rect CellHighlightRect(MeasureModel measure, IReadOnlyList<TabCell> cells, int cellIndex,
        double measureX, double slotWidth, double y, double height) =>
        CellHighlightRect(measure, cells, cellIndex, measureX, slotWidth, null, 0, y, height);

    internal static Rect CellHighlightRect(MeasureModel measure, IReadOnlyList<TabCell> cells, int cellIndex,
        double measureX, double slotWidth, MeasureWarp? warp, double measureWidth, double y, double height)
    {
        var cell = cellIndex >= 0 && cellIndex < cells.Count ? cells[cellIndex] : null;
        var widthSlots = cell is not null && (cell.Notes.Count > 0 || cell.IsRest)
            ? Math.Max(1, MusicTime.CellSlotsRounded(cell)) : 1;
        var startSlots = CellStartSlots(measure, cellIndex, cells);
        if (warp is not null && measureWidth > 0)
        {
            var left = measureX + warp.Fraction(startSlots) * measureWidth;
            var right = measureX + warp.Fraction(startSlots + widthSlots) * measureWidth;
            return new Rect(left + 1, y, Math.Max(6, right - left - 2), height);
        }
        return new Rect(measureX + startSlots * slotWidth + 1, y,
            Math.Max(slotWidth, widthSlots * slotWidth) - 2, height);
    }

    // Text layout and typeface creation are the most expensive part of a custom drawing pass, and a
    // playback repaint redraws the same fret numbers, technique labels and headers every time. Frozen
    // brushes plus bounded caches keep that work out of the playback loop.
    private static readonly Dictionary<(string Font, int Weight, bool Italic), Typeface> TypefaceCache = new();
    private static readonly Dictionary<(string Text, int Size, uint Colour, int Weight, string Font, bool Italic, int Area, int Dpi), FormattedText> TextCache = new();
    private const int TextCacheLimit = 4096;

    private static uint ColourKey(Color c) => ((uint)c.A << 24) | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;

    /// <summary>Shared cached text layout (also used by the staff engraver).</summary>
    internal static FormattedText CachedText(string text, double size, Brush brush, FontWeight? weight, string font) =>
        MakeText(text, size, brush, weight, font);

    private static Typeface TypefaceFor(string font, FontWeight weight) => TypefaceFor(font, weight, _scoreTextItalic);

    private static Typeface TypefaceFor(string font, FontWeight weight, bool italic)
    {
        var key = (font, weight.ToOpenTypeWeight(), italic);
        if (TypefaceCache.TryGetValue(key, out var cached)) return cached;
        var created = new Typeface(new FontFamily(font), italic ? FontStyles.Italic : FontStyles.Normal, weight, FontStretches.Normal);
        TypefaceCache[key] = created;
        return created;
    }

    private static FormattedText MakeText(string text, double size, Brush brush, FontWeight? weight = null, string font = "Segoe UI")
        => MakeTextIn(ScoreTextArea.General, text, size, brush, weight, font);

    private static FormattedText MakeTextIn(ScoreTextArea area, string text, double size, Brush brush, FontWeight? weight = null, string font = "Segoe UI")
    {
        var style = AreaStyles[(int)area];
        var bold = style.Bold ?? _scoreTextBold;
        var italic = style.Italic ?? _scoreTextItalic;
        var effectiveWeight = bold ? FontWeights.Bold : style.Bold == false ? FontWeights.Normal : weight ?? FontWeights.Normal;
        var effectiveSize = size * (_scoreTextSize / 12.0) * style.Scale;
        var effectiveFont = style.Font ?? _scoreFontFamily;
        // A colour override replaces the normal text colour; highlight colours (playing / selected notes) are kept.
        if (style.Colour is { } overrideColour && brush is SolidColorBrush normal &&
            (area != ScoreTextArea.Fret || normal.Color == _normalInk))
            brush = Brush(overrideColour);
        var colour = brush is SolidColorBrush solid ? ColourKey(solid.Color) : 0u;
        var pixelsPerDip = TabForge.Visualization.Draw.PixelsPerDip;   // this editor's display DPI (A-03: scope opened in OnRender)
        var key = (text, (int)Math.Round(effectiveSize * 4), colour, effectiveWeight.ToOpenTypeWeight(), effectiveFont, italic, (int)area, (int)Math.Round(pixelsPerDip * 1000));
        if (TextCache.TryGetValue(key, out var cached)) return cached;
        var created = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            TypefaceFor(effectiveFont, effectiveWeight, italic), effectiveSize, brush, pixelsPerDip);
        if (style.Outline is { } outline) TabForge.Visualization.Draw.SetOutline(created, outline);
        if (TextCache.Count >= TextCacheLimit) TextCache.Clear();
        TextCache[key] = created;
        return created;
    }

    private static void DrawIn(ScoreTextArea area, DrawingContext dc, string text, double x, double y, double size, Brush brush, FontWeight? weight = null, string font = "Segoe UI")
        => TabForge.Visualization.Draw.DrawText(dc, MakeTextIn(area, text, size, brush, weight, font), new Point(x, y));

    private static void DrawCenteredIn(ScoreTextArea area, DrawingContext dc, string text, double cx, double y, double size, Brush brush, FontWeight? weight = null, string font = "Segoe UI")
    {
        var ft = MakeTextIn(area, text, size, brush, weight, font);
        TabForge.Visualization.Draw.DrawText(dc, ft, new Point(cx - ft.Width / 2, y));
    }

    private static Color _normalInk = Colors.White;

    public enum ScoreTextArea { General, Header, BarInfo, Fret, Technique, Chord, Lyrics }

    // ---- per-area text styles (Score > Appearance > Text & fonts) ----
    private sealed record AreaStyle(string? Font, double Scale, bool? Bold, bool? Italic, Color? Colour, Pen? Outline);
    private static readonly AreaStyle[] AreaStyles = Enumerable.Repeat(new AreaStyle(null, 1, null, null, null, null), 7).ToArray();

    public static void ConfigureTextAreas(IReadOnlyDictionary<string, TabForge.Services.ScoreTextAreaStyle>? styles)
    {
        for (var i = 0; i < AreaStyles.Length; i++)
        {
            var name = ((ScoreTextArea)i).ToString();
            if (styles is null || !styles.TryGetValue(name, out var s) || s is null) { AreaStyles[i] = new AreaStyle(null, 1, null, null, null, null); continue; }
            Color? colour = TabForge.Views.ColourChooser.TryParse(s.Colour, out var c) ? c : null;
            Pen? outline = null;
            if (s.OutlineThickness > 0 && TabForge.Views.ColourChooser.TryParse(s.OutlineColour, out var oc))
            {
                outline = new Pen(new SolidColorBrush(oc), s.OutlineThickness * 2) { LineJoin = PenLineJoin.Round };
                outline.Freeze();
            }
            AreaStyles[i] = new AreaStyle(string.IsNullOrWhiteSpace(s.Font) ? null : s.Font,
                Math.Clamp(s.SizePercent <= 0 ? 1 : s.SizePercent / 100.0, 0.4, 3), s.Bold, s.Italic, colour, outline);
        }
        TextCache.Clear();
        TypefaceCache.Clear();
    }
    private static void Draw(DrawingContext dc, string text, double x, double y, double size, Brush brush, FontWeight? weight = null, string font = "Segoe UI")
        => TabForge.Visualization.Draw.DrawText(dc, MakeText(text, size, brush, weight, font), new Point(x, y));

    private static void DrawCentered(DrawingContext dc, string text, double cx, double y, double size, Brush brush, FontWeight? weight = null, string font = "Segoe UI")
    {
        var ft = MakeText(text, size, brush, weight, font);
        TabForge.Visualization.Draw.DrawText(dc, ft, new Point(cx - ft.Width / 2, y));
    }
}
