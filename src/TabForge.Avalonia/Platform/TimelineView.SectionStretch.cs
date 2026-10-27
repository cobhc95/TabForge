using System;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Platform.Theme;

namespace TabForge.Avalonia.Platform;

// Owns: the cheap preview of "Stretch a section" while its right edge is dragged past the next section: the bars asked for (SectionStretchBars), the added bars hatched inside the section's chip with a "+N bars" badge,
//   the next section and every one after drawn N bars later, and a faint hatched column over the lanes where the new bars would go. Drawing only: no model edits, one repaint per bar change.
// Does not own: the drag and the bar count (TimelineInputController.SectionStretch), the edit (SectionStretch in Core).
// Tests: TabForge.Avalonia.Tests.SectionStretchTests.
public sealed partial class TimelineView
{
    private const double StretchHatchGap = 7, StretchHatchOpacity = 0.55, StretchWashOpacity = 0.16, StretchBadgeSize = 10, StretchBadgeOpacity = 0.95;

    /// <summary>The whole bars a section stretch in progress asks for beyond the next section's start (0: none, the preview shows the plain resize).</summary>
    internal int SectionStretchBars { get; set; }

    /// <summary>True when the stretch in progress is backwards (the left edge past the previous section): the added bars sit at the section's start.</summary>
    internal bool SectionStretchBack { get; set; }

    /// <summary>The width (px) of one added bar: the stretched section's last bar.</summary>
    private double StretchBarWidth => SectionResizePreview is not { } p ? 0 : SectionStretchBack ? BarX(p.Start + 1) - BarX(p.Start) : p.End > 0 ? BarX(p.End) - BarX(p.End - 1) : 0;

    /// <summary>How far (px) the next section and everything after it are drawn later.</summary>
    internal double StretchShiftPx => SectionStretchBars > 0 ? SectionStretchBars * StretchBarWidth : 0;

    /// <summary>The hatched added bars inside the stretched section's chip (<paramref name="left"/>, <paramref name="top"/>, <paramref name="width"/>, <paramref name="height"/> are the chip's) and the "+N bars" badge.</summary>
    private void DrawStretchAdded(IDrawContext d, UiColor colour, UiColor white, double left, double top, double width, double height)
    {
        var shift = StretchShiftPx;
        if (shift <= 2) return;
        var x = SectionStretchBack ? left : left + width - shift;
        DrawHatch(d, colour, x, top, shift, height);
        var text = d.Text(SectionStretchBars == 1 ? "+1 bar" : $"+{SectionStretchBars} bars", Font, StretchBadgeSize, white, d.PixelsPerDip, weight: 700);
        var badgeW = text.Width + 8;
        var badgeX = Math.Max(x + 2, x + (shift - badgeW) / 2);
        d.Rect(colour.WithOpacity(StretchBadgeOpacity), new UiStroke(white, 1), badgeX, top + (height - text.Height) / 2 - 2, badgeW, text.Height + 4, 3);
        d.DrawText(text, badgeX + 4, top + (height - text.Height) / 2);
    }

    /// <summary>Diagonal stripes in <paramref name="colour"/> over a rectangle.</summary>
    private static void DrawHatch(IDrawContext d, UiColor colour, double x, double y, double width, double height)
    {
        d.PushClip(x, y, width, height);
        d.Rect(colour.WithOpacity(StretchWashOpacity), null, x, y, width, height);
        var stroke = new UiStroke(colour.WithOpacity(StretchHatchOpacity), 1.2);
        for (var offset = -height; offset < width; offset += StretchHatchGap) d.Line(stroke, x + offset, y + height, x + offset + height, y);
        d.Pop();
    }

    /// <summary>The faint hatched column over the lanes where the new bars would go.</summary>
    private void DrawStretchLanes(IDrawContext d, double gridTop)
    {
        if (SectionStretchBars < 1 || SectionResizePreview is not { } p || p.Index >= _sectionColours.Length) return;
        DrawHatch(d, _sectionColours[p.Index].ToUi(), SectionStretchBack ? BarX(p.Start) : BarX(p.End), gridTop, StretchShiftPx, Math.Max(0, Bounds.Height - gridTop));
    }
}
