using System;
using System.Linq;
using TabForge.AudioEngine.Builtin.Delay;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the built-in delay sound: the first echo at the exact sample for 1/8 at 120 BPM (and other lengths and tempos), the time in milliseconds, feedback,
//   ping-pong, the filters on the repeats, and a time change that glides without a jump.
// Does not own: the editor window (BuiltinEditorWindowTests), the framework (BuiltinEffectSetTests).
// Tests: this class.
public sealed class BuiltinDelayTests
{
    private const int Rate = 48000;

    private static DelayEffect Delay(double mix = 100, double feedback = 0)
    {
        var d = new DelayEffect(Rate, 512);
        d.SetParameter(DelayEffect.MixId, mix);
        d.SetParameter(DelayEffect.FeedbackId, feedback);
        return d;
    }

    private static int FirstEcho(float[] x, int after = 1) { for (var i = after; i < x.Length; i++) if (Math.Abs(x[i]) > 1e-4f) return i; return -1; }

    [Fact]
    public void TheFirstEchoOfAnEighthNoteAtOneHundredTwentyBeatsComesAtExactlyTwelveThousandSamples()
    {
        var delay = Delay();
        var (left, right) = BuiltinTestKit.Run(delay, BuiltinTestKit.Impulse(30000), 256, 120);
        Assert.Equal(12000, FirstEcho(left));
        Assert.Equal(1f, left[12000], 5);
        Assert.Equal(1f, right[12000], 5);
        Assert.Equal(0f, left[11999], 5);
        Assert.Equal(0f, left[12001], 5);
    }

    [Theory]
    [InlineData(4, 120, 24000)]    // 1/4 at 120 BPM: half a second
    [InlineData(8, 120, 18000)]    // 1/8. : three sixteenths
    [InlineData(9, 120, 8000)]     // 1/8T: a third of a quarter note
    [InlineData(7, 90, 16000)]     // 1/8 at 90 BPM
    [InlineData(10, 150, 4800)]    // 1/16 at 150 BPM
    public void OtherNoteLengthsAndTemposLandOnTheirSample(int division, double bpm, int expected)
    {
        var delay = Delay();
        delay.SetParameter(DelayEffect.DivisionId, division);
        var (left, _) = BuiltinTestKit.Run(delay, BuiltinTestKit.Impulse(30000), 300, bpm);
        Assert.Equal(expected, FirstEcho(left));
        Assert.Equal(1f, left[expected], 5);
    }

    [Fact]
    public void TheTimeInMillisecondsIsUsedWhenSyncIsOffAndNoTempoMatters()
    {
        var delay = Delay();
        delay.SetParameter(DelayEffect.SyncId, 0);
        delay.SetParameter(DelayEffect.TimeId, 100);
        var (left, _) = BuiltinTestKit.Run(delay, BuiltinTestKit.Impulse(10000), 256, 63);
        Assert.Equal(4800, FirstEcho(left));
    }

    [Fact]
    public void FeedbackRepeatsAndPingPongAlternatesTheSides()
    {
        var delay = Delay(100, 50);
        delay.SetParameter(DelayEffect.SyncId, 0);
        delay.SetParameter(DelayEffect.TimeId, 100);
        var (left, right) = BuiltinTestKit.Run(delay, BuiltinTestKit.Impulse(20000));
        Assert.Equal(0.5f, left[9600], 5);
        Assert.Equal(0.25f, left[14400], 5);
        var pingPong = Delay(100, 50);
        pingPong.SetParameter(DelayEffect.SyncId, 0);
        pingPong.SetParameter(DelayEffect.TimeId, 100);
        pingPong.SetParameter(DelayEffect.PingPongId, 1);
        var (pl, pr) = BuiltinTestKit.Run(pingPong, BuiltinTestKit.Impulse(20000));   // a centred impulse: ping-pong sends the mono sum to the left first
        Assert.Equal(0f, pr[4800], 5);
        Assert.Equal(1f, pl[4800], 5);       // the first echo is on the left only
        Assert.Equal(0.5f, pr[9600], 5);     // the second on the right
        Assert.Equal(0f, pl[9600], 5);
        Assert.Equal(0.25f, pl[14400], 5);   // the third back on the left
        Assert.True(left.Length == right.Length);
    }

    [Fact]
    public void TheHighCutOnTheRepeatsTakesTheBrightnessOutOfTheEchoOnly()
    {
        double EchoDb(double highCut)
        {
            var delay = Delay();
            delay.SetParameter(DelayEffect.SyncId, 0);
            delay.SetParameter(DelayEffect.TimeId, 100);
            delay.SetParameter(DelayEffect.HighCutId, highCut);
            var input = BuiltinTestKit.Sine(8000, -12, Rate, Rate);
            var (left, _) = BuiltinTestKit.Run(delay, input);
            var echo = left.Zip(input, (o, i) => o - i).ToArray();   // the output without the dry sound
            return BuiltinTestKit.RmsDb(echo, 24000, Rate);
        }
        Assert.True(EchoDb(1000) < EchoDb(20000) - 25);
    }

    [Fact]
    public void TheLowCutOnTheRepeatsTakesTheBassOutOfTheEcho()
    {
        double EchoDb(double lowCut)
        {
            var delay = Delay();
            delay.SetParameter(DelayEffect.SyncId, 0);
            delay.SetParameter(DelayEffect.TimeId, 100);
            delay.SetParameter(DelayEffect.LowCutId, lowCut);
            var input = BuiltinTestKit.Sine(80, -12, Rate, Rate);
            var (left, _) = BuiltinTestKit.Run(delay, input);
            return BuiltinTestKit.RmsDb(left.Zip(input, (o, i) => o - i).ToArray(), 24000, Rate);
        }
        Assert.True(EchoDb(1000) < EchoDb(20) - 25);
    }

    [Fact]
    public void ChangingTheTimeWhilePlayingGlidesWithoutAJump()
    {
        var delay = Delay(100, 40);
        delay.SetParameter(DelayEffect.SyncId, 0);
        delay.SetParameter(DelayEffect.TimeId, 200);
        var tone = BuiltinTestKit.Sine(220, -6, Rate * 2, Rate);
        BuiltinTestKit.Run(delay, tone.AsSpan(0, Rate).ToArray());
        delay.SetParameter(DelayEffect.TimeId, 333);
        var (after, _) = BuiltinTestKit.Run(delay, tone.AsSpan(Rate).ToArray());
        var biggest = 0.0;
        for (var i = 1; i < after.Length; i++) biggest = Math.Max(biggest, Math.Abs(after[i] - after[i - 1]));
        Assert.True(biggest < 0.12, $"largest step between samples {biggest:0.000}");   // a 220 Hz tone at -6 dB plus its echo steps at most about 0.06
    }

    [Fact]
    public void ADelayLongerThanTheBufferPlaysAtHalfLengthAndTheTailIsReported()
    {
        var delay = Delay(100, 70);
        delay.SetParameter(DelayEffect.DivisionId, 0);   // a whole note at 20 BPM is 12 seconds: more than the 4 second buffer
        BuiltinTestKit.Run(delay, BuiltinTestKit.Impulse(2000), 256, 20);
        Assert.True(delay.TailSamples > Rate);
        Assert.True(delay.TailSamples <= Rate * 4 + 8);
    }
}
