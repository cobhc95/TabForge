using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using TabForge.Features.Recording.Rules;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Platform;

// Owns: the repeat pass picker of the timeline (S2 drawing only): which form a repeat region takes from its
//   on-screen width (numbered pills, a "k/N" chip or a dot), the pill geometry one function of draw and hit test,
//   the hollow passes of a track (no clip covers that pass; the selected clip alone when one is on that track),
//   and the floating popover of all passes above the lane. No pointer handling yet (S3 opens and closes it).
// Does not own: the shown pass (RepeatViewState on TimelineView), the fold (RepeatFold), the clip boxes
//   (TimelineAudioClips) or any edit (the clip stays one piece).
// Tests: TabForge.Avalonia.Tests.RepeatPickerDrawTests.
internal static class TimelineRepeatPicker
{
    /// <summary>Pass counts up to this stack the parts (the current look); above it one pass shows full height with a picker.</summary>
    internal const int StackedMaxPasses = 3;

    /// <summary>One pill's advance (its width plus its gap).</summary>
    internal const double PillStepDip = 26;

    /// <summary>The gap between two pills (the pill itself is the step less this).</summary>
    internal const double PillGapDip = 4;

    /// <summary>The picker inset from the region edge (pills fit when N steps fit the usable width).</summary>
    internal const double PickerEdgeDip = 4;

    /// <summary>The narrowest region that still takes a chip instead of a dot.</summary>
    internal const double ChipMinDip = 48;

    /// <summary>The dot's side (a filled circle standing in for the picker on a narrow region).</summary>
    internal const double DotDip = 6;

    /// <summary>The height of the pill and chip row at the lane top.</summary>
    internal const double PillTallDip = 16;

    /// <summary>The picker row's offset below the lane top (the clip boxes' own inset, so the row sits on them).</summary>
    internal const double PickerTopDip = 2;

    /// <summary>The pill numbers, chip text and "Pass k of N" label size.</summary>
    internal const double PickerTextDip = 10;

    /// <summary>The pill, chip and popover corner roundness.</summary>
    internal const double PillRadiusDip = 4;

    /// <summary>The gap between the last pill and the "Pass k of N" label.</summary>
    internal const double LabelGapDip = 6;

    /// <summary>The gap between the lane top and the floating popover above it.</summary>
    internal const double PopoverGapDip = 4;

    /// <summary>The pill, chip and popover outline width.</summary>
    internal const double PickerStrokeDip = 1;

    /// <summary>The accent fill of a covered pill that is not shown (the shown one is solid).</summary>
    internal const double PickerCoveredOpacity = 0.35;

    /// <summary>The picker form of a repeat region from its on-screen width.</summary>
    internal enum RepeatPickerForm
    {
        Pills,
        Chip,
        Dot,
    }

    /// <summary>The picker form of a region <paramref name="regionSpanDip"/> wide with <paramref name="passCount"/> passes:
    /// numbered pills when they fit the usable width, else a "k/N" chip from <see cref="ChipMinDip"/>, else a dot.</summary>
    internal static RepeatPickerForm FormFor(double regionSpanDip, int passCount)
    {
        if (passCount > 0 && regionSpanDip - 2 * PickerEdgeDip >= passCount * PillStepDip) return RepeatPickerForm.Pills;
        return regionSpanDip >= ChipMinDip ? RepeatPickerForm.Chip : RepeatPickerForm.Dot;
    }

    /// <summary>The "Pass k of N" label of the pills form.</summary>
    internal static string CaptionFor(int shownPass, int passCount) => $"Pass {shownPass} of {passCount}";

    /// <summary>The chip text of the chip form.</summary>
    internal static string ChipCaptionFor(int shownPass, int passCount) => $"{shownPass}/{passCount}";

    /// <summary>The pill boxes of a pills-form picker: one per pass, left aligned at the region edge, on the lane-top row.</summary>
    internal static IReadOnlyList<Rect> PillRects(double regionX, double laneTop, int passCount)
    {
        var pills = new List<Rect>(Math.Max(0, passCount));
        for (var pass = 0; pass < passCount; pass++)
            pills.Add(new Rect(regionX + PickerEdgeDip + pass * PillStepDip, laneTop + PickerTopDip, PillStepDip - PillGapDip, PillTallDip));
        return pills;
    }

