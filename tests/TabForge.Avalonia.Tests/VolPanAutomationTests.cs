using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using TabForge.Audio;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>A song with mix automation resumed or live-pushed mid-song sends the value in force at the playback position (the last mix point at or before it, repeats and all-track changes included), not the track's base level.</summary>
public class VolPanAutomationTests
{
    private const int Slow = 400;   // bpm: a bar lasts 0.6 s

    [Fact]
    public void ResumeAfterTheVolumeMixPointPlaysTheAutomatedVolume()
    {
        var song = Song(16);
        song.Tracks[0].Measures[4].Cells[0].Mix = new MixChange { Volume = 4 };
        Assert.Equal(32, AfterResume(song, 7, 7));
    }

    [Fact]
    public void ResumeAfterThePanMixPointPlaysTheAutomatedPan()
    {
        var song = Song(16);
        song.Tracks[0].Measures[4].Cells[0].Mix = new MixChange { Pan = -4 };
        Assert.Equal(32, AfterResume(song, 7, 10));
    }

    [Fact]
    public void WithoutAutomationResumePlaysTheBaseVolume()
    {
        var song = Song(16);
        song.Tracks[0].Volume = 90;
        Assert.Equal(90, AfterResume(song, 7, 7));
    }

    [Fact]
    public void AnAllTracksChangeReachesEveryTrack()
    {
        var song = Song(16);
        song.Tracks[1].Measures[4].Cells[0].Mix = new MixChange { Volume = 5, AllTracks = true };
        Assert.Equal(40, AfterResume(song, 7, 7));
    }

    [Fact]
    public void TheSecondPassOfARepeatUsesTheValueTheFirstPassLeftBehind()
    {
        var song = Song(16);
        song.Tracks[0].Measures[2].RepeatStart = true;
        song.Tracks[0].Measures[3].RepeatEnd = true;
        song.Tracks[0].Measures[3].Cells[0].Mix = new MixChange { Volume = 4 };
        Assert.Equal(32, AfterResume(song, 2, 7, occurrence: 2, cell: 2));
    }

    [Fact]
    public void ASliderPushWhilePlayingSendsTheAutomatedValueAndKeepsTheBase()
    {
        var song = Song(16);
        song.Tracks[0].Measures[4].Cells[0].Mix = new MixChange { Volume = 4 };
        var (engine, output, channel) = PlayingAt(song, 7);
        using (engine)
        {
            song.Tracks[0].Volume = 50;
            output.Take();
            engine.PushLiveChannelState(song);
            Thread.Sleep(200);
            var got = output.Take();
            engine.Stop();
            Assert.Equal(32, got.Last(c => c.Ch == channel && c.Cc == 7).Value);
            Assert.Equal(50, song.Tracks[0].Volume);
        }
    }

    [Fact]
    public void ASliderPushAfterStartingMidSongSendsTheAutomatedValue()
    {
        var song = Song(16);
        song.Tracks[0].Measures[4].Cells[0].Mix = new MixChange { Volume = 4 };
        var output = new Recorder();
        using var engine = new PlaybackEngine(output);
        engine.Start(song, new PlaybackOptions { StartBar = 7 }, _ => { }, () => { });
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < 15000 && !output.Any()) Thread.Sleep(50);
        Thread.Sleep(300);
        song.Tracks[0].Volume = 50;
        output.Take();
        engine.PushLiveChannelState(song);
        Thread.Sleep(200);
        var got = output.Take();
        engine.Stop();
        Assert.Equal(32, got.Last(c => c.Ch == ChannelAllocator.Assign(song)[0] && c.Cc == 7).Value);
        Assert.Equal(50, song.Tracks[0].Volume);
    }

    [Fact]
    public void RepeatedBasePushesKeepSendingTheAutomatedValueUntilASeekBeforeThePoint()
    {
        var song = Song(16);
        song.Tracks[0].Measures[4].Cells[0].Mix = new MixChange { Volume = 4 };
        var output = new Recorder();
        using var engine = new PlaybackEngine(output);
        engine.Start(song, new PlaybackOptions { StartBar = 7 }, _ => { }, () => { });
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < 15000 && !output.Any()) Thread.Sleep(50);
        Thread.Sleep(300);
        var channel = ChannelAllocator.Assign(song)[0];
        var sent = new List<int>();
        foreach (var volume in new[] { 50, 60 })
        {
            song.Tracks[0].Volume = volume;
            output.Take();
            engine.PushLiveChannelState(song);
            Thread.Sleep(200);
            sent.Add(output.Take().Last(c => c.Ch == channel && c.Cc == 7).Value);
        }
        engine.Seek(song, 0, 0);
        Thread.Sleep(1500);
        output.Take();
        engine.PushLiveChannelState(song);
        Thread.Sleep(200);
        sent.Add(output.Take().Last(c => c.Ch == channel && c.Cc == 7).Value);
        engine.Stop();
        Assert.Equal(new[] { 32, 32, 60 }, sent);
    }

    private static int AfterResume(SongProject song, int bar, int cc, int? occurrence = null, int cell = 0)
    {
        var (engine, output, channel) = PlayingAt(song, bar, occurrence, cell);
        using (engine)
        {
            engine.Pause();
            output.Take();
            engine.Resume();
            Thread.Sleep(500);
            var got = output.Take();
            engine.Stop();
            return got.Last(c => c.Ch == channel && c.Cc == cc).Value;
        }
    }

    private static (PlaybackEngine, Recorder, int) PlayingAt(SongProject song, int bar, int? occurrence = null, int cell = 0)
    {
        var output = new Recorder();
        var engine = new PlaybackEngine(output);
        engine.Start(song, new PlaybackOptions(), _ => { }, () => { });
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < 15000 && !output.Any()) Thread.Sleep(50);
        engine.Seek(song, bar, cell, occurrence);
        Thread.Sleep(1200);
        return (engine, output, ChannelAllocator.Assign(song)[0]);
    }

    private static SongProject Song(int bars)
    {
        var song = new SongProject { Title = "T", Tempo = Slow, TimeSignatureNumerator = 4, TimeSignatureDenominator = 4 };
        for (var t = 0; t < 2; t++)
        {
            song.Tracks.Add(new TrackModel
            {
                Name = "T" + t, Kind = TrackKind.Guitar, MidiChannel = t,
                StringTunings = new List<int> { 64, 59, 55, 50, 45, 40 }, Measures = TemplateFactory.Measures(bars),
            });
            foreach (var m in song.Tracks[t].Measures)
                m.Cells = Enumerable.Range(0, 4).Select(_ => new TabCell { DurationDenominator = 4, Notes = { new TabNote { StringIndex = 0, Fret = 3, MidiValue = 67 } } }).ToList();
        }
        return song;
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
