using TabForge.Sf2Synth.Dsp;
using static TabForge.Sf2Synth.Dsp.EqKind;

namespace TabForge.Sf2Synth.Guitar;

// Owns: procedural cabinet impulse responses: an impulse through speaker resonances and roll-off, one close-mic reflection,
//   a faded tail, normalised to 0 dB at 1 kHz; Room adds a small room's early reflections (up to 17 ms) to the guitar cabinet. Built at construction, never in render.
// Does not own: running it (Dsp/FftConvolver), a measured IR (Dsp/WavIr).
// Tests: GuitarLayerTests.
internal static class CabinetImpulse
{
    private const double Seconds = 0.0107, ReflectionSeconds = 0.0006, ReflectionGain = 0.25, FadeShare = 0.3;
    private static readonly (double Ms, double Gain)[] Room = [(2.9, .30), (4.7, -.22), (7.3, .17), (10.1, .13), (13.6, -.09), (17.2, .06)];

    private static readonly EqSpec[] GuitarSpeaker =
    [
        new(HighPass, 50, 0.7), new(Peak, 110, 1.8, 6), new(Peak, 400, 1.2, -4), new(Peak, 2600, 1.5, 5),
        new(Peak, 3800, 3, 3), new(LowPass, 5200, 0.9), new(LowPass, 6000, 0.6), new(Peak, 8000, 1, -8),
    ];

    private static readonly EqSpec[] BassSpeaker =
        [new(HighPass, 22, 0.7), new(Peak, 70, 1.5, 6), new(Peak, 800, 1, -2), new(LowPass, 3500, 0.8), new(LowPass, 4500, 0.6)];

    public static float[] Build(CabKind kind, double rate)
    {
        var specs = kind == CabKind.Bass ? BassSpeaker : GuitarSpeaker;
        // Bass needs several periods of the fundamental (30 Hz = 33 ms) to hold a body; 10 ms truncates it and sounds thin.
        var taps = (int)Math.Round((kind == CabKind.Bass ? 0.04 : Seconds) * rate);
        var filters = specs.Select(s => new Biquad { C = BiquadCoefs.Of(s, rate) }).ToArray();
        var raw = new double[taps];
        for (var n = 0; n < taps; n++)
        {
            float x = n == 0 ? 1 : 0;
            for (var i = 0; i < filters.Length; i++) x = filters[i].Process(x);
            raw[n] = x;
        }
        var lag = (int)Math.Round(ReflectionSeconds * rate);
        var fade = (int)(taps * FadeShare);
        var h = new double[taps];
        for (var n = 0; n < taps; n++)
        {
            h[n] = raw[n] + (n >= lag ? ReflectionGain * raw[n - lag] : 0);
            if (n >= taps - fade) h[n] *= 0.5 * (1 + Math.Cos(Math.PI * (n - (taps - fade)) / fade));
        }
        if (kind == CabKind.Room)
        {
            var r = new double[taps + (int)(Room[^1].Ms * 0.001 * rate)];
            h.CopyTo(r, 0);
            foreach (var (ms, g) in Room)
                for (int n = 0, at = (int)(ms * 0.001 * rate); n < taps; n++) r[at + n] += g * h[n];
            h = r;
        }
        return WavIr.Normalise(h.Select(v => (float)v).ToArray(), rate);
    }
}
