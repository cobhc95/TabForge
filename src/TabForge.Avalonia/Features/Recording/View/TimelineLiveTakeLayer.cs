using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using TabForge.Avalonia.Features.Recording.Presenter;
using TabForge.Avalonia.Platform;
using TabForge.Features.Recording.Model;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Platform.Theme;

namespace TabForge.Avalonia.Features.Recording.View;

// Owns: the live recording take overlay of the arrangement timeline: one growing red box per take in its lane (one live lane per lap,
//   folded onto the loop range, once a looped take wraps), the input-peak envelope (audio) or the notes heard so far (MIDI), the "● REC" label and the per-take clip to the track rows. Drawn on its own layer only, so a
//   recording frame repaints this layer and never the timeline body.
// Does not own: the take's state and lane rules (LiveTakes), the per-frame feed (RecordingPresenter), the timeline geometry
//   (TimelineView), the clip drawing (TimelineAudioClips) or the playhead (TimelinePlayheadLayer).
// Tests: TabForge.Avalonia.Tests.RecordingLiveTakeTests, LiveTakeFoldTests, RepeatTakeLandingTests.
internal sealed class TimelineLiveTakeLayer : Control
{
    private const double BoxInset = 3;
    private const double BoxCornerRadius = 3;
    private const double BoxMinWidth = 2;
    private const double BoxFillOpacity = 0.22;
    private const double BoxStrokeWidth = 1.2;
    /// <summary>The narrowest box that still shows its label.</summary>
    private const double RecLabelMinWidth = 40;
    private const double RecLabelLeft = 5;
    private const double RecLabelTop = 1;
    private const double RecLabelFontSize = 10;
    private const string RecLabel = "● REC";
    /// <summary>How many columns the peak envelope draws at most; bounds the work of a very long take in one frame.</summary>
    private const int MaxPeakColumns = 2048;
    /// <summary>
    /// A repeat band's drawn right edge is measured just inside its end: a play ends exactly where the next play of an
    /// earlier bar starts, which otherwise maps onto that earlier bar. A tenth of a millisecond is far below one DIP.
    /// </summary>
    private const double RepeatEdgeEpsilonSec = 1e-4;    private const double PeakMargin = 6;
    private const double PeakCentreShift = 4;
    private const double PeakMinHeight = 0.5;
    private const double PeakOpacity = 0.9;
    private const double PeakStrokeWidth = 1;
    private const double NoteMargin = 3;

    private readonly TimelineView _view;
    private readonly AvaloniaDrawContext _draw = new();
    private UiText? _label;
    private UiColor _labelColour;
    private string _labelText = "";
    private bool _labelMeasured;

    public TimelineLiveTakeLayer(TimelineView view)
    {
        _view = view;
        IsHitTestVisible = false;
    }

    /// <summary>The takes this layer draws (the same object the recording feeds).</summary>
    public LiveTakes Takes { get; set; } = new();

    public override void Render(DrawingContext context) =>
        Draw(_draw.Use(context), ActualThemeVariant == ThemeVariant.Light, Math.Max(Bounds.Width, _view.BarsWidth));

    /// <summary>
    /// Draws the takes through any draw context (a test records the rectangles). Every take is clipped to its own track row, so a box can
    /// never spill into a neighbour, and a looped take that has wrapped draws each lap on its own live lane inside the row, folded onto
    /// the loop range (never laid end-to-end in time).
    /// </summary>
    internal void Draw(IDrawContext d, bool light, double width)
    {
        if (Takes.Count == 0) return;
        var top = _view.TrackRowsTop;
        var bottom = _view.TrackRowsBottom;
        if (bottom <= top) return;
        var red = AvaloniaPalette.Get("Danger", light);
        d.PushClip(0, top, width, bottom - top);
        for (var i = 0; i < Takes.Count; i++)
        {
            var take = Takes[i];
            var row = _view.Song.Tracks.IndexOf(take.Track);
            if (row < 0) continue;
            var rowTop = _view.RowTop(row);
            var rowHeight = _view.RowHeightOf(row);
            if (rowHeight <= 0) continue;
            // A take is clipped to its own row: a stale lane or a disarmed track can never draw over the next row.
            d.PushClip(0, rowTop, width, rowHeight);
            DrawTake(d, take, row, rowTop, rowHeight, red, light);
            d.Pop();
        }
        d.Pop();
    }

