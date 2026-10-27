using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Views.Score;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the score selection drawing: the blue box spans the selected notes' duration slots and every selected note gets the yellow highlight.
// Does not own: the selection state and its snapping (EditorSelectionState), cursor geometry (ScoreRenderer.Cursor) or the caret rules.
// Tests: this file.
public class ScoreSelectionDrawingTests
{
    /// <summary>(a) Two eighths in 2/4: selecting the second and third eighth spans four slots, not one.</summary>
    [Fact]
    public void EighthNoteSelectionSpansBothEighthSlots()
    {
        var cells = Beats(8, (0, 8), (2, 8), (4, 8), (6, 8));

        var (start, end) = ScoreSelectionGeometry.SpanSlots(cells, 2, 4);

        Assert.Equal(2.0, start);
        Assert.Equal(6.0, end);
        Assert.Equal(4.0, end - start);
    }

    /// <summary>(b) Quarters in 4/4: selecting the second and third quarter spans eight slots.</summary>
    [Fact]
    public void QuarterNoteSelectionSpansBothQuarterSlots()
    {
        var cells = Beats(16, (0, 4), (4, 4), (8, 4), (12, 4));

        var (start, end) = ScoreSelectionGeometry.SpanSlots(cells, 4, 8);

        Assert.Equal(4.0, start);
        Assert.Equal(12.0, end);
    }

    /// <summary>(c) An end on an empty grid cell snaps back to the beat that holds it: never the old one-slot sliver.</summary>
    [Fact]
    public void SelectionEndingOnAnEmptyCellSnapsToItsBeat()
    {
        var cells = Beats(16, (0, 4), (4, 4), (8, 4), (12, 4));

        var (start, end) = ScoreSelectionGeometry.SpanSlots(cells, 4, 6);

        Assert.Equal(4.0, start);
        Assert.Equal(8.0, end);
        Assert.True(end - start > 1, "an empty grid cell must not collapse the box to one slot");
    }

    /// <summary>(d) The drawn blue box uses the helper's slot span, and both selected note centres lie inside it.</summary>
    [AvaloniaFact]
    public void SelectionBoxUsesTheDurationSpanAndHoldsBothNotes()
    {
        var song = EighthNoteSong();
        var track = song.Tracks[0];
        var probe = new ScoreGeometryProbe(song, new AvaloniaDrawContext()) { Notation = NotationMode.TabOnly, Cursor = (0, 2, 0) };
        probe.Selection.Set(0, 2, 0, 4);
        var draw = new FillRecorder();

        probe.DrawPage(draw, int.MaxValue, withPlayhead: false);

        var bar = probe.Layout.GetLayout(track).Systems[0].Measures[0];
        var warp = probe.Layout.WarpFor(track, 0);
        var cells = track.Measures[0].Cells;
        var (startSlots, endSlots) = ScoreSelectionGeometry.SpanSlots(cells, 2, 4);
        var left = bar.X + warp.Fraction(startSlots) * bar.Width;
        var right = bar.X + warp.Fraction(endSlots) * bar.Width;
        var fill = probe.Appearance.SelectionColor with
        {
            A = (byte)Math.Clamp(Math.Round(255 * Math.Clamp(probe.Appearance.SelectionHighlightIntensity, 0, 1)), 0, 255)
        };
        var box = Assert.Single(draw.Rects, rect => rect.Fill == fill);

        Assert.Equal(left, box.X, 3);
        Assert.Equal(right - left, box.Width, 3);
        foreach (var cell in new[] { 2, 4 })
        {
            var centre = bar.X + warp.CenterFraction(CursorPositions.DrawnStart(cells, cell)) * bar.Width;
            Assert.True(centre >= left && centre <= right, $"the note at cell {cell} (x {centre:F1}) lies inside the box {left:F1}..{right:F1}");
        }
    }

    /// <summary>(e) Every selected TAB note gets one yellow chip, both strings of a chord included; unselected notes get none.</summary>
    [AvaloniaFact]
    public void EverySelectedTabNoteGetsAYellowChip()
    {
        var song = EighthNoteSong(chord: true);
        var probe = new ScoreGeometryProbe(song, new AvaloniaDrawContext()) { Notation = NotationMode.TabOnly, Cursor = (0, 2, 0) };
        probe.Selection.Set(0, 2, 0, 4);
        var draw = new FillRecorder();

        probe.DrawPage(draw, int.MaxValue, withPlayhead: false);

        var yellow = draw.Rects.Where(rect => rect.Fill == (TabForge.Platform.Theme.Palette.Get("ScoreCursorTabBox") with { A = 89 })).ToList();
        Assert.Equal(3, yellow.Count);
        Assert.All(yellow, rect => Assert.Equal(14.0, rect.Height, 3));   // a fret chip, not the taller caret box
    }

