using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using TabForge.Controllers;
using TabForge.Diagnostics;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Plugins;
using TabForge.Services;
using TabForge.Visualization;

namespace TabForge.Views;

// TrackTimeline: measure and render (ruler, sections strip, rows, overlays, playhead), lane visuals and bar miniatures.
internal sealed partial class TrackTimeline
{
    protected override Size MeasureOverride(Size availableSize)
    {
        var height = ArrangementPanel.RulerHeight + ArrangementPanel.SectionHeight + ArrangementPanel.RowsHeight(Project) + AddLaneHeight + 2;
        // Never report the full (very wide) timeline as the panel's own desired width: the internal
        // horizontal ScrollViewer scrolls the timeline, but an unbounded desired size would inflate the
        // whole window layout (pushing the caption buttons and side panel off-screen).
        var width = double.IsInfinity(availableSize.Width)
            ? Math.Max(200, TotalWidth)
            : Math.Max(200, availableSize.Width);
        return new Size(width, height);
    }

    /// <summary>Number of full timeline renders (probe and test counter).</summary>
    internal int RenderCount { get; private set; }

    protected override void OnRender(DrawingContext dc)
    {
        using var slowTrace = TabForge.Views.SlowTrace.Measure("timeline render");
        RenderCount++;
        try { RenderGuard.Inject("TrackTimeline"); RenderCore(dc); }
        catch (Exception ex) when (RenderGuard.Contain(ex, "TrackTimeline", dc, ActualWidth, ActualHeight)) { }
    }

