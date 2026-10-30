using System;
using System.Collections.Generic;
using TabForge.Features.Recording.Rules;
using TabForge.Platform;

namespace TabForge.Avalonia.Platform;

// Owns: playhead cues (arrow, bar-number flag) on the arrangement timeline.
// Does not own: the playhead line or its position (TimelineView).

/// <summary>
/// Owns the playhead's cues on the arrangement timeline: the white arrow on the line at the lane's middle,
/// and the amber bar number and flag in the ruler. Does not own the playhead line or its position (TimelineView). Tests: TimelineSelectedRowTests.
/// </summary>
internal static class TimelinePlayheadMarks
{
    public const string AmberKey = "WarningIcon";
    public const string ArrowKey = "TimelinePlayhead";
    public const double ArrowWidth = 14;
    public const double ArrowHeight = 16;
    public const double FlagWidth = 9;
    public const double FlagHeight = 7;
    public const double BarNumberSize = 12;
    public const int BarNumberWeight = 700;
    /// <summary>The top of the ruler's bar numbers (the playhead's own number sits on it too).</summary>
    public const double BarNumberTop = 3;
    /// <summary>The space between the bar numbers' line and the playhead label under it.</summary>
    public const double PlayheadLabelGap = 6;
    /// <summary>The top of the playhead label: its own line under the bar numbers (which sit at the ruler's top), above the ruler's tick marks.</summary>
    public const double PlayheadLabelTop = BarNumberTop + BarNumberSize + PlayheadLabelGap;

    /// <summary>The arrow's vertical centre for a lane that starts at <paramref name="laneTop"/>.</summary>
    public static double ArrowCentreY(double laneTop, double laneHeight) => laneTop + laneHeight / 2;

    /// <summary>A right-pointing triangle in front of the playhead: its base sits on the line's left edge at <paramref name="x"/> and its tip points past the line to the right.</summary>
    public static void DrawArrow(IDrawContext d, Func<string, UiColor> ink, double x, double centreY)
    {
        var path = d.Paths.BuildPath(sink =>
        {
            sink.BeginFigure(new UiPoint(x, centreY - ArrowHeight / 2), true, true);
            sink.LineTo(new UiPoint(x + ArrowWidth, centreY));
            sink.LineTo(new UiPoint(x, centreY + ArrowHeight / 2));
        });
        d.Path(ink(ArrowKey), null, path);
    }

    /// <summary>The amber flag at the top of the playhead, centred on <paramref name="x"/>.</summary>
    public static void DrawRulerFlag(IDrawContext d, Func<string, UiColor> ink, double x)
    {
        var flag = d.Paths.BuildPath(sink =>
        {
            sink.BeginFigure(new UiPoint(x - FlagWidth / 2, 0), true, true);
            sink.LineTo(new UiPoint(x + FlagWidth / 2, 0));
            sink.LineTo(new UiPoint(x, FlagHeight));
        });
        d.Path(ink(AmberKey), null, flag);
    }

    /// <summary>The bold amber number of the current bar, at the bar's left edge.</summary>
    public static void DrawBarNumber(IDrawContext d, Func<string, UiColor> ink, double barLeft, int bar, string font) =>
        d.DrawText(d.Text((bar + 1).ToString(), font, BarNumberSize, ink(AmberKey), d.PixelsPerDip, weight: BarNumberWeight), barLeft + 3, BarNumberTop);

    /// <summary>The bold amber playhead label at the bar's left edge: the bar number, plus " - pass k" when the playhead is inside a repeat.</summary>
    public static void DrawBarNumber(IDrawContext d, Func<string, UiColor> ink, double barLeft, int bar, int pass, IReadOnlyList<RepeatRegion>? regions, string font) =>
        d.DrawText(d.Text(PlayheadLabelFor(bar, pass, regions), font, BarNumberSize, ink(AmberKey), d.PixelsPerDip, weight: BarNumberWeight), barLeft + 3, PlayheadLabelTop);

    /// <summary>The playhead label for 0-based <paramref name="bar"/>: "n - pass k" inside a repeat (N&gt;=2), else "n" (today's number, unchanged).</summary>
    public static string PlayheadLabelFor(int bar, int pass, IReadOnlyList<RepeatRegion>? regions)
    {
        if (regions is not null)
            foreach (var region in regions)
                if (region.PassCount >= 2 && bar >= region.StartBar && bar <= region.EndBar)
                    return $"{bar + 1} - pass {Math.Max(1, pass)}";
        return (bar + 1).ToString();
    }
}
