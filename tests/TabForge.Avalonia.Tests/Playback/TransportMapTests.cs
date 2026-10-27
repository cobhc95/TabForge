using System;
using System.IO;
using TabForge.Audio.Contracts;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the song bar map transport in <see cref="TransportMap"/> (bar times, tempo and meter changes, round trips).
/// Does not own: the timeline that builds the map or the plug-in transport itself.
/// Tests: this class.
/// </summary>
public sealed class TransportMapTests
{
    private const double Tolerance = 1e-6;

    private static double Quarters(int num, int den) => num * 4.0 / den;

    private static TransportBar[] ConstantMap(int count, double tempo, int num, int den)
    {
        var bars = new TransportBar[count];
        var sec = 0.0;
        var ppq = 0.0;
        for (var i = 0; i < count; i++)
        {
            bars[i] = new TransportBar(sec, ppq, tempo, num, den);
            var q = Quarters(num, den);
            sec += q * 60.0 / tempo;
            ppq += q;
        }
        return bars;
    }

    private static TransportBar[] TempoChangeMap()
    {
        // Bars 0-1 at 120 (2 s each), bar 2+ at 60 (4 s each): starts 0, 2, 4, 8; ppq 0, 4, 8, 12.
        return new TransportBar[]
        {
            new(0, 0, 120, 4, 4),
            new(2, 4, 120, 4, 4),
            new(4, 8, 60, 4, 4),
            new(8, 12, 60, 4, 4),
        };
    }

    private static TransportBar[] MeterChangeMap()
    {
        // 4/4 at 120 for bars 0-1, then 3/4: starts 0, 2, 4, 5.5; ppq 0, 4, 8, 11.
        return new TransportBar[]
        {
            new(0, 0, 120, 4, 4),
            new(2, 4, 120, 4, 4),
            new(4, 8, 120, 3, 4),
            new(5.5, 11, 120, 3, 4),
        };
    }

    private static double BackToTime(TransportBar bar, double ppq) => bar.StartSec + (ppq - bar.StartPpq) * 60.0 / bar.Tempo;

    [Theory]
    [InlineData(0, 0.0, 0.0)]
    [InlineData(1, 2.0, 4.0)]
    [InlineData(10, 20.0, 40.0)]
    public void ConstantTempoBarStartIsSumOfPreviousBars(int bar, double expectedSec, double expectedPpq)
    {
        var bars = ConstantMap(11, 120, 4, 4);
        Assert.True(Math.Abs(bars[bar].StartSec - expectedSec) < Tolerance);
        var cursor = 0;
        Assert.True(TransportMap.Locate(bars, expectedSec, ref cursor, out var ppq, out var tempo, out _));
        Assert.True(Math.Abs(ppq - expectedPpq) < Tolerance);
        Assert.Equal(120, tempo);
    }

    [Theory]
    [InlineData(4, 4, 4.0)]
    [InlineData(3, 4, 3.0)]
    [InlineData(6, 8, 3.0)]
    [InlineData(7, 8, 3.5)]
    [InlineData(2, 2, 4.0)]
    public void BarQuartersAreNumeratorTimesFourOverDenominator(int num, int den, double expectedQuarters)
    {
        var bars = ConstantMap(2, 120, num, den);
        Assert.True(Math.Abs(bars[1].StartPpq - expectedQuarters) < Tolerance);
        Assert.True(Math.Abs(bars[1].StartSec - expectedQuarters * 60.0 / 120) < Tolerance);
        var cursor = 0;
        Assert.True(TransportMap.Locate(bars, bars[1].StartSec, ref cursor, out _, out _, out var meter));
        Assert.Equal(num, meter.Numerator);
        Assert.Equal(den, meter.Denominator);
    }

