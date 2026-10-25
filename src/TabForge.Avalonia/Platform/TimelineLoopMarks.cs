using System;
using TabForge.Platform;

namespace TabForge.Avalonia.Platform;

// Owns: how the loop region draws on the arrangement timeline: a thin orange loop bar on the bar-number ruler with a triangular grab handle hanging at each end (Reaper style), and a very faint band down the lanes
//   (theme tokens LoopEdge and LoopRegion, light and dark); the bar is drawn full strength while the Loop button repeats it, softer otherwise.
// Does not own: where the region is (SelectionModel's loop; the loop link sets TimelineView.LoopStart/LoopEnd) or its editing (TimelineInputController.Loop).
// Tests: TabForge.Avalonia.Tests.LoopModelTests.
internal static class TimelineLoopMarks
{
    public const string EdgeKey = "LoopEdge";
    public const string RegionKey = "LoopRegion";
    /// <summary>A region that waits for the Loop button draws its bracket at this strength: it is not repeating.</summary>
    public const double IdleStrength = 0.55;
    public const double BracketWidth = 2;
    /// <summary>The loop bar's height above the ruler's bottom edge (px).</summary>
    public const double BracketRise = 10;
    /// <summary>Half the width of the triangular grab handle hanging under each end of the bar (px); its hit area is LoopRulerRules.EdgeTolerance each side.</summary>
    public const double HandleHalf = 5;
    /// <summary>How far each handle's tip hangs below the bar (px).</summary>
    public const double HandleDepth = 8;

    /// <param name="rulerBottom">The ruler's bottom edge: the loop bar sits just above it.</param>
    /// <param name="bandTop">Where the faint band down the lanes starts; it runs to <paramref name="bottom"/>.</param>
    public static void Draw(IDrawContext d, Func<string, UiColor> ink, double left, double right, double rulerBottom, double bandTop, double bottom, bool looping)
    {
        d.Rect(ink(RegionKey), null, left, bandTop, right - left, Math.Max(0, bottom - bandTop));
        var edge = ink(EdgeKey).WithOpacity(looping ? 1.0 : IdleStrength);
        var top = rulerBottom - BracketRise;
        d.Line(new UiStroke(edge, BracketWidth), left, top, right, top);
        d.Path(edge, null, Handle(d, left, top));
        d.Path(edge, null, Handle(d, right, top));
    }

    // A small downward triangle centred on the bar end at x, its flat side on the bar.
    private static IUiPath Handle(IDrawContext d, double x, double top) => d.Paths.BuildPath(s =>
    {
        s.BeginFigure(new UiPoint(x - HandleHalf, top), filled: true, closed: true);
        s.LineTo(new UiPoint(x + HandleHalf, top));
        s.LineTo(new UiPoint(x, top + HandleDepth));
    });
}