    /// <summary>The chip box of a chip-form picker: the text box at the region's top-left corner, kept inside the region.</summary>
    internal static Rect ChipRect(double regionX, double regionSpan, double laneTop, double textWidth)
    {
        var usable = Math.Max(0, regionSpan - 2 * PickerEdgeDip);
        var width = Math.Min(usable, textWidth + 2 * PickerEdgeDip);
        return new Rect(regionX + PickerEdgeDip, laneTop + PickerTopDip, Math.Max(width, DotDip), PillTallDip);
    }

    /// <summary>The dot box of a dot-form picker: a small square at the region's top-left corner, drawn as a filled circle.</summary>
    internal static Rect DotRect(double regionX, double laneTop) =>
        new(regionX + PickerEdgeDip, laneTop + PickerTopDip, DotDip, DotDip);

    /// <summary>The passes of <paramref name="region"/> no clip on <paramref name="track"/> covers (performed time overlap):
    /// the per-region union, or the selected clip alone when one is on that track. A pass the song never plays is hollow too.</summary>
    internal static ISet<int> HollowPasses(TimelineView view, TrackModel track, RepeatRegion region)
    {
        var hollow = new HashSet<int>();
        if (region.PassCount < 1) return hollow;
        var performed = SongExtent.Timing(view.Song).Performed;
        var selected = view.SelectedClip?.Clip;
        IReadOnlyList<AudioClip> source = selected is not null && track.AudioClips.Contains(selected)
            ? new[] { selected }
            : track.AudioClips;
        var bars = region.EndBar - region.StartBar + 1;
        for (var pass = 1; pass <= region.PassCount; pass++)
        {
            var (start, length) = RepeatPasses.PerformedRangeForPass(performed, region.StartBar, bars, pass);
            if (double.IsNaN(start) || length <= RepeatPasses.OverlapEpsilonSec) { hollow.Add(pass); continue; }
            var covered = false;
            foreach (var clip in source)
                if (clip.StartSec < start + length - RepeatPasses.OverlapEpsilonSec && clip.EndSec > start + RepeatPasses.OverlapEpsilonSec)
                { covered = true; break; }
            if (!covered) hollow.Add(pass);
        }
        return hollow;
    }

    /// <summary>The repeat regions of <paramref name="slices"/> whose parts show a picker (pass count above <see cref="StackedMaxPasses"/>),
    /// matched by pass count and written-bar overlap, in fold order.</summary>
    internal static IReadOnlyList<RepeatRegion> RegionsOfFold(TimelineView view, IReadOnlyList<RepeatSlice> slices)
    {
        var regions = view.RepeatRegionsNow();
        var found = new List<RepeatRegion>();
        foreach (var slice in slices)
        {
            if (!slice.IsPart || slice.PartCount <= StackedMaxPasses) continue;
            if (RegionOfSlice(view, regions, slice) is { } region && !found.Contains(region)) found.Add(region);
        }
        return found;
    }

    /// <summary>The first of <paramref name="regions"/> holding <paramref name="slice"/> (its pass count and written bars), or null.</summary>
    internal static RepeatRegion? RegionOfSlice(TimelineView view, IReadOnlyList<RepeatRegion> regions, RepeatSlice slice)
    {
        foreach (var region in regions)
            if (RegionOfSlice(view, region, slice) is { } match) return match;
        return null;
    }

    /// <summary><paramref name="region"/> when it holds <paramref name="slice"/> (its pass count and written bars), or null.</summary>
    internal static RepeatRegion? RegionOfSlice(TimelineView view, RepeatRegion region, RepeatSlice slice)
    {
        if (region.PassCount != slice.PartCount) return null;
        var (first, _) = AudioClipTimelineRules.BarAtSeconds(view.Song, slice.WrittenStartSec);
        var probe = Math.Max(slice.WrittenStartSec, slice.WrittenEndSec - RepeatPasses.OverlapEpsilonSec);
        var (last, _) = AudioClipTimelineRules.BarAtSeconds(view.Song, probe);
        return last < region.StartBar || first > region.EndBar ? null : region;
    }

