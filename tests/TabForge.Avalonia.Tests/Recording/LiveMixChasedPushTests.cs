using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using TabForge.Audio;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>A live volume push replaces the chased start value of a channel setup event, and a push racing a restore never sends a value that was never set.</summary>
public class LiveMixChasedPushTests
{
    [Fact]
    public void LivePushReplacesAChasedSetupValueAndASeekRestoreKeepsIt()
    {
        var (engine, song, output, channel) = Playing();
        using (engine)
        {
            var timeline = engine.Timeline!;
            var setup = timeline.ChannelSetup.First(e => e.TrackIndex == 0 && (e.Status & 0xF0) == 0xB0 && e.Data1 == 7);
            setup.IsChased = true;   // as a start after mix points leaves it
            song.Tracks[0].Volume = 3;
            engine.PushLiveChannelState(song, 0);
            Assert.False(setup.IsChased);
            output.Take();
            engine.RestoreChannelStateAt(timeline, 0, 0, 0);
            var got = output.Take();
            engine.Stop();
            Assert.Contains((channel, 7, setup.Data2), got);
            Assert.Equal(MixerGroups.Volume(song, song.Tracks[0]), setup.Data2);
        }
    }

    [Fact]
    public void PushRacingRestoreSendsOnlyValuesThatWereSet()
    {
        var (engine, song, output, channel) = Playing();
        using (engine)
        {
            var timeline = engine.Timeline!;
            var values = new[] { 20, 90 };
            var allowed = new HashSet<int>(values.Select(v => { song.Tracks[0].Volume = v; return MixerGroups.Volume(song, song.Tracks[0]); }));
            allowed.Add(MixerGroups.Volume(song, song.Tracks[0]));
            // The compiled setup starts at the song's default level. Establish a value that WAS set before the restorer starts, or a restore
            // that wins the race with the very first push legitimately re-sends that default (the test would read it as a lost update).
            song.Tracks[0].Volume = values[0];
            engine.PushLiveChannelState(song, 0);
            output.Take();
            var stop = false;
            var restorer = new Thread(() => { while (!Volatile.Read(ref stop)) engine.RestoreChannelStateAt(timeline, 0, 0, 0); });
            restorer.Start();
            for (var i = 0; i < 300; i++) { song.Tracks[0].Volume = values[i & 1]; engine.PushLiveChannelState(song, 0); }
            Volatile.Write(ref stop, true);
            restorer.Join();
            var sent = output.Take().Where(c => c.Ch == channel && c.Cc == 7).Select(c => c.Value).ToList();
            engine.Stop();
            Assert.All(sent, v => Assert.Contains(v, allowed));
        }
    }

    [Fact]
    public void RestoreRacingTheFirstPushNeverSendsTheCompiledDefault()
    {
        var (engine, song, output, channel) = Playing();
        using (engine)
        {
            var timeline = engine.Timeline!;
            var compiled = MixerGroups.Volume(song, song.Tracks[0]);
            song.Tracks[0].Volume = 20;
            var set = MixerGroups.Volume(song, song.Tracks[0]);
            Assert.NotEqual(compiled, set);
            output.Take();
            using var go = new ManualResetEventSlim();
            var stop = false;
            // The restorer is already running but cannot restore until the first push has replaced the compiled setup: the smallest window
            // between a push and a restore, forced without sleeps or relying on thread start order.
            var restorer = new Thread(() => { go.Wait(); while (!Volatile.Read(ref stop)) engine.RestoreChannelStateAt(timeline, 0, 0, 0); });
            restorer.Start();
            engine.PushLiveChannelState(song, 0);
            go.Set();
            for (var i = 0; i < 300; i++) { song.Tracks[0].Volume = i % 2 == 0 ? 20 : 90; engine.PushLiveChannelState(song, 0); }
            Volatile.Write(ref stop, true);
            restorer.Join();
            var sent = output.Take().Where(c => c.Ch == channel && c.Cc == 7).Select(c => c.Value).ToList();
            engine.Stop();
            Assert.All(sent, v => Assert.Contains(v, new[] { 20, 90 }));
            Assert.DoesNotContain(compiled, sent);
        }
    }

    private static (PlaybackEngine, SongProject, Recorder, int) Playing()
    {
        var song = FullDemoSongFactory.Create();
        var output = new Recorder();
        var engine = new PlaybackEngine(output);
        engine.Start(song, new PlaybackOptions(), _ => { }, () => { });
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < 15000 && !output.Any()) Thread.Sleep(100);
        Thread.Sleep(400); output.Take();
        return (engine, song, output, ChannelAllocator.Assign(song)[0]);
    }

    private sealed class Recorder : IMidiOutput
    {
        private readonly List<(int Ch, int Cc, int Value)> _cc = new();
        private bool _any;
        public IReadOnlyList<MidiOutputDeviceInfo> Devices { get; } = Array.Empty<MidiOutputDeviceInfo>();
        public bool Any() { lock (_cc) return _any; }
        public void Send(int deviceId, int status, int data1, int data2)
        {
            lock (_cc) { _any = true; if ((status & 0xF0) == 0xB0 && data1 == 7) _cc.Add((status & 0x0F, data1, data2)); }
        }
        public List<(int Ch, int Cc, int Value)> Take() { lock (_cc) { var c = _cc.ToList(); _cc.Clear(); return c; } }
        public void ResetAll() { }
        public void Close() { }
        public void Dispose() { }
    }
}
