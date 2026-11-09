using static TabForge.Sf2Synth.Humanize.HumanTable;

namespace TabForge.Sf2Synth.Humanize;

/// <summary>One stroke (a note or the notes struck together) in its surroundings, as the level and timing parts read it.</summary>
internal struct Ctx
{
    public double IoiPrev, IoiNext, Npb, Vf, Tf;
    public int Count, Key, PrevKey, NextKey, Prev2Key, String, PrevString, Sub, Group;
    public bool Single, Fast, Down, Legato, Chug, Soft;
    public uint Motif;
    public int MotifIndex;
}

// Owns: the humanizer as one pass over a track's WRITTEN notes (no streaming, no look-ahead): strokes, the window around each bar
//   (Evidence.Window) for energy and evidence, the motif of each beat (hash of its rhythm and intervals), neighbours,
//   fast runs, pick direction; then each stroke's level (Dynamics, under a headroom so accents fit below velocity 127), onset (Timing)
//   and strum (Strum), and the per-key order check
//   (no stuck or colliding notes). Every value is a pure function of position and nearby content, so a start mid-song or an edit
//   changes only the neighbourhood.
// Does not own: the parts (Dynamics, Timing, Strum), evidence (Evidence), noise (Noise), values (HumanTable), building notes (host).
// Tests: HumanizerTests.
public static class Humanizer
{
    /// <summary>Plays <paramref name="notes"/> (one track, written; any order) into <paramref name="output"/> (same index).
    /// Untouched (copied) when off or for a program outside the families. <paramref name="parts"/>: each note's level parts, if given.</summary>
    public static void Apply(ReadOnlySpan<HumanNote> notes, Span<HumanOut> output, int program, SynthOptions o, uint seed, Span<LevelParts> parts = default)
    {
        for (var i = 0; i < notes.Length; i++) output[i] = new(notes[i].OnMs, notes[i].OffMs, notes[i].Velocity);
        if (!o.Humanize || Of(program) is not { } f || notes.Length == 0 || !o.HumanizeVelocity && !o.HumanizeTiming) return;
        var s = Styles[(int)o.Feel];
        var a = notes.ToArray();
        var strokes = Strokes(a);
        var ctx = Contexts(a, strokes, seed);
        Evidence.Window(a, strokes, ctx);
        double[] db = new double[16], ms = new double[16];
        for (var k = 0; k < strokes.Count; k++)
        {
            var c = ctx[k];
            var st = strokes[k];
            var n = a[st[0]];
            var lp = Dynamics.Parts(c, n, f, seed);
            var level = o.HumanizeVelocity ? Dynamics.Total(lp, s, f) * c.Vf : 0;
            var shift = o.HumanizeTiming ? Timing.Shift(c, n, s, f, seed, o.HumanizeDrift) * c.Tf : 0;
            var len = o.HumanizeTiming ? Timing.Length(c, n, s) * c.Tf : 0;
            var m = Math.Min(st.Count, db.Length);
            db.AsSpan(0, m).Clear();
            ms.AsSpan(0, m).Clear();
            if (m >= 3 && f.Strum > 0 && st.TrueForAll(i => a[i].OnMs == a[st[0]].OnMs))
                Strum.Spread(f, s, new(c.Down, Math.Min(c.IoiNext, 10000)), 40 * Math.Log10(st.Max(i => a[i].Velocity) / 127.0), db.AsSpan(0, m), ms.AsSpan(0, m),
                    new Rng(Hash.Of(seed, n.Bar, (int)Math.Round(n.Beat * Grid), 5)));
            for (var j = 0; j < st.Count; j++)
            {
                var i = st[j];
                var d = level + (j < m && o.HumanizeVelocity ? db[j] * c.Vf : 0) - (o.HumanizeVelocity ? Math.Max(0, HeadDb - 40 * Math.Log10(127.0 / a[i].Velocity)) * c.Vf : 0);
                var t = shift + (j < m && o.HumanizeTiming ? ms[j] * c.Tf : 0);
                output[i] = new(a[i].OnMs + t, a[i].OffMs + shift + len, Math.Clamp((int)Math.Round(a[i].Velocity * Math.Pow(10, d / 40)), 1, 127));
                if (i < parts.Length) parts[i] = lp;
            }
        }
        KeepOrder(a, output);
    }

    // Strokes in time order; within one, notes low to high.
    private static List<List<int>> Strokes(HumanNote[] a)
    {
        var order = Enumerable.Range(0, a.Length).OrderBy(i => a[i].OnMs).ThenBy(i => a[i].Key).ToArray();
        var map = new Dictionary<long, int>();
        var list = new List<List<int>>();
        foreach (var i in order)
        {
            if (!map.TryGetValue(a[i].Stroke, out var g)) { map[a[i].Stroke] = g = list.Count; list.Add([]); }
            list[g].Add(i);
        }
        foreach (var st in list) st.Sort((x, y) => a[x].Key.CompareTo(a[y].Key));
        return list;
    }