    private void RenderCore(DrawingContext dc)
    {
#if DEBUG
        using var performance = RenderPerformance.Measure(RenderPerformance.PerformanceCategory.Arrangement);
#endif
        using var dpiScope = Draw.UseDpi(this);   // A-03: text shaped for this window's monitor
        var project = Project;
        var width = Math.Max(ActualWidth, TotalWidth);
        var height = ActualHeight;
        dc.DrawRectangle(Draw.Solid(_theme.Background), null, new Rect(0, 0, width, height));
        if (project is null || project.Tracks.Count == 0) return;
        _audible = PlaybackEngine.AudibleMask(project);   // once per full render: dimmed = not heard (the shared mute/solo rule), not the raw mute switch

        var bars = BarCount;
        var trackCount = project.Tracks.Count;
        var gridTop = ArrangementPanel.RulerHeight + ArrangementPanel.SectionHeight;

        // --- ruler ---
        var labelEvery = MeasureWidth < 18 ? 8 : MeasureWidth < 28 ? 4 : 1;
        var (firstBar, endBar) = DrawnBars(bars);
        for (var b = firstBar; b < endBar; b++)
        {
            var x = XOfBar(b);
            var w = WidthOfBar(b);
            var isCurrent = b == SelectedBar;
            var label = b % labelEvery == 0;
            var numberX = x + 3;
            // The current bar is marked with a small play-triangle beside its number in the ruler,
            // instead of a heavy column that reads as if it selected every track.
            if (isCurrent)
            {
                var ty = 5.5;
                var geo = new StreamGeometry();
                using (var g = geo.Open())
                {
                    g.BeginFigure(new Point(x + 3, ty), true, true);
                    g.LineTo(new Point(x + 3, ty + 9), true, false);
                    g.LineTo(new Point(x + 9.5, ty + 4.5), true, false);
                }
                geo.Freeze();
                dc.DrawGeometry(Draw.Solid(_theme.Next), null, geo);
                numberX = x + 11;
            }
            if ((label || isCurrent) && ShowBarNumbers)
            {
                Draw.At(dc, (b + 1).ToString(), numberX, 3, 12,
                    Draw.Solid(isCurrent ? _theme.Next : _theme.Legible), isCurrent);
            }
            if (label)
            {
                dc.DrawLine(Draw.Pen(_theme.BoardEdge, 1), new Point(Math.Round(x) + 0.5, ArrangementPanel.RulerHeight - 6),
                    new Point(Math.Round(x) + 0.5, HideEmptyTimelineGrid ? gridTop : height));
            }
            // Imported master-bar double bars are copied to every track. Show the marker once,
            // quietly in the ruler, instead of joining bright per-track strokes into a false playhead.
            if (project.Tracks.Any(t => b < t.Measures.Count && t.Measures[b].IsDoubleBar))
            {
                var markerX = x + w - 3;
                var markerPen = Draw.Pen(_theme.Muted, 1);
                dc.DrawLine(markerPen, new Point(markerX, 7), new Point(markerX, ArrangementPanel.RulerHeight - 8));
                dc.DrawLine(markerPen, new Point(markerX + 2, 7), new Point(markerX + 2, ArrangementPanel.RulerHeight - 8));
            }
        }
        dc.DrawLine(Draw.Pen(_theme.BoardEdge, 1), new Point(0, ArrangementPanel.RulerHeight - 0.5), new Point(width, ArrangementPanel.RulerHeight - 0.5));

        // --- sections strip (aligned exactly to bar boundaries) ---
        var sections = SectionHits();
        var activeSectionPosition = ActiveSectionIndex(sections, PlayheadBar);
        var activeSectionIndex = activeSectionPosition >= 0
            ? sections[activeSectionPosition].MarkerIndex : -1;
        if (sections.Count == 0 && bars > 0)
        {
            Draw.At(dc, "no sections yet — Sections ▸ Add section…", 8, ArrangementPanel.RulerHeight + 5, 10, Draw.Solid(_theme.Muted));
        }
        foreach (var section in sections)
        {
            if (_sectionDragging && ReferenceEquals(section.Marker, _sectionDragMarker)) continue;
            DrawSectionBlock(dc, section, SectionPreviewBounds(section),
                _sectionSettling && ReferenceEquals(section.Marker, _sectionDragMarker), activeSectionIndex);
        }
        DrawMarkerDragGhost(dc);
        dc.DrawLine(Draw.Pen(_theme.BoardEdge, 1), new Point(0, gridTop - 0.5), new Point(width, gridTop - 0.5));

        // --- track rows ---
        // Draw the floating lane last so it stays above the rows it is sliding over.
        var barGlowOuter = ShowBarGlow ? Draw.Pen(Colors.White, 2, 0.07) : null;
        var barGlowInner = ShowBarGlow ? Draw.Pen(Colors.White, 0.8, 0.16) : null;
        for (var drawIndex = 0; drawIndex < trackCount; drawIndex++)
        {
            var t = DragTrackFrom >= 0 && DragTrackFrom < trackCount && drawIndex == trackCount - 1
                ? DragTrackFrom
                : DragTrackFrom >= 0 && DragTrackFrom < trackCount && drawIndex >= DragTrackFrom
                    ? drawIndex + 1 : drawIndex;
            var track = project.Tracks[t];
            var rowTop = gridTop + ArrangementPanel.RowTopOf(project, t) - VerticalScrollOffset;
            var startsGroup = ArrangementPanel.StartsGroup(project, t);
            void DrawGroupBar(DrawingContext d) =>
                d.DrawRectangle(Draw.Solid(_theme.Board, 0.9), Draw.Pen(_theme.BoardEdge, 0.6, 0.7),
                    new Rect(0, rowTop - ArrangementPanel.GroupHeaderHeight + 3, width, ArrangementPanel.GroupHeaderHeight - 3));
            var collapsedRow = ArrangementPanel.IsCollapsed(project, t);
            // While lanes glide (drag or reorder) the group bar is part of its lane's recording so it glides with it.
            if (startsGroup && (collapsedRow || !_animatingLanes)) DrawGroupBar(dc);
            if (collapsedRow) continue;
            var translation = TrackPreviewTranslation(t);
            if (!_animatingLanes) dc.PushTransform(new TranslateTransform(0, translation));
            // While a track is being dragged every lane is recorded once and then only moved by its
            // transform each frame, instead of re-drawing ~1,200 bar blocks per frame.
            if (_animatingLanes)
            {
                if (_dragLaneScroll != VerticalScrollOffset) { _dragLanes.Clear(); _dragLaneScroll = VerticalScrollOffset; }
                if (!_dragLanes.TryGetValue(t, out var lane))
                {
                    var group = new DrawingGroup();
                    using (var laneDc = group.Open()) DrawLane(laneDc);
                    group.Freeze();
                    lane = group;
                    _dragLanes[t] = lane;
                }
                // Each lane lives in its own retained child visual: a frame only moves its transform, so
                // WPF neither re-sends the lane nor re-walks its ~1,000 rectangles for hit-test bounds.
                PlaceLaneVisual(t, lane, translation, drawIndex);
            }
            else DrawLane(dc);

            void DrawLane(DrawingContext dc)
            {
                if (startsGroup && _animatingLanes) DrawGroupBar(dc);
                var rowRect = new Rect(0, rowTop, width, track.IsAudio ? ArrangementPanel.RowHeightOf(Project, track) : ArrangementPanel.RowHeightFor(Project));
                var highlighted = t == SelectedTrack || t == DragTrackFrom;
                var rowBg = t % 2 == 0 ? _theme.Background : _theme.RowAlt;
                dc.DrawRectangle(Draw.Solid(rowBg), null, rowRect);
                if (ArrangementPanel.TintColour(track, ViewOptions) is { } tint) dc.DrawRectangle(Draw.Solid(tint), null, rowRect);
                if (highlighted)
                    dc.DrawRectangle(Draw.Solid(_theme.Accent, 0.25), null, rowRect);

                var trackColor = Parse(track.ColorHex, _theme.Accent);
                if (Silenced(t)) trackColor = Desaturate(trackColor, Dim);
                for (var b = _animatingLanes ? 0 : firstBar; b < (track.IsAudio ? 0 : _animatingLanes ? bars : endBar); b++)   // an audio track's row shows its clip lanes only: no bar cells
                {
                    var x2 = XOfBar(b);
                    var w = WidthOfBar(b);
                    var cell = new Rect(x2, rowTop + 1, Math.Max(1, w - 1), ArrangementPanel.RowHeightFor(Project) - 2);
                    var hasMeasure = b < track.Measures.Count;
                    var hasContent = false;
                    if (hasMeasure)
                    {
                        var measure = track.Measures[b];
                        var activity = ActivityOf(t, b, measure);
                        hasContent = measure.SimileOneBar || measure.SimileTwoBar || activity.HasContent;
                        if (measure.SimileOneBar || measure.SimileTwoBar)
                        {
                            dc.DrawRectangle(Draw.Solid(_theme.Board, 0.9), null, cell);
                            Draw.Centered(dc, measure.SimileTwoBar ? "𝄌𝄌" : "𝄌", cell.X + cell.Width / 2, cell.Y + 6, 11, Draw.Solid(_theme.Muted));
                        }
                        else if (activity.Notes == 0)
                        {
                            if (HideEmptyTimelineGrid)
                            {
                                // The lane background itself represents an empty bar in clean mode.
                            }
                            else if (ShowIndividualNotes)
                            {
                                // Preserve the detailed empty-cell hatch only in note-detail mode.
                                dc.DrawRectangle(Draw.Solid(_theme.Board, 0.35), null, cell);
                                if (cell.Width > 16)
                                    dc.DrawLine(Draw.Pen(_theme.Past, 0.8), new Point(cell.X + 4, cell.Y + cell.Height / 2), new Point(cell.Right - 4, cell.Y + cell.Height / 2));
                            }
                            else
                            {
                                // Quiet gray empty measure: no note-like center stroke in block mode.
                                dc.DrawRectangle(Draw.Solid(_theme.Board, 0.6), null, cell);
                            }
                        }
                        else
                        {
                            if (ShowContinuousBlocks)
                            {
                                // Draw contiguous runs after the per-measure grid pass below.
                            }
                            else if (ShowIndividualNotes)
                            {
                                dc.DrawRectangle(Draw.Solid(trackColor, Fade(0.30, 0.16, t)), null, cell);
                                DrawMiniature(dc, activity, cell, track, trackColor, Silenced(t) ? Dim : 0);
                            }
                            else
                            {
                                // Default to one readable block per occupied measure. Detailed note
                                // strokes can be restored from the arrangement context menu.
                                var opacity = Fade(0.82, 0.28, t);
                                var block = new Rect(cell.X + 1, cell.Y + 2,
                                    Math.Max(1, cell.Width - 2), Math.Max(1, cell.Height - 4));
                                dc.DrawRoundedRectangle(Draw.Solid(trackColor, opacity), null, block, 2, 2);
                            }
                        }
                        if (measure.RepeatStart) dc.DrawLine(Draw.Pen(trackColor, 2), new Point(cell.X + 1, cell.Y), new Point(cell.X + 1, cell.Bottom));
                        if (measure.RepeatEnd) dc.DrawLine(Draw.Pen(trackColor, 2), new Point(cell.Right - 1, cell.Y), new Point(cell.Right - 1, cell.Bottom));
                    }
                    var showMeasureGrid = !HideEmptyTimelineGrid || hasContent;
                    if (ShowContinuousBlocks && hasContent) showMeasureGrid = false;
                    if (showMeasureGrid)
                        dc.DrawRectangle(null, Draw.Pen(_theme.BoardEdge, 1), new Rect(Math.Round(cell.X) + 0.5, cell.Y, Math.Max(1, Math.Round(cell.Width)), cell.Height));
                    if (hasContent && !ShowContinuousBlocks && barGlowOuter is not null && barGlowInner is not null)
                    {
                        var glowRect = new Rect(cell.X + 0.5, cell.Y + 0.5, Math.Max(1, cell.Width - 1), Math.Max(1, cell.Height - 1));
                        dc.DrawRoundedRectangle(null, barGlowOuter, glowRect, 2.5, 2.5);
                        dc.DrawRoundedRectangle(null, barGlowInner, glowRect, 2.5, 2.5);
                    }
                }
                if (ShowContinuousBlocks)
                    DrawContinuousRuns(dc, t, track, rowTop, trackColor);
                var notationEnd = rowTop + ArrangementPanel.NotationHeightOf(Project, track);
                if (notationEnd > rowTop) dc.DrawLine(Draw.Pen(_theme.BoardEdge, 0.6, 0.7), new Point(0, notationEnd - 0.5), new Point(width, notationEnd - 0.5));
                DrawAudioLane(dc, track, rowTop, width, trackColor);
            }
            if (!_animatingLanes) dc.Pop();
        }
        if (_animatingLanes) TrimLaneVisuals(trackCount);
        // Anything drawn after the lanes must stay above them, so while lanes are child visuals it goes
        // into an overlay child that sits on top of them.
        _overlayGridTop = gridTop; _overlayWidth = width; _overlayHeight = height; _overlayBars = bars;
        // Mix Table points are gathered on a full render only; overlay-only frames reuse the list.
        // The mix points change only with the song: a zoom or scroll redraw reuses them instead of scanning every cell again.
        if (!ReferenceEquals(_mixPointsProject, project) || _mixPointsRevision != project.TimelineRevision)
        {
            _mixPointsProject = project; _mixPointsRevision = project.TimelineRevision;
            _mixPoints.Clear();
            for (var t = 0; t < project.Tracks.Count; t++)
            {
                var measures = project.Tracks[t].Measures;
                for (var b = 0; b < measures.Count; b++)
                    if (measures[b].Cells.Any(c => c.Mix is not null) || measures[b].Voice2Cells.Any(c => c.Mix is not null))
                        _mixPoints.Add((t, b));
            }
        }
        _shiftedFrom = _animatingLanes ? DragTrackFrom : -1;
        using (var overlayDc = OpenLaneOverlay()) DrawLaneOverlay(overlayDc);
    }

