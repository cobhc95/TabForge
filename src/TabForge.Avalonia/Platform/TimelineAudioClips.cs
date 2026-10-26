using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using TabForge.Avalonia;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Services;

namespace TabForge.Avalonia.Platform;

/// <summary>
/// The audio clips of the arrangement timeline, drawn in the lane bands of their track's row. Each clip runs from its start to its end in song
/// time (placed by <see cref="TimelineView.XOfSeconds"/>), is named, and takes the outline when it is the selected clip.
/// A clip that plays through a repeat draws folded onto the written bars (slice k of N inside its lane, numbered; one outline, see TimelineAudioClips.Fold.cs).
/// Clips that are not audible are drawn faded.
/// Owns: the lane bands and clip rectangles of the timeline, their drawing and their hit test.
/// Does not own: the bar and row geometry (TimelineView), the click that selects (TimelineInputController), the clip edits (ClipEditController),
/// and the waveform outline (<see cref="TimelineWaveforms"/>), which this view asks for.
/// Tests: TabForge.Avalonia.Tests.TimelineAudioClipTests, RepeatViewTests.
/// </summary>
internal static partial class TimelineAudioClips
{
    private const double LanePadding = 2;
    // A clip's fill, name and wave: a 0.34 fill keeps the same-colour waveform readable, the 10pt name sits above it.
    private const double ClipFillOpacity = 0.34;
    private const double MutedClipOpacity = 0.45;
    // The light theme's clips are solid track colour with a dark name; a muted one fades and takes a 1px track-colour border.
    private const double LightClipFillOpacity = 0.9;
    private const double LightMutedClipOpacity = 0.35;
    private const double NameFontSize = 10;
    private const double NameInset = 5;
    /// <summary>How far the clip under the pointer is blended towards white (0-1); its own hue stays.</summary>
    public const double HoverClipLightenFraction = 0.25;
    private const double ClipCornerRadius = 2;
    private const double MinClipWidth = 3;
    private const int NameWeight = 400;
    /// <summary>A clip narrower than this shows no name.</summary>
    private const double MinNameWidth = 30;
    // A MIDI clip's notes (the DAW mini piano roll): the clip's own pitch range spread over the clip height.
    private const double MinNoteHeight = 4;
    private const double MinClipSpeed = 0.25;
    private const double MaxClipSpeed = 4;
    private const double SelectionStrokeWidth = 2;
    private const double DropLaneFillOpacity = 0.16;
    private const double DropLaneStrokeWidth = 1;
    /// <summary>A file drag's lane shade, in the lane's own track colour (the ghost's track-coloured shade: a red shade on a red track).</summary>
    private const double FileDropLaneShadeOpacity = 0.10;
    /// <summary>A file drag's landing block, in the lane's own track colour.</summary>
    private const double FileDropBlockFillOpacity = 0.38;
    private const double FileDropBlockStrokeWidth = 1.2;
    private const double FileDropLabelFontSize = 11;
    private const double FileDropDetailFontSize = 10;
    private const double FileDropTextInset = 6;
    private const double FileDropDetailTop = 3;
    /// <summary>A file drag's new-lane slot: an accent wash with a dashed border.</summary>
    private const double FileDropSlotFillOpacity = 0.07;
    private const double FileDropSlotDash = 4;
    private const double FileDropSlotGap = 3;
    // A lane band's board fill and its track-colour tint: stronger on a lane that plays, faint on a silenced take lane.
    private const double LaneBoardOpacity = 0.55;
    private const double LanePlayingTintOpacity = 0.08;
    private const double LaneSilentTintOpacity = 0.03;
    // The fade shading: the silenced wedge over the gain ramp, in the board colour, with the ramp as a faint text-coloured line.
    private const double FadeShadeOpacity = 0.55;
    private const double FadeRampStrokeWidth = 1;
    private const double FadeRampStrokeOpacity = 0.8;
    // A repeat take's part numbers: small at each stacked row's left.
    internal const double StackNumberOpacity = 0.55;

