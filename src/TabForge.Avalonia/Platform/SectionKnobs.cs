using System;
using TabForge.Platform;

namespace TabForge.Avalonia.Platform;

// Owns: the small resize knobs on the bar ruler at both ends of each section chip (a down-pointing triangle in the section's own colour, tip just above the chip) and where they can be grabbed.
// Does not own: the resize rule (SectionLayout.ResizeClamped) or the drag (TimelineInputController.SectionResize).
// Tests: TabForge.Avalonia.Tests.SectionResizeTests.
internal static class SectionKnobs
{
    /// <summary>Half the knob's width (px).</summary>
    public const double Half = 4;
    /// <summary>The knob's height above the chip (px).</summary>
    public const double Depth = 7;
    /// <summary>The grab area reaches this far sideways from the edge and this far below the ruler's bottom into the chip.</summary>
    public const double HitSide = 6;
    public const double HitInto = 5;

    public static void Draw(IDrawContext d, UiColor colour, double x, double rulerBottom) =>
        d.Path(colour, null, d.Paths.BuildPath(s =>
        {
            s.BeginFigure(new UiPoint(x - Half, rulerBottom - Depth), filled: true, closed: true);
            s.LineTo(new UiPoint(x + Half, rulerBottom - Depth));
            s.LineTo(new UiPoint(x, rulerBottom - 0.5));
        }));

    /// <summary>The distance of a pointer from a knob at <paramref name="edgeX"/> when it is inside the grab area, else null.</summary>
    public static double? Reach(double edgeX, double x, double y, double rulerBottom) =>
        y >= rulerBottom - Depth - 1 && y < rulerBottom + HitInto && Math.Abs(x - edgeX) <= HitSide ? Math.Abs(x - edgeX) : null;
}
