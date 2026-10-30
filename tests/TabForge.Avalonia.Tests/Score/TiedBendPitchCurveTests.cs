using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: a tied note after a bend keeps the wheel where the chain was bent, its own vibrato
// wobbles around that value, and the wheel resets to centre once at the end of the chain.
// Tests: this class (synthetic song, plus the Trooper solo fixture when present).
public class TiedBendPitchCurveTests
{
    private const double BendUpValue = 4;   // quarter-tones: +2 semitones

    private sealed record CellSpec(int Fret, bool Tied = false, string? Extra = null, string BendType = "", (double Offset, double Value)[]? Bend = null,
        bool Legato = false, int SlideTo = 0);

    private static (double Offset, double Value)[] BendUp() => new[] { (0.0, 0.0), (60.0, BendUpValue) };
    private static (double Offset, double Value)[] BendRelease() => new[] { (0.0, BendUpValue), (60.0, 0.0) };

    private static ScoreTimeline Build(params CellSpec[] cells)
    {
        var song = new SongProject { Title = "T", Tempo = 120 };
        var track = new TrackModel
        {
            Name = "Lead", Kind = TrackKind.Guitar, MidiChannel = 0,
            StringTunings = new List<int> { 64, 59, 55, 50, 45, 40 }, Measures = TemplateFactory.Measures(1)
        };
        song.Tracks.Add(track);
        for (var i = 0; i < cells.Length; i++)
        {
            var cell = track.Measures[0].Cells[i];
            cell.DurationDenominator = 4;
            var note = new TabNote
            {
                StringIndex = 0, Fret = cells[i].Fret, MidiValue = track.PitchOf(0, cells[i].Fret), Tied = cells[i].Tied,
                SlideTargetMidi = cells[i].SlideTo > 0 ? track.PitchOf(0, cells[i].SlideTo) : 0
            };
            if (cells[i].Legato) note.Techniques.Add("LegatoSlide");
            if (cells[i].Bend is { } points)
            {
                note.BendPoints = points.Select(p => new BendPointModel { Offset = p.Offset, Value = p.Value }).ToList();
                note.BendTypeName = cells[i].BendType;
                note.Techniques.Add("Bend");
            }
            if (cells[i].Extra is { } extra) note.Techniques.Add(extra);
            cell.Notes.Add(note);
        }
        return MidiTimelineBuilder.Build(song, new PlaybackOptions { StartBar = 0 });
    }

    private static double Semi(ScoreEvent e) => ((e.Data2 << 7 | e.Data1) - 8192) * 12 / 8192.0;

    private static List<(double Ms, double Semi)> Wheel(ScoreTimeline timeline, NoteEvent note) =>
        timeline.Events
            .Where(e => (e.Status & 0xF0) == 0xE0 && e.TrackIndex == note.TrackIndex && e.Channel == note.Channel &&
                        e.TimeMs >= note.OnsetMs - 0.01 && e.TimeMs <= note.EndMs + 0.01)
            .OrderBy(e => e.TimeMs)
            .Select(e => (e.TimeMs, Math.Round(Semi(e), 2)))
            .ToList();

    private static void AssertHoldsAround(List<(double Ms, double Semi)> wheel, double from, double to, double held)
    {
        var inside = wheel.Where(e => e.Ms > from + 0.01 && e.Ms < to - 0.01).ToList();
        Assert.NotEmpty(inside);
        Assert.All(inside, e => Assert.InRange(e.Semi, held - 0.45, held + 0.45));
        Assert.Contains(inside, e => e.Semi > held + 0.05);
        Assert.Contains(inside, e => e.Semi < held - 0.05);
    }

