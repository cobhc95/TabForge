namespace TabForge.Sf2Synth.Effects;

// Owns: the reverb behind the SF2 reverb send (CC91): a compact Freeverb (4 damped combs + 2 all-passes per side, Schroeder/Moorer).
// Does not own: the send levels (Voice).
// Tests: Sf2SynthTests.
internal sealed class Reverb
{
    private static readonly int[] Combs = [1116, 1188, 1277, 1356];
    private static readonly int[] Allpasses = [556, 441];
    private const int Spread = 23;
    private const float Feedback = 0.84f, Damp = 0.2f, Gain = 0.015f, Wet = 1.0f, AllpassFeedback = 0.5f;

    private sealed class Line(int length)
    {
        public readonly float[] Buf = new float[length];
        public int Pos;
        public float Store;
    }

    private readonly Line[] _cl, _cr, _al, _ar;

    public Reverb(double rate)
    {
        Line[] Make(int[] lengths, int extra) => lengths.Select(l => new Line((int)((l + extra) * rate / 44100))).ToArray();
        (_cl, _cr, _al, _ar) = (Make(Combs, 0), Make(Combs, Spread), Make(Allpasses, 0), Make(Allpasses, Spread));
    }

    public void Process(ReadOnlySpan<float> input, Span<float> left, Span<float> right)
    {
        for (var i = 0; i < input.Length; i++)
        {
            var x = input[i] * Gain;
            left[i] += Wet * Side(x, _cl, _al);
            right[i] += Wet * Side(x, _cr, _ar);
        }
    }

    private static float Side(float x, Line[] combs, Line[] allpasses)
    {
        var sum = 0f;
        foreach (var c in combs)
        {
            var y = c.Buf[c.Pos];
            c.Store = y * (1 - Damp) + c.Store * Damp;
            c.Buf[c.Pos] = x + c.Store * Feedback;
            if (++c.Pos == c.Buf.Length) c.Pos = 0;
            sum += y;
        }
        foreach (var a in allpasses)
        {
            var b = a.Buf[a.Pos];
            a.Buf[a.Pos] = sum + b * AllpassFeedback;
            if (++a.Pos == a.Buf.Length) a.Pos = 0;
            sum = b - sum;
        }
        return sum;
    }

    public void Reset()
    {
        foreach (var l in _cl.Concat(_cr).Concat(_al).Concat(_ar)) { Array.Clear(l.Buf); l.Pos = 0; l.Store = 0; }
    }
}
