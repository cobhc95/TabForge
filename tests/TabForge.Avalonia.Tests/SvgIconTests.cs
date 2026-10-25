using System.Linq;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using TabForge.Avalonia.Platform;
using TabForge.Platform;
using TabForge.Platform.Testing;
using TabForge.Platform.Theme;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the check of the shared SVG icon model and its Avalonia drawing: a bold label reads as semibold, is anchored on its
/// SVG x and baseline the way the WPF icon places it, and is drawn in the icon colour.
/// Tests: SvgIcon (Core reader) and AvaloniaSvgIcon (Avalonia drawing).
/// </summary>
public class SvgIconTests
{
    private static readonly UiColor Red = new(255, 255, 0, 0);

    // A bold, centred label: the case the Avalonia icon drew at regular weight and at the wrong height before the shared reader.
    private const string BoldLabelSvg =
        """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 32 32"><text x="16" y="20" font-size="10" font-weight="600" text-anchor="middle" fill="currentColor">WWW MMM</text></svg>""";

    [Fact]
    public void ReaderKeepsWeightAnchorSizeAndPositionOfAText()
    {
        var icon = SvgIcon.Parse(XDocument.Parse(BoldLabelSvg));
        var text = Assert.Single(icon!.Texts);
        Assert.Equal("WWW MMM", text.Text);
        Assert.Equal(600, text.Weight);
        Assert.Equal(SvgTextAnchor.Middle, text.Anchor);
        Assert.Equal(10, text.FontSize);
        Assert.Equal(16, text.X);
        Assert.Equal(20, text.Y);
    }

    [AvaloniaFact]
    public void BoldLabelIsLaidOutSemiboldAndHangsFromItsBaseline()
    {
        var icon = AvaloniaSvgIcon.Prepare(SvgIcon.Parse(XDocument.Parse(BoldLabelSvg))!);
        using var bitmap = new RenderTargetBitmap(new PixelSize(32, 32), new Vector(96, 96));
        using var dc = bitmap.CreateDrawingContext();
        var draw = new AvaloniaDrawContext();
        draw.Use(dc);
        var recorder = new GeometryRecorder(draw);

        AvaloniaSvgIcon.Draw(recorder, icon, Red, light: false);

        var drawn = Assert.Single(recorder.Records, record => record.Kind == "Text");
        var semibold = draw.Text("WWW MMM", "Segoe UI", 10, Red, 1, weight: 600);
        var regular = draw.Text("WWW MMM", "Segoe UI", 10, Red, 1, weight: 400);
        Assert.NotEqual(regular.Width, semibold.Width);
        Assert.Equal(16 - semibold.Width / 2, drawn.Values[0], 6);
        Assert.Equal(20 - semibold.Baseline, drawn.Values[1], 6);
    }
}
