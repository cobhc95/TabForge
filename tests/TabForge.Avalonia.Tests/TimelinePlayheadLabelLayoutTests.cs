using System;
using System.Collections.Generic;
using System.Linq;
using TabForge.Avalonia.Platform;
using TabForge.Features.Recording.Rules;
using TabForge.Platform;
using TabForge.Platform.Theme;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the layout of the amber playhead label in the ruler. It sits on its own line below the bar numbers and above the tick marks, so it never
/// overlaps a bar number it used to collide with. Tests: TimelinePlayheadMarks.DrawBarNumber (pass label), TimelineView.RulerHeight.
/// </summary>
public sealed class TimelinePlayheadLabelLayoutTests
{
    private const double BarNumberTop = 3;     // the ruler's bar numbers (TimelineView.DrawRuler)
    private const double BarNumberHeight = 16; // the 12 pt bold text's line height in the recorder

    private sealed record Drawn(string Text, double X, double Y, double Width, double Height)
    {
        public bool Intersects(Drawn other) =>
            X < other.X + other.Width && other.X < X + Width && Y < other.Y + other.Height && other.Y < Y + Height;
    }

    private sealed class Recorder : IDrawContext
    {
        public List<Drawn> Texts { get; } = new();
        public double PixelsPerDip => 1;
        public IPathFactory Paths => throw new NotSupportedException();
        public void Rect(UiColor? fill, UiStroke? stroke, double x, double y, double width, double height, double radius = 0) { }
        public void Rect(IUiPaint? fill, IUiPen? pen, double x, double y, double width, double height, double radiusX = 0, double radiusY = 0) => throw new NotSupportedException();
        public void Line(UiStroke stroke, double x1, double y1, double x2, double y2) { }
        public void Line(IUiPen pen, double x1, double y1, double x2, double y2) { }
        public void Ellipse(UiColor? fill, UiStroke? stroke, double cx, double cy, double rx, double ry) { }
        public void Ellipse(IUiPaint? fill, IUiPen? pen, double cx, double cy, double rx, double ry) { }
        public void Path(UiColor? fill, UiStroke? stroke, IUiPath path) { }
        public void Path(IUiPaint? fill, IUiPen? pen, IUiPath path) { }
        public UiText Text(string text, string fontFamily, double size, UiColor colour, double pixelsPerDip, double maxWidth = 0, UiTextAlign align = UiTextAlign.Left,
            int weight = 400, bool italic = false, UiStroke? outline = null, bool idealLayout = false) => new(text.Length * 6, BarNumberHeight, 12, text);
        public void DrawText(in UiText text, double x, double y) => Texts.Add(new Drawn((string)text.Layout, x, y, text.Width, text.Height));
        public void Image(IUiImage image, double x, double y, double width, double height) { }
        public void Drawing(IUiDrawing drawing) { }
        public void PushClip(double x, double y, double width, double height) { }
        public void PushClip(IUiPath clip) { }
        public void PushTranslate(double dx, double dy) { }
        public void PushScale(double sx, double sy, double centreX = 0, double centreY = 0) { }
        public void PushRotate(double degrees, double centreX, double centreY) { }
        public void PushTransform(IUiTransform transform) { }
        public void PushOpacity(double opacity) { }
        public void Pop() { }
    }

    [Fact]
    public void PlayheadPassLabelDoesNotOverlapTheBarNumberItUsedTo()
    {
        // Zoom with 20 px bars: bar 18 (label "19 - pass 1", inside a 2-pass repeat) used to sit on top of the bar number "21" two bars on.
        const double barWidth = 20;
        var regions = new List<RepeatRegion> { new(16, 20, 2) };
        var draw = new Recorder();
        TimelinePlayheadMarks.DrawBarNumber(draw, _ => default, 18 * barWidth, 18, 1, regions, TimelineView.Font);
        var label = draw.Texts.Single();
        Assert.Equal("19 - pass 1", label.Text);

        var numbers = new[] { 16, 20, 24 }.Select(bar =>
        {
            var text = (bar + 1).ToString();
            return new Drawn(text, bar * barWidth + 3, BarNumberTop, text.Length * 6, BarNumberHeight);
        }).ToList();
        Assert.Contains(numbers, n => n.Intersects(new Drawn(label.Text, label.X, BarNumberTop, label.Width, label.Height)));   // the old y would collide

        foreach (var number in numbers)
            Assert.False(label.Intersects(number), $"'{label.Text}' at y {label.Y} overlaps bar number '{number.Text}'");
    }

    [Fact]
    public void PlayheadLabelSitsBelowTheBarNumbersAndAboveTheTicks()
    {
        var draw = new Recorder();
        TimelinePlayheadMarks.DrawBarNumber(draw, _ => default, 0, 0, 1, null, TimelineView.Font);
        var label = draw.Texts.Single();
        var tickTop = TimelineView.RulerHeight - 6;   // the bar tick marks start here (TimelineView.DrawRuler)
        Assert.True(label.Y >= BarNumberTop + BarNumberHeight, $"label top {label.Y} is inside the bar numbers");
        Assert.True(label.Y + label.Height <= tickTop, $"label bottom {label.Y + label.Height} runs into the ticks at {tickTop}");
    }
}