    private static Ctx[] Contexts(HumanNote[] a, List<List<int>> st, uint seed)
    {
        var n = st.Count;
        var c = new Ctx[n];
        double On(int k) => a[st[k][0]].OnMs;
        bool Has(Marks m, int k) => st[k].Exists(i => (a[i].Marks & m) != 0);
        int Near(int k, int from) => k >= 0 && k < n && st[k].Count == 1 && Math.Abs(On(k) - On(from)) < 2 * a[st[from][0]].BeatMs ? a[st[k][0]].Key : -1;
        var beatStart = 0;
        for (var k = 0; k < n; k++)
        {
            ref readonly var x = ref a[st[k][0]];
            ref var q = ref c[k];
            (q.Count, q.Key, q.String) = (st[k].Count, x.Key, x.String);
            q.IoiPrev = k > 0 ? x.OnMs - On(k - 1) : double.PositiveInfinity;
            q.IoiNext = k + 1 < n ? On(k + 1) - x.OnMs : double.PositiveInfinity;
            (q.PrevKey, q.NextKey, q.Prev2Key) = (Near(k - 1, k), Near(k + 1, k), Near(k - 2, k));
            q.PrevString = k > 0 && st[k - 1].Count == 1 && q.IoiPrev < FastMs ? a[st[k - 1][0]].String : -1;
            q.Single = q.Count == 1;
            q.Fast = q.Single && q.IoiNext < FastMs;
            q.Sub = q.IoiNext < x.BeatMs * 0.75 ? Snap(x.BeatMs / q.IoiNext) : 1;
            q.Group = q.Sub % 3 == 0 ? 3 : q.Sub >= 4 ? 4 : 2;
            (q.Legato, q.Chug, q.Soft) = (Has(Marks.Legato, k), Has(Marks.PalmMute, k), Has(Marks.Ghost | Marks.Dead, k));
            var pos = (int)Math.Round(x.Beat * q.Sub);
            q.Down = Has(Marks.Down, k) || !Has(Marks.Up, k) && (q.Sub < 2 || q.Chug && q.Sub == 2 || pos % 2 == 0);
            // Motif: the beat's content (rhythm, intervals to its first note, chord size, palm mute) keys its accent memory.
            if (k == 0 || x.Bar != a[st[k - 1][0]].Bar || (int)Math.Floor(x.Beat + 1e-6) != (int)Math.Floor(a[st[k - 1][0]].Beat + 1e-6)) beatStart = k;
            q.MotifIndex = k - beatStart;
        }
        for (var k = 0; k < n;)
        {
            var e = k + 1;
            while (e < n && c[e].MotifIndex > 0) e++;
            var h = Hash.Of(seed, 0x51);
            for (var j = k; j < e; j++)
            {
                ref readonly var x = ref a[st[j][0]];
                h = Hash.Of(h, (int)Math.Round((x.Beat - Math.Floor(x.Beat + 1e-6)) * Grid), x.Key - a[st[k][0]].Key, c[j].Count * 2 + (c[j].Chug ? 1 : 0));
            }
            for (var j = k; j < e; j++) c[j].Motif = h;
            k = e;
        }
        return c;
    }

    private static int Snap(double sub) => sub switch { < 1.5 => 1, < 2.5 => 2, < 3.5 => 3, < 5 => 4, < 7 => 6, _ => 8 };

    // Per key: every note keeps its note-off after its note-on (at least the shorter of its length and MinKeepMs) and ends before the
    // same key starts again (the written gap kept, at most RetrigMs); onsets stay in order and never before 0.
    private static void KeepOrder(HumanNote[] a, Span<HumanOut> o)
    {
        foreach (var key in Enumerable.Range(0, a.Length).GroupBy(i => a[i].Key))
        {
            var seq = key.OrderBy(i => a[i].OnMs).ToArray();
            for (var j = 0; j < seq.Length; j++)
            {
                int i = seq[j];
                var on = Math.Max(0, j > 0 ? Math.Max(o[i].OnMs, o[seq[j - 1]].OnMs + 1) : o[i].OnMs);
                var off = Math.Max(o[i].OffMs, on + Math.Min(MinKeepMs, a[i].OffMs - a[i].OnMs));
                o[i] = o[i] with { OnMs = on, OffMs = off };
                if (j == 0) continue;
                var p = seq[j - 1];
                var gap = Math.Clamp(a[i].OnMs - a[p].OffMs, 0, RetrigMs);
                o[p] = o[p] with { OffMs = Math.Max(o[p].OnMs + 1, Math.Min(o[p].OffMs, on - gap)) };
            }
        }
    }
}
