using TabForge.Avalonia.Views;
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using TabForge.Avalonia.Features.Recording.Presenter;
using TabForge.Avalonia.Features.Recording.View;
using TabForge.Avalonia.Platform;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Platform;

/// <summary>
/// The timeline body's drawing: the ruler, section strip, master lane, track rows and bar cells, the interaction layers (hover, selection,
/// section and row drags), the section brackets and glow, the bar marker, and the playhead layer's arrangement; rows outside the band and
/// bars outside the viewport are culled.
/// Owns: the drawing passes and their constants, and the per-track colour and per-bar activity caches they read. Does not own: the layout and
/// hit tests (TimelineView), the display settings (TimelineView.Settings), the playhead's moving layers (TimelinePlayheadLayer) or the rules (Core).
/// Tests: TabForge.Avalonia.Tests.TimelineViewTests.
/// </summary>
public sealed partial class TimelineView
{
    /// <summary>The edge width in DIPs is the setting divided by this: 10 gives about 2.8 DIP. Eye-matched, not derived: WPF's code strokes the full setting.</summary>
    internal const double SectionBracketThicknessDivisor = 3.6;

    /// <summary>The section bracket's arms (18% of the section's width, kept between 8 and 18 DIPs) and the narrowest section it draws.</summary>
    private const double BracketArmShare = 0.18, BracketArmMin = 8, BracketArmMax = 18, BracketMinWidth = 4;
    private const double SectionGlowActiveShare = 0.60;
    private const double SectionGlowHaloWidth = 7, SectionGlowHaloShare = 0.18, SectionGlowEdgeWidth = 2;
    private const double BarGlowOuterWidth = 2, BarGlowOuterOpacity = 0.07, BarGlowInnerWidth = 0.8, BarGlowInnerOpacity = 0.16;

    // The thin outline around a clip box (the ClipOutline palette token), so a box of any colour (even black) stays visible.

    public override void Render(DrawingContext context) => DrawBody(_draw.Use(context), ActualThemeVariant == ThemeVariant.Light);

    /// <summary>Draws the body: the ruler, sections, master lane, track rows and interaction. Rows outside the band and bars outside the viewport are not drawn.</summary>
    internal void DrawBody(IDrawContext d, bool light)
    {
        _frameLight = light;
        var ink = AvaloniaPalette.InkOf(light);
        _frameClipOutline = ink("ClipOutline");
        _frameBoard = ink("TimelineLane").WithOpacity(EmptyCellOpacity);
        var gridTop = RulerHeight + SectionHeight;
        var width = Math.Max(Bounds.Width, _boundaries[^1]);
        var edge = new UiStroke(ink("TimelineLine"), 1);
        d.Rect(ink("TimelineRow"), null, 0, 0, width, Bounds.Height);
        d.Rect(ink("TimelineSectionLane"), null, 0, RulerHeight, width, SectionHeight);
        DrawRuler(d, ink, edge, width);
        DrawSections(d, light);
        d.Line(edge, 0, gridTop - 0.5, width, gridTop - 0.5);
        d.PushClip(0, gridTop, width, Math.Max(0, Bounds.Height - gridTop));
        MasterLaneStrip.Draw(d, ink, _song, _boundaries, gridTop, MasterLaneHeight, width, _masterLaneActivity, _masterLook);
        // The rows scroll under the master lane and above the pinned strip, so they clip to that band.
        d.PushClip(0, TrackRowsTop, width, Math.Max(0, TrackRowsBottom - TrackRowsTop));
        for (var row = 0; row < _rows.Count; row++) if (row != DragRow && RowInBand(row)) DrawRow(d, ink, edge, row, width);
        if (DragRow >= 0 && DragRow < _rows.Count) DrawRow(d, ink, edge, DragRow, width);
        d.Pop();
        DrawInteraction(d, ink, gridTop);
        d.Pop();
        if (LoopStart >= 0 && LoopEnd >= LoopStart && LoopStart < BarCount)   // the bracket sits on the ruler, so it draws outside the lanes' clip
            TimelineLoopMarks.Draw(d, ink, BarX(LoopStart), TimelineGeometryRules.RightOfBar(_boundaries, LoopEnd), RulerHeight, gridTop, Bounds.Height, LoopOn);
        DrawActiveSectionBrackets(d, ink, gridTop);
        if (PlayheadBar >= 0 && PlayheadBar < BarCount && PlayheadStyles.ShowsBarMarker(_playheadStyle)) DrawBarMarker(d, ink);
        DrawSectionDrag(d, ink, gridTop);
        BodyRenderCount++;
    }

