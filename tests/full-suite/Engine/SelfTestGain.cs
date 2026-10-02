using TabForge.Audio.Contracts;

namespace TabForge;

public static partial class SelfTest
{
    /// <summary>
    /// <see cref="Gain"/> gives bit-identical results to every inline dB expression it replaced, over a sweep that includes
    /// -infinity, +infinity, NaN, zero and the clamp edges the call sites use.
    /// </summary>
    private static void TestGainBitIdentical()
    {
        var dbs = new List<double> { double.NegativeInfinity, double.PositiveInfinity, double.NaN, 0, -0.0, -59.9, -60, -60.0001, 12, 12.0001, -96, -90, 6, -6, 3.999843853973347, 1e-300, -1e-300, 1e300, -1e300 };
        for (var d = -200.0; d <= 80; d += 0.0371) dbs.Add(d);
        long bad = 0, compared = 0;
        string first = "";
        void Same(double expected, double actual, string site, double input)
        {
            compared++;
            if (BitConverter.DoubleToInt64Bits(expected) == BitConverter.DoubleToInt64Bits(actual)) return;
            if (bad++ == 0) first = $"{site}({input:R}): {expected:R} vs {actual:R}";
        }
        void SameF(float expected, float actual, string site, double input)
        {
            compared++;
            if (BitConverter.SingleToInt32Bits(expected) == BitConverter.SingleToInt32Bits(actual)) return;
            if (bad++ == 0) first = $"{site}({input:R}): {expected:R} vs {actual:R}";
        }
        foreach (var db in dbs)
        {
            var f = (float)db;
            // float: the engine's Windows path offset and endpoint gain, the tone amplitude.
            SameF(MathF.Pow(10, f / 20), Gain.FromDb(f), "float FromDb", f);
            SameF(MathF.Pow(10, Math.Clamp(f, -96f, 0f) / 20), Gain.FromDb(Math.Clamp(f, -96f, 0f)), "endpoint", f);
            // double: clip gain, plug-in output gain (with its -59.9 floor), DbToLin, silence floor, synth master, shelf, timeline gain, clip velocity.
            SameF((float)Math.Pow(10, db / 20), (float)Gain.FromDb(db), "float(double FromDb)", db);
            SameF(db <= -59.9 ? 0f : (float)Math.Pow(10, db / 20), db <= -59.9 ? 0f : (float)Gain.FromDb(db), "output gain", db);
            SameF(db <= -59.9 ? 0f : (float)Math.Pow(10, Math.Clamp(db, -60, 12) / 20), db <= -59.9 ? 0f : (float)Gain.FromDb(Math.Clamp(db, -60, 12)), "clamped gain", db);
            Same(Math.Pow(10, db / 20), Gain.FromDb(db), "double FromDb", db);
            Same(Math.Atan(Math.Pow(10, db / 20)) / (Math.PI / 2) - 0.5, Math.Atan(Gain.FromDb(db)) / (Math.PI / 2) - 0.5, "pan", db);
            Same(1 * Math.Pow(10, (db != 0 ? db : 0) / 20), 1 * Gain.FromDb(db != 0 ? db : 0), "master", db);
            var v = db; // the linear value: dB-looking sweep reused as linear amplitude, plus floors
            foreach (var lin in new[] { v, Math.Abs(v), Math.Abs(v) / 100 })
            {
                Same(20 * Math.Log10(lin), Gain.ToDb(lin), "ToDb", lin);
                Same(20 * Math.Log10(Math.Max(lin, 1e-6)), Gain.ToDb(Math.Max(lin, 1e-6)), "ToDb floor 1e-6", lin);
                Same(20 * Math.Log10(Math.Max(lin, 1e-12)), Gain.ToDb(Math.Max(lin, 1e-12)), "ToDb floor 1e-12", lin);
                var peak = (float)lin;
                Same(20 * Math.Log10(Math.Max(peak, 1e-6f)), Gain.ToDb(Math.Max(peak, 1e-6f)), "ToDb float peak", lin);   // float argument, widened
                Same(20 * Math.Log10(Math.Tan(lin)) * 1.0, Gain.ToDb(Math.Tan(lin)) * 1.0, "pan db", lin);
                Same(Math.Clamp((20 * Math.Log10(lin) + 60) / 66, 0, 1), Math.Clamp((Gain.ToDb(lin) + 60) / 66, 0, 1), "meter", lin);
            }
        }
        const float toneDb = -50f; const double silenceDb = -90, shelf = 3.999843853973347;
        SameF(MathF.Pow(10, toneDb / 20), Gain.FromDb(toneDb), "tone", toneDb);
        SameF((float)Math.Pow(10, silenceDb / 20), (float)Gain.FromDb(silenceDb), "silence floor", silenceDb);
        Same(Math.Pow(10, shelf / 20), Gain.FromDb(shelf), "shelf", shelf);
        Check($"Gain: FromDb / ToDb are bit-identical to the inline expressions they replaced over a dB sweep with -inf, +inf, NaN, 0 and the clamp edges ({compared} comparisons)",
            bad == 0 && compared > 100_000, first);
        Check("Gain: FromDb(-inf) is 0, FromDb(NaN) is NaN, ToDb(0) is -inf",
            Gain.FromDb(double.NegativeInfinity) == 0 && double.IsNaN(Gain.FromDb(double.NaN)) && double.IsNegativeInfinity(Gain.ToDb(0)));
    }
}
