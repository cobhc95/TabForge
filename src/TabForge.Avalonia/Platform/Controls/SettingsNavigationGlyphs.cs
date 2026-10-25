using System;
using Avalonia;
using Avalonia.Media;
using TabForge.Avalonia.Platform;
using TabForge.Platform;
using TabForge.Services;

namespace TabForge.Avalonia.Platform.Controls;

/// <summary>
/// The outline glyphs of the settings rail, as the WPF rail draws them: one idea per category, on a 24 x 24 grid, one line weight that does not
/// scale with the icon. Categories without a glyph of their own get the Advanced glyph (three sliders).
/// Owns: the glyph geometry and the category-to-glyph choice. Does not own: the colour (<see cref="SettingsNavigationIconView"/>) or the rail row.
/// Tests: TabForge.Avalonia.Tests.PreferencesWindowTests.
/// </summary>
public static class SettingsNavigationGlyphs
{
    /// <summary>The side of the grid the glyphs are drawn on, in grid units.</summary>
    public const double GridSide = 24;

    /// <summary>The line weight in DIPs.</summary>
    public const double LineWidth = 1.5;

    private const int GearTeeth = 8;
    private const double GearCentre = 12, GearRootRadius = 6.6, GearTipRadius = 9, GearHalfToothAngle = 0.27, GearHalfTipAngle = 0.17;

    /// <summary>Draws the glyph of <paramref name="category"/> in a square of <paramref name="size"/> DIPs at the origin, in <paramref name="ink"/>.</summary>
    public static void Draw(IDrawContext d, string category, double size, UiColor ink)
    {
        if (size <= 1) return;
        var canvas = new Canvas(d, size / GridSide, ink);
        switch (category)
        {
            case SettingsCatalog.Home: Home(canvas); break;
            case SettingsCatalog.General: General(canvas); break;
            case SettingsCatalog.Appearance: Appearance(canvas); break;
            case SettingsCatalog.Score: Score(canvas); break;
            case SettingsCatalog.Fretboard: Fretboard(canvas); break;
            case SettingsCatalog.Timeline: Timeline(canvas); break;
            case SettingsCatalog.Editing: Editing(canvas); break;
            case SettingsCatalog.Playback: Playback(canvas); break;
            case SettingsCatalog.AudioVst: Audio(canvas); break;
            case SettingsCatalog.Recording: Recording(canvas); break;
            case SettingsCatalog.Tabs: Tabs(canvas); break;
            case SettingsCatalog.Hotkeys: Shortcuts(canvas); break;
            case SettingsCatalog.Files: Files(canvas); break;
            default: Advanced(canvas); break;
        }
    }

    // A house with a tick: the page that answers "where is the thing I came for?".
    private static void Home(Canvas c)
    {
        c.Path("M3.5,11 L12,4 L20.5,11");
        c.Path("M5.5,9.5 L5.5,20 L18.5,20 L18.5,9.5");
        c.Path("M9.3,14.2 L11.3,16.2 L15,12.2");
    }

    // A cog: eight teeth round a hub.
    private static void General(Canvas c)
    {
        c.Gear();
        c.Circle(GearCentre, GearCentre, 2.8);
    }

    // A painter's palette (thumb notch on the lower edge) with three paint dabs.
    private static void Appearance(Canvas c)
    {
        c.Path("M12,3.5 C6.8,3.5 3,7.2 3,11.8 C3,16.3 6.6,20 11,20 C12.6,20 13.2,19 12.6,17.7 " +
               "C12,16.4 12.8,15.2 14.3,15.2 L16.2,15.2 C18.9,15.2 21,13.3 21,10.6 C21,6.6 17,3.5 12,3.5 Z");
        c.Circle(7.6, 11.4, 1.25, filled: true);
        c.Circle(10.2, 7.3, 1.25, filled: true);
        c.Circle(15, 7.3, 1.25, filled: true);
    }

    // A five-line staff with one note and its stem.
    private static void Score(Canvas c)
    {
        for (var i = 0; i < 5; i++) c.Line(3, 6 + i * 3, 21, 6 + i * 3);
        c.Circle(9, 16.5, 1.9);
        c.Line(10.9, 16.2, 10.9, 6);
    }

    // A short guitar neck: three frets across, three strings along.
    private static void Fretboard(Canvas c)
    {
        c.Box(3, 7.5, 18, 9);
        c.Line(8, 7.5, 8, 16.5);
        c.Line(13, 7.5, 13, 16.5);
        c.Line(17.5, 7.5, 17.5, 16.5);
        c.Line(3, 12, 21, 12);
    }

    // Three track lanes with blocks of different length.
    private static void Timeline(Canvas c)
    {
        c.Box(3, 4.5, 8, 4);
        c.Box(9, 10, 12, 4);
        c.Box(5, 15.5, 9, 4);
    }

    // A pencil: body, tip and the line it draws.
    private static void Editing(Canvas c)
    {
        c.Path("M15.5,4.5 L19.5,8.5 L9,19 L4.5,19.5 L5,15 Z");
        c.Line(13, 7, 17, 11);
        c.Line(5, 15, 9, 19);
    }

