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
/// Owns: the worker start: BeginStart returns at once (the caller's time stays small once warm), the engine reports playing while it compiles,
///     and the count-in still clicks before the music.
/// Does not own: the held playhead during the device reset (PlaybackStartHoldTests) or seeks (PlaybackSeekSwapTests).
/// Tests: this class.
/// </summary>
[Collection(TimingCollection.Name)]
public class PlaybackStartWorkerTests
{
    private const double MaxCallerMs = 2.0;
    private const int WarmRuns = 10;
    private const int StartWaitMs = 5000;
    private const int PercussionChannel = 9;
    private readonly ITestOutputHelper _log;

    public PlaybackStartWorkerTests(ITestOutputHelper log) => _log = log;

    private sealed class Recorder : IMidiOutput
    {
        private readonly List<(int Status, int Data1)> _noteOns = new();
        public IReadOnlyList<MidiOutputDeviceInfo> Devices { get; } = Array.Empty<MidiOutputDeviceInfo>();
        public void Send(int deviceId, int status, int data1, int data2)
        {
            if ((status & 0xF0) == 0x90 && data2 > 0) lock (_noteOns) _noteOns.Add((status, data1));
        }
        public (int Status, int Data1)[] NoteOns { get { lock (_noteOns) return _noteOns.ToArray(); } }
        public void Clear() { lock (_noteOns) _noteOns.Clear(); }
        public void ResetAll() { }
        public void Close() { }
        public void Dispose() { }
    }

    [Fact]
    public void BeginStartReturnsAtOnceAndTheCountInStillClicksFirst()
    {
        var song = FullDemoSongFactory.Create();
        var output = new Recorder();
        using var engine = new PlaybackEngine(output);
        var options = new PlaybackOptions { StartBar = 8, CountIn = true, CountInBars = 1, Metronome = false, LoopEndBar = song.Tracks.Max(t => t.Measures.Count) - 1 };

        var syncMs = Time(() => engine.Start(song, options, _ => { }, () => { }));
        engine.Stop();
        var callerMs = double.MaxValue;
        for (var run = 0; run < WarmRuns; run++)
        {
            engine.Stop();
            output.Clear();
            callerMs = Math.Min(callerMs, Time(() => engine.BeginStart(song, options, _ => { }, () => { })));
            Assert.True(engine.IsPlaying, "the engine reports playing while the start compiles");
            var wait = Stopwatch.StartNew();
            while (output.NoteOns.Length < 2 && wait.ElapsedMilliseconds < StartWaitMs) Thread.Sleep(1);
        }
        var first = output.NoteOns.FirstOrDefault();
        engine.Stop();
        _log.WriteLine($"Start (compile on the caller) {syncMs:0.00} ms; BeginStart (warm) {callerMs:0.00} ms");
        Assert.True(first != default, "the worker start played");
        Assert.True((first.Status & 0x0F) == PercussionChannel, $"the first note is a count-in click, not channel {first.Status & 0x0F}");
        Assert.True(callerMs < MaxCallerMs, $"BeginStart took {callerMs:0.00} ms on the caller (Start took {syncMs:0.00} ms)");
    }

    private static double Time(Action action)
    {
        var clock = Stopwatch.StartNew();
        action();
        return clock.Elapsed.TotalMilliseconds;
    }
}

/// <summary>Tests that measure the caller's time run alone: other tests on the pool would add their load to the measured milliseconds.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TimingCollection
{
    public const string Name = "Timing measurements";
}
