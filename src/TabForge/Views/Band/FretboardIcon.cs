using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace TabForge.Views.Band;

// Owns: the drawing of a Band row's fretboard toggle: a neck seen side-on (outline, a thick nut at the left, three frets, hairline
//       strings behind them, two inlay dots), in the theme's text colour.
// Does not own: the button, its lit state or what it toggles (BandRow).
// Tests: TestBandInstrumentRows (the toggle has content).
internal static class FretboardIcon
{
    public static FrameworkElement Build()
    {
        var box = new Grid { Width = 24, Height = 20, IsHitTestVisible = false, SnapsToDevicePixels = true };
        box.Children.Add(Line("M 5,7.5 H 21.5 M 5,10 H 21.5 M 5,12.5 H 21.5", 0.45, 0.55));   // strings: hairlines that stay behind the rest
        box.Children.Add(Line("M 2.5,5 H 21.5 V 15 H 2.5 Z", 1.3, 1));
        box.Children.Add(Line("M 9,5 V 15 M 13,5 V 15 M 17,5 V 15", 1.1, 1));
        box.Children.Add(Line("M 5,5 V 15", 2.6, 1));   // the nut
        var dots = new Path { Data = Geometry.Parse("M 9.8,10 a 1.2,1.2 0 1 0 2.4,0 a 1.2,1.2 0 1 0 -2.4,0 M 17.8,10 a 1.2,1.2 0 1 0 2.4,0 a 1.2,1.2 0 1 0 -2.4,0") };
        dots.SetResourceReference(Shape.FillProperty, "TextBrush");
        box.Children.Add(dots);
        return box;
    }

    private static Path Line(string data, double thickness, double opacity)
    {
        var path = new Path { Data = Geometry.Parse(data), StrokeThickness = thickness, Opacity = opacity, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Flat, StrokeEndLineCap = PenLineCap.Flat };
        path.SetResourceReference(Shape.StrokeProperty, "TextBrush");
        return path;
    }
}