    /// <summary>How many times the body drew; a playhead frame never adds one.</summary>
    internal int BodyRenderCount { get; private set; }

    /// <summary>The playhead's own layers (line, flag, arrow, bar number), moved by a transform so a frame never redraws the body.</summary>
    internal TimelinePlayheadLayer PlayheadLayer { get; }

    /// <summary>The takes being recorded: the recording feeds them, the overlay layer draws them in their lanes.</summary>
    internal LiveTakes LiveTakes { get; private set; } = new();

    /// <summary>
    /// Raised after a clip or lane edit of this timeline landed (a drag/trim/fade, a clip command, a take click): the window compacts empty
    /// lanes when the setting says so and syncs the engine down its normal path, as WPF's ClipEditController.Changed does.
    /// </summary>
    internal Action<IEditableDocument>? ClipEdited { get; set; }

    /// <summary>Shows a short status notice for a clip gesture that is refused (a trim or fade on a loop take group).</summary>
    internal Action<string>? ClipNotice { get; set; }

    /// <summary>
    /// The lane edit cursor: the track, lane and song second of the last press in a clip lane, as WPF's ClipEditController.LaneCursor. Clip.
    /// Split prefers it when it lies inside the selected clip; otherwise the playhead is used. Null before any clip-lane press.
    /// </summary>
    internal TimelineLaneCursor? LaneCursor { get; set; }

    /// <summary>The selection this timeline draws and the edit keys route by: the shown document's (the input controller gives it); a timeline with no document keeps its own.</summary>
    internal SelectionModel FocusModel { get; set; }

    // The theme the frame being drawn uses, read once by Render: a theme flip mid-frame cannot mix paper and rims.
    private bool _frameLight;

    /// <summary>Whether the frame being drawn is the light theme (the clip painters key off it).</summary>
    internal bool FrameLight => _frameLight;

    // The colours every bar cell reads, looked up once per body draw instead of once per cell.
    private UiColor _frameClipOutline, _frameBoard;
    private const double EmptyCellOpacity = 0.6;

    /// <summary>True while the last press was on a track row (not the ruler or the empty grid below the rows): the focus target is TrackRow. Reads and writes the model's Focus.</summary>
    internal bool TrackRowFocused
    {
        get => FocusModel.Focus == FocusTarget.TrackRow;
        set
        {
            if (value) FocusModel.SetFocus(FocusTarget.TrackRow);
            else if (FocusModel.Focus == FocusTarget.TrackRow) FocusModel.SetFocus(FocusTarget.None);
        }
    }

    /// <summary>The layer that draws the live takes over the body; a recording frame invalidates only this layer.</summary>
    internal TimelineLiveTakeLayer LiveTakeLayer { get; }

    /// <summary>Shows <paramref name="takes"/> (the window's recording state) instead of this timeline's own; a fresh recording state for a new pane.</summary>
    internal void UseLiveTakes(LiveTakes takes)
    {
        if (ReferenceEquals(LiveTakes, takes)) return;
        LiveTakes = takes;
        LiveTakeLayer.Takes = takes;
        LiveTakeLayer.InvalidateVisual();
    }

    /// <summary>Redraws the live-take overlay only (one recording frame); the timeline body is untouched.</summary>
    internal void RefreshLiveTakes() => LiveTakeLayer.InvalidateVisual();

    protected override Size ArrangeOverride(Size finalSize)
    {
        PlayheadLayer.Arrange(new Rect(0, 0, TimelinePlayheadLayer.LineWidth, finalSize.Height));
        PlayheadLayer.Place();
        PlayheadLayer.Redraw();
        PlayheadLayer.NumberLayer.Arrange(new Rect(finalSize));
        LiveTakeLayer.Arrange(new Rect(finalSize));
        return finalSize;
    }

