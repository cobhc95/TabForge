using System;
using System.Linq;
using TabForge.AudioEngine.Builtin.Compressor;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the built-in compressor sound: the 4:1 static gain above the threshold, nothing below it, the soft knee curve, auto makeup, release and the
//   gain-reduction readout.
// Does not own: the editor window (BuiltinEditorWindowTests), the framework (BuiltinEffectSetTests).
// Tests: this class.
public sealed class BuiltinCompressorTests
{
    private const int Rate = 48000;

    private static CompressorEffect Compressor(double threshold, double ratio, double knee)
    {
        var c = new CompressorEffect(Rate, 512);
        c.SetParameter(CompressorEffect.ThresholdId, threshold);
        c.SetParameter(CompressorEffect.RatioId, ratio);
        c.SetParameter(CompressorEffect.KneeId, knee);
        return c;
    }

    private static double Reduction(CompressorEffect c) => c.GetParameters().Single(p => p.Id == CompressorEffect.GainReductionId).Value;

    [Fact]
    public void AFourToOneCompressorGivesNineDecibelsLessForAnInputTwelveAboveTheThreshold()
    {
        var c = Compressor(-20, 4, 0);
        var (output, _) = BuiltinTestKit.Run(c, BuiltinTestKit.Constant(Math.Pow(10, -8 / 20.0), Rate));   // -8 dBFS is 12 dB over: 12 / 4 over remains, 9 dB of reduction
        Assert.Equal(-17.0, BuiltinTestKit.Db(output[^1]), 0.05);
        Assert.Equal(9.0, Reduction(c), 0.3);
        var tone = Compressor(-20, 4, 0);
        var sine = BuiltinTestKit.Sine(1000, -8, Rate, Rate);
        var (sineOut, _) = BuiltinTestKit.Run(tone, sine);
        Assert.Equal(-8.0, BuiltinTestKit.RmsDb(sineOut, Rate / 2, Rate) - BuiltinTestKit.RmsDb(sine, Rate / 2, Rate), 1.2);   // a tone reduces a little less than the static 9 dB: the gain also relaxes between peaks
    }

    [Fact]
    public void BelowTheThresholdNothingChangesAndTheKneeBlendsIn()
    {
        var c = Compressor(-20, 4, 0);
        var quiet = BuiltinTestKit.Sine(1000, -30, Rate, Rate);
        var (output, _) = BuiltinTestKit.Run(c, quiet);
        Assert.Equal(BuiltinTestKit.RmsDb(quiet, Rate / 2, Rate), BuiltinTestKit.RmsDb(output, Rate / 2, Rate), 0.02);
        Assert.Equal(-20.0, CompressorCurve.OutputDb(-20, -20, 4, 0), 1e-9);
        Assert.Equal(-20 - 0.9375, CompressorCurve.OutputDb(-20, -20, 4, 10), 1e-9);   // at the threshold the knee takes (1 - 1/4) * 10 / 8 dB
        Assert.Equal(-25.0, CompressorCurve.OutputDb(-25, -20, 4, 10), 1e-9);   // the knee starts half its width below the threshold
        Assert.Equal(-20 + 10 / 4.0, CompressorCurve.OutputDb(-10, -20, 4, 10), 1e-9);
    }

    [Fact]
    public void AutoMakeupBringsAFullScaleSignalBackToFullScale()
    {
        var c = Compressor(-20, 4, 0);
        c.SetParameter(CompressorEffect.AutoMakeupId, 1);
        var (output, _) = BuiltinTestKit.Run(c, BuiltinTestKit.Constant(1.0, Rate));
        Assert.Equal(1.0, output[^1], 2);
    }

    [Fact]
    public void ReleaseLetsTheGainReductionGoAndTheMeterFollows()
    {
        var c = Compressor(-20, 4, 0);
        c.SetParameter(CompressorEffect.ReleaseId, 50);
        BuiltinTestKit.Run(c, BuiltinTestKit.Constant(0.5, Rate / 2));
        Assert.True(Reduction(c) > 8);
        var (tail, _) = BuiltinTestKit.Run(c, BuiltinTestKit.Constant(0.0001, Rate));
        Assert.Equal(0.0001, tail[^1], 5);
        Assert.True(Reduction(c) < 0.5);
    }
}
