using System;
using System.Collections.Generic;
using TabForge.Models;
using TabForge.Platform;

namespace TabForge.Avalonia.Platform;

/// <summary>
/// One clip of a vertical clip drag as the preview shows it: where it came from, the track (null: a new track under the last one) and lane it would land on, and the clip
/// drawn there: the dragged clip itself for a move (it leaves its own spot), a stand-in holding the copy's start for a Ctrl copy (the original stays).
/// </summary>
internal sealed record ClipDragGhost(TrackModel Source, AudioClip Clip, TrackModel? Track, int Lane, AudioClip Shown);

/// <summary>
/// What a clip drag would do on release, for the timeline to draw: the clips on their landing lanes in their full look (a move lifts the clip off its own spot; a copy keeps the
/// original and shows the copy), the first clip's lane lightly highlighted, red when a track cannot take the clips. <paramref name="NewTrack"/>: the clips land on a new track
/// under the last one (the drag is over the Add track strip). View state only, never saved.
/// </summary>
internal sealed record ClipDragPreview(IReadOnlyList<ClipDragGhost> Ghosts, bool Allowed, bool Copy, bool NewTrack = false);

// Owns: drawing the landing of a clip drag onto other lanes and tracks (the light lane highlight, the new-lane and new-track slots, and the clips on their landing lanes) and
//   telling the row drawing which clips the drag has lifted off their own spot.
// Does not own: choosing the landing (TimelineClipGestureController) or the file drag's preview (DrawFileDropPreview).
// Tests: TabForge.Avalonia.Tests.ClipVerticalDragTests.
internal static partial class TimelineAudioClips
{
    /// <summary>The faint wash that marks the landing lane: the clip itself is the preview, the lane only says where it lands.</summary>
    private const double LandingWashOpacity = 0.10;

    /// <summary>True while a move drag has lifted <paramref name="clip"/> off its own spot to show it on another lane: its row does not draw it there (a copy leaves the original). Allocation-free.</summary>
    internal static bool IsLiftedByDrag(TimelineView view, AudioClip clip)
    {
        if (view.ClipDragPreview is not { Copy: false } preview) return false;
        for (var i = 0; i < preview.Ghosts.Count; i++)
            if (ReferenceEquals(preview.Ghosts[i].Clip, clip)) return true;
        return false;
    }

    /// <summary>Draws the clip drag's landing; nothing when no clip drag is over another lane. Called from the interaction pass, over the lanes and under the playhead.</summary>
    public static void DrawClipDragPreview(IDrawContext d, Func<string, UiColor> ink, TimelineView view, double width)
    {
        if (view.ClipDragPreview is not { } preview || preview.Ghosts.Count == 0) return;
        var laneHeight = ClipLanes.AudioLaneHeight;
        var tone = ink(preview.Allowed ? "InstrumentAccent" : "Danger");
        d.PushClip(0, view.TrackRowsTop, width, Math.Max(0, Math.Max(view.TrackRowsBottom, view.Bounds.Height) - view.TrackRowsTop));
        for (var i = 0; i < preview.Ghosts.Count; i++)
        {
            var ghost = preview.Ghosts[i];
            double laneTop;
            var band = laneHeight;
            bool slot;
            if (ghost.Track is null)
            {
                if (!view.NewTrackSlotFits) continue;   // the Add track strip under the timeline shows this landing
                laneTop = view.NewTrackSlotTop;
                slot = true;
            }
            else
            {
                var row = view.Song.Tracks.IndexOf(ghost.Track);
                if (row < 0) continue;
                var empty = view.LaidOutLaneCountOf(ghost.Track) == 0;
                laneTop = empty ? view.RowTop(row) : view.LaneBandTopOf(row, ghost.Lane);
                if (empty) band = Math.Min(laneHeight, view.RowHeight);
                slot = !empty && ghost.Lane >= view.LaidOutLaneCountOf(ghost.Track);
            }
            if (laneTop + band <= view.TrackRowsTop || laneTop >= view.TrackRowsBottom + laneHeight) continue;
            if (i == 0)
            {
                if (slot) DrawFileDropSlot(d, tone, width, laneTop, laneHeight);
                else d.Rect(tone.WithOpacity(LandingWashOpacity), null, 0, laneTop, width, band);
            }
            var colour = !preview.Allowed ? tone : ghost.Track is null ? ink("InstrumentAccent") : view.TrackColourOf(ghost.Track, tone);
            DrawLanded(d, ink, view, ghost, colour, laneTop, band, width);
        }
        d.Pop();
    }

    /// <summary>Draws the dragged clip (or its copy) on its landing lane in its full look: waveform or notes, name, fades, and the repeat fold at the start it would land on.</summary>
    private static void DrawLanded(IDrawContext d, Func<string, UiColor> ink, TimelineView view, ClipDragGhost ghost, UiColor colour, double laneTop, double band, double width)
    {
        var clip = ghost.Shown;
        var light = view.FrameLight;
        var background = ink("TimelineRow");
        var fillOpacity = FillOpacityOf(light, true, false, 0);
        var nameInk = ClipInk(light ? ink("TimelineLane") : background, colour, fillOpacity);
        var (spanFrom, spanWidth) = view.VisibleSpan;
        var row = Math.Max(0, view.Song.Tracks.IndexOf(ghost.Source));
        if (FoldOf(view, clip) is { } slices)
        {
            DrawFoldedClip(d, view, row, -1, clip, slices, laneTop, width, colour, nameInk, background, ink, fillOpacity, spanFrom, spanWidth);
            return;
        }
        var rect = BoundsOf(view, ghost.Source, clip, laneTop, band);
        if (!double.IsNaN(spanFrom) && (rect.Right < spanFrom || rect.X > spanFrom + spanWidth)) return;
        DrawClipBox(d, view, ghost.Source, row, -1, clip, rect, width, colour, nameInk, background, ink, fillOpacity);
    }
}