    /// <summary>The marker of the bar style: a small square centred in the played bar's cell on the selected track's row (or the first row), as WPF draws it.</summary>
    private void DrawBarMarker(IDrawContext d, Func<string, UiColor> ink)
    {
        if (_rows.Count == 0) return;
        var row = _selectedRow >= 0 && _selectedRow < _rows.Count ? _selectedRow : 0;
        var cellLeft = BarX(PlayheadBar);
        var cellWidth = _boundaries[PlayheadBar + 1] - cellLeft;
        var cellTop = RowTop(row);
        var side = Math.Min(Math.Round(RowHeight * BarMarkerSideShare), Math.Max(4, cellWidth - 2));
        var left = cellLeft + (cellWidth - side) / 2;
        var top = cellTop + (RowHeight - side) / 2;
        var stroke = new UiStroke(ink("OverlayInk").WithOpacity(BarMarkerStrokeOpacity), BarMarkerStrokeWidth);
        d.Rect(ink(BarMarkerFillKey), stroke, left, top, side, side, Math.Max(BarMarkerMinCorner, side * BarMarkerCornerShare));
    }

    private const string BarMarkerFillKey = "BarMarkerFill";
    private const double BarMarkerSideShare = 0.45, BarMarkerCornerShare = 0.22, BarMarkerMinCorner = 2;
    private const double BarMarkerStrokeOpacity = 0.8, BarMarkerStrokeWidth = 1.25;

    /// <summary>The top of the selected row's lane, or -1 when no row is selected.</summary>
    internal double ArrowLaneTop() => _selectedRow >= 0 && _selectedRow < _rows.Count ? RowTop(_selectedRow) : -1;

    /// <summary>The [ ] brackets around the section that holds the playhead bar, in the section's colour, from the section strip down (as the WPF timeline draws them).</summary>
    internal void DrawActiveSectionBrackets(IDrawContext d, Func<string, UiColor> ink, double gridTop)
    {
        if (!ShowSectionBrackets || PlayheadBar < 0) return;
        var index = ActiveSectionIndex(PlayheadBar);
        if (index < 0) return;
        var start = Math.Clamp(_sections[index].MeasureIndex, 0, BarCount);
        var end = SectionLayout.End(_sections, index, BarCount);
        var left = BarX(start);
        var width = Math.Max(BracketMinWidth, BarX(end) - left);
        var right = left + width;
        var arm = Math.Min(BracketArmMax, Math.Max(BracketArmMin, width * BracketArmShare));
        var bottom = Bounds.Height;
        var stroke = new UiStroke(_sectionColours[index].ToUi(), _sectionBracketThickness / SectionBracketThicknessDivisor);
        // Each bracket: a vertical edge over the full lane height, with a short arm at the top and bottom pointing into the section.
        d.Line(stroke, left, gridTop, left, bottom);
        d.Line(stroke, left, gridTop, left + arm, gridTop);
        d.Line(stroke, left, bottom, left + arm, bottom);
        d.Line(stroke, right, gridTop, right, bottom);
        d.Line(stroke, right, gridTop, right - arm, gridTop);
        d.Line(stroke, right, bottom, right - arm, bottom);
    }

    /// <summary>The section whose bars hold <paramref name="bar"/>, or -1.</summary>
    private int ActiveSectionIndex(int bar)
    {
        for (var i = 0; i < _sections.Count; i++)
        {
            var start = Math.Clamp(_sections[i].MeasureIndex, 0, BarCount);
            var end = SectionLayout.End(_sections, i, BarCount);
            if (end > start && bar >= start && bar < end) return i;
        }
        return -1;
    }

    private void DrawInteraction(IDrawContext d, Func<string, UiColor> ink, double gridTop)
    {
        DrawHighlight(d, ink);
        if (Marquee is { } box) MarqueePainter.Draw(d, ink, box.X, box.Y, box.Width, box.Height);
        if (DragRow >= 0) return;
        if (HoverMasterBar >= 0 && HoverMasterBar < BarCount)
            d.Rect(HoverShade.CellFill(_frameLight), null, BarX(HoverMasterBar), RulerHeight + SectionHeight + 1, _boundaries[HoverMasterBar + 1] - BarX(HoverMasterBar), MasterLaneHeight - 2, HoverShade.CellRadius);
        if (HoverBar >= 0 && HoverBar < BarCount && HoverRow >= 0 && HoverRow < _rows.Count)
            d.Rect(HoverShade.CellFill(_frameLight), null, BarX(HoverBar), RowTop(HoverRow) + 1, _boundaries[HoverBar + 1] - BarX(HoverBar), RowHeight - 2, HoverShade.CellRadius);
        TimelineAudioClips.DrawFileDropPreview(d, ink, this, Math.Max(Bounds.Width, _boundaries[^1]));
        TimelineRepeatPicker.DrawPopovers(d, this, ink);
    }

