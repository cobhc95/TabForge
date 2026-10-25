using System;
using System.Collections.Generic;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Presenters;

namespace TabForge.Avalonia.Platform;

// Owns: the timeline's bar highlight (the shown document's, set by the input controller) and putting it on the frame.
// Does not own: the highlight rules (BarHighlight), the painter (TimelineHighlightPainter) or the gestures that move it (TimelineInputController).
public sealed partial class TimelineView
{
    private readonly List<HighlightRow> _highlightRows = new();

    /// <summary>The shown document's bar highlight (set by the input controller); null while no document shows.</summary>
    internal BarHighlight? Highlight { get; set; }

    /// <summary>The highlighted tab cells: a grey fill and frame.</summary>
    private void DrawHighlight(IDrawContext d, Func<string, UiColor> ink)
    {
        if (Highlight is not { Has: true } h) return;
        DrawRectangle(d, ink, h);
        DrawExtraCells(d, ink, h);   // independent of the rectangle: Ctrl-added cells show even when it is off-screen
    }

    // Only cells that hold content are drawn (empty bars and lane space stay bare); the selection itself stays a rectangle. Visible bars only, nothing allocated per frame.
    private void DrawRectangle(IDrawContext d, Func<string, UiColor> ink, BarHighlight h)
    {
        if (h.FirstBar >= BarCount) return;
        var (firstBar, endBar) = VisibleBarRange(Math.Min(BarCount, h.LastBar + 1));
        firstBar = Math.Max(firstBar, h.FirstBar);
        if (firstBar >= endBar) return;
        var (fill, frame) = (ink("TimelineHighlightFill"), ink("TimelineHighlightFrame"));
        var pushed = false;
        for (var row = h.FirstTrack; row <= Math.Min(h.LastTrack, _song.Tracks.Count - 1); row++)
        {
            var track = _song.Tracks[row];
            var (top, height) = (RowTop(row), NotationHeightOf(row));
            if (!track.HasNotation || top + height <= TrackRowsTop || top >= TrackRowsBottom) continue;   // rows scrolled out of view are not drawn
            for (var bar = firstBar; bar < endBar && bar < track.Measures.Count; bar++)
            {
                if (ActivityOf(track, row, bar).Notes == 0) continue;
                var (left, right) = (BarX(bar), TimelineGeometryRules.RightOfBar(_boundaries, bar));
                if (!InViewport(left, right - left)) continue;
                if (!pushed) { d.PushClip(0, TrackRowsTop, Math.Max(Bounds.Width, _boundaries[^1]), TrackRowsBottom - TrackRowsTop); pushed = true; }
                _extraRow[0] = new HighlightRow(top, height);
                TimelineHighlightPainter.Draw(d, fill, frame, left, right, _extraRow);
            }
        }
        if (pushed) d.Pop();
    }

    private readonly List<HighlightRow> _extraRow = new() { default };

    // The single cells Ctrl+click added: each one a cell box of its own (nothing allocated per frame).
    private void DrawExtraCells(IDrawContext d, Func<string, UiColor> ink, BarHighlight h)
    {
        var extras = h.Extras;
        if (extras.Count == 0) return;
        var (fill, frame) = (ink("TimelineHighlightFill"), ink("TimelineHighlightFrame"));
        d.PushClip(0, TrackRowsTop, Math.Max(Bounds.Width, _boundaries[^1]), TrackRowsBottom - TrackRowsTop);   // one clip for the lane area, none per cell
        for (var i = 0; i < extras.Count; i++)
        {
            var (row, bar) = extras[i];
            if (row >= _song.Tracks.Count || bar >= BarCount || !_song.Tracks[row].HasNotation) continue;
            var (top, height) = (RowTop(row), NotationHeightOf(row));
            if (top + height <= TrackRowsTop || top >= TrackRowsBottom) continue;
            var (left, right) = (BarX(bar), TimelineGeometryRules.RightOfBar(_boundaries, bar));
            if (!InViewport(left, right - left)) continue;
            _extraRow[0] = new HighlightRow(top, height);
            TimelineHighlightPainter.Draw(d, fill, frame, left, right, _extraRow);
        }
        d.Pop();
    }
}
