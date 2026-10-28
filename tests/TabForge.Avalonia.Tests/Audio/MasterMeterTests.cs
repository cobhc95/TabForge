using System;
using System.IO;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine.Mixing;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the engine side of the master level meter: peak, RMS and short-term loudness (EBU R128 / Tech 3341) on known signals, the clip count and the
/// hand-over through the shared block.
/// Does not own: the bar model and the saved choices (MasterMeterModelTests) or the drawing (a capture twin shows it).
/// Tests: this class.
/// </summary>
public class MasterMeterTests
{
    private const int Block = 480;

    /// <summary>Runs a 1 kHz sine of the given peak amplitudes through the meter for some seconds; returns the meter and the largest block peaks seen.</summary>
    internal static (MasterMeter Meter, float PeakLeft, float PeakRight) Run(int rate, double seconds, double left, double right, SharedBlock? shared = null)
    {
        var meter = new MasterMeter(rate, shared);
        var bufferL = new float[Block];
        var bufferR = new float[Block];
        float peakL = 0, peakR = 0;
        var total = (int)(seconds * rate);
        for (var done = 0; done < total; done += Block)
        {
            for (var i = 0; i < Block; i++)
            {
                var phase = Math.Sin(2 * Math.PI * 1000 * (done + i) / rate);
                bufferL[i] = (float)(left * phase);
                bufferR[i] = (float)(right * phase);
            }
            meter.Process(bufferL, bufferR, Block);
            peakL = Math.Max(peakL, meter.PeakLeft); peakR = Math.Max(peakR, meter.PeakRight);
        }
        return (meter, peakL, peakR);
    }

    private static double Db(double linear) => 20 * Math.Log10(linear);

    [Theory]
    [InlineData(48000)]
    [InlineData(44100)]
    public void SineAtMinus20ReadsPeakMinus20AndRmsMinus23(int rate)
    {
        var (meter, peakL, _) = Run(rate, 4, 0.1, 0.1);
        Assert.InRange(Db(peakL), -20.05, -19.95);
        Assert.InRange(Db(meter.RmsLeft), -23.1, -22.9);
        Assert.InRange(Db(meter.RmsRight), -23.1, -22.9);
    }

    [Theory]
    [InlineData(48000)]
    [InlineData(44100)]
    public void ShortTermLoudnessFollowsEbuTech3341(int rate)
    {
        // One channel of -20 dBFS: a mono sine reads 3.01 dB under its peak, so -23.0 LUFS.
        Assert.InRange(Run(rate, 4, 0.1, 0).Meter.ShortTermLufs, -23.3, -22.7);
        // EBU Tech 3341 case 1: a stereo 1 kHz sine at -23 dBFS (both channels) reads -23.0 LUFS.
        var level = Math.Pow(10, -23.0 / 20);
        Assert.InRange(Run(rate, 4, level, level).Meter.ShortTermLufs, -23.3, -22.7);
        // Both channels at -20 dBFS are twice the power of one: -20.0 LUFS.
        Assert.InRange(Run(rate, 4, 0.1, 0.1).Meter.ShortTermLufs, -20.3, -19.7);
    }

    [Fact]
    public void SilenceReadsAsSilentLoudnessAndNoPeak()
    {
        var meter = new MasterMeter(48000);
        Assert.Equal(SharedBlock.SilentLufs, meter.ShortTermLufs);
        var silence = new float[Block];
        for (var i = 0; i < 48000 * 4 / Block; i++) meter.Process(silence, silence, Block);
        Assert.Equal(SharedBlock.SilentLufs, meter.ShortTermLufs);
        Assert.Equal(0, meter.PeakLeft);
    }

    [Fact]
    public void ThePeakIsReadBeforeTheOutputLevelAndTheClamp()
    {
        // A -20 dBFS sine handed over as it is (before any master gain): the peak reads -20 dBFS, whatever the Windows volume does after.
        var meter = new MasterMeter(48000);
        var left = new float[Block];
        var right = new float[Block];
        for (var i = 0; i < Block; i++) left[i] = right[i] = (float)(0.1 * Math.Sin(2 * Math.PI * 1000 * i / 48000.0));
        meter.Process(left, right, Block);
        Assert.InRange(Db(meter.PeakLeft), -20.05, -19.95);
        // An over-full-scale sample is counted as a clip and its peak is not clamped to 1.
        left[5] = 1.2f; right[5] = 0f;
        meter.Process(left, right, Block);
        Assert.Equal(1, meter.ClipCount);
        Assert.Equal(1.2f, meter.PeakLeft, 3);
    }

    [Fact]
    public void ClipsAreCountedFromFullScaleUp()
    {
        Assert.Equal(0, Run(48000, 1, 0.99, 0.99).Meter.ClipCount);
        Assert.True(Run(48000, 1, 1.0, 0.5).Meter.ClipCount > 0);
        var clipped = Run(48000, 1, 1.0, 1.0).Meter;
        Assert.True(clipped.ClipCount > 0);
        Assert.Equal(1f, clipped.PeakLeft, 3);
    }

    [Fact]
    public void ASineJustUnderZeroDbfsDoesNotClipAndOneOverLatchesTheLightUntilReset()
    {
        var model = new TabForge.Audio.Metering.MasterMeterModel(() => new TabForge.Audio.Metering.MasterMeterSettings());
        var under = Run(48000, 1, Math.Pow(10, -0.1 / 20), Math.Pow(10, -0.1 / 20)).Meter;
        var over = Run(48000, 1, Math.Pow(10, 0.5 / 20), Math.Pow(10, 0.5 / 20)).Meter;
        Assert.Equal(0, under.ClipCount);
        Assert.True(over.ClipCount > 0);
        model.Feed(TabForge.Audio.Metering.MasterMeterReading.Silent(clips: under.ClipCount), 0);
        model.Feed(TabForge.Audio.Metering.MasterMeterReading.Silent(clips: under.ClipCount), 33);
        Assert.False(model.ClipLit);
        model.Feed(TabForge.Audio.Metering.MasterMeterReading.Silent(clips: over.ClipCount), 66);
        model.Feed(TabForge.Audio.Metering.MasterMeterReading.Silent(clips: over.ClipCount), 99);
        Assert.True(model.ClipLit);   // latched while the signal is gone
        Assert.True(model.ResetClip());
        Assert.False(model.ClipLit);
    }

    [Fact]
    public void ReadingsReachTheSharedBlockAndPeaksAreTakenOnce()
    {
        var folder = Path.Combine(Path.GetTempPath(), "tf-meter-" + Guid.NewGuid().ToString("N"));
        var name = EngineNames.SharedMemory(Guid.NewGuid().ToString("N"));
        try
        {
            using var engine = SharedBlock.CreatePortable(name, folder);
            using var app = SharedBlock.OpenPortable(name, folder);
            var (meter, _, _) = Run(48000, 4, 0.5, 0.25, engine);
            var (left, right) = app.TakeMasterPeaks();
            Assert.Equal(0.5f, left, 2);
            Assert.Equal(0.25f, right, 2);
            Assert.Equal((0f, 0f), app.TakeMasterPeaks());   // taken: the next call starts again from silence
            var levels = app.MasterLevels;
            Assert.Equal(meter.RmsLeft, levels.RmsLeft, 4);
            Assert.Equal(meter.ShortTermLufs, levels.ShortTermLufs, 3);
            Assert.Equal(0, levels.Clips);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
}
