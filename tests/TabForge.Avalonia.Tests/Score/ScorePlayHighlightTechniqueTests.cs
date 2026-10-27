using System.Collections.Generic;
using System.Linq;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presets;
using TabForge.Views.Score;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the playing score lights every note at its own beat as the playhead passes it, whatever the technique: a legato slide target, a hammer-on, a pull-off,
///   a tie destination and a grace note (a slide or tie target has no attack of its own, so it is lit from the beat it sustains).
/// Does not own: the drawing of the glow or the engine clock.
/// Tests: this class.
/// </summary>
public class ScorePlayHighlightTechniqueTests
{
    private const int GraceCell = 8;

    private static (ScoreTimeline Timeline, double SlotMs) Compile()
    {
        var song = new SongProject { Title = "T", Tempo = 120 };
        var track = new TrackModel { Name = "Lead", Kind = TrackKind.Guitar, MidiChannel = 0, StringTunings = new List<int> { 64, 59, 55, 50, 45, 40 }, Measures = TemplateFactory.Measures(1) };
        song.Tracks.Add(track);
        TabNote Note(int cell, int fret, params string[] techniques)
        {
            var c = track.Measures[0].Cells[cell];
            c.DurationDenominator = 16;
            var n = new TabNote { StringIndex = 0, Fret = fret, MidiValue = track.PitchOf(0, fret) };
            foreach (var t in techniques) n.Techniques.Add(t);
            c.Notes.Add(n);
            return n;
        }
        Note(0, 5, "LegatoSlide"); Note(1, 7);                                  // slide: 5 -> 7, the target is not picked
        Note(2, 5, "HOPO", "HOPOOrigin"); Note(3, 7, "HOPO", "HOPODestination");  // hammer-on
        Note(4, 7, "HOPO", "HOPOOrigin"); Note(5, 5, "HOPO", "HOPODestination");  // pull-off
        Note(6, 5); Note(7, 5).Tied = true;                                      // tie
        Note(GraceCell, 7);                                                      // grace note (string 1) before a principal note
        var grace = new TabNote { StringIndex = 1, Fret = 3, MidiValue = track.PitchOf(1, 3), IsGraceNote = true, GraceDurationSlots = 0.5 };
        track.Measures[0].Cells[GraceCell].Notes.Add(grace);
        var timeline = MidiTimelineBuilder.Build(song, new PlaybackOptions { StartBar = 0 });
        var bar = timeline.Bars[0];
        return (timeline, (bar.EndMs - bar.StartMs) / 16);
    }

    private static ScorePlayState PlayAt(ScoreTimeline timeline, double ms)
    {
        var play = new ScorePlayState();
        play.Set(timeline, 0, 0, 0, 0, ms);
        return play;
    }

    [Fact]
    public void EveryBeatOfASlideHammerPullTieAndGraceBarLightsWhenThePlayheadReachesIt()
    {
        var (timeline, slot) = Compile();
        var start = timeline.Bars[0].StartMs;
        for (var cell = 0; cell < 8; cell++)
            Assert.Contains((0, cell, 0), PlayAt(timeline, start + cell * slot + 30).Sounding);
        // The grace note sounds just ahead of the beat and its principal note on it: both light around that beat.
        var lit = Enumerable.Range(0, 30).SelectMany(step => PlayAt(timeline, start + GraceCell * slot + (step - 10) * slot / 20).Sounding).ToHashSet();
        Assert.Contains((0, GraceCell, 1), lit);
        Assert.Contains((0, GraceCell, 0), lit);
    }

    [Fact]
    public void ASlideTargetAndATieDestinationHaveNoAttackButStillLightAtTheirOwnBeatAndOnlyThen()
    {
        var (timeline, slot) = Compile();
        var start = timeline.Bars[0].StartMs;
        var attacked = timeline.NotesFor(0).Select(n => n.Cell).ToHashSet();
        Assert.DoesNotContain(1, attacked);   // the premise: merged into the slide origin
        Assert.DoesNotContain(7, attacked);   // merged into the tie origin
        foreach (var cell in new[] { 1, 7 })
        {
            var before = PlayAt(timeline, start + cell * slot - 20);
            Assert.DoesNotContain((0, cell, 0), before.Sounding);
            var reached = PlayAt(timeline, start + cell * slot + 5);
            Assert.Contains((0, cell, 0), reached.Sounding);
            Assert.Contains((0, cell, 0), reached.Struck);
        }
    }

    [Fact]
    public void ARepaintIsDueWhenThePlayheadCrossesATieDestination()
    {
        var (timeline, slot) = Compile();
        var start = timeline.Bars[0].StartMs;
        var play = PlayAt(timeline, start + 6 * slot + 30);
        Assert.True(play.NeedsRepaint(start + 6 * slot + 30, start + 7 * slot + 30));
    }
}
