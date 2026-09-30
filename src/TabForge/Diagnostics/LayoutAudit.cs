using System.Windows;
using System.Windows.Media;
using TabForge.Models;
using TabForge.Views;

namespace TabForge.Diagnostics;

/// <summary>
/// Layout audit: engraves a track off-screen, walks the per-system cached drawings (nothing is recorded during
/// normal rendering, so the audit costs nothing when it is not run) and reports texts and markings that collide.
/// Texts are compared by their ink boxes; lines and shapes by their real outline, so a curve whose bounding
/// box merely covers a label is not a collision. Legitimate contacts are allowed: stems on noteheads and beams,
/// fret numbers on tab lines, ledger lines.
/// </summary>
internal static class LayoutAudit
{
    internal enum Kind { Text, Head, Line, DashedLine, Shape, Curve }

    internal sealed record Item(Kind Kind, string Label, Rect Box, IReadOnlyList<Point[]> Paths);

    internal sealed record Collision(int Track, int Bar, int System, Item A, Item B)
    {
        public override string ToString() => $"bar {Bar + 1}: {Describe(A)} x {Describe(B)}";
        private static string Describe(Item i) =>
            $"{i.Kind}{(i.Label.Length > 0 ? " \"" + i.Label + "\"" : "")} [{i.Box.X:0}..{i.Box.Right:0}, {i.Box.Y:0}..{i.Box.Bottom:0}]";
    }

    private static readonly Point[][] NoPaths = Array.Empty<Point[]>();

    /// <summary>Collisions for one track of a project.</summary>
    public static List<Collision> Run(SongProject project, int trackIndex, double? pageWidth = null)
    {
        var editor = new TabEditorControl { Project = project, SelectedTrackIndex = trackIndex, DarkPaper = false, HideCursor = true };
        if (pageWidth is { } w) editor.PageWidthOverride = w;
        editor.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        editor.Arrange(new Rect(editor.DesiredSize));
        editor.UpdateLayout();
        var result = new List<Collision>();
        foreach (var (system, drawing) in editor.AuditSystemDrawings())
        {
            var items = new List<Item>();
            Walk(drawing, Matrix.Identity, items);
            foreach (var (a, b) in Collisions(items))
                result.Add(new Collision(trackIndex, editor.AuditBarAt(system, (a.Box.X + a.Box.Right) / 2), system, a, b));
        }
        return result;
    }

    private static void Walk(Drawing drawing, Matrix outer, List<Item> items)
    {
        switch (drawing)
        {
            case DrawingGroup group:
                var m = group.Transform is { } t ? t.Value * outer : outer;
                foreach (var child in group.Children) Walk(child, m, items);
                break;
            case GlyphRunDrawing text:
            {
                var run = text.GlyphRun;
                if (run is null) break;
                var chars = run.Characters is { } c ? new string(c.ToArray()) : "";
                if (chars.Length > 0 && string.IsNullOrWhiteSpace(chars)) break;
                var ink = run.ComputeInkBoundingBox();
                if (ink.IsEmpty) break;
                ink.Offset(run.BaselineOrigin.X, run.BaselineOrigin.Y);
                items.Add(new Item(Kind.Text, chars, new MatrixTransform(outer).TransformBounds(ink), NoPaths));
                break;
            }
            case GeometryDrawing g when g.Geometry is { } geo:
            {
                var box = new MatrixTransform(outer).TransformBounds(geo.GetRenderBounds(g.Pen));
                if (box.IsEmpty) break;
                var dashed = g.Pen?.DashStyle is { } d && d.Dashes.Count > 0;
                switch (geo)
                {
                    case LineGeometry line:
                        items.Add(new Item(dashed ? Kind.DashedLine : Kind.Line, "", box,
                            new[] { new[] { outer.Transform(line.StartPoint), outer.Transform(line.EndPoint) } }));
                        break;
                    case EllipseGeometry e:
                        if (e.RadiusX <= 16) items.Add(new Item(Kind.Head, "", box, NoPaths));
                        break;
                    case RectangleGeometry:
                        break; // chips, masks, highlights
                    default:
                        if (g.Brush is not null || g.Pen is not null)
                            items.Add(new Item(g.Brush is null ? Kind.Curve : Kind.Shape, "", box, Outline(geo, outer)));
                        break;
                }
                break;
            }
        }
    }

    private static Point[][] Outline(Geometry geo, Matrix outer)
    {
        var flat = geo.GetFlattenedPathGeometry(0.4, ToleranceType.Absolute);
        var paths = new List<Point[]>();
        foreach (var figure in flat.Figures)
        {
            var points = new List<Point> { outer.Transform(figure.StartPoint) };
            foreach (var segment in figure.Segments)
                switch (segment)
                {
                    case PolyLineSegment poly: points.AddRange(poly.Points.Select(outer.Transform)); break;
                    case LineSegment line: points.Add(outer.Transform(line.Point)); break;
                }
            if (figure.IsClosed) points.Add(points[0]);
            paths.Add(points.ToArray());
        }
        return paths.ToArray();
    }

    private static IEnumerable<(Item, Item)> Collisions(List<Item> items)
    {
        var sorted = items.OrderBy(i => i.Box.X).ToList();
        for (var i = 0; i < sorted.Count; i++)
        {
            var a = sorted[i];
            for (var j = i + 1; j < sorted.Count && sorted[j].Box.X < a.Box.Right; j++)
            {
                var b = sorted[j];
                if (!Collides(a, b) && !Collides(b, a)) continue;
                yield return (a, b);
            }
        }
    }

    private static Rect Shrink(Rect r, double by) => new(r.X + by, r.Y + by, Math.Max(0, r.Width - 2 * by), Math.Max(0, r.Height - 2 * by));

    private static bool BoxesOverlap(Rect a, Rect b)
    {
        var w = Math.Min(a.Right, b.Right) - Math.Max(a.X, b.X);
        var h = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Y, b.Y);
        return w > 1.6 && h > 1.6;
    }

    /// <summary>Directional test so each pair is judged once (text vs anything, dashed marking vs notehead).</summary>
    private static bool Collides(Item a, Item b)
    {
        if (a.Kind == Kind.Text && b.Kind == Kind.Text) return a.Box.X <= b.Box.X && BoxesOverlap(a.Box, b.Box);
        if (a.Kind == Kind.Text)
        {
            if (b.Kind == Kind.Head) return BoxesOverlap(a.Box, b.Box) && !Rect.Inflate(b.Box, 0.6, 0.6).Contains(a.Box); // a digit inside its ring is fine
            if (b.Kind == Kind.Line && IsStaffLine(b)) return false;
            var target = Shrink(a.Box, 0.6);
            return b.Paths.Any(path => PathHits(path, target));
        }
        if (a.Kind == Kind.Curve && b.Kind == Kind.Curve && a.Box.IntersectsWith(b.Box))
            return a.Paths.Any(pa => b.Paths.Any(pb => PathsCross(pa, pb)));   // bend, slur, tie or whammy curves must not cross each other
        if (a.Kind == Kind.DashedLine && b.Kind == Kind.Head)
            return a.Paths.Any(path => PathHits(path, Shrink(b.Box, 1.0)));
        return false;
    }

    private static bool PathsCross(Point[] p, Point[] q)
    {
        for (var i = 0; i + 1 < p.Length; i++)
            for (var j = 0; j + 1 < q.Length; j++)
                if (SegmentsCross(p[i], p[i + 1], q[j], q[j + 1])) return true;
        return false;
    }

    private static bool SegmentsCross(Point a, Point b, Point c, Point d)
    {
        static double Cross(Point o, Point p, Point q) => (p.X - o.X) * (q.Y - o.Y) - (p.Y - o.Y) * (q.X - o.X);
        var d1 = Cross(c, d, a); var d2 = Cross(c, d, b); var d3 = Cross(a, b, c); var d4 = Cross(a, b, d);
        const double eps = 0.05; // touching at an end point is not a crossing
        return ((d1 > eps && d2 < -eps) || (d1 < -eps && d2 > eps)) && ((d3 > eps && d4 < -eps) || (d3 < -eps && d4 > eps));
    }

    private static bool IsStaffLine(Item line) =>
        line.Paths.Count == 1 && Math.Abs(line.Paths[0][0].Y - line.Paths[0][^1].Y) < 0.05 && Math.Abs(line.Paths[0][0].X - line.Paths[0][^1].X) > 60;

    private static bool PathHits(Point[] path, Rect r)
    {
        if (r.Width <= 0 || r.Height <= 0) return false;
        for (var i = 0; i + 1 < path.Length; i++)
            if (SegmentHits(path[i], path[i + 1], r)) return true;
        return false;
    }

    /// <summary>Liang-Barsky segment / rectangle intersection.</summary>
    private static bool SegmentHits(Point p, Point q, Rect r)
    {
        double t0 = 0, t1 = 1;
        double dx = q.X - p.X, dy = q.Y - p.Y;
        bool Clip(double pp, double qq)
        {
            if (Math.Abs(pp) < 1e-9) return qq >= 0;
            var t = qq / pp;
            if (pp < 0) { if (t > t1) return false; if (t > t0) t0 = t; }
            else { if (t < t0) return false; if (t < t1) t1 = t; }
            return true;
        }
        return Clip(-dx, p.X - r.Left) && Clip(dx, r.Right - p.X) && Clip(-dy, p.Y - r.Top) && Clip(dy, r.Bottom - p.Y);
    }

    public static string Format(IEnumerable<Collision> collisions, string trackName, int trackIndex)
    {
        var sb = new System.Text.StringBuilder();
        var list = collisions.ToList();
        sb.AppendLine($"track {trackIndex + 1} \"{trackName}\": {list.Count} collision(s)");
        foreach (var c in list.OrderBy(c => c.Bar).ThenBy(c => c.A.Box.X)) sb.AppendLine("  " + c);
        return sb.ToString();
    }
}