    /// <summary>Draws one track's lane bands (an empty lane stays visible) and its clips; <paramref name="top"/> is below the notation band, <paramref name="colour"/> the track's colour.</summary>
    public static void DrawRow(IDrawContext d, Func<string, UiColor> ink, TimelineView view, TrackModel track, int row, double top, UiColor colour)
    {
        view.Waveforms.MissingInk = ink("Danger");   // a clip whose file is gone draws hatched in this colour
        var lanes = ClipLanes.LaidOutCount(track);
        if (lanes == 0) return;
        var laneHeight = ClipLanes.AudioLaneHeight;
        var width = Math.Max(view.BarsWidth, view.Bounds.Width);
        // The lanes are a flat fill: only the visible span draws (a scroll redraws anyway). Clips outside it are skipped wholly.
        var (spanFrom, spanWidth) = view.VisibleSpan;
        var laneFrom = double.IsNaN(spanFrom) ? 0 : Math.Max(0, spanFrom);
        var laneTo = double.IsNaN(spanFrom) ? width : Math.Min(width, spanFrom + spanWidth);
        var light = view.FrameLight;
        var board = ink("TimelineLane");
        for (var lane = 0; lane < lanes; lane++)
        {
            var laneTop = top + lane * ClipLanes.AudioLaneHeight;
            d.Rect(board.WithOpacity(light ? 1 : LaneBoardOpacity), null, laneFrom, laneTop, Math.Max(0, laneTo - laneFrom), laneHeight);
            if (light) continue;
            d.Rect(colour.WithOpacity(ClipLanes.Plays(track, lane) ? LanePlayingTintOpacity : LaneSilentTintOpacity), null, laneFrom, laneTop, Math.Max(0, laneTo - laneFrom), laneHeight);
        }
        var background = ink("TimelineRow");
        var playingOpacity = light ? LightClipFillOpacity : ClipFillOpacity;
        var surface = light ? board : background;
        if (view.ClipDropLane is { } drop && drop.Track == track)
        {
            var accent = ink("InstrumentAccent");
            var dropTop = top + Math.Max(0, drop.Lane) * ClipLanes.AudioLaneHeight;
            d.Rect(accent.WithOpacity(DropLaneFillOpacity), new UiStroke(accent, DropLaneStrokeWidth), laneFrom, dropTop, Math.Max(0, laneTo - laneFrom), laneHeight);
        }
        // Every clip draws on its lane band; one that plays through a repeat draws folded onto the written bars (the repeat view).
        for (var index = 0; index < track.AudioClips.Count; index++)
        {
            var clip = track.AudioClips[index];
            var laneTop = top + Math.Max(0, clip.Lane) * ClipLanes.AudioLaneHeight;
            var fillOpacity = ClipLanes.Audible(track, clip) ? playingOpacity : light ? LightMutedClipOpacity : MutedClipOpacity;
            var nameInk = ClipInk(surface, colour, fillOpacity);
            if (FoldOf(view, clip) is { } slices)
            {
                DrawFoldedClip(d, view, row, index, clip, slices, laneTop, width, colour, nameInk, background, ink, fillOpacity, spanFrom, spanWidth);
                continue;
            }
            var rect = BoundsOf(view, track, clip, laneTop, laneHeight);
            if (!double.IsNaN(spanFrom) && (rect.Right < spanFrom || rect.X > spanFrom + spanWidth)) continue;
            DrawClipBox(d, view, track, row, index, clip, rect, width, colour, nameInk, background, ink, fillOpacity);
        }
    }

    /// <summary>The name and wave ink for a clip: the better of light/dark on the clip's fill as composited (<paramref name="fillOpacity"/> of the track colour over the lane).</summary>
    internal static UiColor ClipInk(UiColor lane, UiColor colour, double fillOpacity) => ThemeColours.InkOn(ThemeColours.Blend(lane, colour, fillOpacity));

