using System;
using System.Collections.Generic;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Platform;

/// <summary>
/// Owns: how a dragged section looks on the arrangement timeline: the lifted column (a bright raised card in the section colour with a light
/// outline and glow, a translucent wash over the lanes with every track's notes drawn through it), the drop line and the shifted bar numbers in the ruler.
/// Does not own: the drag state (TimelineView, TimelineInputController), the sliding of the other sections (SectionDragAnimator).
/// Tests: TimelineSectionDragTests.
/// </summary>
internal static class SectionDragVisual
{
    private const double CardOpacity = 0.92;
    private const double CardOutlineWidth = 1.5;
    private const double WashOpacity = 0.20;
    private const double GhostEmptyOpacity = 0.12;
    private const double GhostNoteOpacity = 0.70;
    private const double GlowSpread = 8;
    private const double GlowOpacityScale = 0.5;
    private const double DropLineWidth = 2.5;
    private const double DropLineGlowWidth = 8;
    private const double DropLineGlowOpacity = 0.25;
    private const double DropDotRadius = 5;
    private const double ShiftedNumberSize = 9;
    private const double ShiftedNumberOffsetX = 18;
    private const double ShiftedNumberY = 5;
    private const int ShiftedNumberWeight = 700;
    private const int TitleWeight = 600;
    private const double TitleSize = 11;
    private const double CardRadius = 3;
    private const double WashRadius = 4;

    /// <summary>The lifted column of the section spanning bars <paramref name="start"/> up to <paramref name="end"/> at left edge <paramref name="x"/>.</summary>
    public static void DrawColumn(IDrawContext d, Func<string, UiColor> ink, TimelineView view, SongProject song, int start, int end,
        double x, UiColor colour, string title, double glow, double gridTop)
    {
        var originalX = view.BarX(start);
        var width = Math.Max(2, view.BarX(end) - originalX - 2);
        var height = Math.Max(0, view.Bounds.Height - gridTop);
        var white = ink("White");

        d.Rect(colour.WithOpacity(WashOpacity), null, x, gridTop, width + 2, height, WashRadius);
        d.PushClip(x, gridTop, width + 2, height);
        d.PushTranslate(x - originalX, 0);
        for (var row = 0; row < song.Tracks.Count; row++)
            DrawGhostRow(d, view, song.Tracks[row], row, start, end, colour);
        d.Pop();
        d.Pop();

        var y = TimelineView.RulerHeight + 1;
        var cardHeight = TimelineView.SectionHeight - 4;
        if (glow > 0)
            d.Rect(colour.WithOpacity(glow * GlowOpacityScale), null, x - GlowSpread / 2, y - GlowSpread / 2, width + 2 + GlowSpread, cardHeight + GlowSpread, CardRadius + GlowSpread / 2);
        d.Rect(colour.WithOpacity(CardOpacity), new UiStroke(white, CardOutlineWidth), x + 1, y, width, cardHeight, CardRadius);
        d.Rect(null, new UiStroke(colour.WithOpacity(1), CardOutlineWidth), x, gridTop, width + 2, height, WashRadius);
        if (width <= 34) return;
        d.PushClip(x + 1, y, width, cardHeight);
        d.DrawText(d.Text(title, TimelineView.Font, TitleSize, white, d.PixelsPerDip, weight: TitleWeight), x + 6, y + 2);
        d.Pop();
    }

    private static void DrawGhostRow(IDrawContext d, TimelineView view, TrackModel track, int row, int start, int end, UiColor colour)
    {
        var top = view.RowTop(row);
        for (var bar = start; bar < end && bar < track.Measures.Count; bar++)
        {
            var x = view.BarX(bar) + 1;
            var width = Math.Max(1, view.BarX(bar + 1) - view.BarX(bar) - 3);
            var activity = TimelineActivityRules.Build(track.Measures[bar], track.Kind == TrackKind.Drums);
            var empty = activity.Notes == 0;
            d.Rect(colour.WithOpacity(empty ? GhostEmptyOpacity : TimelineNoteMarks.NotesCellOpacity), null, x, top + 3, width, view.RowHeight - 6, 2);
            if (!empty)
                TimelineNoteMarks.DrawNotes(d, activity, track.Kind == TrackKind.Drums, x, top + 3, width, view.RowHeight - 6, colour.WithOpacity(GhostNoteOpacity));
        }
    }

    /// <summary>The drop line at <paramref name="x"/>, from the ruler to the bottom of the lanes.</summary>
    public static void DrawDropLine(IDrawContext d, Func<string, UiColor> ink, double x, UiColor colour, double height)
    {
        var top = TimelineView.RulerHeight - 1;
        var centre = TimelineView.RulerHeight + 1 + (TimelineView.SectionHeight - 4) / 2;
        d.Line(new UiStroke(colour.WithOpacity(DropLineGlowOpacity), DropLineGlowWidth), x, top, x, height - 1);
        d.Line(new UiStroke(ink("White"), DropLineWidth), x, top, x, height - 1);
        d.Ellipse(colour, new UiStroke(ink("White"), 1), x, centre, DropDotRadius, DropDotRadius);
    }

    /// <summary>
    /// The first bar number each section will have after the drop: the sections ordered by their resting x, bars counted in that order.
    /// <paramref name="firstBars"/> and <paramref name="barCounts"/> are indexed by slot like <paramref name="restingX"/>.
    /// </summary>
    public static int[] NewFirstBars(IReadOnlyList<int> barCounts, double[] restingX)
    {
        var order = new int[barCounts.Count];
        for (var i = 0; i < order.Length; i++) order[i] = i;
        Array.Sort(order, (a, b) => restingX[a].CompareTo(restingX[b]));
        var result = new int[order.Length];
        var bar = 0;
        foreach (var slot in order) { result[slot] = bar; bar += barCounts[slot]; }
        return result;
    }

    /// <summary>A small bold number in the ruler beside the old one for a section whose first bar changes.</summary>
    public static void DrawShiftedNumber(IDrawContext d, Func<string, UiColor> ink, double x, int newFirstBar, UiColor colour)
    {
        var text = d.Text((newFirstBar + 1).ToString(), TimelineView.Font, ShiftedNumberSize, ink("White"), d.PixelsPerDip, weight: ShiftedNumberWeight);
        d.Rect(colour.WithOpacity(CardOpacity), null, x + ShiftedNumberOffsetX - 2, ShiftedNumberY - 1, text.Width + 4, text.Height + 2, 2);
        d.DrawText(text, x + ShiftedNumberOffsetX, ShiftedNumberY);
    }
}
