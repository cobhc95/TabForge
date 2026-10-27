using System.Collections.Generic;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Presenters;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the fret number inside a fretboard dot, in every playing state (the upcoming state included), inked for contrast on the dot, and its size
/// against the dot, so a two-digit number is never clipped.
/// Does not own: the playing lists (InstrumentPlaybackLists) or the dot's position and size (FretboardGeometryRules, FretboardMarkerSizeRules).
/// Tests: InstrumentPanelView.MarkFret, FretboardMarkerSizeRules.LabelFontSize through a recording and an Avalonia draw context.
/// </summary>
public class InstrumentFretNumberTests
{
    [Xunit.Fact]
    public void UpcomingNoteDrawsItsFretNumberInContrastInk()
    {
        var layout = FretboardGeometryRules.Compute(new FretboardGeometryRules.Input(6, 24, 24, 1, false, 0), new UiRect(0, 0, 800, 300));
        var upcomingFill = new UiColor(255, 230, 190, 40);
        var draw = new RecordingDrawContext();
        InstrumentPanelView.MarkFret(draw, _ => new UiColor(255, 0, 0, 0), layout, 2, 3, upcomingFill, 11);
        var number = Assert.Single(draw.Texts, t => t.Text == "3");
        Assert.Equal(ThemeColours.InkOn(upcomingFill), number.Colour);
    }

    /// <summary>A two-digit fret number stays inside the dot at the sizes the board draws, down to the smallest one the panel makes.</summary>
    [AvaloniaFact]
    public void TwoDigitNumberFitsInsideItsDotAtEveryMarkerSize()
    {
        var draw = new AvaloniaDrawContext();
        foreach (var radius in new[] { 6.0, 8.0, 11.7, 16.0 })
        {
            var number = draw.Text("12", InstrumentPanelView.Font, FretboardMarkerSizeRules.LabelFontSize(radius), new UiColor(255, 0, 0, 0), 1, weight: 700);
            Assert.True(number.Width <= radius * 2 && number.Height <= radius * 2,
                $"at radius {radius:F1} the \"12\" lays out {number.Width:F1} x {number.Height:F1}, past the {radius * 2:F1} dot");
        }
    }

    /// <summary>Records the text each drawn fret marker shows and the stroke of each line; the other shapes are not needed by the checks.</summary>
    internal sealed class RecordingDrawContext : IDrawContext
    {
        public List<(string Text, UiColor Colour)> Texts { get; } = new();
        /// <summary>The stroke of each line drawn with a stroke, in drawing order.</summary>
        public List<UiStroke> LineStrokes { get; } = new();
        /// <summary>Each filled dot drawn with a colour fill: the fill and the radius.</summary>
        public List<(UiColor? Fill, double Radius, double X, double Y)> Ellipses { get; } = new();
        /// <summary>Each rectangle drawn: its fill (null when outlined only) and its box, in drawing order.</summary>
        public List<(UiColor? Fill, double X, double Y, double Width, double Height)> Rects { get; } = new();
        public double PixelsPerDip => 1;
        public IPathFactory Paths => throw new System.NotSupportedException("The fret-number check records text only.");

        public UiText Text(string text, string fontFamily, double size, UiColor colour, double pixelsPerDip, double maxWidth = 0, UiTextAlign align = UiTextAlign.Left,
            int weight = 400, bool italic = false, UiStroke? outline = null, bool idealLayout = false)
        {
            Texts.Add((text, colour));
            return new UiText(text.Length * 6, 12, 10, null!);
        }

        public void DrawText(in UiText text, double x, double y) { }
        public void Rect(UiColor? fill, UiStroke? stroke, double x, double y, double width, double height, double radius = 0) => Rects.Add((fill, x, y, width, height));
        public List<(double X1, double Y1, double X2, double Y2)> LineOps { get; } = new();
        public void Line(UiStroke stroke, double x1, double y1, double x2, double y2) { LineStrokes.Add(stroke); LineOps.Add((x1, y1, x2, y2)); }
        public void Ellipse(UiColor? fill, UiStroke? stroke, double cx, double cy, double rx, double ry) => Ellipses.Add((fill, rx, cx, cy));
        public void Path(UiColor? fill, UiStroke? stroke, IUiPath path) { }
        public void Image(IUiImage image, double x, double y, double width, double height) { }
        public void PushClip(double x, double y, double width, double height) { }
        public void PushTranslate(double dx, double dy) { }
        public void PushScale(double sx, double sy, double centreX = 0, double centreY = 0) { }
        public void PushRotate(double degrees, double centreX, double centreY) { }
        public void Pop() { }
        public void Rect(IUiPaint? fill, IUiPen? pen, double x, double y, double width, double height, double radiusX = 0, double radiusY = 0) { }
        public void Line(IUiPen pen, double x1, double y1, double x2, double y2) { }
        public void Ellipse(IUiPaint? fill, IUiPen? pen, double cx, double cy, double rx, double ry) { }
        public void Path(IUiPaint? fill, IUiPen? pen, IUiPath path) { }
        public void Drawing(IUiDrawing drawing) { }
        public void PushClip(IUiPath clip) { }
        public void PushTransform(IUiTransform transform) { }
        public void PushOpacity(double opacity) { }
    }
}