    /// <summary>The lanes where <paramref name="region"/> folds, with their track: one entry per lane covering the region (popover, chip and dot hits share it).</summary>
    internal static IEnumerable<(TrackModel Track, double LaneTop)> LaneTopsForRegion(TimelineView view, RepeatRegion region)
    {
        var seen = new HashSet<double>();
        for (var row = 0; row < view.RowCount; row++)
        {
            var track = view.Song.Tracks[row];
            var offsets = view.RowOffsets;
            var baseTop = view.LaneBandTopOf(row) + (offsets is { } o && row < o.Length ? o[row] : 0);
            foreach (var clip in track.AudioClips)
            {
                if (TimelineAudioClips.FoldOf(view, clip) is not { } slices) continue;
                var covers = false;
                foreach (var slice in slices)
                {
                    if (!slice.IsPart || slice.PartCount != region.PassCount) continue;
                    if (RegionOfSlice(view, region, slice) is not null) { covers = true; break; }
                }
                if (!covers) continue;
                var laneTop = baseTop + Math.Max(0, clip.Lane) * ClipLanes.AudioLaneHeight;
                if (seen.Add(laneTop)) yield return (track, laneTop);
            }
        }
    }

    /// <summary>Draws the picker of <paramref name="region"/> on its lane-top row: pills plus "Pass k of N", a chip, or a dot.</summary>
    internal static void DrawPicker(IDrawContext d, TimelineView view, TrackModel track, RepeatRegion region, double laneTop, Func<string, UiColor> ink)
    {
        var total = region.PassCount;
        if (total <= StackedMaxPasses) return;
        var regionX = view.BarX(region.StartBar);
        var regionSpan = view.BarX(region.EndBar + 1) - regionX;
        if (regionSpan <= 0) return;
        var shown = view.RepeatShown.ShownPass(region);
        var accent = ink("InstrumentAccent");
        switch (FormFor(regionSpan, total))
        {
            case RepeatPickerForm.Dot:
                var dot = DotRect(regionX, laneTop);
                d.Ellipse(accent, null, dot.X + dot.Width / 2, dot.Y + dot.Height / 2, dot.Width / 2, dot.Height / 2);
                return;
            case RepeatPickerForm.Chip:
            {
                var caption = d.Text(ChipCaptionFor(shown, total), TimelineView.Font, PickerTextDip, ThemeColours.InkOn(accent), d.PixelsPerDip, weight: 600);
                var chip = ChipRect(regionX, regionSpan, laneTop, caption.Width);
                d.Rect(accent, new UiStroke(accent, PickerStrokeDip), chip.X, chip.Y, chip.Width, chip.Height, PillRadiusDip);
                d.DrawText(caption, chip.X + (chip.Width - caption.Width) / 2, chip.Y + Math.Max(0, (chip.Height - caption.Height) / 2));
                return;
            }
            default:
                var pills = PillRects(regionX, laneTop, total);
                DrawPills(d, pills, view, track, region, accent, ink("InstrumentLegible"));
                var label = d.Text(CaptionFor(shown, total), TimelineView.Font, PickerTextDip, ink("InstrumentLegible"), d.PixelsPerDip);
                d.PushClip(regionX, pills[0].Y, regionSpan, PillTallDip);
                d.DrawText(label, pills[total - 1].Right + LabelGapDip, pills[0].Y + Math.Max(0, (PillTallDip - label.Height) / 2));
                d.Pop();
                return;
        }
    }

    /// <summary>Draws the floating popover of <paramref name="region"/> above its lane: all its pills over the other tracks.
    /// One lane's popover; <see cref="DrawPopovers"/> draws every lane the flag region folds on.
    /// One geometry with <see cref="PopoverBox"/> and <see cref="PopoverPillRects"/> (draw, cull and hit-test share them).</summary>
    internal static void DrawPopover(IDrawContext d, TimelineView view, TrackModel track, RepeatRegion region, double laneTop, Func<string, UiColor> ink)
    {
        var total = region.PassCount;
        if (total <= StackedMaxPasses) return;
        var regionX = view.BarX(region.StartBar);
        var box = PopoverBox(regionX, laneTop, total);
        d.Rect(ink("InstrumentBackground"), new UiStroke(ink("ClipOutline"), PickerStrokeDip), box.X, box.Y, box.Width, box.Height, PillRadiusDip);
        var pills = PopoverPillRects(regionX, laneTop, total);
        DrawPills(d, pills, view, track, region, ink("InstrumentAccent"), ink("InstrumentLegible"));
    }

