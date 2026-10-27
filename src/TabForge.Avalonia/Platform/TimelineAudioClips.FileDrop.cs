using System;
using System.Linq;
using Avalonia;
using TabForge.Models;
using TabForge.Platform;

namespace TabForge.Avalonia.Platform;

// Owns: the ghost clips of a file drag from outside (one per file, on the lane they land on, folded over a repeat like a landed clip), their labels, the thin time line at the
//   drop start and its "Bar 17.3 · 0:32.4" readout.
// Does not own: choosing the landing (AudioDropLanding, AudioDropPreviewPlanner), the lane shade and slot (DrawFileDropPreview) or the drag events (MainWindowAudioDrop).
// Tests: TabForge.Avalonia.Tests.AudioDropPreviewTests, TabForge.Avalonia.Tests.AudioDropLandingTests.
internal static partial class TimelineAudioClips
{
    private const double FileDropLineWidth = 1;
    private const double FileDropReadoutPad = 5;
    private const double FileDropReadoutGap = 6;
    private const double FileDropReadoutFillOpacity = 0.92;

    /// <summary>Draws the file ghosts at their landing, then the time line and readout. A ghost over a repeat draws through the clip fold (<see cref="FoldOf"/>), so it shows the
    /// slices the landed clip will have.</summary>
    private static void DrawFileDropParts(IDrawContext d, Func<string, UiColor> ink, TimelineView view, AudioFileDropPreview preview, UiColor colour, double laneTop, UiColor accent)
    {
        var parts = preview.Parts is { Count: > 0 } given ? given : new[] { new AudioClip { Name = preview.Label, StartSec = preview.StartSec, SourceLengthSec = preview.EndSec - preview.StartSec } };
        var textInk = ink("TextPrimary");
        var boxHeight = ClipLanes.AudioLaneHeight - 2 * LanePadding;
        foreach (var part in parts)
        {
            // The ghost box is the landed clip's box (BoundsOf / SliceRect): the same lane padding, height and minimum width, so the drop shows what lands.
            Rect[] boxes;
            if (FoldOf(view, part) is { } slices) boxes = slices.Select(slice => SliceRect(view, slice, laneTop, fullHeight: true)).ToArray();
            else
            {
                var x1 = view.XOfSeconds(part.StartSec);
                boxes = new[] { new Rect(x1, laneTop + LanePadding, Math.Max(x1 + MinClipWidth, view.XOfSeconds(part.EndSec)) - x1, boxHeight) };
            }
            for (var i = 0; i < boxes.Length; i++)
            {
                var box = boxes[i];
                d.Rect(colour.WithOpacity(FileDropBlockFillOpacity), new UiStroke(colour, FileDropBlockStrokeWidth), box.X, box.Y, box.Width, box.Height, ClipCornerRadius);
                if (i == 0) DrawFileDropText(d, textInk, box, part.Name, parts.Count == 1 ? AudioDropPreviewPlanner.Detail(preview) : "");
            }
        }
        DrawFileDropTimeLine(d, ink, view, preview, laneTop, accent);
    }

    private static void DrawFileDropText(IDrawContext d, UiColor textInk, Rect box, string label, string detail)
    {
        if (box.Width <= 2 * FileDropTextInset || string.IsNullOrEmpty(label)) return;
        d.PushClip(box.X, box.Y, box.Width, box.Height);
        d.DrawText(d.Text(label, TimelineView.Font, FileDropLabelFontSize, textInk, d.PixelsPerDip, weight: 600), box.X + FileDropTextInset, box.Y + LanePadding);
        if (box.Width > 4 * FileDropTextInset && detail.Length > 0)
        {
            // The length (and "new lane") only when it fits: a cut-off number would read as a different length.
            var pieces = detail.Split(" · ");
            for (var count = pieces.Length; count >= 1; count = count > 1 ? 1 : 0)
            {
                var line = string.Join(" · ", pieces.Take(count));
                if (d.Text(line, TimelineView.Font, FileDropDetailFontSize, textInk, d.PixelsPerDip).Width + 2 * FileDropTextInset > box.Width) continue;
                d.DrawText(d.Text(line, TimelineView.Font, FileDropDetailFontSize, textInk, d.PixelsPerDip), box.X + FileDropTextInset, box.Y + FileDropLabelFontSize + FileDropDetailTop);
                break;
            }
        }
        d.Pop();
    }

    /// <summary>The thin vertical line at the drop start and, beside it, the bar/beat and time of that second (kept inside the visible span).</summary>
    private static void DrawFileDropTimeLine(IDrawContext d, Func<string, UiColor> ink, TimelineView view, AudioFileDropPreview preview, double laneTop, UiColor accent)
    {
        var x = view.XOfSeconds(preview.StartSec);
        d.Rect(accent, null, x - FileDropLineWidth / 2, 0, FileDropLineWidth, Math.Max(0, view.Bounds.Height));
        if (string.IsNullOrEmpty(preview.Readout)) return;
        var text = d.Text(preview.Readout, TimelineView.Font, FileDropDetailFontSize, ink("TextPrimary"), d.PixelsPerDip);
        var width = text.Width + 2 * FileDropReadoutPad;
        var height = text.Height + FileDropReadoutPad;
        var (from, span) = view.VisibleSpan;
        var right = double.IsNaN(from) ? view.Bounds.Width : from + span;
        var left = x + FileDropReadoutGap;
        if (left + width > right) left = x - FileDropReadoutGap - width;
        var top = Math.Max(2, laneTop - height - 3);
        d.Rect(ink("TimelineRow").WithOpacity(FileDropReadoutFillOpacity), new UiStroke(accent, FileDropLineWidth), left, top, width, height, ClipCornerRadius);
        d.DrawText(text, left + FileDropReadoutPad, top + FileDropReadoutPad / 2);
    }
}
