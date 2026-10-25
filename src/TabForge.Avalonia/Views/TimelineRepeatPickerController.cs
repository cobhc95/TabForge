using System;
using System.Collections.Generic;
using Avalonia;
using TabForge.Avalonia.Platform;
using TabForge.Features.Recording.Rules;
using TabForge.Models;

namespace TabForge.Avalonia.Views;

// Owns: the repeat picker interaction (S3 only, no follow-playback): the pill/chip/dot/popover hit-test before any clip
//   hit, the pill and popover-pill click setting the shown pass, the chip click advancing (wrapping), the chip/dot hover
//   or click opening the popover, and its close on leaving the popover+chip area or Esc. A press on a picker rect never
//   seeks, selects or moves a clip. Alt+wheel steps elsewhere (ArrangementView).
// Does not own: the picker drawing and geometry (TimelineRepeatPicker), the shown pass (RepeatViewState), the fold,
//   or any seek/select/drag (TimelineInputController).
// Tests: TabForge.Avalonia.Tests.RepeatPickerInputTests.
internal sealed class TimelineRepeatPickerController
{
    internal enum PickerHitKind { InlinePill, Chip, Dot, PopoverPill, PopoverBox }

    internal readonly record struct PickerHit(RepeatRegion Region, PickerHitKind Kind, int Pass, double LaneTop);

    private readonly TimelineView _view;
    private PickerHit? _pressed;

    public TimelineRepeatPickerController(TimelineView view) => _view = view;

    /// <summary>Whether a press on a picker rect is in progress (its release must not seek/select/drag).</summary>
    public bool HasPressed => _pressed is not null;

    /// <summary>The picker hit at view-local <paramref name="point"/>, popover first (it floats over other tracks),
    /// then the inline pills/chip/dot; null when the pointer is on no picker rect of an N&gt;=4 region.</summary>
    public PickerHit? HitAt(Point point)
    {
        var view = _view;
        if (view.PickerPopoverRegion is { } open && open.PassCount > TimelineRepeatPicker.StackedMaxPasses)
        {
            foreach (var laneTop in LaneTopsForRegion(open))
            {
                var regionX = view.BarX(open.StartBar);
                var pills = TimelineRepeatPicker.PopoverPillRects(regionX, laneTop, open.PassCount);
                for (var i = 0; i < pills.Count; i++)
                    if (pills[i].Contains(point)) return new PickerHit(open, PickerHitKind.PopoverPill, i + 1, laneTop);
                if (TimelineRepeatPicker.PopoverBox(regionX, laneTop, open.PassCount).Contains(point))
                    return new PickerHit(open, PickerHitKind.PopoverBox, 0, laneTop);
            }
        }
        for (var row = 0; row < view.RowCount; row++)
        {
            var track = view.Song.Tracks[row];
            var offsets = view.RowOffsets;
            var baseTop = view.LaneBandTopOf(row) + (offsets is { } o && row < o.Length ? o[row] : 0);
            foreach (var clip in track.AudioClips)
            {
                var slices = TimelineAudioClips.FoldOf(view, clip);
                if (slices is null) continue;
                foreach (var region in TimelineRepeatPicker.RegionsOfFold(view, slices))
                {
                    if (region.PassCount <= TimelineRepeatPicker.StackedMaxPasses) continue;
                    var laneTop = baseTop + Math.Max(0, clip.Lane) * ClipLanes.AudioLaneHeight;
                    var regionX = view.BarX(region.StartBar);
                    var regionSpan = view.BarX(region.EndBar + 1) - regionX;
                    if (regionSpan <= 0) continue;
                    switch (TimelineRepeatPicker.FormFor(regionSpan, region.PassCount))
                    {
                        case TimelineRepeatPicker.RepeatPickerForm.Pills:
                        {
                            var pills = TimelineRepeatPicker.PillRects(regionX, laneTop, region.PassCount);
                            for (var i = 0; i < pills.Count; i++)
                                if (pills[i].Contains(point)) return new PickerHit(region, PickerHitKind.InlinePill, i + 1, laneTop);
                            break;
                        }
                        case TimelineRepeatPicker.RepeatPickerForm.Chip:
                        {
                            var shown = view.RepeatShown.ShownPass(region);
                            if (TimelineRepeatPicker.ChipHitRect(regionX, regionSpan, laneTop, shown, region.PassCount).Contains(point))
                                return new PickerHit(region, PickerHitKind.Chip, shown, laneTop);
                            break;
                        }
                        default:
                        {
                            if (TimelineRepeatPicker.DotRect(regionX, laneTop).Contains(point))
                                return new PickerHit(region, PickerHitKind.Dot, view.RepeatShown.ShownPass(region), laneTop);
                            break;
                        }
                    }
                }
            }
        }
        return null;
    }

