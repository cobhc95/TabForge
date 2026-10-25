using System.Collections.Generic;
using TabForge.Platform;

namespace TabForge.Avalonia.Platform;

// Owns: how the bar highlight draws on the arrangement timeline: a grey fill and a grey frame over each highlighted tab cell row.
// Does not own: the highlight (BarHighlight), where bars and rows sit (TimelineView) or the loop region's drawing (TimelineLoopMarks).
// Tests: TabForge.Avalonia.Tests.TimelineBarHighlightDrawingTests.

/// <summary>One highlighted tab row as the painter needs it: the cell box.</summary>
internal readonly record struct HighlightRow(double Top, double Height);

/// <summary>Draws the grey bar highlight of the timeline.</summary>
internal static class TimelineHighlightPainter
{
    public const double FrameWidth = 2;

    /// <param name="left">The left edge of the highlighted bars.</param>
    /// <param name="right">The right edge of the highlighted bars.</param>
    /// <param name="rows">The highlighted tab rows, top to bottom.</param>
    public static void Draw(IDrawContext d, UiColor fill, UiColor frame, double left, double right, IReadOnlyList<HighlightRow> rows)
    {
        var stroke = new UiStroke(frame, FrameWidth);
        var half = FrameWidth / 2;
        foreach (var row in rows)
        {
            d.Rect(fill, null, left, row.Top, right - left, row.Height);
            d.Rect(null, stroke, left + half, row.Top + half, right - left - FrameWidth, row.Height - FrameWidth);
        }
    }
}