    [Fact]
    public void Tied_vibrato_after_a_bend_holds_the_bent_pitch_and_resets_once()
    {
        var timeline = Build(new CellSpec(15, Bend: BendUp(), BendType: "Bend"), new CellSpec(15, Tied: true, Extra: "Vibrato"));
        var note = Assert.Single(timeline.Notes);
        var wheel = Wheel(timeline, note);

        Assert.Equal(2.0, wheel.Single(e => Math.Abs(e.Ms - 500) < 0.01).Semi, 2);   // the join is not a reset to centre
        AssertHoldsAround(wheel, 500, note.EndMs, 2.0);
        Assert.Equal(0.0, wheel[^1].Semi, 2);                                          // one reset, at the chain end
        Assert.Equal(note.EndMs, wheel[^1].Ms, 1);
        Assert.Single(wheel, e => Math.Abs(e.Semi) < 0.01 && e.Ms > 1);                 // no other reset between
    }

    [Fact]
    public void Tie_chain_of_three_holds_the_bend_to_the_last_note()
    {
        var timeline = Build(
            new CellSpec(15, Bend: BendUp(), BendType: "Bend"),
            new CellSpec(15, Tied: true, Extra: "Vibrato"),
            new CellSpec(15, Tied: true));
        var note = Assert.Single(timeline.Notes);
        var wheel = Wheel(timeline, note);

        Assert.Equal(2.0, wheel.Single(e => Math.Abs(e.Ms - 500) < 0.01).Semi, 2);
        AssertHoldsAround(wheel, 500, 1000, 2.0);
        Assert.DoesNotContain(wheel, e => Math.Abs(e.Semi) < 0.01 && e.Ms > 1 && e.Ms < note.EndMs - 0.01);
        Assert.Equal(0.0, wheel[^1].Semi, 2);
        Assert.Equal(note.EndMs, wheel[^1].Ms, 1);
    }

    [Fact]
    public void Tie_after_a_bend_release_stays_at_centre()
    {
        var timeline = Build(new CellSpec(15, Bend: BendRelease(), BendType: "Release"), new CellSpec(15, Tied: true, Extra: "Vibrato"));
        var note = Assert.Single(timeline.Notes);
        var wheel = Wheel(timeline, note);

        Assert.Equal(0.0, wheel.Single(e => Math.Abs(e.Ms - 500) < 0.01).Semi, 2);
        AssertHoldsAround(wheel, 500, note.EndMs, 0.0);
        Assert.Equal(0.0, wheel[^1].Semi, 2);
    }

    [Fact]
    public void Continuation_with_its_own_bend_points_replaces_the_held_value_from_its_first_point()
    {
        var timeline = Build(
            new CellSpec(15, Bend: BendUp(), BendType: "Bend"),
            new CellSpec(15, Tied: true, Bend: BendRelease(), BendType: "Release"));
        var note = Assert.Single(timeline.Notes);
        var wheel = Wheel(timeline, note);

        Assert.Equal(2.0, wheel.Single(e => Math.Abs(e.Ms - 500) < 0.01).Semi, 2);   // starts continuous at the held +2
        Assert.All(wheel.Where(e => e.Ms > 500 && e.Ms < note.EndMs - 0.01), e => Assert.InRange(e.Semi, -0.05, 2.05));
        Assert.Contains(wheel.Where(e => e.Ms > 500 && e.Ms < note.EndMs - 0.01), e => e.Semi < 0.5);   // its own points released it
        Assert.Equal(0.0, wheel[^1].Semi, 2);
    }

    [Fact]
    public void A_legato_slide_destination_adds_its_own_bend_to_the_slide_offset()
    {
        var timeline = Build(new CellSpec(5, Legato: true, SlideTo: 7), new CellSpec(7, Bend: BendUp(), BendType: "Bend"));
        var note = Assert.Single(timeline.Notes);
        var wheel = Wheel(timeline, note);

        Assert.Equal(2.0, wheel.Where(e => Math.Abs(e.Ms - 500) < 0.001).Max(e => e.Semi), 2);   // the slide's +2 stays the base
        Assert.Equal(4.0, wheel.Where(e => e.Ms > 500.02 && e.Ms < note.EndMs - 0.01).Max(e => e.Semi), 2);
        Assert.Equal(0.0, wheel[^1].Semi, 2);
    }

