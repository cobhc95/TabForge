namespace TabForge.KeyboardMode.Hands;

// Owns: deciding which hand plays each note of a part (any instrument, as sounding pitches): a Viterbi pass over the chords in time order, where each chord's state is
//   its split point (the lowest k notes left, the rest right, so a chord never crosses), each state carries both hands' context along its best path, and a cost model
//   (HandAssignerOptions) scores span, held notes, reach over fast figures, jumps, register, crossing and line continuity. Pure and deterministic; no statics that change.
// Does not own: the notes (KeyboardNoteSource builds them), drawing or colours (the Keyboard mode view), or the fitted constants' derivation (docs/KEYBOARD_HANDS.md).
// Tests: TestKeyboardHands, TestKeyboardHandsAccuracy.
public static class HandAssigner
{
    private const long Never = -1_000_000_000_000;

    /// <summary>The hand of each note, index-aligned with <paramref name="notes"/> (any order).</summary>
    public static Hand[] Assign(IReadOnlyList<HandNote> notes) => Assign(notes, HandAssignerOptions.Default);

    public static Hand[] Assign(IReadOnlyList<HandNote> notes, HandAssignerOptions options)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(options);
        var n = notes.Count;
        var result = new Hand[n];
        if (n == 0) return result;

        // Time order, then chords: pitch order inside a chord.
        var order = new int[n];
        for (var i = 0; i < n; i++) order[i] = i;
        Array.Sort(order, (a, b) => notes[a].StartMs != notes[b].StartMs ? notes[a].StartMs.CompareTo(notes[b].StartMs) : notes[a].Pitch != notes[b].Pitch ? notes[a].Pitch.CompareTo(notes[b].Pitch) : a.CompareTo(b));
        var evFirst = new List<int>();
        for (var i = 0; i < n;)
        {
            evFirst.Add(i);
            var t0 = notes[order[i]].StartMs;
            var j = i;
            while (j < n && notes[order[j]].StartMs - t0 <= options.ChordToleranceMs) j++;
            if (j - i > 1) Array.Sort(order, i, j - i, Comparer<int>.Create((a, b) => notes[a].Pitch != notes[b].Pitch ? notes[a].Pitch.CompareTo(notes[b].Pitch) : notes[a].StartMs != notes[b].StartMs ? notes[a].StartMs.CompareTo(notes[b].StartMs) : a.CompareTo(b)));
            i = j;
        }
        var events = evFirst.Count;
        evFirst.Add(n);

        // Pitches in event order, and for each note the nearest-pitched note of the previous chord (its position in that chord) and the distance.
        var pitch = new int[n];
        var end = new long[n];
        for (var i = 0; i < n; i++) { var x = notes[order[i]]; pitch[i] = x.Pitch; end[i] = x.StartMs + Math.Max(0, x.DurationMs); }
        var nearest = new int[n];
        var nearestDist = new int[n];
        for (var e = 1; e < events; e++)
        {
            int pf = evFirst[e - 1], pl = evFirst[e];
            for (var i = evFirst[e]; i < evFirst[e + 1]; i++)
            {
                var best = pf;
                for (var j = pf + 1; j < pl; j++) if (Math.Abs(pitch[j] - pitch[i]) < Math.Abs(pitch[best] - pitch[i])) best = j;
                nearest[i] = best - pf;
                nearestDist[i] = Math.Abs(pitch[best] - pitch[i]);
            }
        }

        // States: event e has (count + 1) states, state k = the lowest k notes left; flat, offset by stateFirst[e].
        var stateFirst = new int[events + 1];
        for (var e = 0; e < events; e++) stateFirst[e + 1] = stateFirst[e] + (evFirst[e + 1] - evFirst[e]) + 1;
        var states = new State[stateFirst[events]];
        var start = new State
        {
            Left = new HandContext { Pos = options.LeftHome, LastOn = Never, End = Never, Lo = options.LeftHome, Hi = options.LeftHome, PrevOn = Never, PrevLo = options.LeftHome, PrevHi = options.LeftHome },
            Right = new HandContext { Pos = options.RightHome, LastOn = Never, End = Never, Lo = options.RightHome, Hi = options.RightHome, PrevOn = Never, PrevLo = options.RightHome, PrevHi = options.RightHome },
        };

        for (var e = 0; e < events; e++)
        {
            int first = evFirst[e], count = evFirst[e + 1] - first;
            var t = notes[order[first]].StartMs;
            var linePrev = e > 0 && t - notes[order[evFirst[e - 1]]].StartMs < options.LineWindowMs;
            int prevFirstState = e == 0 ? -1 : stateFirst[e - 1], prevStates = e == 0 ? 1 : stateFirst[e] - stateFirst[e - 1];
            for (var k = 0; k <= count; k++)
            {
                var stat = StaticCost(pitch, first, k, count, options);
                var bestCost = double.PositiveInfinity;
                var bestState = default(State);
                for (var s = 0; s < prevStates; s++)
                {
                    ref readonly var from = ref (e == 0 ? ref start : ref states[prevFirstState + s]);
                    var cost = from.Cost + stat;
                    if (cost >= bestCost) continue;
                    var left = Play(from.Left, pitch, end, first, k, t, options, ref cost);
                    var right = Play(from.Right, pitch, end, first + k, count - k, t, options, ref cost);
                    if (left.Pos > right.Pos) cost += options.CrossWeight * (left.Pos - right.Pos);
                    if (linePrev) cost += LineCost(nearest, nearestDist, first, count, k, from.Split, options);
                    if (cost < bestCost) { bestCost = cost; bestState = new State { Cost = cost, Back = s, Split = k, Left = left, Right = right }; }
                }
                states[stateFirst[e] + k] = bestState;
            }
        }

