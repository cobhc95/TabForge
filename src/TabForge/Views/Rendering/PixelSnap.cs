using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace TabForge.Views.Rendering;

// Owns: rounding coordinates to whole device pixels, so a layer drawn once and scrolled by a transform keeps crisp text and edges, and the FormattedText setup that renders hinted text.
// Does not own: which layers scroll, when they are drawn or any layout.
// Tests: TestPixelSnap.
public static class PixelSnap
{
    /// <summary>The pixels per DIP of the visual's monitor (1 when the visual is not yet connected).</summary>
    public static double Dpi(Visual visual) => VisualTreeHelper.GetDpi(visual).PixelsPerDip;

    /// <summary>The DIP value nearest to <paramref name="dips"/> that lies on a whole device pixel.</summary>
    public static double Snap(double dips, double pixelsPerDip) => pixelsPerDip <= 0 ? dips : Math.Round(dips * pixelsPerDip) / pixelsPerDip;

    public static Point Snap(Point p, double pixelsPerDip) => new(Snap(p.X, pixelsPerDip), Snap(p.Y, pixelsPerDip));

    /// <summary>Sets a scrolling layer's offset on whole device pixels (a fractional offset resamples the layer every frame and blurs it).</summary>
    public static void SetOffset(TranslateTransform shift, double x, double y, double pixelsPerDip)
    {
        shift.X = Snap(x, pixelsPerDip);
        shift.Y = Snap(y, pixelsPerDip);
    }

    /// <summary>Text for a retained layer: hinted (display mode) metrics at the control's own DPI.</summary>
    public static FormattedText Text(string text, Typeface face, double size, Brush brush, double pixelsPerDip) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, size, brush, null, TextFormattingMode.Display, pixelsPerDip);
}
