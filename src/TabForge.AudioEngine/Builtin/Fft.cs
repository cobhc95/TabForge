namespace TabForge.AudioEngine.Builtin;

// Owns: an in-place radix-2 complex FFT of one power-of-two size, with its twiddle and bit-reverse tables made once (the built-in effects' convolution
//   and the IR Loader's frequency-response curve). No allocation after construction, so the audio thread may call Forward / Inverse.
// Does not own: windowing, partitioning or spectra (PartitionedConvolver, IrPrep).
// Tests: BuiltinEffectTests.

/// <summary>Complex FFT of a fixed power-of-two size on separate real and imaginary arrays. <see cref="Inverse"/> scales by 1/N.</summary>
public sealed class Fft
{
    private readonly float[] _cos, _sin;
    private readonly int[] _reverse;

    public int Size { get; }

    public Fft(int size)
    {
        if (size < 2 || (size & (size - 1)) != 0) throw new ArgumentOutOfRangeException(nameof(size), "The FFT size must be a power of two.");
        Size = size;
        _cos = new float[size / 2];
        _sin = new float[size / 2];
        for (var i = 0; i < size / 2; i++)
        {
            var angle = -2.0 * Math.PI * i / size;
            _cos[i] = (float)Math.Cos(angle);
            _sin[i] = (float)Math.Sin(angle);
        }
        _reverse = new int[size];
        var bits = System.Numerics.BitOperations.Log2((uint)size);
        for (var i = 0; i < size; i++)
        {
            var r = 0;
            for (var b = 0; b < bits; b++) r |= ((i >> b) & 1) << (bits - 1 - b);
            _reverse[i] = r;
        }
    }

    public void Forward(float[] re, float[] im) => Transform(re, im, false);

    /// <summary>The inverse transform, scaled by 1/N (Forward then Inverse gives the input back).</summary>
    public void Inverse(float[] re, float[] im)
    {
        Transform(re, im, true);
        var scale = 1f / Size;
        for (var i = 0; i < Size; i++) { re[i] *= scale; im[i] *= scale; }
    }

    private void Transform(float[] re, float[] im, bool inverse)
    {
        var n = Size;
        for (var i = 0; i < n; i++)
        {
            var j = _reverse[i];
            if (j <= i) continue;
            (re[i], re[j]) = (re[j], re[i]);
            (im[i], im[j]) = (im[j], im[i]);
        }
        var sign = inverse ? -1f : 1f;
        for (var len = 2; len <= n; len <<= 1)
        {
            var half = len >> 1;
            var step = n / len;
            for (var start = 0; start < n; start += len)
            {
                for (var k = 0; k < half; k++)
                {
                    var wr = _cos[k * step];
                    var wi = sign * _sin[k * step];
                    var a = start + k;
                    var b = a + half;
                    var tr = re[b] * wr - im[b] * wi;
                    var ti = re[b] * wi + im[b] * wr;
                    re[b] = re[a] - tr;
                    im[b] = im[a] - ti;
                    re[a] += tr;
                    im[a] += ti;
                }
            }
        }
    }
}
