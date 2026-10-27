using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using TabForge.Playback;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the drawn playhead during a start: it holds at the start position until the scheduler's first real position, then only moves forward
///     (a slow device reset must not let it run ahead and snap back).
/// Does not own: seeks while playing (PlaybackSeekSwapTests) or the transport fields (PlaybackTransportTests).
/// Tests: this class.
/// </summary>
[Collection(TimingCollection.Name)]
public class PlaybackStartHoldTests
{
    private const int DeviceResetMs = 120;
    private const int SampleSpanMs = 300;
    private const int FirstReportWaitMs = 5000;
    private const int StartBar = 4;
    // The scheduler anchors the clock a moment before its first report; an unheld start runs up to about 31 ms ahead (MaxSleepMs + PositionIntervalMs).
    private const double ToleranceMs = 5;

    private sealed class SlowResetOutput : IMidiOutput
    {
        public IReadOnlyList<MidiOutputDeviceInfo> Devices { get; } = Array.Empty<MidiOutputDeviceInfo>();
        public void Send(int deviceId, int status, int data1, int data2) { }
        public void ResetAll() => Thread.Sleep(DeviceResetMs);
        public void Close() { }
        public void Dispose() { }
    }

    [Fact]
    public void ThePlayheadHoldsAtTheStartAndNeverMovesBackDuringAStart()
    {
        var song = FullDemoSongFactory.Create();
        using var engine = new PlaybackEngine(new SlowResetOutput());
        var reported = 0;
        engine.Start(song, new PlaybackOptions { StartBar = StartBar, LoopEndBar = song.Tracks.Max(t => t.Measures.Count) - 1 },
            _ => Interlocked.Increment(ref reported), () => { });
        var startMs = engine.Timeline!.PlayFromMs;
        var frame = new PlaybackPosition();
        var previous = double.NegativeInfinity;
        var clock = Stopwatch.StartNew();
        var firstReportAt = -1L;
        // Samples until the first report and SampleSpanMs beyond it (the device reset can be slow on a loaded machine).
        while (firstReportAt < 0 ? clock.ElapsedMilliseconds < FirstReportWaitMs : clock.ElapsedMilliseconds - firstReportAt < SampleSpanMs)
        {
            var anyReport = Volatile.Read(ref reported) > 0;
            if (anyReport && firstReportAt < 0) firstReportAt = clock.ElapsedMilliseconds;
            var ms = engine.AudiblePlayhead(frame).ElapsedMs;
            Assert.True(ms >= previous - ToleranceMs, $"the playhead moved back from {previous:0.0} to {ms:0.0} ms at {clock.ElapsedMilliseconds} ms");
            if (!anyReport) Assert.True(ms <= startMs + ToleranceMs, $"the playhead ran to {ms:0.0} ms before the first position (start {startMs:0.0})");
            previous = ms;
            Thread.Sleep(1);
        }
        engine.Stop();
        Assert.True(Volatile.Read(ref reported) > 0, "the scheduler reported a position");
    }
}
