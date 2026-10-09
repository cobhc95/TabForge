using System.Windows.Media;

namespace TabForge.Visualization;

// Owns: the label colour for a fret / key marker: white or near-black, whichever reads better on the marker's actual surface
//   (its fill blended over the board), so outlined markers on a dark board never get dark numbers.
// Does not own: the marker's shape or fill (FretboardRenderer.RenderMarker).
// Tests: TestFretMarkerLabelContrast.
internal static class MarkerInk
{
    public static readonly Color Light = Color.FromRgb(0xF4, 0xF6, 0xF8);
    public static readonly Color Dark = Color.FromRgb(0x11, 0x14, 0x18);

    public static Color On(Color surface) => Contrast(Light, surface) >= Contrast(Dark, surface) ? Light : Dark;

    public static double Contrast(Color a, Color b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(Color c)
    {
        static double Ch(byte v) { var x = v / 255.0; return x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4); }
        return 0.2126 * Ch(c.R) + 0.7152 * Ch(c.G) + 0.0722 * Ch(c.B);
    }
}
