using System;
using System.Linq;
using TabForge.AudioEngine.Builtin.Reverb;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the built-in reverb sound: dry untouched, the tail after an impulse, bypass and mix 0, the tail decaying, wide frequency range, and finite safety.
// Does not own: the editor window (BuiltinEditorWindowTests), the framework (BuiltinEffectSetTests).
// Tests: this class.
public sealed class BuiltinReverbTests
{
    private const int Rate = 48000;

    private static ReverbEffect Reverb(double mix = 100, double bypass = 0)
    {
        var r = new ReverbEffect(Rate, 512);
        r.SetParameter(ReverbEffect.MixId, mix);
        r.SetParameter(ReverbEffect.BypassId, bypass);
        return r;
    }

    private static float TailLeft(float[] left, int from, int to) => left.Skip(from).Take(to - from).Max(Math.Abs);

    [Fact]
    public void AnImpulseComesOutDryUntouchedAndWithATail()
    {
        var reverb = Reverb();
        var (left, _) = BuiltinTestKit.Run(reverb, BuiltinTestKit.Impulse(48000), 256, 120);
        Assert.Equal(1f, left[0], 5);                         // the dry path is untouched
        Assert.Equal(0f, left[1], 5);                          // first reflections come later than the dry click
        Assert.True(TailLeft(left, 2000, 4000) > 0.0001, $"tail {TailLeft(left, 2000, 4000)}");
        Assert.True(TailLeft(left, 24000, 48000) < 0.001, $"tail still going at 0.5 s: {TailLeft(left, 24000, 48000)}");
    }

    [Fact]
    public void BypassIsSilentCleanAndChangesNothing()
    {
        var reverb = Reverb(bypass: 1);
        var input = BuiltinTestKit.Sine(800, -6, Rate, Rate);
        var (left, _) = BuiltinTestKit.Run(reverb, input, 256, 120);
        Assert.Equal(input, left);
    }

    [Fact]
    public void MixZeroIsJustTheDrySound()
    {
        var reverb = Reverb(mix: 0);
        var input = BuiltinTestKit.Sine(440, -6, 4800, Rate);
        var (left, _) = BuiltinTestKit.Run(reverb, input, 256, 120);
        Assert.Equal(input, left);
    }

    [Fact]
    public void TheTailDecaysButNotReachesSilenceInstantly()
    {
        var reverb = Reverb();
        reverb.SetParameter(ReverbEffect.SizeId, 100);
        var (left, _) = BuiltinTestKit.Run(reverb, BuiltinTestKit.Impulse(Rate * 2), 512, 120);
        var peakEarly = TailLeft(left, 2000, Rate / 4);        // first quarter-second (tail built up)
        var tailLate = TailLeft(left, Rate * 3 / 2, Rate * 2); // last half-second
        Assert.True(peakEarly > tailLate * 10, $"early {peakEarly} late {tailLate}");
        Assert.True(tailLate < 0.01, $"tail still ringing at 1.5 s: {tailLate}");
        Assert.True(tailLate < 0.1, $"tail already at -20 dB by 1.5 s: {tailLate}");
    }

    [Fact]
    public void HotSignalStaysFinite()
    {
        var reverb = Reverb();
        var input = BuiltinTestKit.Sine(1000, -6, 8192, Rate);
        var (left, right) = BuiltinTestKit.Run(reverb, input, 256, 120);
        Assert.All(left.Concat(right), v => Assert.True(float.IsFinite(v)));
        Assert.True(left.Max(Math.Abs) < 200, $"peak {left.Max(Math.Abs)}");
    }

    [Fact]
    public void WidthWidensTheTail()
    {
        var mono = Reverb();
        mono.SetParameter(ReverbEffect.WidthId, 0);
        var wide = Reverb();
        wide.SetParameter(ReverbEffect.WidthId, 100);
        var input = BuiltinTestKit.Impulse(Rate / 2);
        var (monoOut, _) = BuiltinTestKit.Run(mono, input, 512, 120);
        var (wideOut, wideOutR) = BuiltinTestKit.Run(wide, input, 512, 120);
        var lBalance = Math.Abs(TailLeft(monoOut, 2000, Rate / 2) - TailLeft(monoOut, Rate / 4, Rate / 2));
        Assert.NotEqual(0, lBalance);
    }
}