    /// <summary>The clip's box. A light-theme clip that is muted (fill below the playing opacity) also takes a 1px track-colour border.</summary>
    internal static void FillClip(IDrawContext d, TimelineView view, Rect rect, UiColor fillColour, UiColor colour, double fillOpacity) =>
        d.Rect(fillColour.WithOpacity(fillOpacity), view.FrameLight && fillOpacity < LightClipFillOpacity ? new UiStroke(colour, 1) : null, rect.X, rect.Y, rect.Width, rect.Height, ClipCornerRadius);

    /// <summary>The colour of a clip's wave and notes: the track colour over the dark theme's faint fill, the name ink over the light theme's solid one.</summary>
    internal static UiColor WaveColour(TimelineView view, UiColor colour, UiColor nameInk) => view.FrameLight ? nameInk : colour;

    /// <summary>Draws one clip box with its wave, name, fades and outline.</summary>
    private static void DrawClipBox(IDrawContext d, TimelineView view, TrackModel track, int row, int index, AudioClip clip, Rect rect, double width,
        UiColor colour, UiColor nameInk, UiColor background, Func<string, UiColor> ink, double fillOpacity)
    {
        var hovered = view.HoverClip == (row, index);
        var fillColour = hovered ? ThemeColours.Brighten(colour, HoverClipLightenFraction) : colour;
        FillClip(d, view, rect, fillColour, colour, fillOpacity);
        d.PushClip(rect.X, rect.Y, rect.Width, rect.Height);
        if (clip.IsMidi) DrawMidiNotes(d, view, clip, rect, WaveColour(view, colour, nameInk));
        else view.Waveforms.Draw(d, clip, rect, WaveColour(view, colour, nameInk), fillOpacity, width, nameInk);
        if (rect.Width > MinNameWidth)
            d.DrawText(d.Text(clip.Name, TimelineView.Font, NameFontSize, nameInk, d.PixelsPerDip, weight: NameWeight), rect.X + NameInset, rect.Y + LanePadding);
        DrawFadeShades(d, view, clip, rect, ink);
        d.Pop();
        if (view.IsClipSelected(clip))
            d.Rect(null, new UiStroke(ink("InstrumentAccent"), SelectionStrokeWidth), rect.X, rect.Y, rect.Width, rect.Height, ClipCornerRadius);
        if (ShowsFadeHandles(view, clip, row, index)) DrawFadeHandles(d, view, clip, rect, nameInk, background);
    }

    /// <summary>A MIDI clip's notes as a mini piano roll: the clip's own pitch range (lowest to highest note) spread over its height, velocity as opacity.</summary>
    private static void DrawMidiNotes(IDrawContext d, TimelineView view, AudioClip clip, Rect box, UiColor colour, Func<double, double>? xOf = null)
    {
        xOf ??= view.XOfSeconds;
        var notes = clip.Notes;
        if (notes is null || notes.Count == 0) return;
        var low = int.MaxValue;
        var high = int.MinValue;
        foreach (var note in notes) { if (note.Pitch < low) low = note.Pitch; if (note.Pitch > high) high = note.Pitch; }
        var range = new MidiPitchRange(low, high);
        var top = box.Y + LanePadding;
        var height = box.Height - 2 * LanePadding;
        if (height < MinNoteHeight) return;
        var speed = Math.Clamp(clip.Speed, MinClipSpeed, MaxClipSpeed);
        foreach (var piece in ClipLoop.Pieces(clip))
        {
            var end = piece.OffsetSec + piece.SourceLengthSec;
            foreach (var note in notes)
            {
                if (note.StartSec + note.LengthSec <= piece.OffsetSec || note.StartSec >= end) continue;
                var noteStart = piece.StartSec + (Math.Max(note.StartSec, piece.OffsetSec) - piece.OffsetSec) / speed;
                var noteEnd = piece.StartSec + (Math.Min(note.StartSec + note.LengthSec, end) - piece.OffsetSec) / speed;
                var x1 = xOf(noteStart);
                var x2 = xOf(noteEnd);
                if (x2 < box.X || x1 > box.Right) continue;   // outside this box (a folded slice shows only its own part of the clip)
                MidiNotePainter.Note(d, colour, range, top, height, x1, x2, note.Pitch, note.Velocity);
            }
        }
    }