    private readonly List<(int Track, int Bar)> _mixPoints = new();
    private SongProject? _mixPointsProject;
    private int _mixPointsRevision = -1;
    private bool[] _audible = Array.Empty<bool>();
    /// <summary>The track is explicitly muted (and not soloed): its lane is drawn grey and dim. Silence implied by another track's solo is not drawn.</summary>
    private bool Silenced(int track) => track >= 0 && track < _audible.Length && !_audible[track] && Project is { } p && track < p.Tracks.Count && p.Tracks[track].Mute;
    private double Dim => Math.Clamp(ViewOptions.MutedDim, 0, 1);
    /// <summary>Opacity of a lane element: <paramref name="normal"/>, moving to <paramref name="silenced"/> as the muted-track dimming setting rises.</summary>
    internal static double FadeBy(double normal, double silenced, double dim) => normal + (silenced - normal) * Math.Clamp(dim, 0, 1);
    private double Fade(double normal, double silenced, int track) => Silenced(track) ? FadeBy(normal, silenced, Dim) : normal;
    private static Color Desaturate(Color c, double amount)
    {
        var g = 0.3 * c.R + 0.59 * c.G + 0.11 * c.B;
        return Color.FromRgb((byte)(c.R + (g - c.R) * amount), (byte)(c.G + (g - c.G) * amount), (byte)(c.B + (g - c.B) * amount));
    }
    private double _overlayGridTop, _overlayWidth, _overlayHeight;
    private int _overlayBars;