    private void DrawRuler(IDrawContext d, Func<string, UiColor> ink, UiStroke edge, double width)
    {
        var barWidth = BarCount > 0 ? _boundaries[^1] / BarCount : BarUnitWidth * _zoomFactor;
        var labelEvery = barWidth < 18 ? 8 : barWidth < 28 ? 4 : 1;
        var (firstBar, endBar) = VisibleBarRange(BarCount);
        for (var bar = firstBar - firstBar % labelEvery; bar < endBar; bar += labelEvery)
        {
            var x = BarX(bar);
            if (_showBarNumbers && (bar != PlayheadBar || _compact))
                d.DrawText(d.Text((bar + 1).ToString(), Font, TimelinePlayheadMarks.BarNumberSize, ink("InstrumentLegible"), d.PixelsPerDip), x + 3, TimelinePlayheadMarks.BarNumberTop);
            d.Line(edge, Math.Round(x) + 0.5, RulerHeight - 6, Math.Round(x) + 0.5, RulerHeight - 0.5);
            d.Line(edge, Math.Round(x) + 0.5, RulerHeight + SectionHeight, Math.Round(x) + 0.5, Bounds.Height);
        }
        DrawGhostRuler(d, ink, edge);
        d.Line(edge, 0, RulerHeight - 0.5, width, RulerHeight - 0.5);
    }

    private void DrawSections(IDrawContext d, bool light)
    {
        var accent = AvaloniaPalette.Get("InstrumentAccent", light);
        for (var i = 0; i < _sections.Count; i++)
        {
            var start = Math.Clamp(_sections[i].MeasureIndex, 0, BarCount);
            var end = SectionLayout.End(_sections, i, BarCount);
            if (end <= start || i == DragSection) continue;
            var colour = _sectionColours[i].ToUi();
            var x = BarX(start);
            var w = Math.Max(2, BarX(end) - x - 2);
            var y = RulerHeight + 1;
            if (SectionPreviewX is { } preview && SectionSlotIndex(i) is var slot and >= 0) x = preview[slot];
            var section = _sections[i];
            var inset = SectionButtons.PressDepth(section) * SectionButtonFeedback.PressedInsetDips;
            var left = x + 1 + inset;
            var top = y + inset;
            var width = Math.Max(0, w - 2 * inset);
            var height = SectionHeight - 4 - 2 * inset;
            d.Rect(colour.WithOpacity(SectionFillOf(light)), new UiStroke(colour, 1), left, top, width, height, 3);
            if (ReferenceEquals(SectionButtons.Hovered, section)) d.Rect(HoverShade.Overlay(light), null, left, top, width, height, 3);
            if (SectionActive(i)) DrawSectionGlow(d, colour, x + 1, y, w, _sectionGlowIntensity * SectionGlowActiveShare);
            var flash = SectionButtons.FlashAlpha(section);
            if (flash > 0) DrawSectionGlow(d, accent, x + 1, y, w, flash);
            if (!_showSectionNames || w <= 34) continue;
            d.PushClip(x + 1, y, w, SectionHeight - 4);
            d.DrawText(d.Text(_sections[i].Title, Font, 11, LabelInk(colour, AvaloniaPalette.Get("TimelineRow", light)), d.PixelsPerDip, weight: 600, outline: SectionLabelOutline), x + 6, y + 2);
            d.Pop();
        }
    }

    // Scratch lists of DrawSectionDrag, reused every frame (cleared, never reallocated). Thread: UI thread only.
    private readonly List<int> _dragCounts = new(), _dragFirstBars = new();

