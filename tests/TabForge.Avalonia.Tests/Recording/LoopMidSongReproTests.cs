using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using TabForge.Avalonia.Transport;
using TabForge.Models;
using TabForge.Playback;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Temporary repro: enabling the loop mid-play when playback started mid-song.
[Collection(TimingCollection.Name)]
public sealed class LoopMidSongReproTests
{
    private const int FastTempo = 480;
    private const int Bars = 6;
    private const int SongNote = 64;
    private const double TimeoutMs = 8000;
    private const int FrameSleepMs = 5;

    [Fact]
    public void LoopAreaBeforeStartBarWrapsWhenEnabledWhilePlaying()
    {
        var project = Song();
        var sink = new CaptureSink();
        using var engine = new PlaybackEngine(sink, new PlaybackPreferences { MetronomeBoost = false });
        using var session = new AvaloniaPlaybackSession(engine, new AvaloniaPlaybackSessionTests.FakeUiThread(), () => project);
        // Start mid-song with loop off, like clicking bar 5 then Space.
        session.Seek(4, 0);
        session.PlayOrPause();
        Assert.True(WaitFor(session, () => session.Transport.PlayheadBar >= 4), "never reached bar 5");
        // UI order in SelectionLoopLink.SetLoopActive: flag first, then range.
        session.SetLoop(true);
        session.SetLoopRange(1, 2);
        // A restart jump (4 -> 1) is not a loop: require two full passes of the area.
        var heard = new List<int>();
        var clock = Stopwatch.StartNew();
        var looped = false;
        while (clock.Elapsed.TotalMilliseconds < TimeoutMs && !looped)
        {
            session.PumpFrame();
            foreach (var bar in sink.HeardBars.Skip(heard.Count)) heard.Add(bar);
            looped = ContainsSequence(heard, new[] { 1, 2, 1, 2 });
            Thread.Sleep(FrameSleepMs);
        }
        Assert.True(looped, "no loop; heard: " + string.Join(",", heard));
    }

    private static bool ContainsSequence(List<int> heard, int[] want)
    {
        for (var i = 0; i + want.Length <= heard.Count; i++)
        {
            var match = true;
            for (var j = 0; j < want.Length; j++) match &= heard[i + j] == want[j];
            if (match) return true;
        }
        return false;
    }

    private static bool WaitFor(AvaloniaPlaybackSession session, Func<bool> done)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed.TotalMilliseconds < TimeoutMs)
        {
            session.PumpFrame();
            if (done()) return true;
            Thread.Sleep(FrameSleepMs);
        }
        return false;
    }

    private static SongProject Song()
    {
        var project = new SongProject { Tempo = FastTempo };
        var track = new TrackModel { StringTunings = new List<int> { 64, 59, 55, 50, 45, 40 }, MidiChannel = 0 };
        for (var bar = 0; bar < Bars; bar++)
        {
            var measure = new MeasureModel { Number = bar + 1 };
            measure.Cells[0].Notes.Add(new TabNote { StringIndex = 0, Fret = bar, MidiValue = SongNote + bar });
            measure.Cells[0].DurationDenominator = 4;
            track.Measures.Add(measure);
        }
        project.Tracks.Add(track);
        return project;
    }

    private sealed class CaptureSink : IMidiOutput
    {
        private readonly object _gate = new();
        private readonly List<int> _bars = new();
        public IReadOnlyList<MidiOutputDeviceInfo> Devices { get; } = Array.Empty<MidiOutputDeviceInfo>();

        public void Send(int deviceId, int status, int data1, int data2)
        {
            if ((status & 0xF0) != 0x90 || data2 == 0 || data1 < SongNote || data1 >= SongNote + Bars) return;
            lock (_gate) _bars.Add(data1 - SongNote);
        }

        public void SendMetronome(int deviceId, int status, int data1, int data2) { }
        public IReadOnlyList<int> HeardBars { get { lock (_gate) return _bars.ToArray(); } }
        public void ResetAll() { }
        public void Close() { }
        public void Dispose() { }
    }
}
