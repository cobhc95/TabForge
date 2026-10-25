using System;
using System.Collections.Generic;
using Avalonia;
using TabForge.Features.Recording.Rules;
using TabForge.Platform;
using TabForge.Services;

namespace TabForge.Avalonia.Platform;

// Owns: the faint "skipped" hatch in the stacked lane of a folded repeat where a pass does not play an ending bar (pass 2 never plays the [1.] bar): the empty row of that pass
//   over that bar, so an empty lane reads as skipped and not as missing audio.
// Does not own: the fold (RepeatFold), the endings (RepeatEndings) or the slice boxes (TimelineAudioClips.Fold.cs).
// Tests: TabForge.Avalonia.Tests.SkippedPassHatchTests.
internal static partial class TimelineAudioClips
{
    // Layout tokens of the hatch: faint diagonal lines in the text colour.
    private const double SkippedHatchOpacity = 0.22;
    private const double SkippedHatchStrokeWidth = 1;
    private const double SkippedHatchStep = 6;

    /// <summary>
    /// The empty pass rows of a folded clip over the ending bars of its repeats: for each ending bar the clip's stacked fold covers, the rows of the passes that do not play it,
    /// across the bar. Many-pass repeats show one pass at a time and have none.
    /// </summary>
    internal static IReadOnlyList<Rect> SkippedPassRects(TimelineView view, IReadOnlyList<RepeatSlice> slices, double laneTop)
    {
        var rects = new List<Rect>();
        var performed = SongExtent.Timing(view.Song).Performed;
        foreach (var (bar, ending) in view.Folds.MapOf(performed).Endings)
        {
            if (ending.PassCount > TimelineRepeatPicker.StackedMaxPasses || bar < 0 || bar >= view.BarCount) continue;
            var (left, right) = (view.BarX(bar), view.BarX(bar + 1));
            var middle = (left + right) / 2;
            var folded = false;
            foreach (var slice in slices)
                if (slice.IsPart && slice.PartCount == ending.PassCount)
                {
                    var box = SliceRect(view, slice, laneTop);
                    folded |= box.Right > view.BarX(ending.RegionStartBar) && box.X < right;
                }
            if (!folded) continue;
            for (var pass = 0; pass < ending.PassCount; pass++)
            {
                if (pass == ending.Pass - 1 || PlaysAt(view, slices, pass, ending.PassCount, middle, laneTop)) continue;
                var row = SliceRect(view, new RepeatSlice(0, 1, 0, 1, pass, ending.PassCount), laneTop);
                rects.Add(new Rect(left, row.Y, right - left, row.Height));
            }
        }
        return rects;
    }

    private static bool PlaysAt(TimelineView view, IReadOnlyList<RepeatSlice> slices, int pass, int passCount, double x, double laneTop)
    {
        foreach (var slice in slices)
        {
            if (!slice.IsPart || slice.PartCount != passCount || slice.PartIndex != pass) continue;
            var box = SliceRect(view, slice, laneTop);
            if (box.X <= x && x <= box.Right) return true;
        }
        return false;
    }

    private static void DrawSkippedPasses(IDrawContext d, TimelineView view, IReadOnlyList<RepeatSlice> slices, double laneTop, Func<string, UiColor> ink, double spanFrom, double spanWidth)
    {
        var stroke = view.Folds.HatchStroke(ink("TextPrimary").WithOpacity(SkippedHatchOpacity), SkippedHatchStrokeWidth);
        foreach (var rect in SkippedPassRects(view, slices, laneTop))
        {
            if (!double.IsNaN(spanFrom) && (rect.Right < spanFrom || rect.X > spanFrom + spanWidth)) continue;
            d.PushClip(rect.X, rect.Y, rect.Width, rect.Height);
            // Only the lines that reach the visible span: the hatch keeps its phase from the rect's left edge however far the rect is scrolled.
            var first = rect.X - rect.Height;
            var from = double.IsNaN(spanFrom) ? first : Math.Max(first, spanFrom - rect.Height);
            var to = double.IsNaN(spanFrom) ? rect.Right : Math.Min(rect.Right, spanFrom + spanWidth);
            for (var x = first + Math.Floor((from - first) / SkippedHatchStep) * SkippedHatchStep; x < to; x += SkippedHatchStep)
                d.Line(stroke, x, rect.Bottom, x + rect.Height, rect.Y);
            d.Pop();
        }
    }
}
