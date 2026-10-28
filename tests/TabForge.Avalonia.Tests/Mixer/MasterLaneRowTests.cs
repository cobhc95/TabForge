using System;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the "Master" row of the track list: its FX split button, master volume and master pan cells are drawn in the track rows' columns (not M or S,
/// as WPF's), and the volume thumb follows the transport's master knob and the pan thumb the mixer's master pan.
/// Does not own: the row's pointer handling (the track row cells' hit testing), the lane above track 1 (TimelineViewTests), the knob itself.
/// Tests: this class.
/// </summary>
public class MasterLaneRowTests
{
    private const double ThumbWidth = 18, ThumbHeight = 14;

    /// <summary>The master lane's top in the track list: under the ruler and the section strip.</summary>
    private static readonly double MasterTop = TimelineView.RulerHeight + TimelineView.SectionHeight;

    private static (SongProject Project, TimelineView Timeline, TrackHeaderColumn Column) Open()
    {
        var song = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        var timeline = new TimelineView(song.Project) { TrackListNameWidth = TrackRowCells.NameWidth };   // the geometry the Box numbers below are written for
        timeline.MasterLaneShown = true;
        return (song.Project, timeline, new TrackHeaderColumn(timeline, song.Project));
    }

    [AvaloniaFact]
    public void MasterRowDrawsFxPowerVolumeAndPanButNoMuteOrSolo()
    {
        var (_, timeline, column) = Open();
        var height = timeline.MasterLaneHeight;
        var recorder = new TrackRowSelectionTests.RectRecorder();

        column.PaintCells(recorder, key => AvaloniaPalette.Get(key, false), false);

        var fx = TrackRowCells.Box(TrackRowCells.Cell.Fx, MasterTop, height);
        var power = TrackRowCells.Box(TrackRowCells.Cell.FxPower, MasterTop, height);
        Assert.Contains(recorder.Rects, r => Near(r.X, fx.X + 0.5) && Near(r.Y, fx.Y + 0.5) && Near(r.Width, fx.Width + power.Width - 1));
        Assert.Contains(recorder.Rects, r => ThumbAt(r, TrackRowCells.Cell.Volume, 0, height));
        Assert.Contains(recorder.Rects, r => ThumbAt(r, TrackRowCells.Cell.Pan, 0, height));
        var mute = TrackRowCells.Box(TrackRowCells.Cell.Mute, MasterTop, height);
        Assert.DoesNotContain(recorder.Rects, r => Near(r.X, mute.X) && Near(r.Y, mute.Y) && Near(r.Width, mute.Width));
    }

    [AvaloniaFact]
    public void MasterVolumeThumbFollowsTheTransportKnob()
    {
        var song = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        var view = new ArrangementView(song.Project, song);
        view.SetTrackListWidth(TrackRowCells.TotalWidth);   // the geometry the Thumb numbers are written for
        view.ShowMasterLane(true);
        var height = view.Timeline.MasterLaneHeight;

        view.Transport.MasterVolumeKnob.Value = 25;
        Assert.Contains(PaintedRects(view.Header), r => ThumbAt(r, TrackRowCells.Cell.Volume, 25, height));

        view.Transport.MasterVolumeKnob.Value = 75;
        Assert.Contains(PaintedRects(view.Header), r => ThumbAt(r, TrackRowCells.Cell.Volume, 75, height));
        Assert.DoesNotContain(PaintedRects(view.Header), r => ThumbAt(r, TrackRowCells.Cell.Volume, 25, height));
    }

    [AvaloniaFact]
    public void MasterPanThumbFollowsTheSongsMasterPan()
    {
        var (project, timeline, column) = Open();
        var height = timeline.MasterLaneHeight;

        project.Mixer.MasterPan = 32;
        column.Reload(project);

        Assert.Contains(PaintedRects(column), r => ThumbAt(r, TrackRowCells.Cell.Pan, 32, height));
    }

    /// <summary>The rectangles a header column paints, in order.</summary>
    private static System.Collections.Generic.List<(double X, double Y, double Width, double Height)> PaintedRects(TrackHeaderColumn column)
    {
        var recorder = new TrackRowSelectionTests.RectRecorder();
        column.PaintCells(recorder, key => AvaloniaPalette.Get(key, false), false);
        return recorder.Rects;
    }

    /// <summary>True when the rectangle is the thumb of the master slider that shows <paramref name="value"/>.</summary>
    private static bool ThumbAt((double X, double Y, double Width, double Height) rect, TrackRowCells.Cell cell, int value, double height)
    {
        var (x, y, width, h) = Thumb(cell, value, MasterTop, height);
        return Near(rect.X, x) && Near(rect.Y, y) && Near(rect.Width, width) && Near(rect.Height, h);
    }

    /// <summary>The thumb's rectangle for a master slider value, as the slider draws it: the thumb travels the rail less its own width.</summary>
    private static (double X, double Y, double Width, double Height) Thumb(TrackRowCells.Cell cell, int value, double top, double height)
    {
        var box = TrackRowCells.Box(cell, top, height);
        var (minimum, maximum) = cell == TrackRowCells.Cell.Volume ? (MasterLaneRowPainter.VolumeMinimum, MasterLaneRowPainter.VolumeMaximum) : MasterLaneRowPainter.PanRange;
        var t = (value - minimum) / (double)(maximum - minimum);
        return (box.X + t * (box.Width - ThumbWidth), box.Center.Y - ThumbHeight / 2, ThumbWidth, ThumbHeight);
    }

    private static bool Near(double a, double b) => Math.Abs(a - b) < 0.01;
}
