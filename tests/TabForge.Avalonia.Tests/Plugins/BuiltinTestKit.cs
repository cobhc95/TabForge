using System;
using TabForge.Audio.Contracts.Builtin;
using TabForge.AudioEngine.Builtin;
using TabForge.AudioEngine.Plugins;

namespace TabForge.Avalonia.Tests;

// Owns: what the built-in effect sound tests share: test signals (sine, impulse), running an effect over a whole signal in blocks with a given tempo, level
//   measurement in dB, and an independent 8x true-peak measure that does not use the limiter detector.
// Does not own: the effects or their expected values (BuiltinEqTests, BuiltinDynamicsTests, BuiltinDelayTests).
// Tests: the three classes above.
internal static class BuiltinTestKit
{
    public static float[] Sine(double hz, double db, int frames, int rate, double phase = 0)
    {
        var x = new float[frames];
        var amplitude = Math.Pow(10, db / 20);
        for (var i = 0; i < frames; i++) x[i] = (float)(amplitude * Math.Sin(2 * Math.PI * hz * i / rate + phase));
        return x;
    }

    public static float[] Constant(double value, int frames) { var x = new float[frames]; Array.Fill(x, (float)value); return x; }

    public static float[] Impulse(int frames, float height = 1f) { var x = new float[frames]; x[0] = height; return x; }

    /// <summary>Runs <paramref name="effect"/> over the whole signal in blocks (so block edges are exercised); the tempo goes to the transport of every block.</summary>
    public static (float[] Left, float[] Right) Run(IPluginInstance effect, float[] left, float[] right, int block = 256, double tempo = 120)
    {
        var outLeft = new float[left.Length];
        var outRight = new float[left.Length];
        var transport = new TransportInfo { Tempo = tempo, Speed = 1 };
        for (var at = 0; at < left.Length; at += block)
        {
            var n = Math.Min(block, left.Length - at);
            var input = new[] { new float[n], new float[n] };
            var output = new[] { new float[n], new float[n] };
            Array.Copy(left, at, input[0], 0, n);
            Array.Copy(right, at, input[1], 0, n);
            effect.Process(input, output, n, default, transport);
            Array.Copy(output[0], 0, outLeft, at, n);
            Array.Copy(output[1], 0, outRight, at, n);
        }
        return (outLeft, outRight);
    }

    public static (float[] Left, float[] Right) Run(IPluginInstance effect, float[] mono, int block = 256, double tempo = 120) => Run(effect, mono, (float[])mono.Clone(), block, tempo);

    /// <summary>RMS of <paramref name="x"/> between two sample positions, in dB.</summary>
    public static double RmsDb(float[] x, int from, int to)
    {
        double sum = 0;
        for (var i = from; i < to; i++) sum += (double)x[i] * x[i];
        return 10 * Math.Log10(Math.Max(sum / (to - from), 1e-30));
    }

    /// <summary>The gain in dB the effect gives a sine of <paramref name="hz"/>: output RMS over input RMS, measured after the first third of a second.</summary>
    public static double GainDb(IPluginInstance effect, double hz, double inDb, int rate, double seconds = 1.0)
    {
        var frames = (int)(rate * seconds);
        var input = Sine(hz, inDb, frames, rate);
        var (output, _) = Run(effect, input);
        var from = frames / 3;
        return RmsDb(output, from, frames) - RmsDb(input, from, frames);
    }

    /// <summary>The largest magnitude of the band-limited signal, found by 8x oversampling with a long Blackman-windowed sinc (independent of the limiter detector).</summary>
    public static double TruePeak(float[] x, int from, int to)
    {
        const int Over = 8, Half = 32;
        double peak = 0;
        for (var i = from; i < to; i++)
        {
            peak = Math.Max(peak, Math.Abs(x[i]));
            for (var p = 1; p < Over; p++)
            {
                double sum = 0;
                for (var k = -Half + 1; k <= Half; k++)
                {
                    var j = i + k;
                    if (j < 0 || j >= x.Length) continue;
                    var t = p / (double)Over - k;
                    var sinc = Math.Sin(Math.PI * t) / (Math.PI * t);
                    var u = t / Half;
                    sum += x[j] * sinc * (0.42 + 0.5 * Math.Cos(Math.PI * u) + 0.08 * Math.Cos(2 * Math.PI * u));
                }
                peak = Math.Max(peak, Math.Abs(sum));
            }
        }
        return peak;
    }

    public static double Db(double linear) => 20 * Math.Log10(Math.Max(linear, 1e-12));
}