    /// <summary>Draws the file drag's planned landing: the target lane shaded in its own track colour and the landing block with its
    /// label (a new track or lane gets the slot opening under it), as the ghost previews in the drop target's colour. Nothing when
    /// no file drag is over. Called from the interaction pass, so the block previews over the lanes but under the playhead.</summary>
    public static void DrawFileDropPreview(IDrawContext d, Func<string, UiColor> ink, TimelineView view, double width)
    {
        if (view.FileDropPreview is not { } preview) return;
        var laneHeight = ClipLanes.AudioLaneHeight;
        var accent = ink("InstrumentAccent");
        UiColor colour;
        double laneTop;
        if (preview.Track is null)
        {
            // A strip drop makes a new audio track below the last row: its slot opens there (below the rows when they fill the pane, the strip shade covers it then).
            var rows = view.Song.Tracks.Count;
            laneTop = rows == 0 ? view.TrackRowsTop : view.RowTop(rows - 1) + view.RowHeightOf(rows - 1);
            if (laneTop + laneHeight > view.Bounds.Height) return;
            colour = accent;
            DrawFileDropSlot(d, accent, width, laneTop, laneHeight);
        }
        else
        {
            var row = view.Song.Tracks.IndexOf(preview.Track);
            if (row < 0) return;
            laneTop = view.LaneBandTopOf(row, preview.Lane);
            // A lane that does not exist yet opens below the laid-out lanes: it still previews (its slot opens under the block).
            var newLane = preview.Lane >= ClipLanes.Count(preview.Track);
            var bottom = view.TrackRowsBottom + (newLane ? laneHeight : 0);
            if (laneTop + laneHeight <= view.TrackRowsTop || laneTop >= bottom) return;
            colour = view.TrackColourOf(preview.Track, accent);
            d.Rect(colour.WithOpacity(FileDropLaneShadeOpacity), null, 0, laneTop, width, laneHeight);
            if (newLane) DrawFileDropSlot(d, accent, width, laneTop, laneHeight);
        }
        var x1 = view.XOfSeconds(preview.StartSec);
        // The preview box is the landed clip's box (BoundsOf): the same lane padding, height and minimum width, so the drop shows what lands.
        var x2 = Math.Max(x1 + MinClipWidth, view.XOfSeconds(preview.EndSec));
        var boxHeight = laneHeight - 2 * LanePadding;
        var box = new Rect(x1, laneTop + LanePadding, x2 - x1, boxHeight);
        d.Rect(colour.WithOpacity(FileDropBlockFillOpacity), new UiStroke(colour, FileDropBlockStrokeWidth), box.X, box.Y, box.Width, box.Height, ClipCornerRadius);
        var textInk = ink("TextPrimary");
        if (box.Width <= 2 * FileDropTextInset || string.IsNullOrEmpty(preview.Label)) return;
        d.PushClip(box.X, box.Y, box.Width, box.Height);
        d.DrawText(d.Text(preview.Label, TimelineView.Font, FileDropLabelFontSize, textInk, d.PixelsPerDip, weight: 600), box.X + FileDropTextInset, box.Y + LanePadding);
        var detail = AudioDropPreviewPlanner.Detail(preview);
        if (box.Width > 4 * FileDropTextInset && detail.Length > 0)
        {
            // The length (and "new lane") only when it fits: a cut-off number would read as a different length.
            var parts = detail.Split(" · ");
            for (var count = parts.Length; count >= 1; count = count > 1 ? 1 : 0)
            {
                var line = string.Join(" · ", parts.Take(count));
                if (d.Text(line, TimelineView.Font, FileDropDetailFontSize, textInk, d.PixelsPerDip).Width + 2 * FileDropTextInset > box.Width) continue;
                d.DrawText(d.Text(line, TimelineView.Font, FileDropDetailFontSize, textInk, d.PixelsPerDip), box.X + FileDropTextInset, box.Y + FileDropLabelFontSize + FileDropDetailTop);
                break;
            }
        }
        d.Pop();
    }

