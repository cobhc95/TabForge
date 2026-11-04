using System;
using System.Collections.Generic;
using TabForge.Platform;
using TabForge.Presenters;

namespace TabForge.Avalonia.Platform;

// Owns: the inlay shapes of the current layout and look, built once (paths, centres, box sizes) and kept until the layout or the look changes,
//   so drawing them each frame builds nothing. One plan per panel.
// Does not own: where inlays sit and how big they are (Core FretboardRealRules), the textures (FretboardAssets) or the drawing (FretboardArtist).
// Tests: TabForge.Avalonia.Tests.InstrumentPanelViewTests, FretboardPlaybackFrameTests (a steady frame's allocation).
internal sealed class FretboardInlayPlan
{
    /// <summary>One planned inlay: its shape, centre, the side of the square the material is drawn into, and whether it is round (gets a glint).</summary>
    internal readonly record struct Placed(IUiPath Path, double X, double Y, double Side, bool Round);

    private readonly record struct Key(UiRect Board, double Gap, int First, int Last, int Strings, bool LeftHanded, bool Bass, string Shape);

    private Key? _key;
    private Placed[] _placed = Array.Empty<Placed>();

    /// <summary>The planned inlays for the scene: reused while the layout and shape are unchanged, else rebuilt.</summary>
    internal Placed[] For(in FretboardScene s)
    {
        var l = s.Layout;
        var key = new Key(l.Board, l.StringGap, l.FirstFret, l.LastFret, l.Strings, l.LeftHanded, s.Bass, s.Look.InlayShape);
        if (_key == key) return _placed;
        _key = key;
        _placed = s.Look.InlayShape == TabForge.Services.FretboardStyles.None ? Array.Empty<Placed>() : Build(s);
        return _placed;
    }

    private static Placed[] Build(in FretboardScene s)
    {
        var l = s.Layout;
        var wood = FretboardArtist.Wood(l, s.Bass);
        var middle = (l.Board.Y + l.Board.Bottom) / 2;
        var list = new List<Placed>();
        for (var fret = l.FirstFret; fret <= l.LastFret; fret++)
        {
            if (!FretboardRealRules.HasInlay(fret)) continue;
            var x = FretboardGeometryRules.FretX(l, fret);
            var cell = Math.Abs(FretboardGeometryRules.WireX(l, fret) - FretboardGeometryRules.WireX(l, fret - 1));
            if (FretboardRealRules.IsDouble(fret) && s.Look.InlayShape is "Dots" or "Small dots")
            {
                var offset = FretboardRealRules.DoubleOffset(l.StringGap, l.Strings);
                list.Add(Shape(s, wood, x, middle - offset, cell, fret, doubled: true));
                list.Add(Shape(s, wood, x, middle + offset, cell, fret, doubled: true));
            }
            else list.Add(Shape(s, wood, x, middle, cell, fret, doubled: false));
        }
        return list.ToArray();
    }

    private static Placed Shape(in FretboardScene s, UiRect wood, double x, double y, double cell, int fret, bool doubled)
    {
        var gap = s.Layout.StringGap;
        var d = s.D;
        double w, h;
        switch (s.Look.InlayShape)
        {
            case "Blocks":
                (w, h) = (Math.Clamp(cell * 0.65, 4, cell * 0.85), wood.Height * (doubled ? 0.26 : 0.60));
                return Polygon(d, x, y, w, h, new[] { new UiPoint(x - w / 2, y - h / 2), new UiPoint(x + w / 2, y - h / 2), new UiPoint(x + w / 2, y + h / 2), new UiPoint(x - w / 2, y + h / 2) });
            case "Trapezoids":
                (w, h) = (Math.Clamp(cell * 0.5, 3.5, gap * 1.2), wood.Height * (doubled ? 0.25 : 0.6));
                return Polygon(d, x, y, w, h, new[] { new UiPoint(x - w * 0.36, y - h / 2), new UiPoint(x + w * 0.36, y - h / 2), new UiPoint(x + w / 2, y + h / 2), new UiPoint(x - w / 2, y + h / 2) });
            case "Shark fins":
                (w, h) = (cell * 0.72, wood.Height * 0.78);
                return Polygon(d, x, y, w, h, new[] {
                    new UiPoint(x + w * 0.5, y - h / 2),
                    new UiPoint(x + w * 0.5, y + h / 2),
                    new UiPoint(x - w * 0.5, y + h / 2) });
            default:
                var diameter = Math.Min(FretboardRealRules.DotDiameter(gap, s.Bass, s.Look.InlayShape == "Small dots"), cell * 0.78);
                var r = diameter / 2;
                var circle = d.Paths.BuildPath(sink =>
                {
                    sink.BeginFigure(new UiPoint(x + r, y), true, true);
                    sink.ArcTo(new UiPoint(x - r, y), r, r, 0, false, true);
                    sink.ArcTo(new UiPoint(x + r, y), r, r, 0, false, true);
                });
                return new Placed(circle, x, y, diameter * 1.3, true);
        }
    }

    private static Placed Polygon(IDrawContext d, double x, double y, double w, double h, UiPoint[] points)
    {
        var path = d.Paths.BuildPath(sink =>
        {
            sink.BeginFigure(points[0], true, true);
            for (var i = 1; i < points.Length; i++) sink.LineTo(points[i]);
        });
        return new Placed(path, x, y, Math.Max(w, h) * 1.3, false);
    }
}
