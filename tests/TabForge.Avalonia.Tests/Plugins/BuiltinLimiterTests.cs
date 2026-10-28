using System;
using System.Linq;
using TabForge.AudioEngine.Builtin.Limiter;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the built-in limiter sound: the ceiling on a +6 dB overshoot signal and on an inter-sample peak measured by an independent 8x true-peak measure, at three
//   sample rates, the reported latency and the exact delay, the release back to unity, and the bypass.
// Does not own: the editor window (BuiltinEditorWindowTests), the framework (BuiltinEffectSetTests).
// Tests: this class.
public sealed class BuiltinLimiterTests
{
    private const int Rate = 48000;

    private static LimiterEffect Limiter(int rate = Rate)
    {
        var l = new LimiterEffect(rate, 512);
        l.SetParameter(LimiterEffect.CeilingId, -1.0);
        l.SetParameter(LimiterEffect.ReleaseId, 80);
        return l;
    }

    [Theory]
    [InlineData(48000)]
    [InlineData(44100)]
    [InlineData(96000)]
    public void TheLimiterNeverPassesItsCeilingOnASixDecibelOvershoot(int rate)
    {
        var limiter = Limiter(rate);
        var ceiling = Math.Pow(10, -1.0 / 20);
        var rng = new Random(6);
        var input = BuiltinTestKit.Sine(997, 6, rate, rate);   // +6 dBFS: twice full scale
        for (var i = 0; i < input.Length; i++) input[i] = Math.Clamp(input[i] + (float)(rng.NextDouble() - 0.5) * 0.8f * (i % 5000 < 300 ? 1 : 0), -2.5f, 2.5f);
        var (output, _) = BuiltinTestKit.Run(limiter, input, 200);
        var from = limiter.LatencySamples;
        var samplePeak = output.Skip(from).Max(Math.Abs);
        Assert.True(samplePeak <= ceiling + 1e-6, $"sample peak {BuiltinTestKit.Db(samplePeak):0.000} dB");
        var truePeak = BuiltinTestKit.TruePeak(output, from + 100, output.Length - 100);
        Assert.True(BuiltinTestKit.Db(truePeak) <= -1.0 + 0.1, $"true peak {BuiltinTestKit.Db(truePeak):0.000} dBTP");   // noise bursts reach 24 kHz, where any 4x detector is least exact: within 0.1 dB
        Assert.True(limiter.GetParameters().Single(p => p.Id == LimiterEffect.GainReductionId).Value > 5);
    }

    [Fact]
    public void TheLimiterCatchesAnInterSamplePeakThatNoSampleShows()
    {
        var limiter = Limiter();
        var tone = BuiltinTestKit.Sine(Rate / 4.0, BuiltinTestKit.Db(1.5), Rate, Rate, Math.PI / 4);   // samples reach 1.06 but the wave reaches 1.5
        var (output, _) = BuiltinTestKit.Run(limiter, tone);
        var truePeak = BuiltinTestKit.Db(BuiltinTestKit.TruePeak(output, Rate / 2, Rate - 100));
        Assert.True(truePeak <= -1.0 + 0.01, $"true peak {truePeak:0.000} dBTP");
        Assert.True(truePeak > -1.3, $"it limits to the ceiling, not far below it ({truePeak:0.000} dBTP)");
    }

    [Fact]
    public void TheLimiterReportsItsLookAheadAndDelaysTheSoundByExactlyThat()
    {
        var limiter = Limiter();
        Assert.Equal(72, limiter.LatencySamples);   // 1.5 ms at 48 kHz
        Assert.Equal(66, new LimiterEffect(44100, 512).LatencySamples);
        var (output, _) = BuiltinTestKit.Run(limiter, BuiltinTestKit.Impulse(1000, 0.5f));
        for (var i = 0; i < 72; i++) Assert.Equal(0f, output[i]);
        Assert.Equal(0.5f, output[72], 6);
        Assert.Equal(0f, output[73], 6);
        Assert.Contains("72 samples", limiter.GetParameters().Single(p => p.Id == LimiterEffect.LatencyId).Text);
    }

    [Fact]
    public void TheLimiterReleasesBackToUnityAndBypassPassesTheDelayedSound()
    {
        var limiter = Limiter();
        var burst = BuiltinTestKit.Sine(300, 6, Rate / 4, Rate);
        var quiet = BuiltinTestKit.Sine(300, -20, Rate, Rate);
        var (output, _) = BuiltinTestKit.Run(limiter, burst.Concat(quiet).ToArray());
        var tail = Rate / 4 + Rate - 4800;
        Assert.Equal(BuiltinTestKit.RmsDb(quiet, Rate - 4800, Rate), BuiltinTestKit.RmsDb(output, tail, tail + 4800), 0.1);
        limiter.SetParameter(LimiterEffect.BypassId, 1);
        var loud = BuiltinTestKit.Sine(300, 6, Rate / 2, Rate);
        var (bypassed, _) = BuiltinTestKit.Run(limiter, loud);
        for (var i = 4800; i < loud.Length - 72; i++) Assert.Equal(loud[i], bypassed[i + 72], 5);
    }
}