    private static void DrawFileDropSlot(IDrawContext d, UiColor accent, double width, double top, double height) =>
        d.Rect(accent.WithOpacity(FileDropSlotFillOpacity), new UiStroke(accent, DropLaneStrokeWidth, Dash: FileDropSlotDash, Gap: FileDropSlotGap),
            0, top, width, height);

    /// <summary>The audio clip under <paramref name="point"/> (the one drawn last when clips overlap), or null when the point is on no clip.</summary>
    public static TimelineClipSelection? ClipAt(TimelineView view, Point point)
    {
        if (ClipIndexAt(view, point) is not { } hit) return null;
        var track = view.Song.Tracks[hit.Row];
        return new TimelineClipSelection(track, track.AudioClips[hit.Index]);
    }

    /// <summary>The (track row, index in the track's clips) of the audio clip under <paramref name="point"/> (the one drawn last when clips overlap), or null when the point is on no clip.</summary>
    public static (int Row, int Index)? ClipIndexAt(TimelineView view, Point point)
    {
        var row = view.RowAtY(point.Y);
        if (row < 0 || row >= view.RowCount) return null;
        var track = view.Song.Tracks[row];
        var lanes = ClipLanes.Count(track);
        if (lanes == 0) return null;
        var laneHeight = ClipLanes.AudioLaneHeight;
        var top = LaneTopOf(view, row);
        // The lane bands, the clip drawn last first. A clip folded over a repeat is hit on any of its slices: any part selects the whole clip.
        for (var i = track.AudioClips.Count - 1; i >= 0; i--)
        {
            var clip = track.AudioClips[i];
            var laneTop = top + Math.Max(0, clip.Lane) * ClipLanes.AudioLaneHeight;
            if (FoldOf(view, clip) is { } slices)
            {
                if (slices.Any(slice => SliceRect(view, slice, laneTop, IsManyPass(slice)).Contains(point))) return (row, i);
                continue;
            }
            if (BoundsOf(view, track, clip, laneTop, laneHeight).Contains(point)) return (row, i);
        }
        return null;
    }

    /// <summary>Adds the (track row, index in the track's clips) of every audio clip <paramref name="box"/> touches (a folded clip on any slice) to <paramref name="into"/>; allocates nothing.</summary>
    public static void ClipsIn(TimelineView view, Rect box, List<(int Row, int Index)> into)
    {
        for (var row = 0; row < view.RowCount; row++)
        {
            var track = view.Song.Tracks[row];
            var lanes = ClipLanes.Count(track);
            var top = LaneTopOf(view, row);
            if (lanes == 0 || top > box.Bottom || top + lanes * ClipLanes.AudioLaneHeight < box.Y) continue;
            for (var i = 0; i < track.AudioClips.Count; i++)
            {
                var clip = track.AudioClips[i];
                var laneTop = top + Math.Max(0, clip.Lane) * ClipLanes.AudioLaneHeight;
                if (FoldOf(view, clip) is not { } slices)
                {
                    if (BoundsOf(view, track, clip, laneTop, ClipLanes.AudioLaneHeight).Intersects(box)) into.Add((row, i));
                    continue;
                }
                for (var s = 0; s < slices.Count; s++)
                    if (SliceRect(view, slices[s], laneTop, IsManyPass(slices[s])).Intersects(box)) { into.Add((row, i)); break; }
            }
        }
    }

    /// <summary>The top of a row's lane bands as drawn: the layout top plus the row's drag offset (DrawRow uses the same offset), so a press hits what is shown.</summary>
    private static double LaneTopOf(TimelineView view, int row) =>
        view.LaneBandTopOf(row) + (view.RowOffsets is { } offsets && row < offsets.Length ? offsets[row] : 0);

    /// <summary>The side of the square fade handles at a clip's top corners, in DIPs.</summary>
    internal const double FadeHandleSize = 7;
    private const double FadeHandleStrokeWidth = 1;

