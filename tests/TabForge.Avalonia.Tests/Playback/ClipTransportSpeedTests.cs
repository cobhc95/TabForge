using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using NAudio.Wave;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine;
using TabForge.AudioEngine.Audio;
using TabForge.Avalonia.Transport;
using TabForge.Playback;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: audio clips follow the transport speed (the read position stays in step with the engine song second at any speed and across a
// speed change), and the session anchors a speed change once and holds steady at a slower speed.
// Does not own: the anchor rules of play, pause and seek (EnginePositionAnchorTests) or the transport clock math (SongTransportSpeedTests).
// Tests: this file.
public class ClipTransportSpeedTests
{
    private const int Rate = 8000, Block = 512;

    // Runs blocks of Block frames through a streamed-style clip (the test plays the disk thread) and returns, per block, the first sample
    // (the source second / 100 of a ramp clip); a silent block is the re-seek loop. Each phase is (blocks, speed); the disk thread gets
    // laterPasses Service calls before each block of every phase after the first (0 = the re-fill after a speed change never lands).
    private static List<(double SongSec, float First)> Run(int laterPasses, params (int Blocks, double Speed)[] phases)
    {
        var file = Path.Combine(Path.GetTempPath(), "tf-ramp-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            using (var writer = new WaveFileWriter(file, WaveFormat.CreateIeeeFloatWaveFormat(Rate, 1)))
                for (var i = 0; i < 120 * Rate; i++) writer.WriteSample((float)(i / (double)Rate / 100.0));
            using var player = new ClipPlayer(new ClipSpec(file, 0, 0, 120, 0, 0, 1), Rate, new EngineLogWriter());
            var left = new float[Block];
            var right = new float[Block];
            var blocks = new List<(double, float)>();
            var songSec = 5.0;
            player.Mix(left, right, Block, songSec, true, phases[0].Speed);   // asks the disk thread for the place
            for (var phase = 0; phase < phases.Length; phase++)
            {
                var (count, speed) = phases[phase];
                var passes = phase == 0 ? 4 : laterPasses;
                for (var b = 0; b < count; b++)
                {
                    for (var pass = 0; pass < passes; pass++) player.Service();
                    Array.Clear(left);
                    player.Mix(left, right, Block, songSec, true, speed);
                    blocks.Add((songSec, left[0]));
                    songSec += Block * speed / Rate;
                }
            }
            return blocks;
        }
        finally { File.Delete(file); }
    }

    [Theory]
    [InlineData(0.25)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void AClipKeepsInStepWithTheSongAtAnySpeed(double speed)
    {
        var blocks = Run(4, (200, speed));
        foreach (var (sec, first) in blocks)
            Assert.InRange(first, (float)(sec / 100 - 0.003), (float)(sec / 100 + 0.003));   // never silent (no re-seek), within 0.3 source seconds
    }

    [Fact]
    public void ASpeedChangeMidPlayContinuesWithoutAGap()
    {
        var blocks = Run(4, (60, 1.0), (60, 0.5), (60, 2.0), (60, 1.0));
        foreach (var (sec, first) in blocks)
            Assert.InRange(first, (float)(sec / 100 - 0.003), (float)(sec / 100 + 0.003));
    }

    [Fact]
    public void BeforeTheReFillLandsAfterASpeedChangeTheClipPlaysOnWithABoundedError()
    {
        // No Service between the change and Mix: the old-stretch ring plays at the new advance (here 256 source frames too far per block).
        var blocks = Run(0, (20, 1.0), (8, 0.5));
        foreach (var (sec, first) in blocks)
            Assert.InRange(first, (float)(sec / 100 - 0.004), (float)(sec / 100 + 0.004));   // sound all along; error under 0.4 source seconds
    }

    // Starts a session on a fake engine at speed 1, plays from the third bar, then changes the speed; returns the anchors (playing, sec, speed).
    private static (List<(bool Playing, double Sec, double Speed)> Anchors, AvaloniaPlaybackSessionTests.FakeEngine Engine, AvaloniaPlaybackSession Session, TabForge.Models.SongProject Song) Play()
    {
        var song = DemoSongFactory.Create();
        var engine = new AvaloniaPlaybackSessionTests.FakeEngine();
        var session = new AvaloniaPlaybackSession(engine, new AvaloniaPlaybackSessionTests.FakeUiThread(), () => song);
        var anchors = new List<(bool, double, double)>();
        session.ReportEnginePosition = (playing, sec, speed) => anchors.Add((playing, sec, speed));
        session.PlayOrPause();
        var timeline = MidiTimelineBuilder.Build(song, engine.LastOptions!);
        engine.RaiseTimelineChanged(timeline);
        var bar = timeline.Bars[2];
        engine.Report(new PlaybackPosition { Bar = bar.Bar, Cell = 0, BarFraction = 0, ElapsedMs = bar.StartMs });
        session.PumpFrame();
        return (anchors, engine, session, song);
    }

    [Fact]
    public void ASpeedChangeMidPlayReAnchorsOnceAtTheSameSecond()
    {
        var (anchors, engine, session, song) = Play();
        using var _ = session;
        Assert.Single(anchors);
        session.SetSpeed(0.5);
        var timeline = MidiTimelineBuilder.Build(song, engine.LastOptions!);
        engine.RaiseTimelineChanged(timeline);
        engine.Report(new PlaybackPosition { Bar = engine.LastOptions!.StartBar, Cell = 0, BarFraction = 0, ElapsedMs = timeline.PlayFromMs });
        session.PumpFrame();
        Assert.Equal(2, anchors.Count);
        Assert.Equal(anchors[0].Sec, anchors[1].Sec, 1);   // the song second does not jump
        Assert.Equal(0.5, anchors[1].Speed);
    }

    [Fact]
    public void AHalfSpeedSessionHoldsSteadyWithoutAnotherAnchor()
    {
        var song = DemoSongFactory.Create();
        var engine = new AvaloniaPlaybackSessionTests.FakeEngine();
        using var session = new AvaloniaPlaybackSession(engine, new AvaloniaPlaybackSessionTests.FakeUiThread(), () => song);
        var anchors = new List<double>();
        session.ReportEnginePosition = (_, sec, _) => anchors.Add(sec);
        session.SetSpeed(0.5);
        session.PlayOrPause();
        var timeline = MidiTimelineBuilder.Build(song, engine.LastOptions!);
        engine.RaiseTimelineChanged(timeline);
        var clock = Stopwatch.StartNew();
        var settled = 0;
        for (var frame = 0; frame < 10; frame++)
        {
            engine.Report(new PlaybackPosition { ElapsedMs = timeline.PlayFromMs + clock.Elapsed.TotalMilliseconds });
            session.PumpFrame();
            if (frame == 2) settled = anchors.Count;   // the first frames run cold code: a slow one may anchor once more
            Thread.Sleep(30);
        }
        Assert.Equal(settled, anchors.Count);
    }
}
