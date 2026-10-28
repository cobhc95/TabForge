using TabForge.AudioEngine.Builtin.Dsp;

namespace TabForge.AudioEngine.Builtin.Eq;

// Owns: turning the EQ values into filter sections: the coefficients of a band and of the two cut filters, and the whole response in dB at a frequency
//   computed from those coefficients. The audio path and the curve of the editor call the same functions, so the picture is the sound.
// Does not own: the audio path or the smoothing (EqEffect.Process.cs, EqBand), the drawing (EqCurveView in the app).
// Tests: BuiltinEqTests.
public static class EqDesign
{
    /// <summary>Q of the two sections of a 24 dB per octave (4th order Butterworth) cut; a 12 dB cut is one section at <see cref="BiquadCoef.Butterworth"/>.</summary>
    public const double Cut24Q1 = 0.5411961, Cut24Q2 = 1.3065630;

    /// <summary>One band: band 0 is a low shelf or bell, band 3 a high shelf or bell, the others a bell.</summary>
    public static BiquadCoef Band(double sampleRate, int band, double hz, double gainDb, double q, bool shelf)
    {
        if (shelf && band == 0) return BiquadCoef.LowShelf(sampleRate, hz, gainDb, q);
        if (shelf && band == EqEffect.Bands - 1) return BiquadCoef.HighShelf(sampleRate, hz, gainDb, q);
        return BiquadCoef.Peak(sampleRate, hz, gainDb, q);
    }

    /// <summary>Q of the first section of a cut filter (it glides when the slope changes).</summary>
    public static double CutQ1(bool slope24) => slope24 ? Cut24Q1 : BiquadCoef.Butterworth;

    public static BiquadCoef Cut(double sampleRate, bool high, double hz, double q) =>
        high ? BiquadCoef.LowPass(sampleRate, hz, q) : BiquadCoef.HighPass(sampleRate, hz, q);

    /// <summary>The response of all filters (not the output gain) in dB at <paramref name="hz"/>; <paramref name="values"/> is indexed like <see cref="EqEffect.Descriptors"/>.</summary>
    public static double ResponseDb(ReadOnlySpan<double> values, double hz, double sampleRate)
    {
        var db = CutDb(false, values[EqEffect.LowCutSlopeIndex], values[EqEffect.LowCutFreqIndex], hz, sampleRate)
            + CutDb(true, values[EqEffect.HighCutSlopeIndex], values[EqEffect.HighCutFreqIndex], hz, sampleRate);
        for (var b = 0; b < EqEffect.Bands; b++) db += BandDb(values, b, hz, sampleRate);
        return db;
    }

    /// <summary>One band response (0 when it is off).</summary>
    public static double BandDb(ReadOnlySpan<double> values, int band, double hz, double sampleRate)
    {
        if (values[EqEffect.Index(band, EqEffect.OnField)] < 0.5) return 0;
        var coef = Band(sampleRate, band, values[EqEffect.Index(band, EqEffect.FreqField)], values[EqEffect.Index(band, EqEffect.GainField)],
            values[EqEffect.Index(band, EqEffect.QField)], EqEffect.HasType(band) && values[EqEffect.TypeIndex(band)] < 0.5);
        return coef.MagnitudeDb(hz, sampleRate);
    }

    private static double CutDb(bool high, double slope, double cutHz, double hz, double sampleRate)
    {
        if (slope < EqEffect.Slope12) return 0;
        if (slope < EqEffect.Slope24) return Cut(sampleRate, high, cutHz, BiquadCoef.Butterworth).MagnitudeDb(hz, sampleRate);
        return Cut(sampleRate, high, cutHz, Cut24Q1).MagnitudeDb(hz, sampleRate) + Cut(sampleRate, high, cutHz, Cut24Q2).MagnitudeDb(hz, sampleRate);
    }
}
