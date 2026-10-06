using System.Windows;
using System.Windows.Media;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Views;

/// <summary>The bounded timeline preview and the track or lane that a media drop targets.</summary>
internal sealed record DropPreview(MediaDropPlan Plan, Rect Block, Rect? Slot, Color Colour, string Label, string Detail, string? SlotLabel, double[] Splits)
{
    public bool Valid => Plan.Valid;
}

internal interface IDropPreviewGeometryHost
{
    SongProject? Project { get; }
    double TotalWidth { get; }
    double ActualWidth { get; }
    double VerticalScrollOffset { get; }
    double XOfSec(double sec);
    double ClipEndX(double startSec, double endSec);
    double BarWidthOf(int bar);
    int BarAt(double x);
    double LaneTop(int track, int lane);
    Color ColourFor(TrackModel? track);
}

// Owns: clip, file-drop and score-write ghost rectangles.
// Does not own: media-drop planning, row layout, or ghost rendering.
// Tests: TestMediaDropPreviewGeometry, TestClipMoveGhost, TestMidiClipMoves.
internal sealed class DropPreviewGeometryController
{
    private readonly IDropPreviewGeometryHost _host;

    public DropPreviewGeometryController(IDropPreviewGeometryHost host) => _host = host;

    public DropPreview Build(MediaDropPlan plan, Point p, IReadOnlyList<DropItem> items)
    {
        var project = _host.Project!;
        var laneHeight = ArrangementPanel.AudioLaneHeight;
        var width = Math.Max(_host.TotalWidth, _host.ActualWidth);
        var target = plan.TrackIndex >= 0 && plan.TrackIndex < project.Tracks.Count ? project.Tracks[plan.TrackIndex] : null;
        var colour = _host.ColourFor(target);
        if (!plan.Valid)
        {
            var barWidth = Math.Max(40, _host.BarWidthOf(_host.BarAt(Math.Max(0, p.X))));
            var block = new Rect(Math.Max(0, p.X), p.Y - (laneHeight - 6) / 2, barWidth, laneHeight - 6);
            var reason = items.Count == 0 ? "Not an audio or MIDI file" : plan.Problem ?? "Cannot drop here";
            return new DropPreview(plan, block, null, colour, reason, "", null, Array.Empty<double>());
        }

        double laneTop;
        var blockHeight = laneHeight - 6;
        var targetHeight = laneHeight;
        Rect? slot = null;
        string? slotLabel = null;
        if (plan.NewTrack)
        {
            var rowTop = ArrangementPanel.RulerHeight + ArrangementPanel.SectionHeight + ArrangementPanel.RowsHeight(project) - _host.VerticalScrollOffset;
            var notation = plan.NewTrackKind is TrackKind.Drums or TrackKind.Keys ? ArrangementPanel.RowHeightFor(project) : 0;
            slot = new Rect(0, rowTop, width, notation + laneHeight);
            slotLabel = $"New {plan.NewTrackKind switch { TrackKind.Drums => "drum", TrackKind.Keys => "keys", _ => "audio" }} track";
            laneTop = rowTop + notation;
        }
        else
        {
            var track = project.Tracks[plan.TrackIndex];
            if (plan.NewLane)
            {
                // A new lane changes row height only after the drop. Keep its ghost within the hovered row or lane.
                var rowTop = ArrangementPanel.RulerHeight + ArrangementPanel.SectionHeight
                    + ArrangementPanel.RowTopOf(project, plan.TrackIndex) - _host.VerticalScrollOffset;
                var rowHeight = ArrangementPanel.RowHeightOf(project, track);
                var notation = ArrangementPanel.NotationHeightOf(project, track);
                var laneCount = ArrangementPanel.LaneCountOf(track);
                var pointerOffset = p.Y - rowTop;
                var targetTop = rowTop;
                targetHeight = Math.Max(0, rowHeight);
                if (laneCount > 0 && pointerOffset >= notation)
                {
                    var hoveredLane = Math.Clamp((int)((pointerOffset - notation) / laneHeight), 0, laneCount - 1);
                    targetTop += notation + hoveredLane * laneHeight;
                    targetHeight = laneHeight;
                }
                else if (laneCount > 0 && notation > 0) targetHeight = notation;
                laneTop = targetTop;
                blockHeight = Math.Max(3, Math.Min(blockHeight, targetHeight - 6));
                slot = new Rect(0, targetTop, width, targetHeight);
                slotLabel = "New lane";
            }
            else laneTop = _host.LaneTop(plan.TrackIndex, plan.Lane);
        }
        var x1 = _host.XOfSec(plan.StartSec);
        var x2 = _host.ClipEndX(plan.StartSec, plan.EndSec);
        var rect = new Rect(x1, laneTop + (targetHeight - blockHeight) / 2, Math.Max(6, x2 - x1), blockHeight);
        var splits = plan.Clips.Count <= 1 ? Array.Empty<double>() : plan.Clips.Skip(1).Select(c => _host.ClipEndX(plan.StartSec, c.StartSec) - x1).ToArray();
        var label = plan.Clips.Count == 1 ? plan.Clips[0].Item.Name : $"{plan.Clips.Count} files";
        var seconds = plan.EndSec - plan.StartSec;
        var detail = (plan.Estimated ? "length on drop" : TrackTimeline.LengthText(seconds))
            + (plan.Clips.Count == 1 && plan.Clips[0].Item.Midi is { Musical: true } midi ? $" · {midi.LengthQuarters:0.##} beats" : "")
            + (slotLabel is null ? "" : $" · {slotLabel.ToLowerInvariant()}");
        return new DropPreview(plan, rect, slot, colour, label, detail, slotLabel, splits);
    }

    public DropPreview Notation(MediaDropPlan plan, int trackIndex, double startSec, double endSec)
    {
        var project = _host.Project!;
        var track = project.Tracks[trackIndex];
        var rowTop = ArrangementPanel.RulerHeight + ArrangementPanel.SectionHeight
            + ArrangementPanel.RowTopOf(project, trackIndex) - _host.VerticalScrollOffset;
        var height = ArrangementPanel.NotationHeightOf(project, track);
        var x1 = _host.XOfSec(startSec);
        var x2 = _host.ClipEndX(startSec, endSec);
        var block = new Rect(x1, rowTop + 3, Math.Max(6, x2 - x1), Math.Max(3, height - 6));
        var slot = new Rect(0, rowTop, Math.Max(_host.TotalWidth, _host.ActualWidth), height);
        return new DropPreview(plan, block, slot, _host.ColourFor(track), "Write notes", "Score notation", "Notation row", Array.Empty<double>());
    }
}