    private void DrawLaneOverlay(DrawingContext dc)
    {
        var gridTop = _overlayGridTop; var width = _overlayWidth; var height = _overlayHeight; var bars = _overlayBars;
        // The Add-track lane lives in the overlay: its hover and drag states repaint this small layer, never the whole grid.
        DrawAddLane(dc, width);
        DrawLiveTakes(dc, width);
        DrawSectionHover(dc);

        // --- track drag: the moving lane border is a Canvas overlay above the complete lane rendering. ---
        if (DragTrackFrom >= 0 && DragTrackTo >= 0 && DragTrackTo != DragTrackFrom)
        {
            var dstTop = gridTop + ArrangementPanel.RowTopOf(Project, DragTrackTo) - VerticalScrollOffset + TrackPreviewTranslation(DragTrackTo);
            // Insertion caret on the side the track is heading toward.
            var caretY = DragTrackTo > DragTrackFrom ? dstTop + ArrangementPanel.RowHeightOf(Project, Project?.Tracks.ElementAtOrDefault(DragTrackTo)) : dstTop;
            dc.DrawRectangle(Draw.Solid(_theme.Next), null, new Rect(0, caretY - 1, width, 2));
        }

        // --- current bar ---
        // (marked only by the play-triangle + tinted number in the ruler; no full-height column,
        // so the arrangement no longer looks like the whole track stack is selected)

        // --- score selection mirrored into the timeline ---
        if (ScoreSelectionStart >= 0 && ScoreSelectionEnd >= ScoreSelectionStart && ScoreSelectionEnd < bars)
        {
            var sx = XOfBar(ScoreSelectionStart);
            var sw = XOfBar(ScoreSelectionEnd + 1) - sx;
            dc.DrawRectangle(Draw.Solid(_theme.Accent, 0.12), Draw.Pen(_theme.Accent, 1.2, 0.7),
                new Rect(sx + 0.5, ArrangementPanel.RulerHeight + 0.5, sw - 1,
                    height - ArrangementPanel.RulerHeight - 1));
        }

        // --- selected area / loop region: shown as soon as an area is picked; brighter while looping ---
        if ((LoopEnabled || AreaVisible) && LoopStart >= 0 && LoopEnd >= LoopStart && LoopEnd < bars)
        {
            var sx = XOfBar(LoopStart);
            var sw = XOfBar(LoopEnd + 1) - sx;
            var strength = LoopEnabled ? 1.0 : 0.6;
            dc.DrawRectangle(Draw.Solid(_theme.Accent, 0.10 * strength), Draw.Pen(_theme.Accent, 1.4, strength),
                new Rect(sx + 0.5, ArrangementPanel.RulerHeight + 0.5, sw - 1, height - ArrangementPanel.RulerHeight - 1));
            dc.DrawRectangle(Draw.Solid(_theme.Accent, 0.85 * strength), null, new Rect(sx, ArrangementPanel.RulerHeight, sw, 2));
        }
        // --- Mix Table points: a small red dot at the top-left of the bar in that track's lane ---
        if (_mixPoints.Count > 0)
        {
            var dot = Draw.Solid(Color.FromRgb(0xE0, 0x3B, 0x3B));
            foreach (var (t, b) in _mixPoints)
            {
                var rowTop = gridTop + ArrangementPanel.RowTopOf(Project, t) - VerticalScrollOffset;
                if (b >= bars || rowTop + ArrangementPanel.RowHeightFor(Project) < gridTop || rowTop > height) continue;
                dc.DrawEllipse(dot, null, new Point(XOfBar(b) + 5, rowTop + 6), 3, 3);
            }
        }
        // --- move-area drop caret ---
        if (AreaMove.Active && AreaMove.Target >= 0)
        {
            var cx = XOfBar(Math.Clamp(AreaMove.Target, 0, bars));
            dc.DrawRectangle(Draw.Solid(Color.FromRgb(0x4C, 0xB8, 0xFF)), null, new Rect(cx - 1.5, ArrangementPanel.RulerHeight, 3, height - ArrangementPanel.RulerHeight));
        }
        // --- bars skipped during playback: dim overlay ---
        foreach (var (skipStart, skipEnd) in SkipRanges)
        {
            if (skipStart < 0 || skipStart >= bars) continue;
            var sx = XOfBar(skipStart);
            var sw = XOfBar(Math.Min(skipEnd, bars - 1) + 1) - sx;
            dc.DrawRectangle(Draw.Solid(Colors.Black, 0.45), Draw.Pen(Colors.OrangeRed, 1, 0.7),
                new Rect(sx + 0.5, ArrangementPanel.RulerHeight + 0.5, sw - 1, height - ArrangementPanel.RulerHeight - 1));
        }

        // --- playhead: exact musical position ---
        // (drawn as a cheap overlay by ArrangementPanel, not as part of this cached drawing)
    }

