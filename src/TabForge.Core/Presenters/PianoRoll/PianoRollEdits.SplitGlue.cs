namespace TabForge.Presenters;

// Owns: the piano roll's keyboard split and glue: cut every selected note a beat crosses (S), and join the selected notes of one pitch and voice that
//       touch, overlap or lie within a gap of each other (Ctrl+Shift+G). Each returns how many cuts / joins it made so the caller can say "Nothing to glue".
// Does not own: the pointer-driven split line and glue paint (PianoRollEdits.cs, PianoRollEdits.More.cs) or the commit (PianoRollPresenter).
// Tests: TabForge.Avalonia.Tests.PianoRollSplitGlueTests.
public static partial class PianoRollEdits
{
    /// <summary>True when a cut at <paramref name="at"/> falls strictly inside <paramref name="note"/> (not at an edge): the one rule the split key and the cut marker share.</summary>
    public static bool IsCrossedBy(PianoRollNote note, double at) => at > note.Start + MinLength / 2 && at < note.End - MinLength / 2;

    /// <summary>The selected notes a cut at <paramref name="at"/> crosses.</summary>
    public static IEnumerable<int> CrossedBy(IReadOnlyList<PianoRollNote> notes, IReadOnlyCollection<int> selected, double at) =>
        Valid(notes, selected).Where(i => IsCrossedBy(notes[i], at));

    /// <summary>Cuts each selected note that <paramref name="at"/> falls inside, once; both halves are selected. Cuts is the number of notes cut.</summary>
    public static (PianoRollEditResult Result, int Cuts) SplitSelectedAt(IReadOnlyList<PianoRollNote> notes, IReadOnlyCollection<int> selected, double at)
    {
        var crossing = CrossedBy(notes, selected, at).ToList();
        if (crossing.Count == 0) return (new PianoRollEditResult(notes.ToList(), Valid(notes, selected)), 0);
        return (SplitMany(notes, crossing.Select(i => (i, at))), crossing.Count);
    }

    /// <summary>Joins the selected notes of the same pitch, voice and channel that touch, overlap or start within <paramref name="gap"/> beats of the
    /// previous one's end: one note from the first start to the last end, with the first note's velocity and mute. Pairs is the number of joins made
    /// (three notes in a row are two pairs). Other pitches and unselected notes are untouched.</summary>
    public static (PianoRollEditResult Result, int Pairs) GlueTouching(IReadOnlyList<PianoRollNote> notes, IReadOnlyCollection<int> selected, double gap)
    {
        var picked = Valid(notes, selected);
        var drop = new HashSet<int>();
        var merged = new List<PianoRollNote>();
        var pairs = 0;
        foreach (var group in picked.GroupBy(i => (notes[i].Pitch, notes[i].Voice, notes[i].Channel)))
        {
            var run = new List<int>();
            void Flush()
            {
                if (run.Count > 1)
                {
                    foreach (var i in run) drop.Add(i);
                    var first = notes[run[0]];
                    merged.Add(first with { Length = run.Max(i => notes[i].End) - first.Start });
                    pairs += run.Count - 1;
                }
                run.Clear();
            }
            var end = double.NegativeInfinity;
            foreach (var i in group.OrderBy(i => notes[i].Start))
            {
                if (run.Count > 0 && notes[i].Start > end + Math.Max(0, gap) + 1e-9) Flush();
                run.Add(i);
                end = run.Count == 1 ? notes[i].End : Math.Max(end, notes[i].End);
            }
            Flush();
        }
        if (pairs == 0) return (new PianoRollEditResult(notes.ToList(), picked), 0);
        var list = new List<PianoRollNote>();
        var chosen = new List<int>();
        for (var i = 0; i < notes.Count; i++)
        {
            if (drop.Contains(i)) continue;
            if (picked.Contains(i)) chosen.Add(list.Count);
            list.Add(notes[i]);
        }
        foreach (var note in merged) { chosen.Add(list.Count); list.Add(note); }
        return (new PianoRollEditResult(list, chosen), pairs);
    }
}
