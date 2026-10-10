namespace TabForge.Visualization;

/// <summary>
/// Owns: the decision whether the fretboard draws a movement line from the sounding shape to the next shape, and which notes it joins.
/// Does not own: drawing the line (FretboardRenderer) or building the notes (InstrumentVisualizer).
/// Tests: TestFretboardConnector.
/// </summary>
internal static class FretboardConnector
{
    /// <summary>The (string, fret) positions to join, or null when the hand does not move.
    /// One pass over one note list, so the shapes always come from the same tick. The sounding shape is the held notes,
    /// or (during a rest) the whole chord last struck, never a single note of it. A line needs a next position that
    /// is not already sounding; only the changed positions are joined.</summary>
    internal static (List<(int String, int Fret)> From, List<(int String, int Fret)> To)? Between(IReadOnlyList<VisualNote> notes)
    {
        var anyHeld = false;
        foreach (var n in notes) if (n.Role == VisualRole.Current && !n.Released) { anyHeld = true; break; }
        var current = new HashSet<(int, int)>();
        var next = new HashSet<(int, int)>();
        var releasedOnset = double.NaN;
        foreach (var n in notes)
        {
            if (n.Role == VisualRole.Current && (anyHeld ? !n.Released : n.Released)) { current.Add((n.StringIndex, n.Fret)); releasedOnset = n.OnsetMs; }
            else if (n.Role == VisualRole.Next) next.Add((n.StringIndex, n.Fret));
        }
        if (!anyHeld && !double.IsNaN(releasedOnset))
            foreach (var n in notes)
                if (n.Role == VisualRole.Past && Math.Abs(n.OnsetMs - releasedOnset) <= 0.5) current.Add((n.StringIndex, n.Fret));
        if (current.Count == 0 || next.Count == 0) return null;
        var to = next.Where(p => !current.Contains(p)).ToList();
        if (to.Count == 0) return null;
        var from = current.Where(p => !next.Contains(p)).ToList();
        return (from.Count > 0 ? from : current.ToList(), to);
    }
}
