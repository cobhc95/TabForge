using TabForge.Visualization;

namespace TabForge;

/// <summary>The fretboard movement line joins only changed positions: none for a repeated chord, also not in the rest gap
/// between repeats (where only one note of the last chord is shown as released) or with a stale next.</summary>
public static partial class SelfTest
{
    private static void TestFretboardConnector()
    {
        VisualNote V(VisualRole r, int s, int f, double onset = 0, bool released = false) =>
            new() { Role = r, StringIndex = s, Fret = f, OnsetMs = onset, Released = released, Held = r == VisualRole.Current && !released };
        var chordA = new[] { (4, 3), (5, 1) };
        List<VisualNote> Sounding(params (int s, int f)[] c) => c.Select(p => V(VisualRole.Current, p.s, p.f)).ToList();
        List<VisualNote> Next(params (int s, int f)[] c) => c.Select(p => V(VisualRole.Next, p.s, p.f, 500)).ToList();

        Check("repeated chord: no line", FretboardConnector.Between(Sounding(chordA).Concat(Next(chordA)).ToList()) is null);
        // rest between repeats: the visualizer shows the last struck note as released, the rest of its chord as past
        var gap = new List<VisualNote> { V(VisualRole.Current, 5, 1, 0, released: true), V(VisualRole.Past, 4, 3, 0), V(VisualRole.Past, 5, 1, 0) };
        Check("rest gap before the same chord: no line", FretboardConnector.Between(gap.Concat(Next(chordA)).ToList()) is null);
        Check("one-tick stale next (still the old chord): no line", FretboardConnector.Between(Sounding(chordA).Concat(Next((5, 1))).ToList()) is null);
        var change = FretboardConnector.Between(Sounding((5, 1)).Concat(Next((5, 3))).ToList());
        Check("real change 1 -> 3: line", change is { } c1 && c1.From[0] == (5, 1) && c1.To[0] == (5, 3));
        var partial = FretboardConnector.Between(Sounding((4, 3), (5, 1)).Concat(Next((4, 3), (5, 3))).ToList());
        Check("partial overlap: only changed notes", partial is { } c2 && c2.From.SequenceEqual(new[] { (5, 1) }) && c2.To.SequenceEqual(new[] { (5, 3) }));
        Check("loop wrap, nothing sounding yet: no line", FretboardConnector.Between(Next(chordA)) is null);
        Check("slide to another fret still draws", FretboardConnector.Between(Sounding((3, 5)).Concat(Next((3, 7))).ToList()) is not null);

        var rng = new Random(7);
        var bad = 0;
        for (var i = 0; i < 5000; i++)
        {
            var now = Enumerable.Range(0, rng.Next(0, 4)).Select(_ => (rng.Next(0, 3), rng.Next(0, 3))).ToArray();
            var nxt = Enumerable.Range(0, rng.Next(0, 4)).Select(_ => (rng.Next(0, 3), rng.Next(0, 3))).ToArray();
            var notes = Sounding(now).Concat(Next(nxt)).OrderBy(_ => rng.Next()).ToList();
            if (FretboardConnector.Between(notes) is { } m && (m.To.Any(p => now.Contains(p)) || m.From.Any(p => nxt.Contains(p) && now.Except(nxt).Any()) || m.To.Count == 0)) bad++;
        }
        Check($"random sequences never join identical positions ({bad} bad)", bad == 0);
    }
}