    // The transport play triangle in a ring.
    private static void Playback(Canvas c)
    {
        c.Circle(12, 12, 9);
        c.Path("M10,8.4 L16,12 L10,15.6 Z");
    }

    // A speaker with two waves: where the sound goes out.
    private static void Audio(Canvas c)
    {
        c.Path("M3.5,9.5 L7.5,9.5 L12.5,5.5 L12.5,18.5 L7.5,14.5 L3.5,14.5 Z");
        c.Path("M15.6,9 C17.2,10.8 17.2,13.2 15.6,15");
        c.Path("M18.4,6.4 C21.4,9.8 21.4,14.2 18.4,17.6");
    }

    // A microphone: capsule, pickup arc and stand.
    private static void Recording(Canvas c)
    {
        c.RoundBox(9, 3, 6, 10.5, 3);
        c.Path("M6,11 C6,14.8 8.7,17.2 12,17.2 C15.3,17.2 18,14.8 18,11");
        c.Line(12, 17.2, 12, 21);
        c.Line(8.5, 21, 15.5, 21);
    }

    // Two overlapping windows, the front one with a tab line.
    private static void Tabs(Canvas c)
    {
        c.Path("M8,9.5 L8,4.5 L21,4.5 L21,15 L16,15");
        c.Box(3, 9.5, 13, 10);
        c.Line(3, 12.5, 16, 12.5);
    }

    // A keycap with a modifier mark in its corner.
    private static void Shortcuts(Canvas c)
    {
        c.RoundBox(4, 4.5, 16, 15, 3.5);
        c.Line(7.5, 8.5, 10.5, 8.5);
        c.Line(7.5, 8.5, 7.5, 11.5);
        c.Line(9, 15.5, 15, 15.5);
    }

    // A folder with a circular arrow: opening, saving and recovery.
    private static void Files(Canvas c)
    {
        c.Path("M3,7 C3,6.2 3.7,5.5 4.5,5.5 L9,5.5 L11,8 L19.5,8 C20.3,8 21,8.7 21,9.5 L21,18.5 C21,19.3 20.3,20 19.5,20 L4.5,20 C3.7,20 3,19.3 3,18.5 Z");
        c.Path("M14.6,13.2 A2.9,2.9 0 1 0 14.9,15.6");
        c.Line(14.6, 13.2, 16.4, 13.3);
        c.Line(14.6, 13.2, 14.5, 11.5);
    }

    // Three horizontal sliders.
    private static void Advanced(Canvas c)
    {
        c.Line(4, 7, 9, 7); c.Circle(12, 7, 2); c.Line(15, 7, 20, 7);
        c.Line(4, 12, 5, 12); c.Circle(8, 12, 2); c.Line(11, 12, 20, 12);
        c.Line(4, 17, 12, 17); c.Circle(15, 17, 2); c.Line(18, 17, 20, 17);
    }

    /// <summary>The drawing calls of one glyph, with grid coordinates scaled to DIPs and the line style of the rail.</summary>
    private sealed class Canvas(IDrawContext draw, double scale, UiColor ink)
    {
        private readonly UiStroke _stroke = new(ink, LineWidth, RoundCaps: true, RoundJoins: true);

        public void Line(double x1, double y1, double x2, double y2) =>
            draw.Line(_stroke, x1 * scale, y1 * scale, x2 * scale, y2 * scale);

        public void Box(double x, double y, double width, double height) => RoundBox(x, y, width, height, 0);

        public void RoundBox(double x, double y, double width, double height, double radius) =>
            draw.Rect(null, _stroke, x * scale, y * scale, width * scale, height * scale, radius * scale);

        public void Circle(double cx, double cy, double radius, bool filled = false) =>
            draw.Ellipse(filled ? ink : (UiColor?)null, filled ? (UiStroke?)null : _stroke, cx * scale, cy * scale, radius * scale, radius * scale);

        public void Path(string data)
        {
            var geometry = Geometry.Parse(data);
            geometry.Transform = new ScaleTransform(scale, scale);
            draw.Path(null, _stroke, new AvaloniaPath(geometry));
        }

        /// <summary>One closed outline of eight teeth round the hub.</summary>
        public void Gear()
        {
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                var started = false;
                for (var tooth = 0; tooth < GearTeeth; tooth++)
                {
                    var centre = tooth * Math.PI / 4;
                    foreach (var (radius, angle) in new[]
                    {
                        (GearRootRadius, centre - GearHalfToothAngle), (GearTipRadius, centre - GearHalfTipAngle),
                        (GearTipRadius, centre + GearHalfTipAngle), (GearRootRadius, centre + GearHalfToothAngle)
                    })
                    {
                        var point = new Point((GearCentre + Math.Cos(angle) * radius) * scale, (GearCentre + Math.Sin(angle) * radius) * scale);
                        if (started) context.LineTo(point);
                        else
                        {
                            context.BeginFigure(point, false);
                            started = true;
                        }
                    }
                }
                context.EndFigure(true);
            }
            draw.Path(null, _stroke, new AvaloniaPath(geometry));
        }
    }
}
