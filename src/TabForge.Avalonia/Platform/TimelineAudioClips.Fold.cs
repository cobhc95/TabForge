using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using TabForge.Features.Recording.Rules;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Services;

namespace TabForge.Avalonia.Platform;

// Owns: the repeat view of a clip on the timeline: a clip that plays through a repeat is drawn folded onto the written bars (slice k of N
//   inside the lane, numbered), as one clip: one outline, one hit area, one set of fade handles. Drawing and hit-testing only.
// Does not own: the fold itself (RepeatFold), the lane geometry (TimelineAudioClips) or any clip edit (the clip stays one piece).
// Tests: TabForge.Avalonia.Tests.RepeatViewTests.
internal static partial class TimelineAudioClips
{
    /// <summary>
    /// The drawn right edge of a slice is measured just inside its end: a pass that ends exactly where the next play of an earlier bar starts
    /// would otherwise map onto that earlier bar (the timeline draws each written bar once). A tenth of a millisecond is far below one DIP.
    /// </summary>
    private const double FoldEdgeEpsilonSec = 1e-4;

    // The waveform of a slice is read through a stand-in clip holding the slice own source span, cached per clip so the outline is reused between frames.
    private static readonly ConditionalWeakTable<AudioClip, List<AudioClip>> SliceProxies = new();

    /// <summary>The slices of <paramref name="clip"/> when it plays through a repeat, or null when it is drawn as one ordinary box.
    /// A repeat the clip covers in only one pass is a non-part slice (one full-height box, tagged "pass k" from pass 2; see <see cref="RepeatFold.FoldedSlices"/>):
    /// the one fold decision of drawing, hit test and drag preview (the preview folds at the candidate start, so it looks as the drop will). Such a slice keeps the slice geometry (its end is measured just inside the pass).</summary>
    internal static IReadOnlyList<RepeatSlice>? FoldOf(TimelineView view, AudioClip clip) =>
        view.Folds.FoldOf(SongExtent.Timing(view.Song).Performed, clip);   // a clip being dragged is folded at its candidate start, as it will be once dropped (cached per move step)

    /// <summary>Draws the "pass k" tag at the left of an unfolded one-pass box (pass 2 and later); nothing for pass 1.</summary>
    private static void DrawPassTag(IDrawContext d, RepeatSlice slice, Rect rect, UiColor nameInk)
    {
        if (slice.IsPart) return;
        DrawPassTag(d, slice.TagPass, rect, nameInk);
    }

    /// <summary>The one "pass k" tag drawing (committed clip slices and the live recording box): nothing below pass 2.</summary>
    internal static void DrawPassTag(IDrawContext d, int tagPass, Rect rect, UiColor nameInk)
    {
        if (tagPass < 2) return;
        var label = d.Text("pass " + tagPass.ToString(CultureInfo.InvariantCulture), TimelineView.Font, NameFontSize,
            nameInk.WithOpacity(StackNumberOpacity), d.PixelsPerDip);
        d.DrawText(label, rect.X + NameInset, rect.Y + Math.Max(0, (rect.Height - label.Height) / 2));
    }

    /// <summary>True when <paramref name="slice"/> is a part of a many-pass repeat (above the stacked look): it draws full height with a picker.</summary>
    internal static bool IsManyPass(RepeatSlice slice) => slice.IsPart && slice.PartCount > TimelineRepeatPicker.StackedMaxPasses;

    /// <summary>The drawn box of one slice: a span played once is full lane height, a part is row k of N of the lane,
    /// or the full lane when <paramref name="fullHeight"/> (a many-pass repeat shows one pass). The one geometry of draw, cull and hit test.</summary>
    internal static Rect SliceRect(TimelineView view, RepeatSlice slice, double laneTop, bool fullHeight = false)
    {
        var left = view.XOfSeconds(slice.StartSec);
        var right = Math.Max(left + MinClipWidth, view.XOfSeconds(Math.Max(slice.StartSec, slice.EndSec - FoldEdgeEpsilonSec)));
        var inner = Math.Max(1, ClipLanes.AudioLaneHeight - 2 * LanePadding);
        if (!slice.IsPart || fullHeight) return new Rect(left, laneTop + LanePadding, right - left, inner);
        var rowHeight = Math.Max(1, inner / slice.PartCount);
        return new Rect(left, laneTop + LanePadding + slice.PartIndex * rowHeight, right - left, rowHeight);
    }

    /// <summary>The union of a folded clip slice boxes: the box the outline, the edge grips and the fade band use.
    /// A many-pass repeat unions the full-height boxes, so the outline and the hit area stay the whole clip (RV4).</summary>
    private static Rect FoldBox(TimelineView view, IReadOnlyList<RepeatSlice> slices, double laneTop)
    {
        var box = SliceRect(view, slices[0], laneTop, IsManyPass(slices[0]));
        for (var i = 1; i < slices.Count; i++)
            box = box.Union(SliceRect(view, slices[i], laneTop, IsManyPass(slices[i])));
        return box;
    }

