using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using TabForge.Playback;
using TabForge.Presets;
using Xunit;
using Xunit.Abstractions;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: Stop never waits for the scheduler thread: it returns at once while that thread is stuck in a slow device call, and the stopped run
///     starts no note and reports no position afterwards.
/// Does not own: the engine process stop (AudioEngineClient.Stop) or the start paths (PlaybackStartWorkerTests).
/// Tests: this class.
/// </summary>
[Collection(TimingCollection.Name)]
public class PlaybackStopNoWaitTests
{
    private const int StuckSendMs = 300;
    private const double MaxStopMs = 10;
    private const int PlayBeforeStopMs = 400;
    private readonly ITestOutputHelper _log;

    public PlaybackStopNoWaitTests(ITestOutputHelper log) => _log = log;

    /// <summary>Holds the scheduler in one note-on send once armed; counts note-ons that arrive after Stop.</summary>
    private sealed class StuckOutput : IMidiOutput
    {
        private int _armed;
        private int _stopped;
        private int _afterStop;
        public IReadOnlyList<MidiOutputDeviceInfo> Devices { get; } = Array.Empty<MidiOutputDeviceInfo>();
        public void Arm() => Volatile.Write(ref _armed, 1);
        public void MarkStopped() => Volatile.Write(ref _stopped, 1);
        public int NoteOnsAfterStop => Volatile.Read(ref _afterStop);
        public bool Stuck { get; private set; }
        public void Send(int deviceId, int status, int data1, int data2)
        {
            if ((status & 0xF0) != 0x90 || data2 == 0) return;
            if (Volatile.Read(ref _stopped) == 1) Interlocked.Increment(ref _afterStop);
            if (Interlocked.Exchange(ref _armed, 0) == 1) { Stuck = true; Thread.Sleep(StuckSendMs); }
        }
        public void ResetAll() { }
        public void Close() { }
        public void Dispose() { }
    }

    [Fact]
    public void StopReturnsAtOnceWhileTheSchedulerIsStuckAndTheStoppedRunStaysSilent()
    {
        var song = FullDemoSongFactory.Create();
        var output = new StuckOutput();
        using var engine = new PlaybackEngine(output);
        var reportsAfterStop = 0;
        var stopped = 0;
        engine.Start(song, new PlaybackOptions { LoopEndBar = song.Tracks.Max(t => t.Measures.Count) - 1 },
            _ => { if (Volatile.Read(ref stopped) == 1) Interlocked.Increment(ref reportsAfterStop); }, () => { });
        Thread.Sleep(PlayBeforeStopMs);
        output.Arm();
        var wait = Stopwatch.StartNew();
        while (!output.Stuck && wait.ElapsedMilliseconds < StuckSendMs) Thread.Sleep(1);

        var clock = Stopwatch.StartNew();
        engine.Stop();
        var stopMs = clock.Elapsed.TotalMilliseconds;
        Volatile.Write(ref stopped, 1);
        output.MarkStopped();
        Thread.Sleep(StuckSendMs * 2);
        _log.WriteLine($"Stop with the scheduler stuck in a send: {stopMs:0.00} ms");

        Assert.True(output.Stuck, "the scheduler reached the slow send");
        Assert.True(stopMs < MaxStopMs, $"Stop took {stopMs:0.00} ms");
        Assert.Equal(0, output.NoteOnsAfterStop);
        Assert.Equal(0, Volatile.Read(ref reportsAfterStop));
    }
}