        // Back-track from the cheapest final state (the first one on a tie).
        var last = stateFirst[events - 1];
        var at = last;
        for (var s = last + 1; s < stateFirst[events]; s++) if (states[s].Cost < states[at].Cost) at = s;
        for (var e = events - 1; e >= 0; e--)
        {
            ref readonly var st = ref states[at];
            for (var i = evFirst[e]; i < evFirst[e + 1]; i++) result[order[i]] = i - evFirst[e] < st.Split ? Hand.Left : Hand.Right;
            if (e > 0) at = stateFirst[e - 1] + st.Back;
        }
        return result;
    }

    /// <summary>The costs of a split that do not depend on the path: register, chord span and notes per hand.</summary>
    private static double StaticCost(int[] pitch, int first, int k, int count, HandAssignerOptions o)
    {
        var cost = 0.0;
        for (var i = 0; i < count; i++)
        {
            var p = pitch[first + i];
            cost += i < k
                ? o.RegisterWeight * Math.Max(0, p - o.LeftTop) / 12.0 + o.RegisterSlope * (p - 60) / 12.0
                : o.RegisterWeight * Math.Max(0, o.RightBottom - p) / 12.0 - o.RegisterSlope * (p - 60) / 12.0;
        }
        cost += ChordCost(k, k > 0 ? pitch[first + k - 1] - pitch[first] : 0, o);
        cost += ChordCost(count - k, count > k ? pitch[first + count - 1] - pitch[first + k] : 0, o);
        return cost;
    }

    private static double ChordCost(int notes, int span, HandAssignerOptions o)
    {
        if (notes == 0) return 0;
        var cost = notes > o.MaxNotesPerHand ? 100.0 * (notes - o.MaxNotesPerHand) : 0;
        var over = span - o.ChordSpan;
        return over > 0 ? cost + o.ChordSpanWeight * over * over + o.ChordStretchWeight * over : cost;
    }

    /// <summary>The hand after playing <paramref name="count"/> notes from <paramref name="first"/> at <paramref name="t"/> (unchanged when it plays none), adding the path costs.</summary>
    private static HandContext Play(in HandContext h, int[] pitch, long[] end, int first, int count, long t, HandAssignerOptions o, ref double cost)
    {
        if (count == 0) return h;
        int lo = pitch[first], hi = pitch[first + count - 1];
        if (h.End > t + o.ChordToleranceMs)
        {
            var held = Math.Max(hi, h.Hi) - Math.Min(lo, h.Lo) - o.HeldSpan;
            if (held > 0) cost += o.HeldWeight * held * held;
        }
        var centre = (lo + hi) / 2.0;
        var move = Math.Abs(centre - h.Pos) - o.MoveFree;
        if (move > 0) cost += o.MoveWeight * move * o.MoveTauMs / (o.MoveTauMs + (t - h.LastOn));
        int rlo = lo, rhi = hi;
        if (t - h.LastOn < o.RecentMs) { rlo = Math.Min(rlo, h.Lo); rhi = Math.Max(rhi, h.Hi); }
        if (t - h.PrevOn < o.RecentMs) { rlo = Math.Min(rlo, h.PrevLo); rhi = Math.Max(rhi, h.PrevHi); }
        if (rhi - rlo > o.RecentSpan) cost += o.RecentWeight * (rhi - rlo - o.RecentSpan);
        var last = end[first];
        for (var i = first + 1; i < first + count; i++) last = Math.Max(last, end[i]);
        return new HandContext { Pos = centre, LastOn = t, End = last, Lo = lo, Hi = hi, PrevOn = h.LastOn, PrevLo = h.Lo, PrevHi = h.Hi };
    }

    /// <summary>Notes close in pitch to the previous chord's nearest note but in the other hand break a line.</summary>
    private static double LineCost(int[] nearest, int[] nearestDist, int first, int count, int k, int prevSplit, HandAssignerOptions o)
    {
        var cost = 0.0;
        for (var i = 0; i < count; i++)
        {
            var d = nearestDist[first + i];
            if (d > o.LineMax || (i < k) == (nearest[first + i] < prevSplit)) continue;
            cost += o.LineWeight * (o.LineMax + 1 - d) / (o.LineMax + 1);
        }
        return cost;
    }

    private struct HandContext
    {
        public double Pos;
        public long LastOn, End, PrevOn;
        public int Lo, Hi, PrevLo, PrevHi;
    }

    private struct State
    {
        public double Cost;
        public int Back, Split;
        public HandContext Left, Right;
    }
}