    private ContainerVisual? _laneLayer;
    private readonly List<(DrawingVisual Visual, TranslateTransform Shift, Drawing? Content)> _laneVisuals = new();

    // Always-present overlay child (loop / selected area / skip / move caret / insertion caret): redrawn on
    // its own, so changing the selection never re-renders the thousands of lane blocks underneath.
    private DrawingVisual? _overlayVisual;
    private DrawingVisual OverlayVisual
    {
        get
        {
            if (_overlayVisual is null) { _overlayVisual = new DrawingVisual(); AddVisualChild(_overlayVisual); }
            return _overlayVisual;
        }
    }
    protected override int VisualChildrenCount => (_laneLayer is null ? 0 : 1) + (_overlayVisual is null ? 0 : 1);
    protected override Visual GetVisualChild(int index) => _laneLayer is not null && index == 0 ? _laneLayer : _overlayVisual!;

    public void RefreshOverlay()
    {
        if (Project is null || _overlayWidth <= 0) { InvalidateVisual(); return; }
        using var dpiScope = Draw.UseDpi(this);
        using var dc = OverlayVisual.RenderOpen();
        DrawLaneOverlay(dc);
    }

    private void PlaceLaneVisual(int track, Drawing lane, double translation, int drawIndex)
    {
        if (_laneLayer is null)
        {
            // Re-add the overlay after the lanes so it stays on top.
            var overlay = OverlayVisual;
            RemoveVisualChild(overlay);
            _laneLayer = new ContainerVisual();
            AddVisualChild(_laneLayer);
            AddVisualChild(overlay);
        }
        while (_laneVisuals.Count <= drawIndex)
        {
            var shift = new TranslateTransform();
            // Each lane is a GPU texture while it slides: the render thread moves a bitmap instead of
            // re-rasterising a thousand bar blocks per lane on every display refresh.
            var visual = new DrawingVisual
            {
                Transform = shift,
                CacheMode = new BitmapCache(VisualTreeHelper.GetDpi(this).PixelsPerDip) { EnableClearType = true, SnapsToDevicePixels = true },
            };
            _laneLayer.Children.Add(visual);
            _laneVisuals.Add((visual, shift, null));
        }
        // Slot order is draw order, so the floating lane (drawn last) stays on top.
        var slot = _laneVisuals[drawIndex];
        if (!ReferenceEquals(slot.Content, lane))
        {
            using (var laneDc = slot.Visual.RenderOpen()) laneDc.DrawDrawing(lane);
            _laneVisuals[drawIndex] = (slot.Visual, slot.Shift, lane);
        }
        if (slot.Shift.Y != translation) slot.Shift.Y = translation;
    }

