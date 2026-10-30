namespace TabForge.Presenters;

// Owns: the piano roll's selection-geometry helpers: the lasso polygon hit test (a note counts when its centre is
//       inside the polygon or its rectangle overlaps the polygon's outline) and the split line's crossing beat.
// Does not own: the edits (PianoRollEdits) or the pointer (PianoRollSurface).
// Tests: TabForge.Avalonia.Tests.PianoRollSelectTests.
public static class PianoRollSelection
{
    /// <summary>True when point (<paramref name="beat"/>, <paramref name="pitch"/>) is inside <paramref name="polygon"/>.</summary>
    public static bool Inside(IReadOnlyList<(double Beat, double Pitch)> polygon, double beat, double pitch)
    {
        var inside = false;
        for (var i = 0; i < polygon.Count; i++)
        {
            var (x1, y1) = polygon[i];
            var (x2, y2) = polygon[(i + 1) % polygon.Count];
            if ((y1 > pitch) == (y2 > pitch)) continue;
            var at = x1 + (pitch - y1) / (y2 - y1) * (x2 - x1);
            if (beat < at) inside = !inside;
        }
        return inside;
    }

    /// <summary>The notes a lasso <paramref name="polygon"/> (in beat/pitch space) catches: centre inside, or rectangle overlapping its outline.</summary>
    public static List<int> InPolygon(IReadOnlyList<PianoRollNote> notes, IReadOnlyList<(double Beat, double Pitch)> polygon)
    {
        var list = new List<int>();
        if (polygon.Count < 3) return list;
        for (var i = 0; i < notes.Count; i++)
        {
            var n = notes[i];
            var centre = (n.Start + n.Length / 2, (double)n.Pitch);
            if (Inside(polygon, centre.Item1, centre.Item2)) { list.Add(i); continue; }
            for (var s = 0; s < polygon.Count; s++)
                if (SegRect(polygon[s], polygon[(s + 1) % polygon.Count], n.Start, n.End, n.Pitch - 0.5, n.Pitch + 0.5)) { list.Add(i); break; }
        }
        return list;
    }

    private static bool SegRect((double Beat, double Pitch) a, (double Beat, double Pitch) b, double x0, double x1, double y0, double y1)
    {
        return SegSeg(a, b, (x0, y0), (x1, y0)) || SegSeg(a, b, (x1, y0), (x1, y1))
            || SegSeg(a, b, (x1, y1), (x0, y1)) || SegSeg(a, b, (x0, y1), (x0, y0))
            || (a.Beat >= x0 && a.Beat <= x1 && a.Pitch >= y0 && a.Pitch <= y1);
    }

    private static bool SegSeg((double Beat, double Pitch) a, (double Beat, double Pitch) b, (double Beat, double Pitch) c, (double Beat, double Pitch) d)
    {
        var r = (b.Beat - a.Beat, b.Pitch - a.Pitch);
        var s = (d.Beat - c.Beat, d.Pitch - c.Pitch);
        var denom = r.Item1 * s.Item2 - r.Item2 * s.Item1;
        if (Math.Abs(denom) < 1e-12) return false;
        var t = ((c.Beat - a.Beat) * s.Item2 - (c.Pitch - a.Pitch) * s.Item1) / denom;
        var u = ((c.Beat - a.Beat) * r.Item2 - (c.Pitch - a.Pitch) * r.Item1) / denom;
        return t >= 0 && t <= 1 && u >= 0 && u <= 1;
    }

    /// <summary>The beat where segment <paramref name="a"/>-<paramref name="b"/> (beat/pitch space) crosses <paramref name="note"/>'s
    /// rectangle, or NaN when it misses.</summary>
    public static double CrossBeat(PianoRollNote note, (double Beat, double Pitch) a, (double Beat, double Pitch) b)
    {
        var x0 = note.Start;
        var x1 = note.End;
        var y0 = note.Pitch - 0.5;
        var y1 = note.Pitch + 0.5;
        if (Math.Abs(b.Pitch - a.Pitch) > 1e-12)
        {
            var t = (note.Pitch - a.Pitch) / (b.Pitch - a.Pitch);
            if (t >= 0 && t <= 1)
            {
                var beat = a.Beat + t * (b.Beat - a.Beat);
                if (beat > x0 && beat < x1) return beat;
            }
        }
        if (Math.Abs(b.Beat - a.Beat) > 1e-12)
        {
            var u = (Math.Clamp((a.Beat + b.Beat) / 2, x0, x1) - a.Beat) / (b.Beat - a.Beat);
            var pitch = a.Pitch + u * (b.Pitch - a.Pitch);
            if (u >= 0 && u <= 1 && pitch >= y0 && pitch <= y1) return Math.Clamp((a.Beat + b.Beat) / 2, x0, x1);
        }
        return double.NaN;
    }
}