    /// <summary>The lifted column of the dragged section, the drop line and the shifted bar numbers (drawn over the lanes, under the playhead).</summary>
    private void DrawSectionDrag(IDrawContext d, Func<string, UiColor> ink, double gridTop)
    {
        if (DragSection < 0 || DragSection >= _sections.Count) return;
        var start = Math.Clamp(_sections[DragSection].MeasureIndex, 0, BarCount);
        var end = SectionLayout.End(_sections, DragSection, BarCount);
        if (end <= start) return;
        var colour = _sectionColours[DragSection].ToUi();
        if (SectionPreviewX is { } preview && SectionRestingX is { } resting)
        {
            var counts = _dragCounts;
            var firstBars = _dragFirstBars;
            counts.Clear();
            firstBars.Clear();
            for (var i = 0; i < _sections.Count; i++)
            {
                var first = Math.Clamp(_sections[i].MeasureIndex, 0, BarCount);
                var last = SectionLayout.End(_sections, i, BarCount);
                if (last <= first) continue;
                counts.Add(last - first);
                firstBars.Add(first);
            }
            var newFirst = SectionDragVisual.NewFirstBars(counts, resting);
            for (var slot = 0; slot < newFirst.Length; slot++)
                if (newFirst[slot] != firstBars[slot]) SectionDragVisual.DrawShiftedNumber(d, ink, preview[slot], newFirst[slot], colour);
            SectionDragVisual.DrawDropLine(d, ink, SectionDropX, colour, Bounds.Height);
        }
        SectionDragVisual.DrawColumn(d, ink, this, _song, start, end, DragSectionX, colour, _sections[DragSection].Title, SectionGlowIntensity, gridTop);
    }

    private bool SectionActive(int index) => PlayheadBar >= 0 && ActiveSectionIndex(PlayheadBar) == index;

    /// <summary>The glow round the section that holds the playhead: a wide faint halo and a thin edge, in the section's colour.</summary>
    private void DrawSectionGlow(IDrawContext d, UiColor colour, double x, double y, double w, double intensity)
    {
        if (intensity <= 0) return;
        d.Rect(null, new UiStroke(colour.WithOpacity(intensity * SectionGlowHaloShare), SectionGlowHaloWidth), x - 1, y - 1, w + 2, SectionHeight - 2, 4);
        d.Rect(null, new UiStroke(colour.WithOpacity(intensity), SectionGlowEdgeWidth), x, y, w, SectionHeight - 4, 3);
    }

    /// <summary>Position of section <paramref name="markerIndex"/> in the slot array (sections with no bars have no slot), or -1.</summary>
    private int SectionSlotIndex(int markerIndex)
    {
        var slot = 0;
        for (var i = 0; i < markerIndex; i++)
            if (SectionLayout.End(_sections, i, BarCount) > Math.Clamp(_sections[i].MeasureIndex, 0, BarCount)) slot++;
        return slot;
    }

    private void DrawRow(IDrawContext d, Func<string, UiColor> ink, UiStroke edge, int row, double width)
    {
        var track = _song.Tracks[row];
        var top = RowDrawTop(row);
        var height = RowHeightOf(row);
        d.Rect(ink(row % 2 == 0 ? "TimelineRow" : "TimelineRowAlt"), null, 0, top, width, height);
        TrackRowTint.Fill(d, track, _frameLight, TrackTintPercent, 0, top, width, height);
        if (row == _selectedRow && _shadeSelectedTrack || IsRowPicked(row)) TimelinePlayheadMarks.DrawLaneTint(d, ink, _frameLight, top, width, height);
        TrackRowTint.Stripe(d, track, _frameLight, double.IsNaN(VisibleSpan.From) ? 0 : Math.Max(0, VisibleSpan.From), top, height);
        if (_showTrackLines) d.Line(edge, 0, top + height - 0.5, width, top + height - 0.5);
        if (IsArmed(row) && track.AudioClips.Count == 0) ArmedRowPainter.DrawLaneHint(d, ink, track, top + BaseBandHeightOf(row));
        var colour = TrackColourOf(track, ink("InstrumentAccent"));
        var muted = track.Mute && !track.Solo;
        if (track.IsAudio) { TimelineAudioClips.DrawRow(d, ink, this, track, row, top + NotationHeightOf(row), colour); return; }
        DrawnRowCount++;
        var (firstBar, endBar) = VisibleBarRange(Math.Min(BarCount, track.Measures.Count));
        var runStart = -1;
        for (var bar = firstBar; bar < endBar; bar++)
        {
            var x = BarX(bar);
            var cellWidth = Math.Max(1, _boundaries[bar + 1] - x - 1);
            var activity = ActivityOf(track, row, bar);
            if (InViewport(x, cellWidth)) DrawBarCell(d, ink, edge, track.Measures[bar], track.Kind == TrackKind.Drums, colour, muted, top, x, cellWidth, activity);
            // The continuous run follows every bar, even those outside the viewport, so its ends stay where the song puts them.
            if (ShowContinuousBlocks)
            {
                if (activity.Notes > 0) runStart = runStart < 0 ? bar : runStart;
                else if (runStart >= 0) { DrawContinuousRun(d, runStart, bar, top, colour); runStart = -1; }
            }
        }
        if (ShowContinuousBlocks && runStart >= 0) DrawContinuousRun(d, runStart, endBar, top, colour);
        // An instrument track's lane bands sit under its notation band (MIDI takes recorded on it).
        TimelineAudioClips.DrawRow(d, ink, this, track, row, top + NotationHeightOf(row), colour);
    }