    /// <summary>Remembers a press on a picker rect (opening the popover for chip/dot); true when the press must not start any drag/range/clip gesture.</summary>
    public bool OnPress(Point point)
    {
        var hit = HitAt(point);
        if (hit is null) return false;
        _pressed = hit;
        if (hit.Value.Kind is PickerHitKind.Chip or PickerHitKind.Dot)
            OpenPopover(hit.Value.Region);
        return true;
    }

    /// <summary>Applies a picker press's release: a pill sets the pass, a chip click advances (wrapping N-&gt;1);
    /// never seeks, selects or moves a clip. True when the press was on a picker rect (the release is consumed).</summary>
    public bool OnRelease(Point point)
    {
        if (_pressed is null) return false;
        var pressed = _pressed.Value;
        _pressed = null;
        var release = HitAt(point);
        if (release is { } r && r.Region.Equals(pressed.Region) &&
            (r.Kind is PickerHitKind.InlinePill or PickerHitKind.PopoverPill))
            _view.RepeatShown.SetPass(r.Region, r.Pass);
        else if (pressed.Kind == PickerHitKind.Chip && release is { } rc &&
                 rc.Kind == PickerHitKind.Chip && rc.Region.Equals(pressed.Region))
        {
            var current = _view.RepeatShown.ShownPass(pressed.Region);
            _view.RepeatShown.SetPass(pressed.Region, current >= pressed.Region.PassCount ? 1 : current + 1);
        }
        _view.InvalidateVisual();
        return true;
    }

    /// <summary>Opens the popover over a chip/dot hover (pills need none); closes it once the pointer leaves the popover+chip area.</summary>
    public void OnHover(Point point)
    {
        var view = _view;
        if (view.PickerPopoverRegion is { } open)
        {
            if (IsInPopoverOrChipArea(point, open)) return;
            view.PickerPopoverRegion = null;
            view.InvalidateVisual();
        }
        if (HitAt(point) is { } hit && hit.Kind is PickerHitKind.Chip or PickerHitKind.Dot)
        {
            view.PickerPopoverRegion = hit.Region;
            view.InvalidateVisual();
        }
    }

    /// <summary>Closes the popover on Esc; true when it was open.</summary>
    public bool OnEsc()
    {
        if (_view.PickerPopoverRegion is null) return false;
        _view.PickerPopoverRegion = null;
        _view.InvalidateVisual();
        return true;
    }

    /// <summary>Closes the popover when the pointer leaves the control; a press in progress is kept for its release.</summary>
    public void OnPointerExited()
    {
        if (_view.PickerPopoverRegion is null) return;
        _view.PickerPopoverRegion = null;
        _view.InvalidateVisual();
    }

    /// <summary>Forgets a picker press without an edit (capture lost or tab switch).</summary>
    public void Cancel() => _pressed = null;

    private void OpenPopover(RepeatRegion region)
    {
        if (region.PassCount <= TimelineRepeatPicker.StackedMaxPasses) return;
        _view.PickerPopoverRegion = region;
        _view.InvalidateVisual();
    }

    private bool IsInPopoverOrChipArea(Point point, RepeatRegion region)
    {
        var view = _view;
        foreach (var laneTop in LaneTopsForRegion(region))
        {
            var regionX = view.BarX(region.StartBar);
            if (TimelineRepeatPicker.PopoverBox(regionX, laneTop, region.PassCount).Contains(point)) return true;
            var regionSpan = view.BarX(region.EndBar + 1) - regionX;
            Rect trigger;
            switch (TimelineRepeatPicker.FormFor(regionSpan, region.PassCount))
            {
                case TimelineRepeatPicker.RepeatPickerForm.Chip:
                    trigger = TimelineRepeatPicker.ChipHitRect(regionX, regionSpan, laneTop,
                        view.RepeatShown.ShownPass(region), region.PassCount);
                    break;
                case TimelineRepeatPicker.RepeatPickerForm.Dot:
                    trigger = TimelineRepeatPicker.DotRect(regionX, laneTop);
                    break;
                default:
                    continue;
            }
            if (trigger.Contains(point)) return true;
            if (TimelineRepeatPicker.PopoverBox(regionX, laneTop, region.PassCount).Union(trigger).Contains(point)) return true;
        }
        return false;
    }

    private IEnumerable<double> LaneTopsForRegion(RepeatRegion region)
    {
        foreach (var (_, laneTop) in TimelineRepeatPicker.LaneTopsForRegion(_view, region))
            yield return laneTop;
    }
}
