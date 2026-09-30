using System.Windows;
using System.Windows.Media;

namespace TabForge.Views;

/// <summary>Small DPI-independent outline glyphs for the native settings category rail.</summary>
internal sealed class SettingsNavigationIcon : FrameworkElement
{
    public string Kind { get; init; } = "Settings";

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

        switch (Kind)
        {
            case "General":
                Circle(12, 12, 3.3);
                Circle(12, 12, 7);
                for (var i = 0; i < 8; i++)
                {
                    var angle = i * Math.PI / 4;
                    Line(12 + Math.Cos(angle) * 7, 12 + Math.Sin(angle) * 7,
                        12 + Math.Cos(angle) * 9, 12 + Math.Sin(angle) * 9);
                }
                break;
            case "Appearance":
            {
                // Painter's palette (thumb hole notch on the lower edge) with paint dabs, and a brush.
                var palette = Geometry.Parse("M12,3.5 C6.8,3.5 3,7.2 3,11.8 C3,16.3 6.6,20 11,20 C12.6,20 13.2,19 12.6,17.7 " +
                                             "C12,16.4 12.8,15.2 14.3,15.2 L16.2,15.2 C18.9,15.2 21,13.3 21,10.6 C21,6.6 17,3.5 12,3.5 Z").Clone();
                palette.Transform = new ScaleTransform(scaleX, scaleY);
                context.DrawGeometry(null, pen, palette);
                Circle(7.4, 11.6, 1.25, true);
                Circle(9.3, 7.6, 1.25, true);
                Circle(13.8, 6.9, 1.25, true);
                Circle(17.3, 9.8, 1.25, true);
                var brushPen = new Pen(pen.Brush, 2.2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                context.DrawLine(brushPen, P(16.2, 21.2), P(21.2, 16.2));
                context.DrawEllipse(pen.Brush, null, P(15.6, 21.8), 1.3 * scaleX, 1.3 * scaleY);
                break;
            }
            case "Fretboard":
                // A short guitar neck: frets across, strings along.
                Box(3, 7.5, 18, 9);
                Line(8, 7.5, 8, 16.5);
                Line(13, 7.5, 13, 16.5);
                Line(17.5, 7.5, 17.5, 16.5);
                Line(3, 10.5, 21, 10.5);
                Line(3, 13.5, 21, 13.5);
                Circle(10.5, 12, 1.1, true);
                break;
            case "Interface":
                Box(3.5, 4.5, 17, 15);
                Line(3.5, 9, 20.5, 9);
                Circle(6.5, 6.8, 0.7, true);
                Circle(9.5, 6.8, 0.7, true);
                break;
            case "Viewing":
                Box(4, 5, 16, 11);
                Line(9, 20, 15, 20);
                Line(12, 16, 12, 20);
                break;
            case "AudioPlugins":
                // Three faders: audio mixing and plug-ins (effects and instruments).
                Line(6, 4, 6, 20); Line(12, 4, 12, 20); Line(18, 4, 18, 20);
                context.DrawRoundedRectangle(pen.Brush, null, R(3.8, 13, 4.4, 3.4), 1 * scaleX, 1 * scaleY);
                context.DrawRoundedRectangle(pen.Brush, null, R(9.8, 6.5, 4.4, 3.4), 1 * scaleX, 1 * scaleY);
                context.DrawRoundedRectangle(pen.Brush, null, R(15.8, 10.5, 4.4, 3.4), 1 * scaleX, 1 * scaleY);
                break;
            case "Pencil":
            {
                // A pencil: body, tip and the line it draws.
                var body = Geometry.Parse("M15.5,4.5 L19.5,8.5 L9,19 L4.5,19.5 L5,15 Z").Clone();
                body.Transform = new ScaleTransform(scaleX, scaleY);
                context.DrawGeometry(null, pen, body);
                Line(13, 7, 17, 11);
                Line(5, 15, 9, 19);
                break;
            }
            case "PlaybackSound":
            {
                // A play triangle with sound waves coming out of it: playback and sound in one.
                var play = Geometry.Parse("M4.5,5.5 L4.5,18.5 L14,12 Z").Clone();
                play.Transform = new ScaleTransform(scaleX, scaleY);
                context.DrawGeometry(null, pen, play);
                foreach (var wave in new[] { "M16.6,8.6 C18.2,10.4 18.2,13.6 16.6,15.4", "M19.2,6 C22.1,9.5 22.1,14.5 19.2,18" })
                {
                    var arc = Geometry.Parse(wave).Clone();
                    arc.Transform = new ScaleTransform(scaleX, scaleY);
                    context.DrawGeometry(null, pen, arc);
                }
                break;
            }
            case "Performance":
                Line(4, 17, 6, 12);
                Line(6, 12, 10, 8);
                Line(10, 8, 15, 7);
                Line(15, 7, 20, 12);
                Line(12, 14, 16, 9);
                break;
            case "Tabs":
                Box(3, 7, 18, 12);
                Line(7, 7, 7, 11);
                Line(12, 7, 12, 11);
                break;
            case "Hotkeys":
                Box(3, 6, 18, 12);
                for (var x = 6; x <= 18; x += 4)
                {
                    Line(x, 9, x + 1, 9);
                    Line(x, 13, x + 1, 13);
                }
                Line(8, 16, 16, 16);
                break;
            default:
                Line(4, 7, 9, 7); Circle(12, 7, 2); Line(15, 7, 20, 7);
                Line(4, 12, 5, 12); Circle(8, 12, 2); Line(11, 12, 20, 12);
                Line(4, 17, 12, 17); Circle(15, 17, 2); Line(18, 17, 20, 17);
                break;
        }
    }
}