    /// <summary>Draws one folded clip: every slice box with its wave (or notes), the clip name on the first, the part numbers, the fades, and one outline around the whole fold when selected.</summary>
    private static void DrawFoldedClip(IDrawContext d, TimelineView view, int row, int index, AudioClip clip, IReadOnlyList<RepeatSlice> slices,
        double laneTop, double width, UiColor colour, UiColor nameInk, UiColor background, Func<string, UiColor> ink, double fillOpacity, double spanFrom, double spanWidth)
    {
        if (slices.Any(IsManyPass))
        {
            DrawManyPassFold(d, view, row, index, clip, slices, laneTop, width, colour, nameInk, background, ink, fillOpacity, spanFrom, spanWidth);
            return;
        }
        var hovered = view.HoverClip == (row, index);
        var fillColour = hovered ? ThemeColours.Brighten(colour, HoverClipLightenFraction) : colour;
        for (var i = 0; i < slices.Count; i++)
        {
            var slice = slices[i];
            var rect = SliceRect(view, slice, laneTop);
            if (!double.IsNaN(spanFrom) && (rect.Right < spanFrom || rect.X > spanFrom + spanWidth)) continue;
            FillClip(d, view, rect, fillColour, colour, fillOpacity);
            d.PushClip(rect.X, rect.Y, rect.Width, rect.Height);
            if (clip.IsMidi)
            {
                var span = Math.Max(1e-9, slice.EndSec - slice.StartSec);
                DrawMidiNotes(d, view, clip, rect, WaveColour(view, colour, nameInk), seconds => rect.X + (seconds - slice.StartSec) / span * rect.Width);
            }
            else view.Waveforms.Draw(d, SliceProxy(clip, i, slice), rect, WaveColour(view, colour, nameInk), fillOpacity, width, nameInk);
            if (i == 0 && rect.Width > MinNameWidth)
                d.DrawText(d.Text(clip.Name, TimelineView.Font, NameFontSize, nameInk, d.PixelsPerDip, weight: NameWeight), rect.X + NameInset, rect.Y + LanePadding);
            if (slice.IsPart)
            {
                var label = d.Text((slice.PartIndex + 1).ToString(CultureInfo.InvariantCulture), TimelineView.Font, NameFontSize,
                    nameInk.WithOpacity(StackNumberOpacity), d.PixelsPerDip);
                d.DrawText(label, rect.X + NameInset, rect.Y + Math.Max(0, (rect.Height - label.Height) / 2));
            }
            else DrawPassTag(d, slice, rect, nameInk);
            DrawSliceFades(d, clip, slice, rect, ink);
            d.Pop();
        }
        DrawSkippedPasses(d, view, slices, laneTop, ink, spanFrom, spanWidth);
        var box = FoldBox(view, slices, laneTop);
        if (view.IsClipSelected(clip))
            d.Rect(null, new UiStroke(ink("InstrumentAccent"), SelectionStrokeWidth), box.X, box.Y, box.Width, box.Height, ClipCornerRadius);
        if (ShowsFadeHandles(view, clip, row, index)) DrawFadeHandles(d, view, clip, box, nameInk, background, clampToBox: true);
    }