    private void TrimLaneVisuals(int count)
    {
        while (_laneVisuals.Count > count)
        {
            _laneLayer!.Children.Remove(_laneVisuals[^1].Visual);
            _laneVisuals.RemoveAt(_laneVisuals.Count - 1);
        }
    }

    private DrawingContext OpenLaneOverlay() => OverlayVisual.RenderOpen();

    private void ReleaseLaneVisuals()
    {
        if (_laneLayer is null) return;
        RemoveVisualChild(_laneLayer);
        _laneLayer = null;
        _laneVisuals.Clear();
    }

    private void DrawContinuousRuns(DrawingContext dc, int trackIndex, TrackModel track, double rowTop, Color trackColor)
    {
        var runStart = -1;
        var glowOuter = ShowBarGlow ? Draw.Pen(Colors.White, 4, 0.055) : null;
        var glowInner = ShowBarGlow ? Draw.Pen(Colors.White, 2, 0.10) : null;
        var (first, end) = _animatingLanes ? (0, BarCount) : DrawnBars(BarCount);   // only the runs near the visible span
        for (var bar = first; bar <= end; bar++)
        {
            var occupied = bar < end && bar < track.Measures.Count &&
                (track.Measures[bar].SimileOneBar || track.Measures[bar].SimileTwoBar ||
                 ActivityOf(trackIndex, bar, track.Measures[bar]).HasContent);
            if (occupied)
            {
                if (runStart < 0) runStart = bar;
                continue;
            }

            if (runStart < 0) continue;
            var x = XOfBar(runStart) + 1;
            var endX = XOfBar(bar) - 1;
            const double lineThickness = 9;
            var line = new Rect(x, rowTop + (ArrangementPanel.RowHeightFor(Project) - lineThickness) / 2,
                Math.Max(1, endX - x), lineThickness);
            if (glowOuter is not null && glowInner is not null)
            {
                dc.DrawRoundedRectangle(null, glowOuter, line, lineThickness / 2, lineThickness / 2);
                dc.DrawRoundedRectangle(null, glowInner, line, lineThickness / 2, lineThickness / 2);
            }
            dc.DrawRoundedRectangle(Draw.Solid(trackColor, Fade(0.96, 0.38, trackIndex)), null, line, lineThickness / 2, lineThickness / 2);
            runStart = -1;
        }
    }

