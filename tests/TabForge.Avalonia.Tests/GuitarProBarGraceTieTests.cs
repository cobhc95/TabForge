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

// Owns: the bar converter's rules for an on-beat grace on a half-slot principal (one shared cell, no late bar) and for a tie chain
//   (every link sounds the origin's pitch, harmonics included), on hand-built bars and on the corpus files that showed them.
// Env: TABFORGE_SAMPLES_DIR = the sample folder (TabForge\Samples\corpus); without it the file tests return at once.
public class GuitarProBarGraceTieTests
{
    /// <summary>A fact that is skipped, with the reason shown, when the corpus folder is not set.</summary>
    private sealed class CorpusFactAttribute : FactAttribute
    {
        public CorpusFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TABFORGE_SAMPLES_DIR")))
                Skip = "TABFORGE_SAMPLES_DIR is not set (the corpus files are not in the repo)";
        }
    }

    private sealed class CorpusTheoryAttribute : TheoryAttribute
    {
        public CorpusTheoryAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TABFORGE_SAMPLES_DIR")))
                Skip = "TABFORGE_SAMPLES_DIR is not set (the corpus files are not in the repo)";
        }
    }

    private sealed class FakeNote
    {
        public int String { get; set; } = 1;
        public int Fret { get; set; }
        public int RealValue { get; set; }
        public bool IsTieDestination { get; set; }
        public FakeNote? TieOrigin { get; set; }
        public string HarmonicType { get; set; } = "None";
        public double HarmonicValue { get; set; }
        public FakeBeat? Beat { get; set; }
    }

    private sealed class FakeBeat
    {
        public int PlaybackStart { get; set; }
        public int DisplayStart { get; set; }
        public string GraceType { get; set; } = "None";
        public string Duration { get; set; } = "Sixteenth";
        public List<FakeNote> Notes { get; set; } = new();
        public FakeTuplet? Tuplet { get; set; }
        public FakeBeat? PreviousBeat { get; set; }
    }

    private sealed class FakeTuplet { public int Numerator { get; set; } public int Denominator { get; set; } }

    private sealed class FakeVoice { public List<FakeBeat> Beats { get; set; } = new(); }
    private sealed class FakeBar { public List<FakeVoice> Voices { get; set; } = new(); }

    private static MeasureModel Convert(TrackModel track, params FakeBeat[] beats)
    {
        var measure = new MeasureModel { Number = 1 };
        var bar = new FakeBar { Voices = { new FakeVoice { Beats = beats.ToList() } } };
        var notes = 0;
        GuitarProBarConverter.ConvertBar(bar, measure, track, new ImportBudget(new ImportContext()), ref notes);
        return measure;
    }

    private static FakeBeat Beat(int slot, int fret = 3, string grace = "None", int? displayStart = null) => new()
    {
        PlaybackStart = slot * 240, DisplayStart = displayStart ?? slot * 240, GraceType = grace,
        Duration = grace == "None" ? "Sixteenth" : "Eighth",
        Notes = { new FakeNote { Fret = fret, RealValue = 64 + fret } }
    };

    [Fact]
    public void An_on_beat_grace_before_a_half_slot_principal_shares_its_cell_and_keeps_the_bar_16_slots()
    {
        // Grace display 720 (slot 3.0), principal 840 (slot 3.5): rounding sent the principal to slot 4, the grace kept cell 3 alone.
        var principal = Beat(0); principal.PlaybackStart = principal.DisplayStart = 840;
        var grace = Beat(3, grace: "OnBeat", displayStart: 720); grace.PlaybackStart = 600;
        var measure = Convert(new TrackModel(), Beat(0, 1), Beat(1, 2), Beat(2, 3), grace, principal, Beat(4, 7), Beat(5, 8), Beat(6, 9));
        Assert.Equal(16, measure.Cells.Count);
        Assert.Equal(new[] { true, false }, measure.Cells[3].Notes.Select(n => n.IsGraceNote).OrderByDescending(g => g));
        Assert.Equal(new[] { 7, 8, 9 }, new[] { 4, 5, 6 }.Select(i => measure.Cells[i].Notes.Single().Fret));
        Assert.Equal(7, measure.Cells.Sum(c => c.Notes.Count(n => !n.IsGraceNote)));
    }

    [Fact]
    public void A_three_link_tie_keeps_the_origins_pitch()
    {
        var origin = new FakeNote { Fret = 5, RealValue = 69 };
        var first = new FakeNote { Fret = 5, RealValue = 99, IsTieDestination = true, TieOrigin = origin };
        var second = new FakeNote { Fret = 0, RealValue = 58, IsTieDestination = true, TieOrigin = first };
        var measure = Convert(new TrackModel(), Tie(0, origin), Tie(1, first), Tie(2, second));
        Assert.Equal(new[] { 69, 69, 69 }, measure.Cells.Take(3).Select(c => c.Notes.Single().MidiValue));
        Assert.Equal(new[] { false, true, true }, measure.Cells.Take(3).Select(c => c.Notes.Single().Tied));
        Assert.Equal(new[] { 5, 5, 0 }, measure.Cells.Take(3).Select(c => c.Notes.Single().Fret));   // each link keeps its own fret
    }

    [Fact]
    public void A_tie_after_a_harmonic_sounds_the_harmonic_not_the_stacked_octave_value()   // burn_it_down.gp3 track 2: 93, not 112
    {
        var track = new TrackModel();
        var origin = new FakeNote { Fret = 14, RealValue = 93, HarmonicType = "Artificial", HarmonicValue = 12 };
        var tied = new FakeNote { Fret = 14, RealValue = 112, IsTieDestination = true, TieOrigin = origin };
        var measure = Convert(track, Tie(0, origin), Tie(1, tied));
        var expected = GuitarProImporter.HarmonicMidi("Artificial", track.StringTunings[^1], 14, 12);
        Assert.Equal(expected, measure.Cells[0].Notes.Single().MidiValue);
        Assert.Equal(expected, measure.Cells[1].Notes.Single().MidiValue);
    }


    private static FakeBeat Principal(int ticks) { var b = Beat(0, 4); b.PlaybackStart = b.DisplayStart = ticks; return b; }

    [Fact]
    public void An_on_beat_grace_cell_sits_at_the_principals_written_position_and_the_last_note_still_plays_on_time()
    {
        var grace = Beat(3, grace: "OnBeat", displayStart: 720); grace.PlaybackStart = 600;
        var measure = Convert(new TrackModel(), Beat(0, 1), Beat(1, 2), Beat(2, 3), grace, Principal(840), Beat(4, 7), Beat(5, 8), Beat(6, 9));
        Assert.Equal(3.5, measure.Cells[3].RhythmicPosition);
        var song = new SongProject { Title = "T", Tempo = 120 };   // 125 ms a slot
        song.Tracks.Add(new TrackModel { Measures = { measure } });
        var notes = MidiTimelineBuilder.Build(song, new PlaybackOptions { StartBar = 0 }).Notes;
        Assert.Equal(750, notes.Single(n => n.Cell == 6).OnsetMs, 1);
        Assert.True(notes.Single(n => n.Cell == 3 && n.Fret == 4).OnsetMs >= 375);   // the grace plays first, so the principal is no earlier than its slot
    }

    [Fact]
    public void Two_on_beat_graces_in_a_row_and_their_principal_share_one_cell()
    {
        var first = Beat(3, grace: "OnBeat", displayStart: 660);
        var second = Beat(3, grace: "OnBeat", displayStart: 720);
        var measure = Convert(new TrackModel(), Beat(0, 1), Beat(1, 2), Beat(2, 3), first, second, Principal(840), Beat(4, 7));
        Assert.Equal(2, measure.Cells[3].Notes.Count(n => n.IsGraceNote));
        Assert.Single(measure.Cells[3].Notes, n => !n.IsGraceNote);
        Assert.Equal(7, measure.Cells[4].Notes.Single().Fret);
    }

    [Fact]
    public void A_before_beat_grace_keeps_the_principal_on_the_rounded_slot()
    {
        var measure = Convert(new TrackModel(), Beat(0, 1), Beat(1, 2), Beat(2, 3), Beat(3, grace: "BeforeBeat", displayStart: 720), Principal(840));
        Assert.All(measure.Cells[3].Notes, n => Assert.True(n.IsGraceNote));
        Assert.Equal(4, measure.Cells[4].Notes.Single(n => !n.IsGraceNote).Fret);
    }

    [Fact]
    public void A_tie_destination_keeps_its_own_string_and_fret_while_sounding_the_origin()
    {
        var origin = new FakeNote { Fret = 5, RealValue = 69 };
        var tied = new FakeNote { Fret = 7, RealValue = 71, IsTieDestination = true, TieOrigin = origin };
        var note = Convert(new TrackModel(), Tie(0, origin), Tie(1, tied)).Cells[1].Notes.Single();
        Assert.Equal((7, 69, true), (note.Fret, note.MidiValue, note.Tied));
    }

    [Fact]
    public void A_tie_chain_stops_at_an_origin_on_another_string()
    {
        var origin = new FakeNote { String = 2, Fret = 5, RealValue = 64 };
        var tied = new FakeNote { String = 1, Fret = 3, RealValue = 67, IsTieDestination = true, TieOrigin = origin };
        var note = Convert(new TrackModel(), Tie(0, origin), Tie(1, tied)).Cells[1].Notes.Single();
        Assert.Equal(67, note.MidiValue);
    }

    [Fact]
    public void A_tie_destination_keeps_its_own_harmonic_mark()
    {
        var origin = new FakeNote { Fret = 14, HarmonicType = "Artificial", HarmonicValue = 12 };
        var tied = new FakeNote { Fret = 14, HarmonicType = "Artificial", HarmonicValue = 7, IsTieDestination = true, TieOrigin = origin };
        Assert.Equal(7, Convert(new TrackModel(), Tie(0, origin), Tie(1, tied)).Cells[1].Notes.Single().HarmonicFret);
    }

    [Fact]
    public void A_tuplet_given_only_as_a_tuplet_object_places_each_beat_at_the_cumulative_cell_length()
    {
        // Triplet eighths 320 ticks apart, each start one tick short (alphaTab truncates): the exact position must come from the same ratio the cell uses.
        var beats = Enumerable.Range(0, 6).Select(k => new FakeBeat
        {
            PlaybackStart = Math.Max(0, k * 320 - 1), DisplayStart = Math.Max(0, k * 320 - 1), Duration = "Eighth",
            Tuplet = new FakeTuplet { Numerator = 3, Denominator = 2 }, Notes = { new FakeNote { Fret = k } }
        }).ToArray();
        var cells = Convert(new TrackModel(), beats).Cells.Where(c => c.Notes.Count > 0).OrderBy(c => c.RhythmicPosition).ToList();
        Assert.Equal(6, cells.Count);
        var expected = 0.0;
        foreach (var cell in cells)
        {
            Assert.Equal(expected, cell.RhythmicPosition!.Value, 9);
            expected += MusicTime.CellSlots(cell);
        }
    }

    [Fact]
    public void A_tie_chain_survives_a_gp_export_with_the_origins_pitch()
    {
        AlphaTabWarmUp.EnsureReady();
        var song = new SongProject { Title = "T", Tempo = 120 };
        var track = new TrackModel { Name = "Lead", Kind = TrackKind.Guitar, StringTunings = new List<int> { 64, 59, 55, 50, 45, 40 }, Measures = TemplateFactory.Measures(1) };
        song.Tracks.Add(track);
        // The destinations sound the origin's pitch (as the importer gives them) but are written on frets 5 and 7.
        foreach (var (cell, fret, tied) in new[] { (0, 5, false), (4, 5, true), (8, 7, true) })
        {
            track.Measures[0].Cells[cell].DurationDenominator = 4;
            track.Measures[0].Cells[cell].Notes.Add(new TabNote { StringIndex = 0, Fret = fret, MidiValue = track.PitchOf(0, 5), Tied = tied });
        }
        var path = Path.Combine(Path.GetTempPath(), $"tf-tie-{Guid.NewGuid():N}.gp5");
        try
        {
            GuitarProExporter.Save(song, path, embedProject: false);
            var back = GuitarProImporter.Import(path).Tracks[0].Measures[0].Cells.SelectMany(c => c.Notes).ToList();
            Assert.Equal(new[] { false, true, true }, back.Select(n => n.Tied));   // the reader gives a tie its origin's fret, so the pitch is what must survive
            Assert.All(back, n => Assert.Equal(track.PitchOf(0, 5), n.MidiValue));
        }
        finally { File.Delete(path); }
    }

    [CorpusFact]
    public void Another_life_bar_6_grace_bend_tie_sounds_the_origins_pitch()   // another_life_2.gpx track 1: origin f10 (65), tie destination written f12 (67)
    {
        var song = Open("iron_maiden/another_life_2.gpx")!;
        var cells = song.Tracks[0].Measures[5].Cells;
        var origin = cells[4].Notes.Single(n => n.StringIndex == 2);
        var tied = cells[6].Notes.Single(n => n.Tied && n.StringIndex == 2);
        Assert.Equal(origin.MidiValue, tied.MidiValue);
    }

    [CorpusFact]
    public void Prowler_drum_ties_keep_their_own_piece_each()   // prowler_2.gp3 track 4 bar 35: the three tied hits of bar 1 are 57, 49 and 35
    {
        var song = Open("iron_maiden/prowler_2.gp3")!;
        var cells = song.Tracks[3].Measures[34].Cells;
        Assert.Equal(cells[0].Notes.Select(n => n.MidiValue).OrderBy(m => m), cells[4].Notes.Select(n => n.MidiValue).OrderBy(m => m));
    }

    [Fact]
    public void A_drum_tie_without_a_sound_of_its_own_takes_the_chains_sound_and_one_with_a_sound_keeps_it()
    {
        var drums = new TrackModel { Kind = TrackKind.Drums, StringTunings = new List<int> { 35, 38, 42, 46, 49, 51 } };
        var first = new FakeNote { String = 0, Fret = 49, RealValue = 49 };
        var link = new FakeNote { String = 0, Fret = 0, IsTieDestination = true, TieOrigin = first };
        var last = new FakeNote { String = 0, Fret = 0, IsTieDestination = true, TieOrigin = link };
        var own = new FakeNote { String = 0, Fret = 57, RealValue = 57, IsTieDestination = true, TieOrigin = first };
        var measure = Convert(drums, Tie(0, first), Tie(1, link), Tie(2, last), Tie(3, own));
        Assert.Equal(new[] { 49, 49, 49, 57 }, measure.Cells.Take(4).Select(c => c.Notes.Single().MidiValue));
    }

    [Fact]
    public void A_tie_linked_to_the_grace_before_it_sounds_the_note_before_that_grace()
    {
        var origin = new FakeNote { Fret = 10, RealValue = 74 };
        var played = Tie(0, origin); origin.Beat = played;
        var graceNote = new FakeNote { Fret = 12, RealValue = 76 };
        var grace = Tie(1, graceNote); grace.GraceType = "BeforeBeat"; grace.PreviousBeat = played; graceNote.Beat = grace;
        var tied = new FakeNote { Fret = 12, RealValue = 76, IsTieDestination = true, TieOrigin = graceNote };
        var note = Convert(new TrackModel(), played, grace, Tie(2, tied)).Cells[2].Notes.Single();
        Assert.Equal((74, 12), (note.MidiValue, note.Fret));   // the origin's pitch, the destination's own fret
    }

    private static FakeBeat Tie(int slot, FakeNote note) => new() { PlaybackStart = slot * 240, DisplayStart = slot * 240, Notes = { note } };

    private static SongProject? Open(string relative)
    {
        AlphaTabWarmUp.EnsureReady();   // alphaTab's lazy start-up is not thread-safe next to the other import tests
        var dir = Environment.GetEnvironmentVariable("TABFORGE_SAMPLES_DIR");
        var path = string.IsNullOrWhiteSpace(dir) ? null : Path.Combine(dir, relative);
        return path is not null && File.Exists(path) ? GuitarProImporter.Import(path) : null;
    }

    [CorpusFact]
    public void Burn_it_down_tied_harmonic_keeps_pitch_93()   // track 2 bar 42 follows the artificial harmonic of bar 41
    {
        var song = Open("metallica/metallica/burn_it_down.gp3")!;
        var tied = song.Tracks[1].Measures[41].Cells.SelectMany(c => c.Notes).Where(n => n.Tied).ToList();
        Assert.NotEmpty(tied);
        Assert.DoesNotContain(tied, n => n.MidiValue == 112);
    }

    [CorpusFact]
    public void Aces_high_bar_61_three_link_tie_keeps_the_harmonic_pitch()   // track 1: c3 harmonic 77, ties at c6 and c10
    {
        var song = Open("children_of_bodom/aces_high.gp4")!;
        var cells = song.Tracks[0].Measures[60].Cells;
        Assert.Equal(cells[3].Notes.First(n => !n.IsGraceNote).MidiValue, cells[10].Notes.First(n => n.Tied).MidiValue);
    }

    [CorpusTheory]
    [InlineData("metallica/metallica/dyers_eve_10.gp5", 3, 51)]       // track 4 bar 52
    [InlineData("metallica/metallica/wherever_i_may_roam_6.gp5", 0, 14)]   // track 1 bar 15
    public void A_grace_split_bar_keeps_16_cells_with_every_graces_principal_in_its_cell(string relative, int track, int bar)
    {
        if (Open(relative) is not { } song) { Assert.Fail("TABFORGE_SAMPLES_DIR is not set or the file is missing"); return; }
        var measure = song.Tracks[track].Measures[bar];
        Assert.True(!measure.Cells.Any(c => c.Notes.Count > 0 && c.Notes.All(n => n.IsGraceNote)), string.Join(" | ", measure.Cells.Select((c, i) => $"{i}:{c.RhythmicPosition}:{string.Join(',', c.Notes.Select(n => (n.IsGraceNote ? "g" : "") + n.Fret))}")));
    }
}