    /// <summary>Draws a many-pass fold (a repeat above <see cref="TimelineRepeatPicker.StackedMaxPasses"/>): the shown pass only,
    /// at full lane height, plus its picker; the outline and the fade handles still span the whole fold (RV4).</summary>
    private static void DrawManyPassFold(IDrawContext d, TimelineView view, int row, int index, AudioClip clip, IReadOnlyList<RepeatSlice> slices,
        double laneTop, double width, UiColor colour, UiColor nameInk, UiColor background, Func<string, UiColor> ink, double fillOpacity, double spanFrom, double spanWidth)
    {
        var hovered = view.HoverClip == (row, index);
        var fillColour = hovered ? ThemeColours.Brighten(colour, HoverClipLightenFraction) : colour;
        var regions = TimelineRepeatPicker.RegionsOfFold(view, slices);
        var track = view.Song.Tracks[row];
        var named = false;
        for (var i = 0; i < slices.Count; i++)
        {
            var slice = slices[i];
            var region = IsManyPass(slice)
                ? TimelineRepeatPicker.RegionOfSlice(view, regions, slice)
                : null;
            if (region is { } picked && slice.PartIndex != view.RepeatShown.ShownPass(picked) - 1) continue;
            var full = region is not null;
            var rect = SliceRect(view, slice, laneTop, full);
            if (!double.IsNaN(spanFrom) && (rect.Right < spanFrom || rect.X > spanFrom + spanWidth)) continue;
            FillClip(d, view, rect, fillColour, colour, fillOpacity);
            d.PushClip(rect.X, rect.Y, rect.Width, rect.Height);
            if (clip.IsMidi)
            {
                var span = Math.Max(1e-9, slice.EndSec - slice.StartSec);
                DrawMidiNotes(d, view, clip, rect, WaveColour(view, colour, nameInk), seconds => rect.X + (seconds - slice.StartSec) / span * rect.Width);
            }
            else view.Waveforms.Draw(d, SliceProxy(clip, i, slice), rect, WaveColour(view, colour, nameInk), fillOpacity, width, nameInk);
            if (!named && rect.Width > MinNameWidth)
            {
                d.DrawText(d.Text(clip.Name, TimelineView.Font, NameFontSize, nameInk, d.PixelsPerDip, weight: NameWeight), rect.X + NameInset, rect.Y + LanePadding);
                named = true;
            }
            if (slice.IsPart && !full)
            {
                var label = d.Text((slice.PartIndex + 1).ToString(CultureInfo.InvariantCulture), TimelineView.Font, NameFontSize,
                    nameInk.WithOpacity(StackNumberOpacity), d.PixelsPerDip);
                d.DrawText(label, rect.X + NameInset, rect.Y + Math.Max(0, (rect.Height - label.Height) / 2));
            }
            else DrawPassTag(d, slice, rect, nameInk);
            DrawSliceFades(d, clip, slice, rect, ink);
            d.Pop();
        }
        foreach (var region in regions)
        {
            var left = view.BarX(region.StartBar);
            var right = view.BarX(region.EndBar + 1);
            if (!double.IsNaN(spanFrom) && (right < spanFrom || left > spanFrom + spanWidth)) continue;
            TimelineRepeatPicker.DrawPicker(d, view, track, region, laneTop, ink);
        }
        var box = FoldBox(view, slices, laneTop);
        if (view.IsClipSelected(clip))
            d.Rect(null, new UiStroke(ink("InstrumentAccent"), SelectionStrokeWidth), box.X, box.Y, box.Width, box.Height, ClipCornerRadius);
        if (ShowsFadeHandles(view, clip, row, index)) DrawFadeHandles(d, view, clip, box, nameInk, background, clampToBox: true);
    }

    /// <summary>The fade wedges that fall inside <paramref name="slice"/>: the fade-in on the slice holding the clip start, the fade-out on the one holding its end.</summary>
    private static void DrawSliceFades(IDrawContext d, AudioClip clip, RepeatSlice slice, Rect box, Func<string, UiColor> ink)
    {
        if (clip.IsMidi || (clip.FadeInSec <= 0 && clip.FadeOutSec <= 0)) return;   // MIDI takes have no fades
        var fill = ink("TimelineRow").WithOpacity(FadeShadeOpacity);
        var stroke = new UiStroke(ink("TextPrimary").WithOpacity(FadeRampStrokeOpacity), FadeRampStrokeWidth);
        var span = Math.Max(1e-9, slice.EndSec - slice.StartSec);
        double XAt(double seconds) => Math.Clamp(box.X + (seconds - slice.StartSec) / span * box.Width, box.X, box.Right);
        if (clip.FadeInSec > 0 && Math.Abs(slice.StartSec - clip.StartSec) < 1e-6)
            Wedge(d, fill, stroke, box.X, XAt(clip.StartSec + clip.FadeInSec), box);
        if (clip.FadeOutSec > 0 && Math.Abs(slice.EndSec - clip.EndSec) < 1e-6)
            Wedge(d, fill, stroke, box.Right, XAt(clip.EndSec - clip.FadeOutSec), box);
    }

    /// <summary>A stand-in clip holding exactly the source span of <paramref name="slice"/>, so the waveform of that slice fills the slice box.</summary>
    private static AudioClip SliceProxy(AudioClip clip, int index, RepeatSlice slice)
    {
        var proxies = SliceProxies.GetOrCreateValue(clip);
        while (proxies.Count <= index) proxies.Add(new AudioClip());
        var proxy = proxies[index];
        var speed = Math.Clamp(clip.Speed, MinClipSpeed, MaxClipSpeed);
        var into = Math.Max(0, slice.StartSec - clip.StartSec) * speed;
        var looped = TabForge.Services.ClipLoop.Loops(clip);
        proxy.File = clip.File;
        proxy.FileLengthSec = clip.FileLengthSec;
        proxy.GainDb = clip.GainDb;
        proxy.Speed = 1;
        proxy.StartSec = slice.StartSec;
        proxy.OffsetSec = clip.OffsetSec + (looped ? into % TabForge.Services.ClipLoop.PeriodSec(clip) : into);
        proxy.SourceLengthSec = Math.Max(0, (slice.EndSec - slice.StartSec) * speed);
        return proxy;
    }
}