    private double TrackPreviewTranslation(int trackIndex)
    {
        if (trackIndex >= 0 && trackIndex < _trackPreviewOffsets.Length)
            return _trackPreviewOffsets[trackIndex];
        if (DragTrackFrom < 0 || DragTrackFrom >= (Project?.Tracks.Count ?? 0) || DragTrackTo < 0)
            return 0;
        if (trackIndex == DragTrackFrom) return DragTrackDeltaY;
        if (DragTrackFrom < DragTrackTo && trackIndex > DragTrackFrom && trackIndex <= DragTrackTo)
            return -MovingRowHeight;
        if (DragTrackFrom > DragTrackTo && trackIndex >= DragTrackTo && trackIndex < DragTrackFrom)
            return MovingRowHeight;
        return 0;
    }

    /// <summary>Height of the row being dragged (rows with an audio lane are taller).</summary>
    private double MovingRowHeight => ArrangementPanel.RowHeightOf(Project, Project?.Tracks.ElementAtOrDefault(DragTrackFrom));

    internal readonly record struct MiniatureEvent(int CellIndex, double ConsumedSlots, int NoteCount,
        int[] DrumVelocities);
    internal readonly record struct Activity(int Notes, int Beats, int Rests, int CellCount, bool HasContent,
        MiniatureEvent[] MiniatureEvents);
    private readonly record struct ActivityCacheEntry(MeasureModel? Measure, int Generation, Activity Value);

