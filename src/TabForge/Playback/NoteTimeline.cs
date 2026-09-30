namespace TabForge.Playback;

/// <summary>
/// Queries over a track's canonical note events (see <see cref="ScoreTimeline.NotesFor"/>).
/// All binary searches over note arrays live here so the score, the arrangement and the fretboard
/// answer "what is sounding now?" with exactly the same code and can never disagree.
/// </summary>
public static class NoteTimeline
{
    /// <summary>Index of the first note whose onset is past <paramref name="ms"/> (insertion point).</summary>
    public static int FirstIndexAfter(NoteEvent[] notes, double ms)
    {
        var lo = 0;
        var hi = notes.Length;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (notes[mid].OnsetMs <= ms + 0.5) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }

    /// <summary>
    /// Notes that are sounding at <paramref name="ms"/> (onset &lt;= ms &lt; end), newest first.
    /// <paramref name="lookbackMs"/> bounds the scan so a long rest costs nothing.
    /// </summary>
    public static List<NoteEvent> SoundingAt(NoteEvent[] notes, double ms, double lookbackMs = 6000)
    {
        var result = new List<NoteEvent>();
        var index = FirstIndexAfter(notes, ms) - 1;
        for (var i = index; i >= 0; i--)
        {
            var n = notes[i];
            if (n.OnsetMs < ms - lookbackMs) break;
            if (n.EndMs > ms) result.Add(n);
        }
        return result;
    }

    /// <summary>Notes that started very recently and are still within <paramref name="windowMs"/> of their onset.</summary>
    public static List<NoteEvent> StruckWithin(NoteEvent[] notes, double ms, double windowMs)
    {
        var result = new List<NoteEvent>();
        var index = FirstIndexAfter(notes, ms) - 1;
        for (var i = index; i >= 0; i--)
        {
            var n = notes[i];
            if (n.OnsetMs < ms - windowMs) break;
            if (n.OnsetMs >= ms - windowMs) result.Add(n);
        }
        return result;
    }

    /// <summary>
    /// Notes that have finished within <paramref name="holdMs"/> of <paramref name="ms"/>, newest first
    /// (a bounded "recent" trace for the fretboard), capped at <paramref name="maxCount"/>.
    /// </summary>
    public static List<NoteEvent> RecentlyEnded(NoteEvent[] notes, double ms, double holdMs, int maxCount)
    {
        var result = new List<NoteEvent>();
        var index = FirstIndexAfter(notes, ms) - 1;
        for (var i = index; i >= 0 && result.Count < maxCount; i--)
        {
            var n = notes[i];
            if (n.EndMs > ms) continue;
            if (ms - n.EndMs > holdMs) break;
            result.Add(n);
        }
        return result;
    }

    /// <summary>True when any note starts or ends inside the half-open window (fromMs, toMs].</summary>
    public static bool AnyBoundaryBetween(NoteEvent[] notes, double fromMs, double toMs)
    {
        if (notes.Length == 0) return true;   // unknown: force a repaint
        if (toMs < fromMs) (fromMs, toMs) = (toMs, fromMs);
        var index = FirstIndexAfter(notes, toMs) - 1;
        for (var i = index; i >= 0 && notes[i].OnsetMs > fromMs - 4000; i--)
        {
            var n = notes[i];
            if (n.OnsetMs > fromMs && n.OnsetMs <= toMs) return true;
            if (n.EndMs > fromMs && n.EndMs <= toMs) return true;
        }
        return false;
    }
}
