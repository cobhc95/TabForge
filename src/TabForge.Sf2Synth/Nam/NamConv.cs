using System.Numerics;
using System.Runtime.InteropServices;

namespace TabForge.Sf2Synth.Nam;

// Owns: one causal dilated 1-D convolution (In -> Out channels, K taps, dilation D, optional bias) over blocks of up to Max frames.
//   Blocks are channel rows of stride Max; the input history is a linear buffer per channel, moved back only when full; four
//   output rows accumulate in registers per input load. File weight order: out, in, tap, then bias; tap K-1 is the current frame.
// Does not own: the layers around it (NamWaveNet).
// Tests: NamTests.
internal sealed class NamConv
{
    public const int Max = 128, Keep = 8 * Max;
    public readonly int In, Out;
    private readonly int _k, _d, _hist, _cap;
    private readonly float[] _w, _b, _buf;   // _w[(o*In + j)*K + k], rows padded to a multiple of 4; _buf row j = history of input j
    private readonly int[] _off;   // per (j, k): where this block's frame 0 sits in the source
    private int _len;

    public NamConv(int inCh, int outCh, int k = 1, int d = 1, bool bias = false)
    {
        (In, Out, _k, _d, _hist) = (inCh, outCh, k, d, d * (k - 1));
        _w = new float[k * inCh * ((outCh + 3) / 4 * 4)];
        _off = new int[k * inCh];
        _b = new float[bias ? outCh : 0];
        _cap = k > 1 ? _hist + Keep : 0;
        _buf = new float[inCh * _cap];
        _len = _hist;
    }

    public int Load(float[] w, int at)
    {
        if (at + _k * In * Out + _b.Length > w.Length) throw new FormatException("too few weights");
        var size = _k * In * Out;
        Array.Copy(w, at, _w, 0, size);
        Array.Copy(w, at + size, _b, 0, _b.Length);
        return at + size + _b.Length;
    }

    public void Reset() { Array.Clear(_buf); _len = _hist; }

    // y (+)= bias + Σ_k W_k · x[t - D(K-1-k)], n frames.
    public void Run(float[] x, float[] y, int n, bool add = false)
    {
        for (var o = 0; o < Out; o++)
        {
            var row = y.AsSpan(o * Max, n);
            if (!add) row.Fill(_b.Length > 0 ? _b[o] : 0);
            else if (_b.Length > 0) Add(row, _b[o]);
        }
        if (_k == 1)
        {
            for (var j = 0; j < In; j++) _off[j] = j * Max;
            Mac(x, y, n);
            return;
        }
        if (_len + n > _cap)
        {
            for (var j = 0; j < In; j++) Array.Copy(_buf, j * _cap + _len - _hist, _buf, j * _cap, _hist);
            _len = _hist;
        }
        for (var j = 0; j < In; j++)
        {
            Array.Copy(x, j * Max, _buf, j * _cap + _len, n);
            for (var k = 0; k < _k; k++) _off[j * _k + k] = j * _cap + _len - _d * (_k - 1 - k);
        }
        Mac(_buf, y, n);
        _len += n;
    }

    // y[o][t] += Σ_i w[o][i] · src[off[i] + t], four rows per pass.
    private void Mac(float[] src, float[] y, int n)
    {
        int taps = _off.Length, vc = Vector<float>.Count, whole = n / vc * vc;
        ref var s0 = ref MemoryMarshal.GetArrayDataReference(src);
        ref var y0 = ref MemoryMarshal.GetArrayDataReference(y);
        for (var o = 0; o < Out; o += 4)
        {
            var rows = Math.Min(4, Out - o);
            for (var t = 0; t < whole; t += vc)
            {
                Vector<float> a0 = Vector.LoadUnsafe(ref y0, (nuint)(o * Max + t)), a1 = default, a2 = default, a3 = default;
                if (rows > 1) a1 = Vector.LoadUnsafe(ref y0, (nuint)((o + 1) * Max + t));
                if (rows > 2) a2 = Vector.LoadUnsafe(ref y0, (nuint)((o + 2) * Max + t));
                if (rows > 3) a3 = Vector.LoadUnsafe(ref y0, (nuint)((o + 3) * Max + t));
                for (var i = 0; i < taps; i++)
                {
                    var xv = Vector.LoadUnsafe(ref s0, (nuint)(_off[i] + t));
                    a0 += _w[o * taps + i] * xv;
                    a1 += _w[(o + 1) * taps + i] * xv;
                    a2 += _w[(o + 2) * taps + i] * xv;
                    a3 += _w[(o + 3) * taps + i] * xv;
                }
                a0.StoreUnsafe(ref y0, (nuint)(o * Max + t));
                if (rows > 1) a1.StoreUnsafe(ref y0, (nuint)((o + 1) * Max + t));
                if (rows > 2) a2.StoreUnsafe(ref y0, (nuint)((o + 2) * Max + t));
                if (rows > 3) a3.StoreUnsafe(ref y0, (nuint)((o + 3) * Max + t));
            }
            for (var r = o; r < o + rows; r++)
                for (var t = whole; t < n; t++)
                    for (var i = 0; i < taps; i++) y[r * Max + t] += _w[r * taps + i] * src[_off[i] + t];
        }
    }

    private static void Add(Span<float> y, float a)
    {
        for (var i = 0; i < y.Length; i++) y[i] += a;
    }

    public static void Axpy(Span<float> y, float a, ReadOnlySpan<float> x)
    {
        var vy = MemoryMarshal.Cast<float, Vector<float>>(y);
        var vx = MemoryMarshal.Cast<float, Vector<float>>(x);
        for (var v = 0; v < vy.Length; v++) vy[v] += a * vx[v];
        for (var i = vy.Length * Vector<float>.Count; i < y.Length; i++) y[i] += a * x[i];
    }
}