    /// <summary>Bars drawn in track rows so far (a test reads it to check the culling): one per bar that the viewport showed.</summary>
    internal int DrawnBarCount { get; private set; }

    /// <summary>Track rows (not audio rows) drawn so far, with their bars; a test reads it next to <see cref="DrawnBarCount"/>.</summary>
    internal int DrawnRowCount { get; private set; }

    /// <summary>One bar's cell in a track row: the notes, the block or the empty grid, the repeat marks and the outline.</summary>
    private void DrawBarCell(IDrawContext d, Func<string, UiColor> ink, UiStroke edge, MeasureModel measure, bool drums, UiColor colour, bool muted,
        double top, double x, double cellWidth, TimelineActivityRules.Activity activity)
    {
        DrawnBarCount++;
        var empty = activity.Notes == 0;
        var cellX = x + 1;
        var cellY = top + 3;
        var cellDrawWidth = Math.Max(1, cellWidth - 2);
        // With the grid hidden (the default) an empty bar is the bare lane: no cell and no outline, but its repeat marks still draw.
        if (empty)
        {
            if (!HideEmptyGrid) d.Rect(_frameBoard, null, x, top + 1, cellWidth, RowHeight - 2);
        }
        else if (ShowIndividualNotes)
        {
            d.Rect(colour.WithOpacity(TimelineNoteMarks.NotesCellOpacity), new UiStroke(_frameClipOutline, 1), cellX, cellY, cellDrawWidth, RowHeight - 6, 2);
            TimelineNoteMarks.DrawNotes(d, activity, drums, cellX, cellY, cellDrawWidth, RowHeight - 6, colour.WithOpacity(TimelineNoteMarks.NoteMarkOpacity));
        }
        else if (!ShowContinuousBlocks)
        {
            if (_barGlow) DrawBarGlow(d, ink, cellX, cellY, cellDrawWidth);
            d.Rect(colour.WithOpacity(BlockOpacity(muted)), new UiStroke(_frameClipOutline, 1), cellX, cellY, cellDrawWidth, RowHeight - 6, 2);
        }
        if (measure.RepeatStart) d.Line(new UiStroke(colour, 2), x + 1, top + 1, x + 1, top + RowHeight - 1);
        if (measure.RepeatEnd) d.Line(new UiStroke(colour, 2), x + cellWidth - 1, top + 1, x + cellWidth - 1, top + RowHeight - 1);
        // In the light theme a note's outline is its own colour at the fill's opacity, so the pill has no dark rim (the WPF light look).
        var rim = !empty && _frameLight ? new UiStroke(colour.WithOpacity(BlockOpacity(muted)), 1) : edge;
        // The continuous line replaces the outline of the bars it covers, as in WPF.
        if (empty ? !HideEmptyGrid : !ShowContinuousBlocks) d.Rect(null, rim, Math.Round(x) + 0.5, top + 1, Math.Max(1, Math.Round(cellWidth)), RowHeight - 2);
    }

}
