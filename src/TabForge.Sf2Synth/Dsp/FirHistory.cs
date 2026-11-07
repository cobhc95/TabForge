using System.Numerics;
using System.Runtime.InteropServices;

namespace TabForge.Sf2Synth.Dsp;

// Owns: direct-form FIR history kept contiguous (each sample written twice) so every output is one SIMD dot product.
// Does not own: the kernels (Oversampler2x, Guitar/CabinetImpulse).
// Tests: GuitarLayerTests.
internal sealed class FirHistory
{
    private readonly float[] _h;
    private int _p;

    public int Length { get; }

    public FirHistory(int length)
    {
        Length = Pad(length);
        _h = new float[2 * Length];
    }

    public static int Pad(int n) => (n + Vector<float>.Count - 1) / Vector<float>.Count * Vector<float>.Count;

    public void Push(float x)
    {
        _p = _p == 0 ? Length - 1 : _p - 1;
        _h[_p] = x;
        _h[_p + Length] = x;
    }

    // Σ kernel[k]·x[n-k]; kernel length padded, at most Length.
    public float Dot(ReadOnlySpan<float> kernel)
    {
        var a = MemoryMarshal.Cast<float, Vector<float>>(kernel);
        var b = MemoryMarshal.Cast<float, Vector<float>>(_h.AsSpan(_p, kernel.Length));
        var acc = Vector<float>.Zero;
        for (var i = 0; i < a.Length; i++) acc += a[i] * b[i];
        return Vector.Dot(acc, Vector<float>.One);
    }

    public void Clear() { Array.Clear(_h); _p = 0; }
}