    [Fact]
    public void TempoChangeAppliesFromChangeBarOnwardOnly()
    {
        var bars = TempoChangeMap();
        Assert.True(Math.Abs(bars[2].StartSec - 4.0) < Tolerance);
        Assert.True(Math.Abs(bars[3].StartSec - 8.0) < Tolerance);
        var cursor = 0;
        Assert.True(TransportMap.Locate(bars, 3.9, ref cursor, out _, out var tempoBefore, out _));
        Assert.Equal(120, tempoBefore);
        Assert.True(TransportMap.Locate(bars, 4.0, ref cursor, out var ppqAt, out var tempoAt, out _));
        Assert.Equal(60, tempoAt);
        Assert.True(Math.Abs(ppqAt - 8.0) < Tolerance);
        Assert.True(TransportMap.Locate(bars, 8.0, ref cursor, out var ppqNext, out var tempoNext, out _));
        Assert.Equal(60, tempoNext);
        Assert.True(Math.Abs(ppqNext - 12.0) < Tolerance);
    }

    [Fact]
    public void TimeSignatureChangeAppliesFromChangeBarOnwardOnly()
    {
        var bars = MeterChangeMap();
        Assert.True(Math.Abs(bars[2].StartSec - 4.0) < Tolerance);
        Assert.True(Math.Abs(bars[3].StartSec - 5.5) < Tolerance);
        var cursor = 0;
        Assert.True(TransportMap.Locate(bars, 3.9, ref cursor, out _, out _, out var meterBefore));
        Assert.Equal((4, 4), (meterBefore.Numerator, meterBefore.Denominator));
        Assert.True(TransportMap.Locate(bars, 4.0, ref cursor, out var ppqAt, out _, out var meterAt));
        Assert.Equal((3, 4), (meterAt.Numerator, meterAt.Denominator));
        Assert.True(Math.Abs(ppqAt - 8.0) < Tolerance);
        Assert.True(TransportMap.Locate(bars, 5.5, ref cursor, out var ppqNext, out _, out _));
        Assert.True(Math.Abs(ppqNext - 11.0) < Tolerance);
    }

    [Theory]
    [InlineData(4, 4, 120, 2.0)]
    [InlineData(6, 8, 120, 1.5)]
    [InlineData(3, 4, 60, 3.0)]
    public void BarSecondsAreQuartersTimesSixtyOverBpm(int num, int den, double tempo, double expectedSec)
    {
        var bars = ConstantMap(2, tempo, num, den);
        Assert.True(Math.Abs(bars[1].StartSec - expectedSec) < Tolerance);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(2.0)]
    [InlineData(3.9)]
    [InlineData(4.0)]
    [InlineData(7.0)]
    [InlineData(8.0)]
    public void RoundTripTimeToPositionToTimeIsIdentity(double songSec)
    {
        var bars = TempoChangeMap();
        var cursor = 0;
        Assert.True(TransportMap.Locate(bars, songSec, ref cursor, out var ppq, out _, out _));
        var bar = bars[cursor];
        Assert.True(Math.Abs(BackToTime(bar, ppq) - songSec) < Tolerance);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(4.0)]
    [InlineData(5.0)]
    [InlineData(5.5)]
    public void RoundTripAcrossMeterChangeIsIdentity(double songSec)
    {
        var bars = MeterChangeMap();
        var cursor = 0;
        Assert.True(TransportMap.Locate(bars, songSec, ref cursor, out var ppq, out _, out _));
        Assert.True(Math.Abs(BackToTime(bars[cursor], ppq) - songSec) < Tolerance);
    }

    [Fact]
    public void EmptyMapLocatesNothing()
    {
        var cursor = 0;
        Assert.False(TransportMap.Locate(Array.Empty<TransportBar>(), 0, ref cursor, out _, out _, out _));
    }

    [Fact]
    public void WriteReadRoundTripPreservesBars()
    {
        var bars = TempoChangeMap();
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            TransportMap.Write(writer, bars);
        stream.Position = 0;
        TransportBar[] back;
        using (var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            back = TransportMap.Read(reader);
        Assert.Equal(bars.Length, back.Length);
        for (var i = 0; i < bars.Length; i++)
            Assert.Equal(bars[i], back[i]);
        Assert.Equal(stream.Length, stream.Position);
    }
}
