namespace TabForge.AudioEngine.Builtin.IrLoader;

// Owns: what the IR Loader editor draws of an impulse response, computed once per file: the waveform peaks, the frequency-response curve on a log scale,
//   and the loudest frequency's magnitude (Auto level's measure).
// Does not own: the drawing (the editor window), preparing the IR for the convolver (IrPrep).
// Tests: BuiltinEffectTests.
public static class IrPictures
{
    public const double LowHz = 20, HighHz = 20000;

    /// <summary>The largest magnitude of the IR's frequency response (1 = 0 dB).</summary>
    public static double PeakResponse(float[] ir)
    {
        var (re, im) = Spectrum(ir);
        var peak = 0.0;
        for (var i = 0; i <= re.Length / 2; i++) peak = Math.Max(peak, Math.Sqrt(re[i] * (double)re[i] + im[i] * (double)im[i]));
        return peak;
    }

    /// <summary>
    /// The response shape in dB at <paramref name="points"/> frequencies spaced evenly on a log scale from <see cref="LowHz"/> to <see cref="HighHz"/> (or Nyquist):
    /// each point is the average power over a 1/6-octave band (so a long IR does not draw as noise), relative to the loudest point (0 dB).
    /// </summary>
    public static float[] ResponseCurve(float[] ir, int sampleRate, int points)
    {
        var curve = new float[Math.Max(points, 0)];
        if (ir.Length == 0 || points < 2) return curve;
        var (re, im) = Spectrum(ir);
        var half = re.Length / 2;
        var sums = new double[half + 2];   // running sum of power per bin, for band averages in one step
        for (var i = 0; i <= half; i++) sums[i + 1] = sums[i] + re[i] * (double)re[i] + im[i] * (double)im[i];
        var top = Math.Min(HighHz, sampleRate / 2.0);
        var band = Math.Pow(2, 1.0 / 12);
        var loudest = double.MinValue;
        for (var p = 0; p < points; p++)
        {
            var hz = LowHz * Math.Pow(top / LowHz, (double)p / (points - 1));
            var from = Math.Clamp((int)Math.Floor(hz / band / sampleRate * re.Length), 0, half);
            var to = Math.Clamp((int)Math.Ceiling(hz * band / sampleRate * re.Length), from, half);
            var power = (sums[to + 1] - sums[from]) / (to - from + 1);
            var db = 10 * Math.Log10(Math.Max(power, 1e-12));
            curve[p] = (float)db;
            loudest = Math.Max(loudest, db);
        }
        for (var p = 0; p < points; p++) curve[p] -= (float)loudest;
        return curve;
    }

    /// <summary>The waveform: the largest absolute sample in each of <paramref name="points"/> equal parts.</summary>
    public static float[] Peaks(float[] ir, int points)
    {
        var peaks = new float[Math.Max(points, 0)];
        if (ir.Length == 0) return peaks;
        for (var p = 0; p < points; p++)
        {
            var from = (int)((long)p * ir.Length / points);
            var to = Math.Max(from + 1, (int)((long)(p + 1) * ir.Length / points));
            for (var i = from; i < to && i < ir.Length; i++) peaks[p] = Math.Max(peaks[p], Math.Abs(ir[i]));
        }
        return peaks;
    }

    private static (float[] Re, float[] Im) Spectrum(float[] ir)
    {
        var size = 4096;   // a short IR still gets a smooth curve
        while (size < ir.Length && size < 1 << 20) size <<= 1;
        var re = new float[size];
        var im = new float[size];
        Array.Copy(ir, re, Math.Min(ir.Length, size));
        new Fft(size).Forward(re, im);
        return (re, im);
    }
}
