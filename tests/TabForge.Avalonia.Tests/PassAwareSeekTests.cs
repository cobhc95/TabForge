using System;
using System.Collections.Generic;
using System.Linq;
using TabForge.Avalonia.Transport;
using TabForge.Features.Recording.Rules;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the S4a pass-aware seek core: the optional occurrence start on PlaybackOptions (null = the unchanged compile),
//   the second-performance start over a repeat and the session's performed-second/pass accessor.
// Does not own: the UI seek/label (S4b), the fold or the repeat mapping itself (RepeatMapTests).
// Tests: this class.
public sealed class PassAwareSeekTests
{
    private const int Bars = 12;

    /// <summary>Bars 0-11 at 120 bpm (2 s each); bars 4-7 repeat twice (written bars 5-8 in the spec). No notes in the repeat bars' first cell
    /// are needed: cell 0 and the mid-bar cell 8 both carry one so a mid-bar pass start has an audible event.</summary>
    private static SongProject Song(int bars = Bars, bool repeat = true)
    {
        var project = new SongProject { Tempo = 120 };
        var track = new TrackModel
        {
            Name = "Keys", AudioInput = AudioInputs.Midi, MidiChannel = 0,
            StringTunings = new List<int> { 64, 59, 55, 50, 45, 40 },
        };
        for (var bar = 0; bar < bars; bar++)
        {
            var measure = new MeasureModel { Number = bar + 1 };
            measure.Cells[0].Notes.Add(new TabNote { StringIndex = 0, Fret = 5, MidiValue = 60 + bar });
            measure.Cells[0].DurationDenominator = 8;
            measure.Cells[8].Notes.Add(new TabNote { StringIndex = 0, Fret = 7, MidiValue = 72 + bar });
            measure.Cells[8].DurationDenominator = 8;
            track.Measures.Add(measure);
        }
        if (repeat)
        {
            track.Measures[4].RepeatStart = true;
            track.Measures[7].RepeatEnd = true;
        }
        project.Tracks.Add(track);
        return project;
    }

    private static PlaybackOptions Options(int bar, int cell, int? occurrence = null) => new()
    {
        StartBar = bar,
        StartCell = cell,
        StartOccurrence = occurrence,
        RepeatExpansion = true,
        RespectMuteSolo = false,
    };

    private static void AssertSameEvents(ScoreTimeline expected, ScoreTimeline actual)
    {
        Assert.Equal(expected.Events.Count, actual.Events.Count);
        for (var i = 0; i < expected.Events.Count; i++)
        {
            var a = expected.Events[i];
            var b = actual.Events[i];
            Assert.Equal(a.TimeMs, b.TimeMs, 6);
            Assert.Equal(a.DeviceId, b.DeviceId);
            Assert.Equal(a.Channel, b.Channel);
            Assert.Equal(a.Status, b.Status);
            Assert.Equal(a.Data1, b.Data1);
            Assert.Equal(a.Data2, b.Data2);
            Assert.Equal(a.IsSetup, b.IsSetup);
            Assert.Equal(a.IsMetronome, b.IsMetronome);
            Assert.Equal(a.IsCountInClick, b.IsCountInClick);
        }
        Assert.Equal(expected.Bars.Select(bar => bar.Bar).ToArray(), actual.Bars.Select(bar => bar.Bar).ToArray());
        Assert.Equal(expected.PlayFromMs, actual.PlayFromMs, 6);
    }

    [Fact]
    public void Golden_ANullStartFieldIsTheUnchangedCompileForEverySeek()
    {
        var song = Song();
        foreach (var (bar, cell) in new[] { (0, 0), (4, 0), (5, 0), (5, 8), (8, 0), (11, 0) })
        {
            var omitted = MidiTimelineBuilder.Build(song, Options(bar, cell));
            var explicitNull = MidiTimelineBuilder.Build(song, Options(bar, cell, occurrence: null));

            AssertSameEvents(omitted, explicitNull);
        }

        // Pass 1 of a repeated bar is the old seek, event for event.
        AssertSameEvents(
            MidiTimelineBuilder.Build(song, Options(5, 8)),
            MidiTimelineBuilder.Build(song, Options(5, 8, occurrence: 1)));

        // A start at written bar 6 follows the repeat back to written bar 5, then plays on through the later pass.
        Assert.Equal(new[] { 5, 6, 7, 4, 5, 6, 7, 8, 9, 10, 11 },
            MidiTimelineBuilder.Build(song, Options(5, 0)).Bars.Select(bar => bar.Bar).ToArray());
        Assert.Equal(new[] { 4, 5, 6, 7, 4, 5, 6, 7, 8, 9, 10, 11 },
            MidiTimelineBuilder.Build(song, Options(4, 0)).Bars.Select(bar => bar.Bar).ToArray());
    }