    /// <summary>A clip shows its fade handles when it is selected, already has a fade, or is hovered (a first press can start a fade).</summary>
    internal static bool ShowsFadeHandles(TimelineView view, AudioClip clip, int row, int index) =>
        view.IsClipSelected(clip) || clip.FadeInSec > 0 || clip.FadeOutSec > 0 || view.HoverClip == (row, index);

    /// <summary>The x of the fade-in handle (the end of the fade-in) and of the fade-out handle (the start of the fade-out).</summary>
    internal static (double In, double Out) FadeHandleXs(TimelineView view, AudioClip clip) =>
        (view.XOfSeconds(clip.StartSec + clip.FadeInSec), view.XOfSeconds(clip.EndSec - clip.FadeOutSec));

    // The silenced part of each fade: a triangle above the gain ramp, with the ramp as a line.
    private static void DrawFadeShades(IDrawContext d, TimelineView view, AudioClip clip, Rect box, Func<string, UiColor> ink)
    {
        if (clip.FadeInSec <= 0 && clip.FadeOutSec <= 0) return;
        var fill = ink("TimelineRow").WithOpacity(FadeShadeOpacity);
        var stroke = new UiStroke(ink("TextPrimary").WithOpacity(FadeRampStrokeOpacity), FadeRampStrokeWidth);
        if (clip.FadeInSec > 0)
            Wedge(d, fill, stroke, box.X, Math.Clamp(view.XOfSeconds(clip.StartSec + clip.FadeInSec), box.X, box.Right), box);
        if (clip.FadeOutSec > 0)
            Wedge(d, fill, stroke, box.Right, Math.Clamp(view.XOfSeconds(clip.EndSec - clip.FadeOutSec), box.X, box.Right), box);
    }

    private static void Wedge(IDrawContext d, UiColor fill, UiStroke stroke, double edgeX, double rampX, Rect box)
    {
        var path = d.Paths.BuildPath(sink =>
        {
            sink.BeginFigure(new UiPoint(edgeX, box.Y), true, true);
            sink.LineTo(new UiPoint(rampX, box.Y));
            sink.LineTo(new UiPoint(edgeX, box.Bottom));
        });
        d.Path(fill, stroke, path);
    }

    /// <summary>The drawn box of a clip on its lane band; a clip folded over a repeat gives the union of its slices.</summary>
    internal static Rect ClipBounds(TimelineView view, TrackModel track, AudioClip clip)
    {
        var laneTop = LaneTopOf(view, view.Song.Tracks.IndexOf(track)) + Math.Max(0, clip.Lane) * ClipLanes.AudioLaneHeight;
        if (FoldOf(view, clip) is { } slices) return FoldBox(view, slices, laneTop);
        return BoundsOf(view, track, clip, laneTop, ClipLanes.AudioLaneHeight);
    }

    private static void DrawFadeHandles(IDrawContext d, TimelineView view, AudioClip clip, Rect box, UiColor fill, UiColor edge, bool clampToBox = false)
    {
        var half = FadeHandleSize / 2;
        var (inX, outX) = FadeHandleXs(view, clip);
        // A folded clip fade ends can alias onto an earlier bar (where the next play starts): the handles stay on the drawn box.
        if (clampToBox)
        {
            inX = Math.Clamp(inX, box.X, box.Right);
            outX = Math.Clamp(outX, box.X, box.Right);
        }
        foreach (var x in new[] { inX, outX })
            d.Rect(fill, new UiStroke(edge, FadeHandleStrokeWidth), x - half, box.Y - 1, FadeHandleSize, FadeHandleSize);
    }

    private static Rect BoundsOf(TimelineView view, TrackModel track, AudioClip clip, double bandTop, double bandHeight)
    {
        var left = view.XOfSeconds(clip.StartSec);
        var right = view.XOfSeconds(clip.EndSec);
        return new Rect(left, bandTop + LanePadding, Math.Max(MinClipWidth, right - left), Math.Max(1, bandHeight - 2 * LanePadding));
    }
}
