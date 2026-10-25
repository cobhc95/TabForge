using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

namespace TabForge.Avalonia.Platform;

// Owns: the drawing of a Band card's fretboard toggle, the same neck as WPF's band row icon (an outline, a thick nut at the left, three frets,
//   hairline strings behind them and two inlay dots), with its geometry in the WPF icon's own 24 x 20 box.
// Does not own: the toggle button, its lit state or what it shows (BandTrackPanel), the brush (passed to Paint from the theme).
// Tests: TabForge.Avalonia.Tests.BandCardTests.
internal static class BandFretboardIcon
{
    /// <summary>The icon's box in device-independent pixels.</summary>
    public const double Width = 24, Height = 20;

    /// <summary>The tag of the inlay dots' path: they are filled, the lines are stroked.</summary>
    private const string InlayTag = "inlay";

    private const string StringsData = "M 5,7.5 H 21.5 M 5,10 H 21.5 M 5,12.5 H 21.5";
    private const string BoardData = "M 2.5,5 H 21.5 V 15 H 2.5 Z";
    private const string FretsData = "M 9,5 V 15 M 13,5 V 15 M 17,5 V 15";
    private const string NutData = "M 5,5 V 15";
    private const string InlayData = "M 9.8,10 a 1.2,1.2 0 1 0 2.4,0 a 1.2,1.2 0 1 0 -2.4,0 M 17.8,10 a 1.2,1.2 0 1 0 2.4,0 a 1.2,1.2 0 1 0 -2.4,0";

    /// <summary>The icon, drawn in no colour yet: <see cref="Paint"/> gives it one.</summary>
    public static Canvas Build()
    {
        var icon = new Canvas { Width = Width, Height = Height, IsHitTestVisible = false };
        icon.Children.Add(Stroke(StringsData, 0.45, 0.8));
        icon.Children.Add(Stroke(BoardData, 1.3, 1));
        icon.Children.Add(Stroke(FretsData, 1.1, 1));
        icon.Children.Add(Stroke(NutData, 2.6, 1));
        icon.Children.Add(new Path { Data = Geometry.Parse(InlayData), Tag = InlayTag });
        return icon;
    }

    /// <summary>Colours the icon's lines and inlay dots with <paramref name="brush"/>.</summary>
    public static void Paint(Canvas icon, IBrush brush)
    {
        foreach (var path in icon.Children.OfType<Path>())
        {
            if (Equals(path.Tag, InlayTag)) path.Fill = brush;
            else path.Stroke = brush;
        }
    }

    private static Path Stroke(string data, double thickness, double opacity) => new()
    {
        Data = Geometry.Parse(data), StrokeThickness = thickness, Opacity = opacity, StrokeJoin = PenLineJoin.Round, StrokeLineCap = PenLineCap.Flat,
    };
}
