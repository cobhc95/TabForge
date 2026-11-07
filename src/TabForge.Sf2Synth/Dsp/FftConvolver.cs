using System.Numerics;
using System.Runtime.InteropServices;

namespace TabForge.Sf2Synth.Dsp;

// Owns: mono convolution with an impulse response of any length: uniformly partitioned overlap-save, block 64, FFT 128 (radix-2,
//   split re/im), frequency-domain delay line, SIMD complex multiply-add. Latency one block (64 samples). No allocation in Process.
// Does not own: the impulse (Guitar/CabinetImpulse, Dsp/WavIr).
// Tests: GuitarLayerTests.
internal sealed class FftConvolver
{
    public const int Block = 64, N = 2 * Block;

    private readonly int _parts;
    private readonly float[] _hRe, _hIm, _xRe, _xIm, _re = new float[N], _im = new float[N], _accRe = new float[N], _accIm = new float[N];
    private readonly float[] _in = new float[N], _out = new float[Block], _cos = new float[N / 2], _sin = new float[N / 2];
    private readonly int[] _rev = new int[N];
    private int _fill, _slot;

    public FftConvolver(ReadOnlySpan<float> ir)
    {
        for (var i = 0; i < N / 2; i++) (_cos[i], _sin[i]) = ((float)Math.Cos(2 * Math.PI * i / N), (float)-Math.Sin(2 * Math.PI * i / N));
        for (int i = 0, bits = BitOperations.Log2(N); i < N; i++) _rev[i] = (int)(ReverseBits((uint)i) >> (32 - bits));
        _parts = Math.Max(1, (ir.Length + Block - 1) / Block);
        (_hRe, _hIm, _xRe, _xIm) = (new float[_parts * N], new float[_parts * N], new float[_parts * N], new float[_parts * N]);
        for (var p = 0; p < _parts; p++)
        {
            Array.Clear(_re); Array.Clear(_im);
            var len = Math.Min(Block, ir.Length - p * Block);
            if (len > 0) ir.Slice(p * Block, len).CopyTo(_re);
            Fft(_re, _im);
            _re.CopyTo(_hRe, p * N);
            _im.CopyTo(_hIm, p * N);
        }
    }

    private static uint ReverseBits(uint v)
    {
        uint r = 0;
        for (var i = 0; i < 32; i++) { r = (r << 1) | (v & 1); v >>= 1; }
        return r;
    }

    public void Reset() { Array.Clear(_xRe); Array.Clear(_xIm); Array.Clear(_in); Array.Clear(_out); _fill = 0; }

    public void Process(Span<float> x)
    {
        for (var i = 0; i < x.Length; i++)
        {
            _in[Block + _fill] = x[i];
            x[i] = _out[_fill];
            if (++_fill == Block) Step();
        }
    }

    private void Step()
    {
        _fill = 0;
        _in.CopyTo(_re, 0);
        Array.Clear(_im);
        Fft(_re, _im);
        _slot = (_slot + _parts - 1) % _parts;
        _re.CopyTo(_xRe, _slot * N);
        _im.CopyTo(_xIm, _slot * N);
        Array.Clear(_accRe); Array.Clear(_accIm);
        var ar = MemoryMarshal.Cast<float, Vector<float>>(_accRe.AsSpan());
        var ai = MemoryMarshal.Cast<float, Vector<float>>(_accIm.AsSpan());
        for (var p = 0; p < _parts; p++)
        {
            var s = (_slot + p) % _parts * N;
            var xr = MemoryMarshal.Cast<float, Vector<float>>(_xRe.AsSpan(s, N));
            var xi = MemoryMarshal.Cast<float, Vector<float>>(_xIm.AsSpan(s, N));
            var hr = MemoryMarshal.Cast<float, Vector<float>>(_hRe.AsSpan(p * N, N));
            var hi = MemoryMarshal.Cast<float, Vector<float>>(_hIm.AsSpan(p * N, N));
            for (var v = 0; v < ar.Length; v++)
            {
                ar[v] += xr[v] * hr[v] - xi[v] * hi[v];
                ai[v] += xr[v] * hi[v] + xi[v] * hr[v];
            }
        }
        for (var k = 0; k < N; k++) _accIm[k] = -_accIm[k];   // inverse via conjugate
        Fft(_accRe, _accIm);
        for (var i = 0; i < Block; i++) _out[i] = _accRe[Block + i] / N;
        Array.Copy(_in, Block, _in, 0, Block);
    }

    // In-place radix-2 forward transform.
    private void Fft(float[] re, float[] im)
    {
        for (var i = 0; i < N; i++)
        {
            var j = _rev[i];
            if (j > i) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
        }
        for (var size = 2; size <= N; size <<= 1)
        {
            int half = size >> 1, step = N / size;
            for (var s = 0; s < N; s += size)
                for (var k = 0; k < half; k++)
                {
                    float c = _cos[k * step], sn = _sin[k * step];
                    int a = s + k, b = a + half;
                    float tr = re[b] * c - im[b] * sn, ti = re[b] * sn + im[b] * c;
                    (re[b], im[b]) = (re[a] - tr, im[a] - ti);
                    (re[a], im[a]) = (re[a] + tr, im[a] + ti);
                }
        }
    }
}