    [Fact]
    public void Pass2SeekStartsAtTheSecondOccurrenceAndPlaysOnThroughThatPass()
    {
        var song = Song();
        var performed = SongExtent.Timing(song).Performed;
        Assert.Equal(19.0, RepeatMap.ToPerformed(performed, new RepeatPosition(5, 0.5, 2)), 6);

        var timeline = MidiTimelineBuilder.Build(song, Options(5, 8, occurrence: 2));

        // Starts at written bar 6, pass 2 (not bar 5 pass 1 and not bar 6 pass 1): then bars 7-8 of that pass, then bar 9.
        Assert.Equal(new[] { 5, 6, 7, 8, 9, 10, 11 }, timeline.Bars.Select(bar => bar.Bar).ToArray());
        Assert.Equal(1000, timeline.PlayFromMs, 6);
        var firstNoteOn = timeline.Events.Where(e => e.IsNoteOn && !e.IsMetronome && !e.IsSetup).OrderBy(e => e.TimeMs).First();
        Assert.Equal(timeline.PlayFromMs, firstNoteOn.TimeMs, 6);
        Assert.Equal(19.0, TimelineSongClock.SongSecAt(performed, timeline, firstNoteOn.TimeMs, preferSongMs: 19), 6);
        var firstNote = timeline.Notes.OrderBy(note => note.OnsetMs).First();
        Assert.Equal(5, firstNote.Bar);

        // After 19 s (timeline 1 s) bars 7-8 of pass 2 play, then bar 9 at performed 24 s (timeline 6 s).
        var bar9 = Assert.Single(timeline.Bars, bar => bar.Bar == 8);
        Assert.Equal(6000, bar9.StartMs, 6);
        Assert.Equal(24.0, TimelineSongClock.SongSecAt(performed, timeline, bar9.StartMs, preferSongMs: 19), 6);
    }

    [Fact]
    public void NoRepeatSong_Pass1SeekIsTheUnchangedSeek()
    {
        var song = Song(repeat: false);
        var performed = SongExtent.Timing(song).Performed;

        Assert.Equal(4.5, RepeatMap.ToPerformed(performed, new RepeatPosition(2, 0.25, 1)), 6);
        AssertSameEvents(
            MidiTimelineBuilder.Build(song, Options(2, 4)),
            MidiTimelineBuilder.Build(song, Options(2, 4, occurrence: 1)));
    }

    [Fact]
    public void SessionPass1SeekMatchesTheOldSeekCall()
    {
        var song = Song(repeat: false);
        var engine = new SeekEngine { IsPlaying = true };
        using var session = new AvaloniaPlaybackSession(engine, new AvaloniaPlaybackSessionTests.FakeUiThread(), () => song);

        session.Seek(new RepeatPosition(2, 0.25, 1));

        (int Bar, int Cell, int? Occurrence)? expected = (2, 4, null);
        Assert.Equal(expected, engine.LastSeek);
    }

    [Fact]
    public void AccessorReportsThePlayingPerformedSecondAndPass()
    {
        var song = Song();
        var engine = new SeekEngine { IsPlaying = true };
        using var session = new AvaloniaPlaybackSession(engine, new AvaloniaPlaybackSessionTests.FakeUiThread(), () => song);

        session.Seek(new RepeatPosition(5, 0.5, 2));

        // The session maps the pass-aware position through RepeatMap and hands the occurrence to the engine.
        (int Bar, int Cell, int? Occurrence)? expected = (5, 8, 2);
        Assert.Equal(expected, engine.LastSeek);
        var timeline = MidiTimelineBuilder.Build(song, Options(5, 8, occurrence: 2));
        engine.RaiseTimelineChanged(timeline);

        // The first audible event is the bar-6 pass-2 note at performed 19 s.
        engine.Now.ElapsedMs = timeline.PlayFromMs;
        Assert.Equal(19.0, session.PerformedSec, 6);
        var at19 = session.CurrentRepeatPosition;
        Assert.Equal(5, at19.WrittenBar);
        Assert.Equal(0.5, at19.Fraction, 6);
        Assert.Equal(2, at19.Pass);

        // Later in the same pass, bar 9 starts at performed 24 s and is pass 1 of its own (not repeated) bar.
        engine.Now.ElapsedMs = Assert.Single(timeline.Bars, bar => bar.Bar == 8).StartMs;
        Assert.Equal(24.0, session.PerformedSec, 6);
        var at24 = session.CurrentRepeatPosition;
        Assert.Equal(8, at24.WrittenBar);
        Assert.Equal(1, at24.Pass);
    }

    /// <summary>An engine that records the seek (including its occurrence) and holds a controllable scheduler position.</summary>
    private sealed class SeekEngine : IPlaybackEngine
    {
        public bool IsPlaying { get; set; }
        public bool IsPaused { get; set; }
        public (int Bar, int Cell, int? Occurrence)? LastSeek { get; private set; }
        public PlaybackPosition Now { get; } = new();

        public event Action<ScoreTimeline>? TimelineChanged;
        public event Action<ScoreTimeline>? TimelineRevised { add { } remove { } }

        public void Start(SongProject project, PlaybackOptions options, Action<PlaybackPosition> onPosition, Action onFinished, bool startPaused = false) => IsPlaying = true;
        public void Stop() => IsPlaying = false;
        public void Pause() => IsPaused = true;
        public void Resume() => IsPaused = false;
        public void SetLoop(bool loop) { }
        public void Seek(SongProject project, int bar, int cell) => LastSeek = (bar, cell, null);
        public void Seek(SongProject project, int bar, int cell, int? occurrence) => LastSeek = (bar, cell, occurrence);

        public PlaybackPosition Playhead() => new()
        {
            Bar = Now.Bar, Cell = Now.Cell, BarFraction = Now.BarFraction, ElapsedMs = Now.ElapsedMs,
        };

        public PlaybackPosition AudiblePlayhead(PlaybackPosition frame)
        {
            frame.Bar = Now.Bar;
            frame.Cell = Now.Cell;
            frame.BarFraction = Now.BarFraction;
            frame.ElapsedMs = Now.ElapsedMs;
            return frame;
        }

        public void RaiseTimelineChanged(ScoreTimeline timeline) => TimelineChanged?.Invoke(timeline);
        public void Dispose() { }
    }
}