    /// <summary>The floating popover box of a region above <paramref name="laneTop"/>: all its pills. One geometry of draw and hit-test.</summary>
    internal static Rect PopoverBox(double regionX, double laneTop, int passCount)
    {
        var boxWidth = passCount * PillStepDip + 2 * PickerEdgeDip;
        var boxHeight = PillTallDip + 2 * PickerEdgeDip;
        var boxY = laneTop - PopoverGapDip - boxHeight;
        return new Rect(regionX, boxY, boxWidth, boxHeight);
    }

    /// <summary>The popover pills of a region above <paramref name="laneTop"/>: one per pass, inside <see cref="PopoverBox"/>.
    /// One geometry of draw and hit-test.</summary>
    internal static IReadOnlyList<Rect> PopoverPillRects(double regionX, double laneTop, int passCount)
    {
        var box = PopoverBox(regionX, laneTop, passCount);
        var pills = new List<Rect>(Math.Max(0, passCount));
        for (var pass = 0; pass < passCount; pass++)
            pills.Add(new Rect(regionX + PickerEdgeDip + pass * PillStepDip, box.Y + PickerEdgeDip, PillStepDip - PillGapDip, PillTallDip));
        return pills;
    }

    /// <summary>The chip box for hit-testing: <see cref="ChipRect"/> with the test-double text width
    /// (caption length * 6), a subset of the real drawn chip, so a hit is always on the drawn chip.</summary>
    internal static Rect ChipHitRect(double regionX, double regionSpan, double laneTop, int shownPass, int passCount) =>
        ChipRect(regionX, regionSpan, laneTop, ChipCaptionFor(shownPass, passCount).Length * 6.0);

    /// <summary>The N&gt;=4 region holding timeline x <paramref name="x"/>, or null (plain wheel keeps zooming then).</summary>
    internal static RepeatRegion? RegionAtX(TimelineView view, double x)
    {
        var bar = view.BarAtX(x);
        foreach (var region in view.RepeatRegionsNow())
            if (region.PassCount > StackedMaxPasses && bar >= region.StartBar && bar <= region.EndBar)
                return region;
        return null;
    }

    /// <summary>Draws the popover of the flag region (<see cref="TimelineView.PickerPopoverRegion"/>) above every lane it folds on;
    /// nothing when the flag is closed. Called after the rows, so it floats over the other tracks.</summary>
    internal static void DrawPopovers(IDrawContext d, TimelineView view, Func<string, UiColor> ink)
    {
        if (view.PickerPopoverRegion is not { } region) return;
        if (region.PassCount <= StackedMaxPasses) return;
        foreach (var (track, laneTop) in LaneTopsForRegion(view, region))
            DrawPopover(d, view, track, region, laneTop, ink);
    }

    private static void DrawPills(IDrawContext d, IReadOnlyList<Rect> pills, TimelineView view, TrackModel track, RepeatRegion region, UiColor accent, UiColor legible)
    {
        var shown = view.RepeatShown.ShownPass(region);
        var hollow = HollowPasses(view, track, region);
        for (var pass = 1; pass <= region.PassCount; pass++)
        {
            var rect = pills[pass - 1];
            var isShown = pass == shown;
            UiColor? fill = isShown ? accent : hollow.Contains(pass) ? null : accent.WithOpacity(PickerCoveredOpacity);
            d.Rect(fill, new UiStroke(accent, PickerStrokeDip), rect.X, rect.Y, rect.Width, rect.Height, PillRadiusDip);
            var number = d.Text(pass.ToString(CultureInfo.InvariantCulture), TimelineView.Font, PickerTextDip,
                isShown && fill is { } solid ? ThemeColours.InkOn(solid) : legible, d.PixelsPerDip, weight: 600);
            d.DrawText(number, rect.X + (rect.Width - number.Width) / 2, rect.Y + Math.Max(0, (rect.Height - number.Height) / 2));
        }
    }
}
