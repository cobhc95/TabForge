namespace TabForge.AudioEngine.Builtin.IrLoader;

// Owns: turning decoded IR files into the two channels the convolver runs: which file and channel feeds Left and Right (one file, or separate Left / Right
//   files), resampling to the session rate (windowed sinc), "Trim silence" (leading and trailing) and "Auto level" (the loudest frequency passes at 0 dB).
// Does not own: reading files (IrWaveFile), the editor's pictures (IrPictures), the convolution (PartitionedConvolver), when this runs (IrLoaderEffect, on a worker).
// Tests: BuiltinEffectTests.

/// <summary>The two channels to convolve with (Left and Right may be the same array) and the gain Auto level applied.</summary>
public sealed record PreparedIr(float[] Left, float[] Right, int SampleRate, double AutoGainDb);

public static class IrPrep
{
    /// <summary>Leading samples quieter than this (dB below the peak) are trimmed; trailing ones quieter than <see cref="TailFloorDb"/>.</summary>
    public const double OnsetFloorDb = -60, TailFloorDb = -90;
    private const int SincSideTaps = 24, TableSteps = 512;

    /// <summary>
    /// The channels for the convolver: one <paramref name="main"/> file (mono feeds both sides, stereo feeds left to left and right to right) or, with
    /// <paramref name="left"/> and / or <paramref name="right"/>, the first channel of each (a missing side uses the other). One source must be given.
    /// </summary>
    public static PreparedIr Prepare(IrAudio? main, IrAudio? left, IrAudio? right, int sampleRate, bool autoLevel, bool trimSilence)
    {
        float[] l, r;
        if (left is not null || right is not null)
        {
            var first = (left ?? right)!;
            l = Resample(first.Channels[0], first.SampleRate, sampleRate);
            r = left is not null && right is not null ? Resample(right.Channels[0], right.SampleRate, sampleRate) : l;
        }
        else
        {
            var source = main ?? throw new ArgumentNullException(nameof(main));
            l = Resample(source.Channels[0], source.SampleRate, sampleRate);
            r = source.Channels.Length > 1 ? Resample(source.Channels[1], source.SampleRate, sampleRate) : l;
        }
        if (trimSilence) (l, r) = Trim(l, r);
        var gainDb = 0.0;
        if (autoLevel)
        {
            var peak = Math.Max(IrPictures.PeakResponse(l), ReferenceEquals(l, r) ? 0 : IrPictures.PeakResponse(r));
            if (peak > 1e-9)
            {
                var gain = (float)(1 / peak);
                gainDb = 20 * Math.Log10(gain);
                var same = ReferenceEquals(l, r);
                l = Scaled(l, gain);
                r = same ? l : Scaled(r, gain);
            }
        }
        return new PreparedIr(l, r, sampleRate, gainDb);
    }

    /// <summary>Windowed-sinc resampling (Blackman window, cut-off just under the lower Nyquist). The same rate returns the input itself.</summary>
    public static float[] Resample(float[] input, int from, int to)
    {
        if (from == to || input.Length == 0) return input;
        var ratio = (double)to / from;
        var cutoff = Math.Min(1.0, ratio) * 0.97;
        var halfWidth = (int)Math.Ceiling(SincSideTaps / Math.Min(1.0, ratio));
        var table = new double[halfWidth * TableSteps + 2];
        for (var i = 0; i < table.Length; i++)
        {
            var u = (double)i / TableSteps;
            var x = Math.PI * cutoff * u;
            var sinc = u == 0 ? 1 : Math.Sin(x) / x;
            var w = u / halfWidth;
            table[i] = w >= 1 ? 0 : cutoff * sinc * (0.42 + 0.5 * Math.Cos(Math.PI * w) + 0.08 * Math.Cos(2 * Math.PI * w));
        }
        var output = new float[(int)Math.Ceiling(input.Length * ratio)];
        for (var n = 0; n < output.Length; n++)
        {
            var t = n / ratio;
            var centre = (int)Math.Floor(t);
            var sum = 0.0;
            for (var k = Math.Max(0, centre - halfWidth + 1); k <= Math.Min(input.Length - 1, centre + halfWidth); k++)
            {
                var pos = Math.Abs(t - k) * TableSteps;
                var i = (int)pos;
                if (i + 1 >= table.Length) continue;
                sum += input[k] * (table[i] + (table[i + 1] - table[i]) * (pos - i));
            }
            output[n] = (float)sum;
        }
        return output;
    }

    /// <summary>Cuts the silence before the onset and the ring-out below the floor, the same on both channels so they stay aligned.</summary>
    public static (float[] Left, float[] Right) Trim(float[] left, float[] right)
    {
        var peak = Math.Max(Peak(left), Peak(right));
        if (peak <= 0) return (left, right);
        var onset = (float)(peak * Math.Pow(10, OnsetFloorDb / 20));
        var floor = (float)(peak * Math.Pow(10, TailFloorDb / 20));
        var length = Math.Max(left.Length, right.Length);
        var start = 0;
        while (start < length - 1 && Abs(left, start) < onset && Abs(right, start) < onset) start++;
        var end = length;
        while (end > start + 1 && Abs(left, end - 1) < floor && Abs(right, end - 1) < floor) end--;
        if (start == 0 && end == length) return (left, right);
        var l = Slice(left, start, end);
        return (l, ReferenceEquals(left, right) ? l : Slice(right, start, end));
    }

    private static float[] Slice(float[] x, int start, int end) => start >= x.Length ? new float[1] : x[start..Math.Min(end, x.Length)];
    private static float Peak(float[] x) { var p = 0f; foreach (var v in x) p = Math.Max(p, Math.Abs(v)); return p; }
    private static float Abs(float[] x, int i) => i < x.Length ? Math.Abs(x[i]) : 0f;
    private static float[] Scaled(float[] x, float gain) { var y = new float[x.Length]; for (var i = 0; i < x.Length; i++) y[i] = x[i] * gain; return y; }
}
