namespace TabForge.KeyboardMode;

// Owns: which notes lie in a stretch of time, with a loop unrolled so the notes after the loop end are the loop's first notes again.
// Does not own: the notes (IKeyboardModeNoteSource), the playing position (KeyboardModeClock) or drawing.
// Tests: TestKeyboardModeNoteStream.
public static class KeyboardModeWindow
{
    // A loop of a few bars cannot fill a page with thousands of passes: the page is cut after this many.
    private const int MaxPasses = 400;

    /// <summary>Adds to <paramref name="into"/> every note that sounds somewhere in the virtual stretch [fromV, toV), in virtual onset order.
    /// Without a loop virtual time is song time. With one, pass 0 runs to the loop end and each later pass replays the loop.</summary>
    public static void Collect(IKeyboardModeNoteSource source, double fromV, double toV, KeyboardModeLoop? loop, List<KeyboardModePlaced> into)
    {
        if (toV <= fromV || source.Notes.Count == 0) return;
        if (loop is not { IsUsable: true } l)
        {
            Segment(source, fromV, toV, 0, double.MaxValue, 0, into);
            return;
        }
        // Pass 0 covers virtual [0, end) at offset 0; pass k >= 1 covers [end + (k-1) len, end + k len) at offset k len, real [start, end).
        Segment(source, fromV, Math.Min(toV, l.EndMs), 0, l.EndMs, 0, into);
        if (toV <= l.EndMs) return;
        var first = (int)Math.Floor((Math.Max(fromV, l.EndMs) - l.EndMs) / l.Length) + 1;
        for (var k = first; l.EndMs + (k - 1) * l.Length < toV && k < first + MaxPasses; k++)
        {
            var passStart = l.EndMs + (k - 1) * l.Length;
            Segment(source, Math.Max(fromV, passStart), Math.Min(toV, passStart + l.Length), l.StartMs, l.EndMs, k * l.Length, into);
        }
    }

    /// <summary>The song time a virtual time is: itself before the loop end, after it the matching moment of the loop.</summary>
    public static double RealOf(double virtualMs, KeyboardModeLoop? loop)
    {
        if (loop is not { IsUsable: true } l || virtualMs < l.EndMs) return virtualMs;
        return l.StartMs + (virtualMs - l.EndMs) % l.Length;
    }

    private static void Segment(IKeyboardModeNoteSource source, double fromV, double toV, double realFrom, double realTo, double offset, List<KeyboardModePlaced> into)
    {
        if (toV <= fromV) return;
        var a = fromV - offset;
        var b = toV - offset;
        var notes = source.Notes;
        var lo = 0;
        var hi = notes.Count;
        var reach = a - source.MaxDurationMs;   // a note that began before the stretch still counts while it sounds
        while (lo < hi) { var mid = (lo + hi) / 2; if (notes[mid].OnsetMs < reach) lo = mid + 1; else hi = mid; }
        for (var i = lo; i < notes.Count; i++)
        {
            var n = notes[i];
            if (n.OnsetMs >= b || n.OnsetMs >= realTo) break;
            if (n.OnsetMs < realFrom || n.EndMs <= a) continue;   // a later pass shows only the loop's own notes
            into.Add(new KeyboardModePlaced(n, n.OnsetMs + offset));
        }
    }
}