    private Activity ActivityOf(int trackIndex, int bar, MeasureModel measure)
    {
        if (_activityCache is null || _activityGenerations is null || trackIndex < 0 ||
            trackIndex >= _activityCache.Length || bar < 0 || bar >= _activityCache[trackIndex].Length)
            return BuildActivity(measure, _activityTracks is not null && trackIndex >= 0 &&
                trackIndex < _activityTracks.Length && _activityTracks[trackIndex].Kind == TrackKind.Drums);

        var generation = _activityGenerations[trackIndex][bar];
        var cached = _activityCache[trackIndex][bar];
        if (ReferenceEquals(cached.Measure, measure) && cached.Generation == generation)
            return cached.Value;

        var isDrums = _activityTracks is not null && trackIndex < _activityTracks.Length &&
                      _activityTracks[trackIndex].Kind == TrackKind.Drums;
        var activity = BuildActivity(measure, isDrums);
        _activityCache[trackIndex][bar] = new ActivityCacheEntry(measure, generation, activity);
        return activity;
    }

    private static Activity BuildActivity(MeasureModel measure, bool isDrums)
    {
        var notes = 0;
        var beats = 0;
        var rests = 0;
        foreach (var cell in measure.Cells)
        {
            if (cell.IsRest) { rests++; continue; }
            if (cell.Notes.Count == 0) continue;
            notes += cell.Notes.Count;
            beats++;
        }

        var miniature = new List<MiniatureEvent>();
        var index = 0;
        while (index < measure.Cells.Count)
        {
            var cell = measure.Cells[index];
            var consumed = MusicTime.ConsumeSlots(cell);
            if (!cell.IsRest && cell.Notes.Count > 0)
            {
                int[] velocities;
                if (isDrums)
                {
                    var count = Math.Min(4, cell.Notes.Count);
                    velocities = new int[count];
                    for (var note = 0; note < count; note++)
                        velocities[note] = cell.Notes[note].Velocity;
                }
                else velocities = Array.Empty<int>();
                miniature.Add(new MiniatureEvent(index, consumed, cell.Notes.Count, velocities));
            }
            index += Math.Max(1, (int)Math.Ceiling(consumed - 0.001));
        }
        return new Activity(notes, beats, rests, measure.Cells.Count, notes > 0, miniature.ToArray());
    }

    private static void DrawMiniature(DrawingContext dc, Activity activity, Rect cell, TrackModel track, Color trackColor, double dim)
    {
        var slots = Math.Max(1, activity.CellCount);
        var slotWidth = cell.Width / slots;
        var isDrums = track.Kind == TrackKind.Drums;
        foreach (var item in activity.MiniatureEvents)
        {
            var x = cell.X + item.CellIndex * slotWidth;
            if (isDrums)
            {
                for (var note = 0; note < item.DrumVelocities.Length; note++)
                {
                    var height = 6 + Math.Min(8, item.DrumVelocities[note] / 14);
                    dc.DrawRectangle(Draw.Solid(trackColor, 0.95 + (0.35 - 0.95) * dim), null,
                        new Rect(x + 1, cell.Bottom - 4 - height, Math.Max(1.5, Math.Min(4, slotWidth - 2)), height));
                }
            }
            else
            {
                var width = Math.Max(1.5, item.ConsumedSlots * slotWidth - 1.5);
                var height = Math.Min(cell.Height - 8, 5 + item.NoteCount * 3);
                var y = cell.Y + (cell.Height - height) / 2;
                dc.DrawRoundedRectangle(Draw.Solid(trackColor, 0.95 + (0.35 - 0.95) * dim), null,
                    new Rect(x + 1, y, width, height), 1.5, 1.5);
            }
        }
        if (dim > 0) dc.DrawRectangle(Draw.Solid(Colors.Black, 0.25 * dim), null, cell);
    }

    private static Color Parse(string hex, Color fallback) =>
        ColourText.TryParse(hex, out var colour) ? Draw.Tame(colour) : fallback;
}
