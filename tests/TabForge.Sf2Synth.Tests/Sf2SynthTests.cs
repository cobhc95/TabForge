using System;
using System.Diagnostics;
using System.Linq;
using TabForge.Sf2Synth;
using TabForge.Sf2Synth.Sf2;
using Xunit;
using Xunit.Abstractions;

namespace TabForge.Sf2Synth.Tests;

// Owns: the standalone checks of the Sf2Synth SoundFont player on a synthetic SF2: loading and preset fallback, pitch through
//   resampling and RPN bend, SF2 velocity/volume curves, click-free start and release, sustain, drums, no allocation, CPU cost.
// Does not own: the guitar layer (GuitarLayerTests), the code it checks.
// Tests: SoundFont, Sf2Reader, Synthesizer, Channel, Voice, Envelope, Mod.
public sealed class Sf2SynthTests(ITestOutputHelper output)
{
    private const int Rate = 48000;
    private static readonly SoundFont Font = new(TestSoundFont.Build());

    private static Synthesizer New(bool effects = false) => new(Font, Rate, 64, effects) { MasterVolume = 1 };

    private static float[] Render(Synthesizer s, int frames, float[]? right = null)
    {
        var l = new float[frames];
        right ??= new float[frames];
        for (var at = 0; at < frames; at += 480) s.Render(l.AsSpan(at, Math.Min(480, frames - at)), right.AsSpan(at, Math.Min(480, frames - at)));
        return l;
    }

    private static double Rms(float[] x, int from, int to) => Math.Sqrt(x[from..to].Sum(v => (double)v * v) / (to - from));

    [Fact]
    public void LoadsPresetsAndFallsBackLikeGm()
    {
        Assert.Equal(2, Font.Presets.Count);
        Assert.Equal("Sine", Font.Find(0, 5, drum: false).Name);
        Assert.Equal("Kit", Font.Find(0, 3, drum: true).Name);
    }

    [Theory]
    [InlineData(69)]
    [InlineData(57)]
    [InlineData(81)]
    public void ANoteSoundsAtItsPitch(int key)
    {
        var s = New();
        s.ProcessMidiMessage(0, 0x90, key, 127);
        var x = Render(s, Rate);
        var cents = TestSoundFont.CentsOff(x, Rate, Rate / 4, 4096, 440 * Math.Pow(2, (key - 69) / 12.0));
        Assert.True(Math.Abs(cents) < 1, $"{cents:F2} cents");
    }

    [Fact]
    public void PitchBendFollowsTheRpnRange()
    {
        var s = New();
        foreach (var (cc, v) in new[] { (101, 0), (100, 0), (6, 12) }) s.ProcessMidiMessage(0, 0xB0, cc, v);
        s.ProcessMidiMessage(0, 0xE0, 0, 96);   // 12288: half way up = +6 semitones
        s.ProcessMidiMessage(0, 0x90, 69, 127);
        var cents = TestSoundFont.CentsOff(Render(s, Rate), Rate, Rate / 4, 4096, 440 * Math.Pow(2, 0.5));
        Assert.True(Math.Abs(cents) < 1, $"{cents:F2} cents");
    }

    [Theory]
    [InlineData(64, 127, 127)]
    [InlineData(127, 64, 127)]
    [InlineData(127, 127, 64)]
    public void VelocityVolumeAndExpressionFollowTheSf2ConcaveCurve(int velocity, int volume, int expression)
    {
        double Level(int vel, int vol, int expr)
        {
            var s = New();
            s.ProcessMidiMessage(0, 0xB0, 7, vol);
            s.ProcessMidiMessage(0, 0xB0, 11, expr);
            s.ProcessMidiMessage(0, 0x90, 69, vel);
            return Rms(Render(s, Rate / 2), Rate / 4, Rate / 2);
        }
        // Each source: 960 cB × concave(1 - v/127) = 40·log10(v/127) dB for these values; velocity also lowers the filter cutoff (inaudible at 440 Hz).
        var expected = 40 * Math.Log10(Math.Min(velocity, Math.Min(volume, expression)) / 127.0);
        var measured = 20 * Math.Log10(Level(velocity, volume, expression) / Level(127, 127, 127));
        output.WriteLine($"expected {expected:F2} dB, measured {measured:F2} dB");
        Assert.InRange(measured, expected - 0.5, expected + 0.5);
    }

    [Fact]
    public void StartAndReleaseAreClickFreeAndTheVoiceEnds()
    {
        var s = New();
        s.ProcessMidiMessage(0, 0x90, 69, 127);
        var on = Render(s, Rate / 4);
        s.ProcessMidiMessage(0, 0x80, 69, 0);
        var off = Render(s, Rate / 2);
        var x = on.Concat(off).ToArray();
        var peak = x.Max(MathF.Abs);
        var maxStep = x.Zip(x.Skip(1), (a, b) => MathF.Abs(b - a)).Max();
        var sineStep = peak * 2 * MathF.PI * 440 / Rate;   // the steepest a clean 440 Hz sine of this peak moves per sample
        Assert.True(maxStep < sineStep * 1.5f, $"step {maxStep} vs sine {sineStep}");
        Assert.Equal(0, s.ActiveVoices);
    }

    [Fact]
    public void SustainHoldsUntilThePedalLifts()
    {
        var s = New();
        s.ProcessMidiMessage(0, 0xB0, 64, 127);
        s.ProcessMidiMessage(0, 0x90, 69, 127);
        s.ProcessMidiMessage(0, 0x80, 69, 0);
        Render(s, Rate / 2);
        Assert.Equal(1, s.ActiveVoices);
        s.ProcessMidiMessage(0, 0xB0, 64, 0);
        Render(s, Rate / 2);
        Assert.Equal(0, s.ActiveVoices);
    }

    [Fact]
    public void TheDrumChannelPlaysBank128()
    {
        var s = New();
        s.ProcessMidiMessage(9, 0x90, 36, 100);
        var x = Render(s, Rate / 10);
        Assert.True(Rms(x, 0, x.Length) > 1e-3);
    }

    [Fact]
    public void RenderingAllocatesNothing()
    {
        var s = New(effects: true);
        var l = new float[512];
        var r = new float[512];
        s.ProcessMidiMessage(0, 0x90, 60, 90);
        s.Render(l, r);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 50; i++)
        {
            s.ProcessMidiMessage(i % 9, 0x90, 48 + i % 24, 60 + i);
            s.ProcessMidiMessage(0, 0xE0, 0, 64 + i % 30);
            s.ProcessMidiMessage(0, 0xB0, 7, 100 - i);
            s.ProcessMidiMessage(i % 9, 0x80, 48 + (i + 12) % 24, 0);
            s.Render(l, r);
        }
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
    }

    [Fact]
    public void RenderCostIsMeasuredPerVoice()
    {
        foreach (var effects in new[] { false, true })
        {
            var s = New(effects);
            for (var k = 0; k < 32; k++) s.ProcessMidiMessage(k % 8, 0x90, 40 + k, 100);   // velocity 100: the filter runs
            Render(s, Rate / 10);
            var watch = Stopwatch.StartNew();
            Render(s, 4 * Rate);
            var ms = watch.Elapsed.TotalMilliseconds / 4;
            output.WriteLine($"32 voices, effects {effects}: {ms:F2} ms per second of audio, {ms / 32 * 1000:F0} us per voice-second");
            Assert.True(ms < 250);
        }
    }
}

