using System.Windows;
using System.Windows.Media;

namespace TabForge.Views;

/// <summary>
/// Small DPI-independent outline glyphs for the native settings category rail: one idea per page, no duplicates, one line weight
/// (fills only for dots of about 3 px or less).
/// </summary>
internal sealed class SettingsNavigationIcon : FrameworkElement
{
    public string Kind { get; init; } = "Advanced";

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            InvalidateVisual();
        }
    }

    private bool _isSelected;

    protected override void OnRender(DrawingContext context)
    {
        try { RenderGuard.Inject("SettingsNavigationIcon"); RenderCore(context); }
        catch (Exception ex) when (RenderGuard.Contain(ex, "SettingsNavigationIcon", context, ActualWidth, ActualHeight)) { } // Not logged: render path: runs per frame.
    }

    private void RenderCore(DrawingContext context)
    {
        base.OnRender(context);
        if (RenderSize.Width <= 1 || RenderSize.Height <= 1) return;

        var scaleX = RenderSize.Width / 24;
        var scaleY = RenderSize.Height / 24;
        var light = TabForge.Visualization.VisualTheme.IsLight;
        var color = IsSelected ? (light ? Color.FromRgb(0x10, 0x5E, 0xB0) : Color.FromRgb(0x38, 0xA9, 0xF5))
            : light ? Color.FromRgb(0x3E, 0x44, 0x4B) : Color.FromRgb(0x98, 0xA3, 0xAE);
        var pen = new Pen(new SolidColorBrush(color), 1.5);
        pen.StartLineCap = PenLineCap.Round;
        pen.EndLineCap = PenLineCap.Round;
        pen.LineJoin = PenLineJoin.Round;
        Point P(double x, double y) => new(x * scaleX, y * scaleY);
        Rect R(double x, double y, double width, double height) => new(x * scaleX, y * scaleY, width * scaleX, height * scaleY);
        void Line(double x1, double y1, double x2, double y2) => context.DrawLine(pen, P(x1, y1), P(x2, y2));
        void Box(double x, double y, double width, double height) => context.DrawRectangle(null, pen, R(x, y, width, height));
        void Circle(double x, double y, double radius, bool fill = false) =>
            context.DrawEllipse(fill ? pen.Brush : null, fill ? null : pen, P(x, y), radius * scaleX, radius * scaleY);
        void Path(string data)
        {
            var geometry = Geometry.Parse(data).Clone();
            geometry.Transform = new ScaleTransform(scaleX, scaleY);
            context.DrawGeometry(null, pen, geometry);
        }

        switch (Kind)
        {
            case "Home":
                // A house with a tick: the page that answers "where is the thing I came for?".
                Path("M3.5,11 L12,4 L20.5,11");
                Path("M5.5,9.5 L5.5,20 L18.5,20 L18.5,9.5");
                Path("M9.3,14.2 L11.3,16.2 L15,12.2");
                break;
            case "General":
            {
                // A cog: eight teeth round a hub.
                var gear = new StreamGeometry();
                using (var g = gear.Open())
                {
                    for (var i = 0; i < 8; i++)
                    {
                        var centre = i * Math.PI / 4;
                        var points = new (double R, double A)[] { (6.6, centre - 0.27), (9, centre - 0.17), (9, centre + 0.17), (6.6, centre + 0.27) };
                        for (var k = 0; k < points.Length; k++)
                        {
                            var point = P(12 + Math.Cos(points[k].A) * points[k].R, 12 + Math.Sin(points[k].A) * points[k].R);
                            if (i == 0 && k == 0) g.BeginFigure(point, false, true);
                            else g.LineTo(point, true, true);
                        }
                    }
                }
                gear.Freeze();
                context.DrawGeometry(null, pen, gear);
                Circle(12, 12, 2.8);
                break;
            }
            case "Appearance":
                // A painter's palette (thumb notch on the lower edge) with three paint dabs.
                Path("M12,3.5 C6.8,3.5 3,7.2 3,11.8 C3,16.3 6.6,20 11,20 C12.6,20 13.2,19 12.6,17.7 " +
                     "C12,16.4 12.8,15.2 14.3,15.2 L16.2,15.2 C18.9,15.2 21,13.3 21,10.6 C21,6.6 17,3.5 12,3.5 Z");
                Circle(7.6, 11.4, 1.25, true);
                Circle(10.2, 7.3, 1.25, true);
                Circle(15, 7.3, 1.25, true);
                break;
            case "Score":
                // A five-line staff with one note and its stem.
                for (var i = 0; i < 5; i++) Line(3, 6 + i * 3, 21, 6 + i * 3);
                Circle(9, 16.5, 1.9);
                Line(10.9, 16.2, 10.9, 6);
                break;
            case "Fretboard":
                // A short guitar neck: three frets across, three strings along.
                Box(3, 7.5, 18, 9);
                Line(8, 7.5, 8, 16.5);
                Line(13, 7.5, 13, 16.5);
                Line(17.5, 7.5, 17.5, 16.5);
                Line(3, 12, 21, 12);
                break;
            case "Timeline":
                // Three track lanes with blocks of different length.
                Box(3, 4.5, 8, 4);
                Box(9, 10, 12, 4);
                Box(5, 15.5, 9, 4);
                break;
            case "Pencil":
            {
                // A pencil: body, tip and the line it draws.
                Path("M15.5,4.5 L19.5,8.5 L9,19 L4.5,19.5 L5,15 Z");
                Line(13, 7, 17, 11);
                Line(5, 15, 9, 19);
                break;
            }
            case "Playback":
                // The transport play triangle in a ring.
                Circle(12, 12, 9);
                Path("M10,8.4 L16,12 L10,15.6 Z");
                break;
            case "Audio":
                // A speaker with two waves: where the sound goes out.
                Path("M3.5,9.5 L7.5,9.5 L12.5,5.5 L12.5,18.5 L7.5,14.5 L3.5,14.5 Z");
                Path("M15.6,9 C17.2,10.8 17.2,13.2 15.6,15");
                Path("M18.4,6.4 C21.4,9.8 21.4,14.2 18.4,17.6");
                break;
            case "Recording":
                // A microphone: capsule, pickup arc and stand.
                context.DrawRoundedRectangle(null, pen, R(9, 3, 6, 10.5), 3 * scaleX, 3 * scaleY);
                Path("M6,11 C6,14.8 8.7,17.2 12,17.2 C15.3,17.2 18,14.8 18,11");
                Line(12, 17.2, 12, 21);
                Line(8.5, 21, 15.5, 21);
                break;
            case "Tabs":
                // Two overlapping windows, the front one with a tab line.
                Path("M8,9.5 L8,4.5 L21,4.5 L21,15 L16,15");
                Box(3, 9.5, 13, 10);
                Line(3, 12.5, 16, 12.5);
                break;
            case "Shortcuts":
                // A keycap with a modifier mark in its corner.
                context.DrawRoundedRectangle(null, pen, R(4, 4.5, 16, 15), 3.5 * scaleX, 3.5 * scaleY);
                Line(7.5, 8.5, 10.5, 8.5);
                Line(7.5, 8.5, 7.5, 11.5);
                Line(9, 15.5, 15, 15.5);
                break;
            case "Files":
                // A folder with a circular arrow: opening, saving and recovery.
                Path("M3,7 C3,6.2 3.7,5.5 4.5,5.5 L9,5.5 L11,8 L19.5,8 C20.3,8 21,8.7 21,9.5 L21,18.5 C21,19.3 20.3,20 19.5,20 L4.5,20 C3.7,20 3,19.3 3,18.5 Z");
                Path("M14.6,13.2 A2.9,2.9 0 1 0 14.9,15.6");
                Line(14.6, 13.2, 16.4, 13.3);
                Line(14.6, 13.2, 14.5, 11.5);
                break;
            default:
                // Advanced: three horizontal sliders.
                Line(4, 7, 9, 7); Circle(12, 7, 2); Line(15, 7, 20, 7);
                Line(4, 12, 5, 12); Circle(8, 12, 2); Line(11, 12, 20, 12);
                Line(4, 17, 12, 17); Circle(15, 17, 2); Line(18, 17, 20, 17);
                break;
        }
    }
}