    [Fact]
    public void A_non_tied_next_note_starts_from_centre()
    {
        var timeline = Build(
            new CellSpec(15, Bend: BendUp(), BendType: "Bend"),
            new CellSpec(15, Tied: true, Extra: "Vibrato"),
            new CellSpec(15));
        Assert.Equal(2, timeline.Notes.Count);
        var chain = timeline.Notes.First(n => n.Continuations is { Count: > 0 });
        var next = timeline.Notes.First(n => !ReferenceEquals(n, chain));
        var wheel = Wheel(timeline, chain);
        Assert.Equal(0.0, wheel[^1].Semi, 2);
        Assert.Equal(chain.EndMs, next.OnsetMs, 1);
        Assert.DoesNotContain(timeline.Events, e => (e.Status & 0xF0) == 0xE0 && e.Channel == next.Channel && e.TimeMs > chain.EndMs + 1);
    }

    [Fact]
    public void The_whole_tie_chain_stays_on_the_origin_effect_channel()
    {
        var timeline = Build(new CellSpec(15, Bend: BendUp(), BendType: "Bend"), new CellSpec(15, Tied: true, Extra: "Vibrato"));
        var note = Assert.Single(timeline.Notes);
        Assert.True(note.UsesEffectChannel);
        Assert.NotEqual(0, note.Channel);   // the track's main channel is 0
        Assert.All(timeline.Events.Where(e => !e.IsSetup && (e.Status & 0xF0) == 0xE0), e => Assert.Equal(note.Channel, e.Channel));
    }

    // ---------- Trooper solo fixture ----------

    private const string TrooperPath = "C:/tfrel-tmp/trooper/trooper.gp5";

    [Fact]
    public void Trooper_tied_bend_holds_the_bent_pitch_until_the_chain_ends()
    {
        if (!File.Exists(TrooperPath)) return;
        AlphaTabWarmUp.EnsureReady();
        var song = GuitarProImporter.Import(TrooperPath, new ImportContext());
        var timeline = MidiTimelineBuilder.Build(song, new PlaybackOptions());
        var origin = timeline.Notes.FirstOrDefault(n => n.Bar == 90 && n.Cell == 14 && n.StringIndex == 0 && n.Fret == 15);
        Assert.NotNull(origin);
        Assert.NotNull(origin!.Continuations);
        var join = origin.Continuations!.First().OnsetMs;   // bar 92 beat 0
        var wheel = Wheel(timeline, origin);
        Assert.NotEmpty(wheel);

        var peakMs = wheel.First(e => e.Ms <= join && e.Semi >= 1.9).Ms;
        var held = 2.0;
        var afterPeak = wheel.Where(e => e.Ms > peakMs + 0.01 && e.Ms < origin.EndMs - 0.01).ToList();
        Assert.NotEmpty(afterPeak);
        Assert.All(afterPeak, e => Assert.True(e.Semi >= held - 0.45, $"wheel {e.Semi} at {e.Ms}ms falls below the held bend"));
        Assert.All(afterPeak.Where(e => e.Ms >= join - 0.01), e => Assert.True(e.Semi <= held + 0.45, $"wheel {e.Semi} at {e.Ms}ms overshoots the held bend"));

        var atJoin = wheel.Where(e => Math.Abs(e.Ms - join) < 0.01).ToList();
        Assert.Single(atJoin);
        Assert.InRange(atJoin[0].Semi, 1.9, 2.1);
        Assert.Equal(0.0, wheel[^1].Semi, 2);
        Assert.Equal(origin.EndMs, wheel[^1].Ms, 1);
        Assert.Single(wheel, e => Math.Abs(e.Semi) < 0.01 && e.Ms > origin.OnsetMs + 1);
    }
}
