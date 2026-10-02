using TabForge.Audio.Contracts;
namespace TabForge.Diagnostics;

/// <summary>Signal maths for the audio audit (offline, allocation is fine here): FFT, spectral-flux onsets, K-weighted loudness, statistics.</summary>
internal static class AudioAuditDsp
{
    public static double Db(double linear) => linear <= 1e-6 ? -120 : Math.Max(-120, Gain.ToDb(linear));

    public static double Percentile(IReadOnlyList<double> sortedAscending, double p)
    {
        if (sortedAscending.Count == 0) return 0;
        var pos = Math.Clamp(p, 0, 1) * (sortedAscending.Count - 1);
        var lo = (int)Math.Floor(pos); var hi = (int)Math.Ceiling(pos);
        return sortedAscending[lo] + (sortedAscending[hi] - sortedAscending[lo]) * (pos - lo);
    }

    public static double StdDev(IReadOnlyList<double> values)
    {
        if (values.Count < 2) return 0;
        var mean = values.Average();
        return Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / (values.Count - 1));
    }

    /// <summary>In-place radix-2 FFT (size = power of two).</summary>
    private sealed class Fft
    {
        private readonly int _n; private readonly int[] _rev; private readonly double[] _cos, _sin;
        public Fft(int n)
        {
            _n = n; _rev = new int[n]; _cos = new double[n / 2]; _sin = new double[n / 2];
            var bits = (int)Math.Log2(n);
            for (var i = 0; i < n; i++) { var r = 0; for (var b = 0; b < bits; b++) if ((i & (1 << b)) != 0) r |= 1 << (bits - 1 - b); _rev[i] = r; }
            for (var i = 0; i < n / 2; i++) { _cos[i] = Math.Cos(2 * Math.PI * i / n); _sin[i] = -Math.Sin(2 * Math.PI * i / n); }
        }
        public void Transform(double[] re, double[] im)
        {
            for (var i = 0; i < _n; i++) { var j = _rev[i]; if (j > i) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); } }
            for (var size = 2; size <= _n; size <<= 1)
            {
                var half = size / 2; var step = _n / size;
                for (var start = 0; start < _n; start += size)
                    for (var k = 0; k < half; k++)
                    {
                        double wr = _cos[k * step], wi = _sin[k * step];
                        int a = start + k, b = a + half;
                        var tr = re[b] * wr - im[b] * wi; var ti = re[b] * wi + im[b] * wr;
                        re[b] = re[a] - tr; im[b] = im[a] - ti; re[a] += tr; im[a] += ti;
                    }
            }
        }
    }

    public const int FrameSize = 1024;
    public const int Hop = 128;

    /// <summary>
    /// Note onsets (seconds) by log-magnitude spectral flux with a neighbour-max reference frame, adaptive threshold and a time-domain refinement
    /// (the steepest 1 ms rise of the envelope near the flux peak). <paramref name="slow"/>: lower thresholds and smoothed flux for slow attacks.
    /// </summary>
    public static List<double> DetectOnsets(float[] mono, int rate, bool slow)
    {
        var onsets = new List<double>();
        if (mono.Length < FrameSize * 2) return onsets;
        var fft = new Fft(FrameSize);
        var window = new double[FrameSize];
        for (var i = 0; i < FrameSize; i++) window[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / FrameSize);
        const int k0 = 2, k1 = 257;
        var frames = (mono.Length - FrameSize) / Hop + 1;
        var flux = new float[frames];
        var prev = new float[k1 + 2]; var cur = new float[k1 + 2];
        var re = new double[FrameSize]; var im = new double[FrameSize];
        for (var t = 0; t < frames; t++)
        {
            var at = t * Hop;
            for (var i = 0; i < FrameSize; i++) { re[i] = mono[at + i] * window[i]; im[i] = 0; }
            fft.Transform(re, im);
            for (var k = k0; k <= k1; k++) cur[k] = (float)Math.Log(1 + 2000 * Math.Sqrt(re[k] * re[k] + im[k] * im[k]) / FrameSize * 4);
            if (t > 0)
            {
                double sum = 0;
                for (var k = k0; k <= k1; k++)
                {
                    var reference = Math.Max(prev[k], Math.Max(k > k0 ? prev[k - 1] : 0, k < k1 ? prev[k + 1] : 0));
                    var d = cur[k] - reference; if (d > 0) sum += d;
                }
                flux[t] = (float)sum;
            }
            (prev, cur) = (cur, prev);
        }
        if (slow) // smooth over 5 frames so a gradual swell forms one broad peak
        {
            var smoothed = new float[frames];
            for (var t = 0; t < frames; t++) { double s = 0; var n = 0; for (var d = -8; d <= 8; d++) { var u = t + d; if (u >= 0 && u < frames) { s += flux[u]; n++; } } smoothed[t] = (float)(s / n); }
            flux = smoothed;
        }
        // Local mean (+-0.3 s) through a prefix sum.
        var prefix = new double[frames + 1];
        for (var t = 0; t < frames; t++) prefix[t + 1] = prefix[t] + flux[t];
        var meanWin = (int)(0.3 * rate / Hop);
        var peakWin = slow ? 24 : 5;                      // +-13 ms (sharp) / +-65 ms (slow)
        var minDelta = slow ? 2.0 : 6.0; var relative = slow ? 1.1 : 1.3;
        var minGap = (slow ? 0.12 : 0.025) * rate / Hop;
        var candidates = new List<int>();
        for (var t = 1; t < frames - 1; t++)
        {
            var f = flux[t];
            if (f < minDelta) continue;
            int lo = Math.Max(0, t - meanWin), hi = Math.Min(frames, t + meanWin + 1);
            var mean = (prefix[hi] - prefix[lo]) / (hi - lo);
            if (f < mean * relative + minDelta * 0.5) continue;
            var isPeak = true;
            for (var d = -peakWin; d <= peakWin && isPeak; d++) { var u = t + d; if (u >= 0 && u < frames && u != t && (flux[u] > f || (flux[u] == f && u < t))) isPeak = false; }
            if (!isPeak) continue;
            if (candidates.Count > 0 && t - candidates[^1] < minGap) { if (flux[t] > flux[candidates[^1]]) candidates[^1] = t; continue; }
            candidates.Add(t);
        }
        // 1 ms envelope for the refinement.
        var block = Math.Max(1, rate / 1000);
        var envLen = mono.Length / block;
        var env = new float[envLen];
        for (var b = 0; b < envLen; b++) { var m = 0f; var o = b * block; for (var i = 0; i < block; i++) m = Math.Max(m, Math.Abs(mono[o + i])); env[b] = m; }
        foreach (var t in candidates)
        {
            if (slow) { onsets.Add((t * Hop + FrameSize / 2.0) / rate); continue; }
            var a = t * Hop;
            int r0 = Math.Max(1, (a + (int)(0.25 * FrameSize)) / block), r1 = Math.Min(envLen - 1, (a + FrameSize + Hop) / block);
            var best = -1.0; var bestB = (a + FrameSize / 2) / block;
            for (var b = r0; b <= r1; b++)
            {
                var rise = Math.Log(env[b] + 1e-5) - Math.Log(env[b - 1] + 1e-5);
                if (rise > best) { best = rise; bestB = b; }
            }
            onsets.Add(bestB * block / (double)rate);
        }
        return onsets;
    }

    /// <summary>ITU-R BS.1770 K-weighting (shelf + high-pass) applied in place.</summary>
    public static void KWeight(float[] x, int rate)
    {
        Biquad(x, ShelfCoeffs(rate)); Biquad(x, HighPassCoeffs(rate));
    }

    private static (double b0, double b1, double b2, double a1, double a2) ShelfCoeffs(int fs)
    {
        const double f0 = 1681.974450955533, g = 3.999843853973347, q = 0.7071752369554196;
        var k = Math.Tan(Math.PI * f0 / fs); var vh = Gain.FromDb(g); var vb = Math.Pow(vh, 0.4996667741545416);
        var a0 = 1 + k / q + k * k;
        return ((vh + vb * k / q + k * k) / a0, 2 * (k * k - vh) / a0, (vh - vb * k / q + k * k) / a0, 2 * (k * k - 1) / a0, (1 - k / q + k * k) / a0);
    }

    private static (double b0, double b1, double b2, double a1, double a2) HighPassCoeffs(int fs)
    {
        const double f0 = 38.13547087602444, q = 0.5003270373238773;
        var k = Math.Tan(Math.PI * f0 / fs);
        var a0 = 1 + k / q + k * k;
        return (1, -2, 1, 2 * (k * k - 1) / a0, (1 - k / q + k * k) / a0);
    }

    private static void Biquad(float[] x, (double b0, double b1, double b2, double a1, double a2) c)
    {
        double z1 = 0, z2 = 0;
        for (var i = 0; i < x.Length; i++)
        {
            var input = x[i];
            var y = c.b0 * input + z1;
            z1 = c.b1 * input - c.a1 * y + z2;
            z2 = c.b2 * input - c.a2 * y;
            x[i] = (float)y;
        }
    }

    /// <summary>Mean square of <paramref name="x"/> over [from, to).</summary>
    public static double MeanSquare(float[] x, long from, long to)
    {
        from = Math.Max(0, from); to = Math.Min(x.Length, to);
        if (to <= from) return 0;
        double s = 0;
        for (var i = from; i < to; i++) s += (double)x[i] * x[i];
        return s / (to - from);
    }

    public static float PeakAbs(float[] x, long from, long to)
    {
        from = Math.Max(0, from); to = Math.Min(x.Length, to);
        var p = 0f;
        for (var i = from; i < to; i++) p = Math.Max(p, Math.Abs(x[i]));
        return p;
    }
}
