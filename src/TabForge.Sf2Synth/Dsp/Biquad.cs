namespace TabForge.Sf2Synth.Dsp;

// Owns: the biquad (RBJ cookbook shapes) and the EqSpec a table writes a band in.
// Does not own: which bands a sound uses (Guitar/Voicings) or the voice filter's modulation (Synth/Voice).
// Tests: Sf2SynthTests, GuitarLayerTests.

internal enum EqKind { HighPass, LowPass, Peak, LowShelf, HighShelf, BandPass }

internal readonly record struct EqSpec(EqKind Kind, double Hz, double Q, double Db = 0);

internal struct BiquadCoefs
{
    public float B0, B1, B2, A1, A2;

    public static BiquadCoefs Of(in EqSpec s, double rate)
    {
        var w0 = 2 * Math.PI * Math.Min(s.Hz, 0.45 * rate) / rate;
        double cos = Math.Cos(w0), alpha = Math.Sin(w0) / (2 * s.Q), a = Math.Pow(10, s.Db / 40), sq = 2 * Math.Sqrt(a) * alpha;
        double b0, b1, b2, a0, a1, a2;
        switch (s.Kind)
        {
            case EqKind.LowPass: b0 = (1 - cos) / 2; b1 = 1 - cos; b2 = b0; a0 = 1 + alpha; a1 = -2 * cos; a2 = 1 - alpha; break;
            case EqKind.BandPass: b0 = alpha; b1 = 0; b2 = -alpha; a0 = 1 + alpha; a1 = -2 * cos; a2 = 1 - alpha; break;   // 0 dB at Hz
            case EqKind.HighPass: b0 = (1 + cos) / 2; b1 = -(1 + cos); b2 = b0; a0 = 1 + alpha; a1 = -2 * cos; a2 = 1 - alpha; break;
            case EqKind.Peak: b0 = 1 + alpha * a; b1 = -2 * cos; b2 = 1 - alpha * a; a0 = 1 + alpha / a; a1 = -2 * cos; a2 = 1 - alpha / a; break;
            case EqKind.LowShelf:
                b0 = a * ((a + 1) - (a - 1) * cos + sq); b1 = 2 * a * ((a - 1) - (a + 1) * cos); b2 = a * ((a + 1) - (a - 1) * cos - sq);
                a0 = (a + 1) + (a - 1) * cos + sq; a1 = -2 * ((a - 1) + (a + 1) * cos); a2 = (a + 1) + (a - 1) * cos - sq; break;
            default:
                b0 = a * ((a + 1) + (a - 1) * cos + sq); b1 = -2 * a * ((a - 1) + (a + 1) * cos); b2 = a * ((a + 1) + (a - 1) * cos - sq);
                a0 = (a + 1) - (a - 1) * cos + sq; a1 = 2 * ((a - 1) - (a + 1) * cos); a2 = (a + 1) - (a - 1) * cos - sq; break;
        }
        return new BiquadCoefs { B0 = (float)(b0 / a0), B1 = (float)(b1 / a0), B2 = (float)(b2 / a0), A1 = (float)(a1 / a0), A2 = (float)(a2 / a0) };
    }
}

// Transposed direct form II.
internal struct Biquad
{
    public BiquadCoefs C;
    private float _z1, _z2;

    public float Process(float x)
    {
        var y = C.B0 * x + _z1;
        _z1 = C.B1 * x - C.A1 * y + _z2;
        _z2 = C.B2 * x - C.A2 * y;
        return y;
    }

    public void Reset() { _z1 = 0; _z2 = 0; }
}
