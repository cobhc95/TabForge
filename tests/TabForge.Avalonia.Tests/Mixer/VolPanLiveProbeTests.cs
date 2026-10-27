using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using TabForge.Audio;
using TabForge.Playback;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>Probe: a volume / pan change on a GM track while playing must reach the MIDI output (CC7 / CC10) at once. The engine half of each writer is the one PlaybackEngine.PushLiveChannelState; resume reads the live song itself.</summary>
public class VolPanLiveProbeTests
{
    [Fact]
    public void TrackListSliderCommitSendsCc7AndCc10WhilePlaying()
    {
        var (engine, song, output, channel) = Playing();
        using (engine)
        {
            song.Tracks[0].Volume = 0; song.Tracks[0].Pan = 0;   // what TimelineInputController.CommitSlider does
            engine.PushLiveChannelState(song);           // then the TrackLevelChanged handler (MainWindowEngineSync -> PushLiveMix -> SetMuteSolo) reaches the engine
            Thread.Sleep(500);
            var got = output.Take();
            engine.Stop();
            Assert.True(got.Contains((channel, 7, 0)) && got.Contains((channel, 10, 0)), "nothing reached the output: " + string.Join(",", got));
        }
    }

    [Fact]
    public void MixerPushSendsCc7AndCc10WhilePlaying()
    {
        var (engine, song, output, channel) = Playing();
        using (engine)
        {
            song.Tracks[0].Volume = 0; song.Tracks[0].Pan = 0;
            engine.SetMuteSolo(song);   // AvaloniaPlaybackSessions.PushLiveMix -> ApplyMuteSolo (the playback-engine half of the mixer push)
            Thread.Sleep(500);
            var got = output.Take();
            engine.Stop();
            Assert.True(got.Contains((channel, 7, 0)) && got.Contains((channel, 10, 0)), "nothing reached the output: " + string.Join(",", got));
        }
    }

    [Fact]
    public void ResumeAfterPauseUsesTheNewLevels()
    {
        var (engine, song, output, channel) = Playing();
        using (engine)
        {
            engine.Pause();
            song.Tracks[0].Volume = 0; song.Tracks[0].Pan = 0;
            output.Take();
            engine.Resume();
            Thread.Sleep(500);
            var got = output.Take();
            engine.Stop();
            Assert.True(got.Contains((channel, 7, 0)) && got.Contains((channel, 10, 0)), "resume kept the old levels: " + string.Join(",", got));
        }
    }

    private static (PlaybackEngine, TabForge.Models.SongProject, Recorder, int) Playing()
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
            lock (_cc) { _any = true; if ((status & 0xF0) == 0xB0 && (data1 == 7 || data1 == 10)) _cc.Add((status & 0x0F, data1, data2)); }
        }
        public List<(int Ch, int Cc, int Value)> Take() { lock (_cc) { var c = _cc.ToList(); _cc.Clear(); return c; } }
        public void ResetAll() { }
        public void Close() { }
        public void Dispose() { }
    }
}