    /// <summary>(f) With no selection the caret highlight is unchanged: the chip and the separate caret TAB box are still drawn.</summary>
    [AvaloniaFact]
    public void NoSelectionKeepsTheCaretHighlight()
    {
        var song = EighthNoteSong();
        var probe = new ScoreGeometryProbe(song, new AvaloniaDrawContext()) { Notation = NotationMode.TabOnly, Cursor = (0, 2, 0) };
        var draw = new FillRecorder();

        probe.DrawPage(draw, int.MaxValue, withPlayhead: false);

        var yellow = draw.Rects.Where(rect => rect.Fill == (TabForge.Platform.Theme.Palette.Get("ScoreCursorTabBox") with { A = 89 })).ToList();
        Assert.Equal(2, yellow.Count);
        Assert.Contains(yellow, rect => Math.Abs(rect.Height - 14) < 0.001);   // the chip behind the fret
        Assert.Contains(yellow, rect => Math.Abs(rect.Height - 16) < 0.001);   // the caret TAB box
    }

    /// <summary>The cells of a bar with a note on each of <paramref name="beats"/> and the given written duration in slots.</summary>
    private static List<TabCell> Beats(int count, params (int Index, int Denominator)[] beats)
    {
        var cells = Enumerable.Range(0, count).Select(_ => new TabCell()).ToList();
        foreach (var (index, denominator) in beats)
        {
            cells[index].DurationDenominator = denominator;
            cells[index].Notes.Add(new TabNote { StringIndex = 0, Fret = index });
        }
        return cells;
    }

    /// <summary>A 2/4 bar of four eighths on cells 0, 2, 4 and 6; a chord on cell 4 when requested.</summary>
    private static SongProject EighthNoteSong(bool chord = false)
    {
        var project = new SongProject { TimeSignatureNumerator = 2, TimeSignatureDenominator = 4 };
        var measure = new MeasureModel { Number = 1, TimeSigNum = 2, TimeSigDenom = 4 };
        foreach (var cell in new[] { 0, 2, 4, 6 })
        {
            measure.Cells[cell].DurationDenominator = 8;
            measure.Cells[cell].Notes.Add(new TabNote { StringIndex = 0, Fret = cell / 2 });
            if (chord && cell == 4) measure.Cells[cell].Notes.Add(new TabNote { StringIndex = 1, Fret = 3 });
        }
        var track = new TrackModel();
        track.Measures.Add(measure);
        project.Tracks.Add(track);
        return project;
    }

    /// <summary>Records every rectangle with its fill; text and paths are measured but not drawn.</summary>
    private sealed class FillRecorder : IDrawContext
    {
        public List<(UiColor? Fill, double X, double Y, double Width, double Height)> Rects { get; } = new();
        public double PixelsPerDip => 1;
        public IPathFactory Paths { get; } = new AvaloniaPathsAndText();

        public UiText Text(string text, string fontFamily, double size, UiColor colour, double pixelsPerDip, double maxWidth = 0, UiTextAlign align = UiTextAlign.Left,
            int weight = 400, bool italic = false, UiStroke? outline = null, bool idealLayout = false) => new(text.Length * 6, 12, 10, null!);
        public void DrawText(in UiText text, double x, double y) { }
        public void Rect(UiColor? fill, UiStroke? stroke, double x, double y, double width, double height, double radius = 0) => Rects.Add((fill, x, y, width, height));
        public void Rect(IUiPaint? fill, IUiPen? pen, double x, double y, double width, double height, double radiusX = 0, double radiusY = 0) => Rects.Add((null, x, y, width, height));
        public void Line(UiStroke stroke, double x1, double y1, double x2, double y2) { }
        public void Line(IUiPen pen, double x1, double y1, double x2, double y2) { }
        public void Ellipse(UiColor? fill, UiStroke? stroke, double cx, double cy, double rx, double ry) { }
        public void Ellipse(IUiPaint? fill, IUiPen? pen, double cx, double cy, double rx, double ry) { }
        public void Path(UiColor? fill, UiStroke? stroke, IUiPath path) { }
        public void Path(IUiPaint? fill, IUiPen? pen, IUiPath path) { }
        public void Image(IUiImage image, double x, double y, double width, double height) { }
        public void PushClip(double x, double y, double width, double height) { }
        public void PushClip(IUiPath clip) { }
        public void PushTranslate(double dx, double dy) { }
        public void PushScale(double sx, double sy, double centreX = 0, double centreY = 0) { }
        public void PushRotate(double degrees, double centreX, double centreY) { }
        public void PushTransform(IUiTransform transform) { }
        public void PushOpacity(double opacity) { }
        public void Drawing(IUiDrawing drawing) { }
        public void Pop() { }
    }
}
