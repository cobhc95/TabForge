using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NAudio.Wave;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine;
using TabForge.AudioEngine.Audio;
using TabForge.Avalonia.Transport;
using TabForge.Features.Recording.Rules;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the proof that the engine song position is the performed song second wherever playback starts (a later bar, the second pass of
// a repeat, a count-in, a slower speed), so an audio clip reads its source from the matching point; and that a clip player seeks its source
// to (song position - clip start + trim).
// Does not own: the anchor rules of play, pause, seek and drift (EnginePositionAnchorTests) or the clip specs sent (EngineAudibilityTests).
// Tests: this file.
public class ClipStartPositionTests
{
    private static (SongProject Song, PerformedBar[] Performed) Demo()
    {
        var song = DemoSongFactory.Create();
        return (song, SongExtent.Timing(song).Performed);
    }

    // The first bar the song plays twice, with the performed index of its second play.
    private static (int Bar, int SecondPlay) RepeatedBar(PerformedBar[] performed)
    {
        for (var i = 0; i < performed.Length; i++)
        {
            var again = Array.FindIndex(performed, i + 1, p => p.Bar == performed[i].Bar);
            if (again >= 0) return (performed[i].Bar, again);
        }
        throw new InvalidOperationException("the demo song has no repeat");
    }

    // The last performed bar that is played only once: a start there means the same bar and second whichever pass is asked for.
    private static PerformedBar LastPlayedOnce(PerformedBar[] performed) =>
        performed.Last(p => performed.Count(q => q.Bar == p.Bar) == 1);

    private static PlaybackOptions From(int bar, int? occurrence = null, bool countIn = false, double speed = 1) =>
        new() { StartBar = bar, StartOccurrence = occurrence, CountIn = countIn, CountInBars = 1, Speed = speed, RepeatExpansion = true, RespectMuteSolo = false, SkipClips = true };

    // Starts a session on a fake engine that hands over the timeline compiled with options and reports elapsedMs (negative: the start point).
    private static List<(bool Playing, double Sec)> Start(SongProject song, PlaybackOptions options, double elapsedMs)
    {
        var engine = new AvaloniaPlaybackSessionTests.FakeEngine();
        using var session = new AvaloniaPlaybackSession(engine, new AvaloniaPlaybackSessionTests.FakeUiThread(), () => song);
        var anchors = new List<(bool Playing, double Sec)>();
        session.ReportEnginePosition = (playing, sec, _) => anchors.Add((playing, sec));
        session.PlayOrPause();
        var timeline = MidiTimelineBuilder.Build(song, options);
        engine.RaiseTimelineChanged(timeline);
        engine.Report(new PlaybackPosition { ElapsedMs = elapsedMs < 0 ? timeline.PlayFromMs : elapsedMs });
        session.PumpFrame();
        return anchors;
    }

    [Fact]
    public void AStartAfterARepeatAnchorsThePerformedSecondOfThatBar()   // E3
    {
        var (song, performed) = Demo();
        var last = LastPlayedOnce(performed);
        var anchors = Start(song, From(last.Bar), -1);
        Assert.Equal(last.StartMs / 1000.0, anchors[0].Sec, 3);
        Assert.True(anchors[0].Sec > 5);   // the timeline second would have been 0
    }

    [Fact]
    public void AStartOnThePlayOfARepeatedBarAnchorsThatPlay()   // E4
    {
        var (song, performed) = Demo();
        var (bar, second) = RepeatedBar(performed);
        var engine = new AvaloniaPlaybackSessionTests.FakeEngine();
        using var session = new AvaloniaPlaybackSession(engine, new AvaloniaPlaybackSessionTests.FakeUiThread(), () => song);
        var anchors = new List<(bool Playing, double Sec)>();
        session.ReportEnginePosition = (playing, sec, _) => anchors.Add((playing, sec));
        session.Seek(new RepeatPosition(bar, 0, 2));   // the second play of that bar
        session.PlayOrPause();
        var timeline = MidiTimelineBuilder.Build(song, From(bar, 2));
        engine.RaiseTimelineChanged(timeline);
        engine.Report(new PlaybackPosition { ElapsedMs = timeline.PlayFromMs });
        session.PumpFrame();
        Assert.Equal(performed[second].StartMs / 1000.0, anchors[0].Sec, 3);
    }

    [Fact]
    public void ASeekWhilePlayingKeepsThePerformedClockOnLaterFrames()   // E5
    {
        var (song, performed) = Demo();
        var (bar, second) = RepeatedBar(performed);
        var engine = new AvaloniaPlaybackSessionTests.FakeEngine();
        using var session = new AvaloniaPlaybackSession(engine, new AvaloniaPlaybackSessionTests.FakeUiThread(), () => song);
        var anchors = new List<(bool Playing, double Sec)>();
        session.ReportEnginePosition = (playing, sec, _) => anchors.Add((playing, sec));
        session.PlayOrPause();
        engine.RaiseTimelineChanged(MidiTimelineBuilder.Build(song, From(0)));
        engine.Report(new PlaybackPosition { ElapsedMs = 0 });
        session.PumpFrame();

        session.Seek(new RepeatPosition(bar, 0, 2));
        var timeline = MidiTimelineBuilder.Build(song, From(bar, 2));
        engine.RaiseTimelineChanged(timeline);
        engine.Report(new PlaybackPosition { ElapsedMs = timeline.PlayFromMs });
        for (var i = 0; i < 3; i++) session.PumpFrame();

        var expected = performed[second].StartMs / 1000.0;
        Assert.All(anchors.Skip(1), a => Assert.Equal(expected, a.Sec, 2));
    }

    [Fact]
    public void ACountInHoldsThePositionAtTheStartBar()   // E6
    {
        var (song, performed) = Demo();
        var last = LastPlayedOnce(performed);
        var options = From(last.Bar, countIn: true);
        var timeline = MidiTimelineBuilder.Build(song, options);
        Assert.True(timeline.CountInMs > 0);
        var anchors = Start(song, options, timeline.CountInMs / 2);
        Assert.Equal(last.StartMs / 1000.0, anchors[0].Sec, 3);
    }

    [Fact]
    public void ACountInSendsOneHoldAnchorThenOneRunningAnchorAtItsEnd()
    {
        var (song, performed) = Demo();
        var last = LastPlayedOnce(performed);
        var timeline = MidiTimelineBuilder.Build(song, From(last.Bar, countIn: true));
        var engine = new AvaloniaPlaybackSessionTests.FakeEngine();
        using var session = new AvaloniaPlaybackSession(engine, new AvaloniaPlaybackSessionTests.FakeUiThread(), () => song);
        var anchors = new List<(bool Playing, double Sec)>();
        session.ReportEnginePosition = (playing, sec, _) => anchors.Add((playing, sec));
        session.PlayOrPause();
        engine.RaiseTimelineChanged(timeline);
        foreach (var ms in new[] { 100.0, 600.0, 1200.0 })
        {
            engine.Report(new PlaybackPosition { ElapsedMs = ms });
            session.PumpFrame();
        }
        Assert.Equal(new[] { (false, last.StartMs / 1000.0) }, anchors);
        engine.Report(new PlaybackPosition { ElapsedMs = timeline.CountInMs + 10 });
        session.PumpFrame();
        Assert.Equal(2, anchors.Count);
        Assert.True(anchors[1].Playing);
    }

    [Fact]
    public void ASpeedChangeOnTheSecondPassKeepsTheEnginePositionOnThatPass()
    {
        var (song, performed) = Demo();
        var (bar, second) = RepeatedBar(performed);
        var engine = new AvaloniaPlaybackSessionTests.FakeEngine();
        using var session = new AvaloniaPlaybackSession(engine, new AvaloniaPlaybackSessionTests.FakeUiThread(), () => song);
        var anchors = new List<(bool Playing, double Sec)>();
        session.ReportEnginePosition = (playing, sec, _) => anchors.Add((playing, sec));
        session.Seek(new RepeatPosition(bar, 0, 2));
        session.PlayOrPause();
        var timeline = MidiTimelineBuilder.Build(song, From(bar, 2));
        engine.RaiseTimelineChanged(timeline);
        engine.Report(new PlaybackPosition { ElapsedMs = timeline.PlayFromMs });
        session.PumpFrame();

        session.SetSpeed(0.5);   // restarts at the playing bar; the fake engine hands over a timeline that starts on pass 2
        var restarted = MidiTimelineBuilder.Build(song, From(bar, 2, speed: 0.5));
        engine.RaiseTimelineChanged(restarted);
        engine.Report(new PlaybackPosition { ElapsedMs = restarted.PlayFromMs });
        session.PumpFrame();
        Assert.Equal(performed[second].StartMs / 1000.0, anchors[^1].Sec, 2);
    }

    [Fact]
    public void AHalfSpeedStartAdvancesHalfASongSecondPerRealSecond()   // E7
    {
        var (song, performed) = Demo();
        var last = LastPlayedOnce(performed);
        var options = From(last.Bar, speed: 0.5);
        var timeline = MidiTimelineBuilder.Build(song, options);
        var anchors = Start(song, options, timeline.PlayFromMs + 1000);
        Assert.Equal(last.StartMs / 1000.0 + 0.5, anchors[0].Sec, 2);
    }

    [Fact]
    public void ALinearStartAtBarOneIsUnchanged()   // E8
    {
        var (song, _) = Demo();
        var anchors = Start(song, From(0), 1500);
        Assert.Equal(1.5, anchors[0].Sec, 3);
    }

    [Fact]
    public void AClipPlayerReadsItsSourceFromTheSongPosition()   // E1, E2
    {
        const int rate = 8000;
        var file = Path.Combine(Path.GetTempPath(), "tf-ramp-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            using (var writer = new WaveFileWriter(file, WaveFormat.CreateIeeeFloatWaveFormat(rate, 1)))
                for (var i = 0; i < 60 * rate; i++) writer.WriteSample((float)(i / (double)rate / 100.0));   // the sample value is the source second / 100
            // E1: the clip spans [0, 60), the song is at 20 s: the source is read from 20 s.
            Assert.Equal(0.20f, FirstSample(new ClipSpec(file, 0, 0, 60, 0, 0, 1), rate, songSec: 20), 3);
            // E2: trimmed in 5 s, placed at 10 s, the song at 20 s: source 15 s.
            Assert.Equal(0.15f, FirstSample(new ClipSpec(file, 10, 5, 50, 0, 0, 1), rate, songSec: 20), 3);
        }
        finally { File.Delete(file); }
    }

    private static float FirstSample(ClipSpec spec, int rate, double songSec)
    {
        using var player = new ClipPlayer(spec, rate, new EngineLogWriter(), offline: true);
        var left = new float[256];
        player.Mix(left, new float[256], 256, songSec, playing: true, speed: 1);
        return left[0];
    }
}
