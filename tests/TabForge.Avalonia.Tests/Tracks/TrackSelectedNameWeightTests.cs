using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the track list's name weights. Every selected track's name is bold (the active track and each multi-selected row),
//   an unselected row's name is not, and a selection change only changes the paint of the rows it touches.
// Does not own: the tint (TrackRowSelectionTests) or the cell geometry (TrackRowControlsTests).
// Tests: TrackHeaderColumn.PaintCells, TrackHeaderColumn.IsNameBold.
public class TrackSelectedNameWeightTests
{
    private static (SongProject Song, TimelineView Timeline, TrackHeaderColumn Column) Open()
    {
        var song = DemoSongFactory.Create();
        while (song.Tracks.Count < 6) song.Tracks.Add(NewTrackFactory.CreateTrack(song, TrackKind.Audio));
        for (var i = 0; i < song.Tracks.Count; i++) song.Tracks[i].Name = $"Track {(char)('A' + i)}";
        var timeline = new TimelineView(song) { PaneHeight = 4000 };
        return (song, timeline, new TrackHeaderColumn(timeline, song));
    }

    [AvaloniaFact]
    public void EverySelectedRowsNameIsBoldAndUnselectedRowsAreNot()
    {
        var (song, timeline, column) = Open();
        timeline.SelectedRow = 1;
        timeline.FocusModel.SetRows(new[] { song.Tracks[1].Id, song.Tracks[2].Id, song.Tracks[4].Id });
        var weights = NameWeights(column);

        Assert.Equal(700, weights["Track B"]);
        Assert.Equal(700, weights["Track C"]);
        Assert.Equal(700, weights["Track E"]);
        Assert.Equal(400, weights["Track A"]);
        Assert.Equal(400, weights["Track D"]);
        Assert.Equal(400, weights["Track F"]);
    }

    [AvaloniaFact]
    public void SingleSelectionBoldsOnlyThatRow()
    {
        var (song, timeline, column) = Open();
        timeline.SelectedRow = 2;
        timeline.FocusModel.SetRows(new[] { song.Tracks[2].Id });
        var weights = NameWeights(column);

        Assert.Equal(700, weights["Track C"]);
        Assert.Equal(weights.Count - 1, weights.Count(w => w.Value == 400));
    }

    [AvaloniaFact]
    public void ActiveTrackAloneKeepsItsBoldName()
    {
        var (_, timeline, column) = Open();
        timeline.SelectedRow = 0;
        Assert.True(column.IsNameBold(0));
        Assert.False(column.IsNameBold(1));
    }

    [AvaloniaFact]
    public void SelectionChangeOnlyRepaintsTheRowsItTouches()
    {
        var (song, timeline, column) = Open();
        timeline.SelectedRow = 1;
        timeline.FocusModel.SetRows(new[] { song.Tracks[0].Id, song.Tracks[1].Id, song.Tracks[2].Id });
        var before = NameWeights(column);

        timeline.FocusModel.SetRows(new[] { song.Tracks[1].Id, song.Tracks[2].Id, song.Tracks[3].Id });
        var after = NameWeights(column);

        Assert.Equal(before["Track B"], after["Track B"]);
        Assert.Equal(before["Track C"], after["Track C"]);
        Assert.NotEqual(before["Track A"], after["Track A"]);
        Assert.NotEqual(before["Track D"], after["Track D"]);
        Assert.Equal(before["Track E"], after["Track E"]);
        Assert.Equal(before["Track F"], after["Track F"]);
    }

    [AvaloniaFact]
    public void BoldLookupAllocatesNothingPerRow()
    {
        var (song, timeline, column) = Open();
        timeline.SelectedRow = 1;
        timeline.FocusModel.SetRows(new[] { song.Tracks[0].Id, song.Tracks[1].Id, song.Tracks[2].Id });
        for (var i = 0; i < 100; i++) for (var r = 0; r < timeline.RowCount; r++) column.IsNameBold(r);
        GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) for (var r = 0; r < timeline.RowCount; r++) column.IsNameBold(r);
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
    }

    private static Dictionary<string, int> NameWeights(TrackHeaderColumn column)
    {
        var recorder = new WeightRecorder();
        column.PaintCells(recorder, key => AvaloniaPalette.Get(key, false), false);
        return recorder.Weights;
    }

    /// <summary>Records the weight of every drawn track name, keyed by its text; everything else is ignored.</summary>
    private sealed class WeightRecorder : IDrawContext
    {
        public Dictionary<string, int> Weights { get; } = new();
        public double PixelsPerDip => 1;
        public IPathFactory Paths { get; } = new AvaloniaPathsAndText();

        public UiText Text(string text, string fontFamily, double size, UiColor colour, double pixelsPerDip, double maxWidth = 0,
            UiTextAlign align = UiTextAlign.Left, int weight = 400, bool italic = false, UiStroke? outline = null, bool idealLayout = false)
        {
            if (Math.Abs(size - 13) < 0.01 && text.StartsWith("Track ", StringComparison.Ordinal)) Weights[text] = weight;
            return new UiText(text.Length * 6, 12, 10, null!);
        }

        public void DrawText(in UiText text, double x, double y) { }
        public void Rect(UiColor? fill, UiStroke? stroke, double x, double y, double width, double height, double radius = 0) { }
        public void Rect(IUiPaint? fill, IUiPen? pen, double x, double y, double width, double height, double radiusX = 0, double radiusY = 0) { }
        public void Line(UiStroke stroke, double x1, double y1, double x2, double y2) { }
        public void Line(IUiPen pen, double x1, double y1, double x2, double y2) { }
        public void Ellipse(UiColor? fill, UiStroke? stroke, double cx, double cy, double rx, double ry) { }
        public void Ellipse(IUiPaint? fill, IUiPen? pen, double cx, double cy, double rx, double ry) { }
        public void Path(UiColor? fill, UiStroke? stroke, IUiPath path) { }
        public void Path(IUiPaint? fill, IUiPen? pen, IUiPath path) { }
        public void Drawing(IUiDrawing drawing) { }
        public void Image(IUiImage image, double x, double y, double width, double height) { }
        public void PushClip(double x, double y, double width, double height) { }
        public void PushClip(IUiPath clip) { }
        public void PushTranslate(double dx, double dy) { }
        public void PushScale(double sx, double sy, double centreX = 0, double centreY = 0) { }
        public void PushRotate(double degrees, double centreX, double centreY) { }
        public void PushTransform(IUiTransform transform) { }
        public void PushOpacity(double opacity) { }
        public void Pop() { }
    }
}