    private void DrawTake(IDrawContext d, LiveTake take, int row, double rowTop, double rowHeight, UiColor red, bool light)
    {
        if (take.LaneCount == 0) return;   // no lane band yet: nothing to draw the take in
        var laidOut = _view.LaidOutLaneCountOf(take.Track);
        if (laidOut == 0) return;
        var bandHeight = ClipLanes.AudioLaneHeight;

        // The one live-lap rule decides the bands: before the first wrap it is one box from the record start to the playhead; after a
        // wrap every lap (completed or partial) is folded onto the loop range on its own live lane below the take's base lane.
        var bands = take.Bands;
        if (take.RepeatBandsActive) { DrawRepeatBands(d, take, bands, row, rowTop, rowHeight, bandHeight, red, light); return; }
        for (var index = 0; index < bands.Count; index++)
        {
            var band = bands[index];
            var lane = Math.Min(Math.Max(0, take.Lane + band.Row), laidOut - 1);   // a stale lane never leaves the laid-out bands
            var top = _view.LaneBandTopOf(row, lane);
            if (top < rowTop) top = rowTop;
            if (top + bandHeight > rowTop + rowHeight) top = Math.Max(rowTop, rowTop + rowHeight - bandHeight);
            // The envelope and notes are sampled from the lap's own capture-time range (a folded lap replays the loop range on screen).
            var (sampleFrom, sampleTo) = SampleRange(take, band);
            DrawBox(d, take, sampleFrom, sampleTo, band.StartSec, band.EndSec, top, bandHeight, red, light, label: index == bands.Count - 1);
        }
    }

