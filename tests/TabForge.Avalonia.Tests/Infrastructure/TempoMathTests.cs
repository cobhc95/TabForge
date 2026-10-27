using System;
using TabForge.Audio.Contracts;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the tempo-to-time conversions in <see cref="TempoMath"/> (quarter-note length, bar length, round trips).
/// Does not own: the bar map or the engine transport.
/// Tests: this class.
/// </summary>
public sealed class TempoMathTests
{
    private const double Tolerance = 1e-9;

    [Theory]
    [InlineData(120, 0.5)]
    [InlineData(60, 1.0)]
    [InlineData(90, 0.6666666666666666)]
    public void QuarterNoteSecondsAreSixtyOverBpm(double bpm, double expected)
    {
        Assert.True(Math.Abs(TempoMath.SecondsPerBeat(bpm) - expected) < Tolerance, $"60 / {bpm}");
    }

    [Theory]
    [InlineData(4, 4, 120, 2.0)]
    [InlineData(3, 4, 120, 1.5)]
    [InlineData(6, 8, 120, 1.5)]
    [InlineData(7, 8, 120, 1.75)]
    [InlineData(2, 2, 120, 2.0)]
    [InlineData(3, 4, 60, 3.0)]
    public void BarSecondsAreQuartersTimesSixtyOverBpm(int num, int den, double bpm, double expectedSec)
    {
        var quarters = num * 4.0 / den;
        Assert.True(Math.Abs(TempoMath.BeatsToSeconds(quarters, bpm) - expectedSec) < Tolerance);
    }

    [Theory]
    [InlineData(120, 500.0)]
    [InlineData(60, 1000.0)]
    [InlineData(90, 666.6666666666666)]
    public void MsPerBeatIsSecondsPerBeatTimesThousand(double bpm, double expectedMs)
    {
        Assert.True(Math.Abs(TempoMath.MsPerBeat(bpm) - expectedMs) < 1e-6);
        Assert.True(Math.Abs(TempoMath.MsPerBeat(bpm) - TempoMath.SecondsPerBeat(bpm) * 1000.0) < Tolerance);
    }

    [Theory]
    [InlineData(4, 120)]
    [InlineData(3, 60)]
    [InlineData(3.5, 90)]
    [InlineData(0.5, 200)]
    public void BeatsToSecondsAndBackIsIdentity(double beats, double bpm)
    {
        var sec = TempoMath.BeatsToSeconds(beats, bpm);
        Assert.True(Math.Abs(TempoMath.SecondsToBeats(sec, bpm) - beats) < Tolerance);
    }

    [Theory]
    [InlineData(0.5, 120)]
    [InlineData(2.0, 120)]
    [InlineData(3.0, 60)]
    [InlineData(1.5, 90)]
    public void SecondsToBeatsAndBackIsIdentity(double seconds, double bpm)
    {
        var beats = TempoMath.SecondsToBeats(seconds, bpm);
        Assert.True(Math.Abs(TempoMath.BeatsToSeconds(beats, bpm) - seconds) < Tolerance);
    }

    [Theory]
    [InlineData(0, 0.0)]
    [InlineData(1, 2.0)]
    [InlineData(10, 20.0)]
    public void BarStartTimeIsSumOfPreviousBarLengths(int bar, double expectedSec)
    {
        // 4/4 at a constant 120 BPM: every bar is 4 quarters = 2 s.
        Assert.True(Math.Abs(TempoMath.BeatsToSeconds(4.0 * bar, 120) - expectedSec) < Tolerance);
    }

    [Fact]
    public void TempoChangeAppliesFromChangeBarOnwardOnly()
    {
        // Bars 0-1 at 120 (2 s each), bar 2+ at 60 (4 s each): bar 2 at 4 s, bar 3 at 8 s.
        var bar0 = TempoMath.BeatsToSeconds(4, 120);
        var bar1 = TempoMath.BeatsToSeconds(4, 120);
        var bar2 = TempoMath.BeatsToSeconds(4, 60);
        Assert.True(Math.Abs(bar0 - 2.0) < Tolerance);
        Assert.True(Math.Abs(bar0 + bar1 - 4.0) < Tolerance);
        Assert.True(Math.Abs(bar0 + bar1 + bar2 - 8.0) < Tolerance);
    }

    [Fact]
    public void TimeSignatureChangeAppliesFromChangeBarOnwardOnly()
    {
        // 4/4 at 120 for bars 0-1 (2 s each), then 3/4: bar 2 at 4 s, bar 3 at 5.5 s.
        var fourFour = TempoMath.BeatsToSeconds(4 * 4.0 / 4, 120);
        var threeFour = TempoMath.BeatsToSeconds(3 * 4.0 / 4, 120);
        Assert.True(Math.Abs(fourFour - 2.0) < Tolerance);
        Assert.True(Math.Abs(threeFour - 1.5) < Tolerance);
        Assert.True(Math.Abs(fourFour + fourFour - 4.0) < Tolerance);
        Assert.True(Math.Abs(fourFour + fourFour + threeFour - 5.5) < Tolerance);
    }

    [Fact]
    public void ZeroBeatsIsZeroSecondsAndZeroSecondsIsZeroBeats()
    {
        Assert.Equal(0.0, TempoMath.BeatsToSeconds(0, 120));
        Assert.Equal(0.0, TempoMath.SecondsToBeats(0, 120));
    }
}
