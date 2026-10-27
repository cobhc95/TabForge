using System.Collections.Generic;
using System.Linq;
using TabForge.Avalonia.Transport;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Playback;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the live tempo readout's source: PlaybackTempo on the performed timeline (changes, ramps, repeats, D.S.), the stopped cursor tempo,
//   and LiveTempo's follow of a playing session (seek, change-only announcements).
// Does not own: the readouts' views or the tempo math itself (MusicTime).
// Tests: this class.
public sealed class LiveTempoTests
{
    /// <summary>24 bars of 4/4 at 150; 130 from bar 9 (index 8), 140 from bar 17 (index 16).</summary>
    private static SongProject Song(int bars = 24)
    {
        var project = new SongProject { Tempo = 150 };
        var track = new TrackModel { Name = "Keys", AudioInput = AudioInputs.Midi, StringTunings = new List<int> { 64, 59, 55, 50, 45, 40 } };
        for (var bar = 0; bar < bars; bar++)
        {
            var measure = new MeasureModel { Number = bar + 1 };
            measure.Cells[0].Notes.Add(new TabNote { StringIndex = 0, Fret = 5, MidiValue = 60 });
            track.Measures.Add(measure);
        }
        if (bars > 16) { track.Measures[8].TempoChange = 130; track.Measures[16].TempoChange = 140; }
        project.Tracks.Add(track);
        return project;
    }

    private static ScoreTimeline Compile(SongProject project) =>
        MidiTimelineBuilder.Build(project, new PlaybackOptions { RepeatExpansion = true, RespectMuteSolo = false });

    private static int AtBarStart(SongProject project, ScoreTimeline timeline, int performedIndex, double fraction = 0.1) =>
        PlaybackTempo.AtTime(project, timeline, timeline.Bars[performedIndex].StartMs + fraction * (timeline.Bars[performedIndex].EndMs - timeline.Bars[performedIndex].StartMs));

    [Fact]
    public void PlayingThroughTempoChangesAndSeekingShowsTheBarsTempo()
    {
        var song = Song();
        var timeline = Compile(song);
        Assert.Equal(150, AtBarStart(song, timeline, 0));
        Assert.Equal(150, AtBarStart(song, timeline, 7));
        Assert.Equal(130, AtBarStart(song, timeline, 8));
        Assert.Equal(130, AtBarStart(song, timeline, 15));
        Assert.Equal(140, AtBarStart(song, timeline, 16));
        Assert.Equal(140, AtBarStart(song, timeline, 19));   // a seek into bar 20 reads the same map
    }

    [Fact]
    public void AGradualRampMovesTheTempoBarByBarAndWithinTheBar()
    {
        var song = Song(8);
        var measures = song.Tracks[0].Measures;
        for (var bar = 1; bar < 5; bar++) measures[bar].TempoChange = 150 - 5 * bar;   // 145, 140, 135, 130: down each bar
        measures[5].MidBarTempos = new List<TempoPoint> { new(0, 110, 16) };           // then a ramp to 110 across bar 6
        var timeline = Compile(song);
        Assert.Equal(new[] { 150, 145, 140, 135, 130 }, new[] { 0, 1, 2, 3, 4 }.Select(i => AtBarStart(song, timeline, i, 0.01)));
        var start = AtBarStart(song, timeline, 5, 0.01);
        var mid = AtBarStart(song, timeline, 5, 0.5);
        var late = AtBarStart(song, timeline, 5, 0.95);
        Assert.True(start >= mid && mid > late && late > 110, $"{start} {mid} {late}");
        Assert.Equal(110, AtBarStart(song, timeline, 6, 0.1));   // the ramp's end carries on
    }

    [Fact]
    public void RepeatSecondPassAndDalSegnoJumpCarryTheTempoInPerformedOrder()
    {
        var song = Song(12);
        var measures = song.Tracks[0].Measures;
        measures[4].RepeatStart = true;
        measures[7].RepeatEnd = true;
        measures[5].TempoChange = 120;   // inside the repeat: the second pass starts at the carried 120
        var timeline = Compile(song);
        var secondPass = timeline.Bars.FindIndex(5, b => b.Bar == 4);
        Assert.True(secondPass > 4);
        Assert.Equal(120, AtBarStart(song, timeline, secondPass));
        Assert.Equal(150, AtBarStart(song, timeline, 4));

        var ds = Song(12);
        ds.Tracks[0].Measures[2].Directions = "Segno";
        ds.Tracks[0].Measures[5].TempoChange = 120;
        ds.Tracks[0].Measures[8].Directions = "DalSegno";
        ds.Tracks[0].Measures[8].TempoChange = null;
        var dsTimeline = Compile(ds);
        var afterJump = dsTimeline.Bars.FindIndex(9, b => b.Bar == 2);
        Assert.True(afterJump > 8);
        Assert.Equal(120, AtBarStart(ds, dsTimeline, afterJump));   // playback keeps the last tempo set, as the compiler does
    }

    [Fact]
    public void StoppedCursorShowsTheTempoInForceThere()
    {
        var song = Song();
        Assert.Equal(150, PlaybackTempo.AtCell(song, 0, 0));
        Assert.Equal(130, PlaybackTempo.AtCell(song, 9, 0));   // bar 10
        Assert.Equal(140, PlaybackTempo.AtCell(song, 19, 3));
    }

    [Fact]
    public void LiveTempoFollowsAPlayingSessionAndAnnouncesOnlyChanges()
    {
        var song = Song();
        var timeline = Compile(song);
        var engine = new AvaloniaPlaybackSessionTests.FakeEngine();
        var ui = new AvaloniaPlaybackSessionTests.FakeUiThread();
        using var sessions = new AvaloniaPlaybackSessions(ui, () => engine);
        var document = new SongDocument(() => null) { Project = song };
        var session = sessions.For(document)!;
        using var live = new LiveTempo(() => document, sessions, minIntervalMs: 0);
        var changes = 0;
        live.Changed += () => changes++;
        Assert.Null(live.Playing);

        session.PlayOrPause();
        engine.RaiseTimelineChanged(timeline);
        void At(int bar)
        {
            var b = timeline.Bars[bar];
            engine.Report(new PlaybackPosition { Bar = b.Bar, Cell = 0, BarFraction = 0.1, ElapsedMs = b.StartMs + 0.1 * (b.EndMs - b.StartMs) });
            ui.FrameTimer.Fire();
        }
        At(2);
        Assert.Equal(150, live.Playing);
        changes = 0;
        At(3);
        Assert.Equal(0, changes);   // same value: no announcement
        At(8);
        Assert.Equal(130, live.Playing);
        Assert.True(changes >= 1);
        At(19);   // a seek into bar 20
        Assert.Equal(140, live.Playing);

        session.Stop();
        Assert.Null(live.Playing);   // stopped: the readouts fall back to the cursor
    }
}
