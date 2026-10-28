using System;
using System.Linq;
using TabForge.AudioEngine.Builtin.Eq;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the built-in EQ sound: a bell of +6 dB at 1 kHz measures +6 at 1 kHz and about 0 away from it, the sound matches the curve computed from the
//   coefficients (shelves, bells, 12 and 24 dB cuts), the same at other sample rates, and a bypass that ends exactly on the input.
// Does not own: the editor window (BuiltinEditorWindowTests), the framework (BuiltinEffectTests, BuiltinEffectSetTests).
// Tests: this class.
public sealed class BuiltinEqTests
{
    private static EqEffect Eq(int rate = 48000) => new(rate, 512);

    private static void Band(EqEffect eq, int band, double hz, double gain, double q)
    {
        eq.SetParameter(EqEffect.BandId(band, EqEffect.FreqField), hz);
        eq.SetParameter(EqEffect.BandId(band, EqEffect.GainField), gain);
        eq.SetParameter(EqEffect.BandId(band, EqEffect.QField), q);
    }

    private static double[] Values(EqEffect eq) => eq.GetParameters().Select(p => p.Value).ToArray();

    [Theory]
    [InlineData(48000)]
    [InlineData(44100)]
    [InlineData(96000)]
    public void ABellOfSixDecibelsAtOneKilohertzMeasuresSixThereAndNothingFarAway(int rate)
    {
        Assert.Equal(6.0, BuiltinTestKit.GainDb(Bell(rate), 1000, -20, rate), 0.1);
        Assert.InRange(BuiltinTestKit.GainDb(Bell(rate), 100, -20, rate), -0.15, 0.15);
        Assert.InRange(BuiltinTestKit.GainDb(Bell(rate), 10000, -20, rate), -0.25, 0.25);
    }

    private static EqEffect Bell(int rate)
    {
        var eq = Eq(rate);
        Band(eq, 1, 1000, 6, 4);
        return eq;
    }

    [Fact]
    public void TheSoundFollowsTheCurveComputedFromTheCoefficients()
    {
        var eq = Eq();
        Band(eq, 0, 200, 9, 0.9);
        Band(eq, 1, 700, -5, 2);
        Band(eq, 2, 3000, 4, 1.4);
        Band(eq, 3, 6000, -6, 0.8);
        eq.SetParameter(EqEffect.LowCutFreqId, 60);
        eq.SetParameter(EqEffect.LowCutSlopeId, EqEffect.Slope12);
        eq.SetParameter(EqEffect.HighCutFreqId, 12000);
        eq.SetParameter(EqEffect.HighCutSlopeId, EqEffect.Slope24);
        var values = Values(eq);
        foreach (var hz in new[] { 40.0, 150, 400, 1000, 2500, 5000, 9000, 14000 })
        {
            var measured = BuiltinTestKit.GainDb(eq, hz, -20, 48000);
            Assert.Equal(EqDesign.ResponseDb(values, hz, 48000), measured, 0.1);
        }
    }

    [Fact]
    public void ATwentyFourDecibelCutFallsTwiceAsFastAsATwelveDecibelCut()
    {
        double At(int slope)
        {
            var eq = Eq();
            eq.SetParameter(EqEffect.LowCutFreqId, 1000);
            eq.SetParameter(EqEffect.LowCutSlopeId, slope);
            return BuiltinTestKit.GainDb(eq, 250, -20, 48000);
        }
        Assert.Equal(-24.1, At(EqEffect.Slope12), 0.5);   // 250 Hz is two octaves below the cut: 1 / (1 + 4^4) for a 2nd order Butterworth
        Assert.Equal(-48.2, At(EqEffect.Slope24), 1.5);   // and twice that for the 4th order
        Assert.InRange(At(EqEffect.SlopeOff), -0.05, 0.05);
    }

    [Fact]
    public void OutputGainAndBypassAreCleanAndTheFlatEqIsTransparent()
    {
        var eq = Eq();
        var input = BuiltinTestKit.Sine(440, -12, 24000, 48000);
        var (flat, _) = BuiltinTestKit.Run(eq, input);
        for (var i = 0; i < input.Length; i++) Assert.Equal(input[i], flat[i], 5);
        eq.SetParameter(EqEffect.OutputId, 6);
        Assert.Equal(6.0, BuiltinTestKit.GainDb(eq, 440, -12, 48000), 0.05);
        Band(eq, 1, 440, 12, 1);
        eq.SetParameter(EqEffect.BypassId, 1);
        var (bypassed, _) = BuiltinTestKit.Run(eq, input);
        for (var i = 9600; i < input.Length; i++) Assert.Equal(input[i], bypassed[i], 5);
    }

    [Fact]
    public void MovingAKnobGlidesInsteadOfJumping()
    {
        var eq = Eq();
        var input = BuiltinTestKit.Sine(300, -6, 48000, 48000);
        BuiltinTestKit.Run(eq, input.AsSpan(0, 24000).ToArray());
        eq.SetParameter(EqEffect.Band1TypeId, EqEffect.TypeBell);
        Band(eq, 0, 300, 15, 1);
        var (after, _) = BuiltinTestKit.Run(eq, input.AsSpan(24000).ToArray());
        var biggestStep = 0.0;
        for (var i = 1; i < after.Length; i++) biggestStep = Math.Max(biggestStep, Math.Abs(after[i] - after[i - 1]));
        Assert.True(biggestStep < 0.5, $"largest step between samples {biggestStep:0.000}");
    }
}