    /// <summary>
    /// The live repeat take (repeat view): every band stays on the take's own lane — ordinary spans full height, each play of a
    /// repeated range as slice k of N inside the lane (the same fold as a landed clip), each numbered by its play. The partial play grows from its occurrence start.
    /// </summary>
    private void DrawRepeatBands(IDrawContext d, LiveTake take, IReadOnlyList<LiveTakeBand> bands, int row, double rowTop, double rowHeight,
        double bandHeight, UiColor red, bool light)
    {
        var laidOut = _view.LaidOutLaneCountOf(take.Track);
        var lane = Math.Min(Math.Max(0, take.Lane), laidOut - 1);
        var top = _view.LaneBandTopOf(row, lane);
        if (top < rowTop) top = rowTop;
        if (top + bandHeight > rowTop + rowHeight) top = Math.Max(rowTop, rowTop + rowHeight - bandHeight);
        for (var index = 0; index < bands.Count; index++)
        {
            var band = bands[index];
            // The box is drawn just inside the band end (see RepeatEdgeEpsilonSec); the envelope still samples the whole band.
            var drawEnd = Math.Max(band.StartSec, band.EndSec - RepeatEdgeEpsilonSec);
            if (band.Pass <= 0)
            {
                DrawBox(d, take, band.StartSec, band.EndSec, band.StartSec, drawEnd, top, bandHeight, red, light,
                    label: index == bands.Count - 1, partNumber: null, tagPass: band.TagPass);
                continue;
            }
            // Slice k of N (N the repeat's part count, even when only some plays exist yet): the repeat view's fold.
            var partHeight = bandHeight / Math.Max(1, band.PartCount);
            var partTop = top + Math.Max(0, band.Pass - 1) * partHeight;
            DrawBox(d, take, band.StartSec, band.EndSec, band.StartSec, drawEnd, partTop, partHeight, red, light,
                label: false, partNumber: band.Pass.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    // The song-time range the take captured for <paramref name="band"/>'s lap: lap 1 runs from the take start to the loop end,
    // every later lap is one loop length from the loop end; the partial lap ends at the take's front. A take with no loop (or
    // before its first wrap) samples the band it draws. A repeat take's bands are occurrence-stamped, so they sample themselves.
    private static (double From, double To) SampleRange(LiveTake take, LiveTakeBand band)
    {
        if (take.RepeatBandsActive) return (band.StartSec, band.EndSec);
        if (take.Loop is not { } loop) return (band.StartSec, band.EndSec);
        var length = loop.End - loop.Start;
        if (length < LiveTakeFold.MinimumLoopSec || band.Pass <= 1) return (take.StartSec, Math.Min(loop.End, take.EndSec));
        var from = loop.End + (band.Pass - 2) * length;
        return (from, Math.Min(from + length, take.EndSec));
    }

    private void DrawBox(IDrawContext d, LiveTake take, double fromSec, double toSec, double startSec, double endSec, double top, double height,
        UiColor red, bool light, bool label, string? partNumber = null, int tagPass = 0)
    {
        var x1 = _view.XOfSeconds(startSec);
        var x2 = _view.XOfSeconds(Math.Max(startSec, endSec));
        var box = new Rect(x1, top + BoxInset, Math.Max(BoxMinWidth, x2 - x1), Math.Max(1, height - 2 * BoxInset));
        d.Rect(red.WithOpacity(BoxFillOpacity), new UiStroke(red, BoxStrokeWidth), box.X, box.Y, box.Width, box.Height, BoxCornerRadius);
        d.PushClip(box.X, box.Y, box.Width, box.Height);
        if (take.Midi) DrawNotes(d, take, box, red, fromSec, toSec);
        else DrawPeaks(d, take, box, red, fromSec, toSec);
        if (partNumber is not null)
        {
            // Repeat part numbers always draw (like the landed take): a just-started part 2 is narrow but still numbered.
            d.DrawText(d.Text(partNumber, TimelineView.Font, RecLabelFontSize, red, d.PixelsPerDip), box.X + RecLabelLeft, box.Y + RecLabelTop);
            d.Pop();
            return;
        }
        if (tagPass >= 2)
        {
            // The same "pass k" tag a landed clip shows (one drawing); like a part number it replaces the label.
            TimelineAudioClips.DrawPassTag(d, tagPass, box, red);
            d.Pop();
            return;
        }
        if (label)
        {
            var wide = box.Width > RecLabelMinWidth;
            if (wide) d.DrawText(Label(d, red, light), box.X + RecLabelLeft, box.Y + RecLabelTop);
            d.Pop();
            // A count-in hold freezes the span at the 2 px minimum: the unclipped label surfaces the hold next to the line.
            if (!wide) d.DrawText(Label(d, red, light), box.X + RecLabelLeft, box.Y + RecLabelTop);
            return;
        }
        d.Pop();
    }

    // The label is measured once per theme, so an overlay frame draws it without a text-layout allocation.
    private UiText Label(IDrawContext d, UiColor colour, bool light)
    {
        if (_labelMeasured && _label is { } cached && _labelColour == colour && _labelText == RecLabel) return cached;
        _label = d.Text(RecLabel, TimelineView.Font, RecLabelFontSize, colour, d.PixelsPerDip);
        _labelColour = colour;
        _labelText = RecLabel;
        _labelMeasured = true;
        return _label.Value;
    }

    // One vertical line per pixel column, square-root scaled (quiet takes stay readable), sampled from the bounded envelope.
    private static void DrawPeaks(IDrawContext d, LiveTake take, in Rect box, UiColor red) =>
        DrawPeaks(d, take, box, red, take.StartSec, take.EndSec);

    // <param name="fromSec">The song second at the piece's left edge; a folded row samples the envelope of its own lap.</param>
    private static void DrawPeaks(IDrawContext d, LiveTake take, in Rect box, UiColor red, double fromSec, double toSec)
    {
        var peaks = take.Peaks;
        if (peaks.Count == 0 || box.Width < 1) return;
        var span = Math.Max(0.01, take.EndSec - take.StartSec);
        var from = Math.Clamp((fromSec - take.StartSec) / span, 0, 1);
        var to = Math.Clamp((toSec - take.StartSec) / span, 0, 1);
        var first = Math.Clamp((int)(from * peaks.Count), 0, peaks.Count - 1);
        var last = Math.Clamp((int)Math.Ceiling(to * peaks.Count), first + 1, peaks.Count);
        var columns = Math.Clamp((int)box.Width, 1, MaxPeakColumns);
        var half = box.Height / 2 - PeakMargin;
        if (half <= 0) return;
        var mid = box.Y + box.Height / 2 + PeakCentreShift;
        var stroke = new UiStroke(red.WithOpacity(PeakOpacity), PeakStrokeWidth);
        for (var i = 0; i < columns; i++)
        {
            var peak = peaks[first + (int)((long)i * (last - first) / columns)];
            var height = Math.Sqrt(Math.Clamp(peak, 0f, 1f)) * half;
            if (height < PeakMinHeight) continue;
            var x = box.X + i + 0.5;
            d.Line(stroke, x, mid - height, x, mid + height);
        }
    }

    // MIDI: one short bar per finished note plus the notes held now, growing to the take's current end.
    private static void DrawNotes(IDrawContext d, LiveTake take, in Rect box, UiColor red) =>
        DrawNotes(d, take, box, red, take.StartSec, take.EndSec);

    // <param name="fromSec">The song second at the piece's left edge; a folded row shows the notes of its own lap.</param>
    private static void DrawNotes(IDrawContext d, LiveTake take, in Rect box, UiColor red, double fromSec, double toSec)
    {
        var span = Math.Max(0.01, toSec - fromSec);
        int low = int.MaxValue, high = int.MinValue;
        foreach (var note in take.Notes) { if (note.Pitch < low) low = note.Pitch; if (note.Pitch > high) high = note.Pitch; }
        foreach (var pitch in take.Held.Keys) { if (pitch < low) low = pitch; if (pitch > high) high = pitch; }
        if (low > high) return;
        var range = new MidiPitchRange(low, high);
        foreach (var note in take.Notes)
        {
            var start = take.StartSec + note.StartSec;
            if (start + note.LengthSec <= fromSec || start >= toSec) continue;
            DrawNote(d, red, box, fromSec, span, range, start, note.LengthSec, note.Pitch, note.Velocity);
        }
        foreach (var (pitch, held) in take.Held)
        {
            var start = Math.Max(take.StartSec, held.Start);
            var end = Math.Max(start + 0.01, take.EndSec);
            if (end <= fromSec || start >= toSec) continue;
            DrawNote(d, red, box, fromSec, span, range, start, Math.Max(0.01, end - start), pitch, held.Velocity);
        }
    }

    private static void DrawNote(IDrawContext d, UiColor colour, in Rect box, double fromSec, double span, MidiPitchRange range,
        double startSec, double lengthSec, int pitch, int velocity)
    {
        var x = box.X + Math.Clamp((startSec - fromSec) / span, 0, 1) * box.Width;
        var end = Math.Min(x + Math.Clamp(lengthSec / span, 0, 1) * box.Width, box.Right);
        MidiNotePainter.Note(d, colour, range, box.Y + NoteMargin, box.Height - 2 * NoteMargin, x, end, pitch, velocity);
    }
}
