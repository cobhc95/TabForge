using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Playback;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: a merged (legato / tied) note carries on: a legato-slide chain is one attack, the merged note keeps its own wheel curve, a tie after a shortened origin does not re-attack, and a tie from a dead click adds no sound, and a legato slide from a dead click or a picked-effect note is never merged.
// Env: TABFORGE_SAMPLES_DIR = the sample folder; the corpus tests return at once without it (nothing to read).
public class LegatoChainMergeTests
{
    private sealed record Cell(int Fret, bool Tied = false, bool Legato = false, bool Dead = false, bool Staccato = false, string? Extra = null, int? SoundFret = null, int SlideTo = 0, int Shift = 0);

    private static ScoreTimeline Build(params Cell[] cells)
    {
        var song = new SongProject { Title = "T", Tempo = 120 };
        var track = new TrackModel { Name = "Lead", Kind = TrackKind.Guitar, MidiChannel = 0, StringTunings = new List<int> { 64, 59, 55, 50, 45, 40 }, Measures = TemplateFactory.Measures(1) };
        song.Tracks.Add(track);
        for (var i = 0; i < cells.Length; i++)
        {
            var c = track.Measures[0].Cells[i];
            c.DurationDenominator = 8;
            c.Staccato = cells[i].Staccato;
            c.OctaveShiftSemitones = cells[i].Shift;
            var n = new TabNote { StringIndex = 0, Fret = cells[i].Fret, MidiValue = track.PitchOf(0, cells[i].SoundFret ?? cells[i].Fret), SlideTargetMidi = cells[i].SlideTo > 0 ? track.PitchOf(0, cells[i].SlideTo) : 0, Tied = cells[i].Tied, Dead = cells[i].Dead };
            if (cells[i].Legato) n.Techniques.Add("LegatoSlide");
            if (cells[i].Extra is { } extra) n.Techniques.Add(extra);
            c.Notes.Add(n);
        }
        return MidiTimelineBuilder.Build(song, new PlaybackOptions { StartBar = 0 });
    }

    private static List<int> Attacks(ScoreTimeline timeline) => timeline.Notes.Where(n => n.Bar == 0).Select(n => n.Cell).OrderBy(c => c).ToList();

    // Pitch-wheel offsets in semitones inside [fromMs, toMs), in time order.
    private static List<double> Wheel(ScoreTimeline timeline, double fromMs, double toMs) =>
        timeline.Events.Where(e => (e.Status & 0xF0) == 0xE0 && e.TimeMs >= fromMs && e.TimeMs < toMs).OrderBy(e => e.TimeMs)
            .Select(e => Math.Round(((e.Data2 << 7 | e.Data1) - 8192) * 12 / 8192.0, 1) + 0.0).ToList();

    [Fact]
    public void Legato_chain_5_7_5_is_one_attack_that_glides_each_link()
    {
        var timeline = Build(new Cell(5, Legato: true), new Cell(7, Legato: true), new Cell(5));
        Assert.Equal(new[] { 0 }, Attacks(timeline));
        var link = Wheel(timeline, 250.005, 500);   // the string sits two semitones up on the second link, then glides back
        Assert.Equal(2.0, link.First());
        Assert.Equal(0.0, link.Last());
    }

    [Fact]
    public void Legato_chain_5_7_5_7_is_one_attack()
    {
        var timeline = Build(new Cell(5, Legato: true), new Cell(7, Legato: true), new Cell(5, Legato: true), new Cell(7));
        Assert.Equal(new[] { 0 }, Attacks(timeline));
        Assert.Equal((2.0, 0.0), (Wheel(timeline, 250.005, 500).First(), Wheel(timeline, 250.005, 500).Last()));
        Assert.Equal((0.0, 2.0), (Wheel(timeline, 500.005, 750).First(), Wheel(timeline, 500.005, 750).Last()));
        Assert.Equal(2.0, Wheel(timeline, 750.005, 1000).First());
    }

    [Fact]
    public void Merged_legato_destination_keeps_its_vibrato()
    {
        var timeline = Build(new Cell(5, Legato: true), new Cell(7, Extra: "Vibrato"));
        Assert.Equal(new[] { 0 }, Attacks(timeline));
        Assert.True(Wheel(timeline, 310, 500).Distinct().Count() >= 3);
    }

    [Fact]
    public void Tied_vibrato_continuation_writes_no_wheel_reset_between_origin_and_continuation()
    {
        var timeline = Build(new Cell(5, Extra: "Vibrato"), new Cell(5, Tied: true, Extra: "Vibrato"));
        Assert.Equal(new[] { 0 }, Attacks(timeline));
        Assert.Single(Wheel(timeline, 249.995, 250.005));   // one event at the join, not a reset to centre followed by the continuation's first sample
    }

    [Fact]
    public void Merged_legato_destination_keeps_its_own_plain_slide()
    {
        var timeline = Build(new Cell(5, Legato: true), new Cell(7, Extra: "Slide"), new Cell(9));
        Assert.Equal(new[] { 0, 2 }, Attacks(timeline));
        Assert.Equal(4.0, Wheel(timeline, 250.005, 500).Max());   // 7 slides up to 9: two more semitones above the held +2
    }

    [Fact]
    public void Tie_after_a_shortened_origin_does_not_re_attack()
    {
        var timeline = Build(new Cell(5, Staccato: true), new Cell(5, Tied: true));
        Assert.Equal(new[] { 0 }, Attacks(timeline));
        Assert.Equal(0, timeline.TieOrphans);
    }

    [Fact]
    public void Tie_after_a_dead_note_stays_the_click_without_a_new_attack_or_sustain()
    {
        var timeline = Build(new Cell(5, Dead: true), new Cell(5, Tied: true));
        Assert.Equal(new[] { 0 }, Attacks(timeline));
        Assert.True(timeline.Notes[0].DurationMs < 100);
        Assert.DoesNotContain(timeline.Events, e => e.IsNoteOn && e.TimeMs >= 100);
        Assert.Equal(0, timeline.TieOrphans);
    }

    [Fact]
    public void Legato_slide_from_a_dead_note_is_picked_again()
    {
        var timeline = Build(new Cell(5, Dead: true, Legato: true), new Cell(7));
        Assert.Equal(new[] { 0, 1 }, Attacks(timeline));
    }

    [Theory]
    [InlineData("TremoloPick")]
    [InlineData("Trill")]
    [InlineData("SemiHarmonic")]
    public void Tied_note_with_picked_effects_is_attacked_itself(string technique)
    {
        var timeline = Build(new Cell(5), new Cell(5, Tied: true, Extra: technique));
        Assert.Equal(new[] { 0, 1 }, Attacks(timeline));
    }

    [Fact]
    public void Legato_chain_past_the_wheel_range_is_picked_again()
    {
        var upTo17 = Build(new Cell(5, Legato: true), new Cell(7, Legato: true), new Cell(9, Legato: true), new Cell(11, Legato: true),
            new Cell(13, Legato: true), new Cell(15, Legato: true), new Cell(17));
        Assert.Equal(new[] { 0 }, Attacks(upTo17));   // the wheel reaches 12 semitones
        var past = Build(new Cell(5, Legato: true), new Cell(7, Legato: true), new Cell(9, Legato: true), new Cell(11, Legato: true),
            new Cell(13, Legato: true), new Cell(15, Legato: true), new Cell(17, Legato: true), new Cell(19));
        Assert.Equal(new[] { 0, 7 }, Attacks(past));   // 14 semitones off the pick would clip
        var withVibrato = Build(new Cell(5, Legato: true), new Cell(7, Legato: true), new Cell(9, Legato: true), new Cell(11, Legato: true),
            new Cell(13, Legato: true), new Cell(15, Legato: true), new Cell(17, Extra: "Vibrato"));
        Assert.Equal(new[] { 0, 6 }, Attacks(withVibrato));   // a vibrato of its own needs two semitones of headroom
    }

    [Fact]
    public void Slide_into_a_tied_note_lands_on_the_written_fret_and_holds()
    {
        // 7 slides to 5; the tied 5 carries the chain root's pitch (7) as an imported tie does, but is written on fret 5.
        var timeline = Build(new Cell(7, Extra: "Slide", SlideTo: 5), new Cell(5, Tied: true, SoundFret: 7));
        Assert.Equal(new[] { 0 }, Attacks(timeline));
        Assert.Equal(-2.0, Wheel(timeline, 250.005, 500).First());
        Assert.Equal(-2.0, Wheel(timeline, 250.005, 500).Last());
    }

    [Fact]
    public void Two_tied_cymbals_on_one_drum_string_are_each_held()
    {
        var song = new SongProject { Title = "T", Tempo = 120 };
        var track = new TrackModel { Name = "Drums", Kind = TrackKind.Drums, MidiChannel = 9, StringTunings = new List<int> { 0, 0, 0, 0, 0, 0 }, Measures = TemplateFactory.Measures(1) };
        song.Tracks.Add(track);
        foreach (var (cell, tied) in new[] { (track.Measures[0].Cells[0], false), (track.Measures[0].Cells[4], true) })
        {
            cell.DurationDenominator = 4;
            foreach (var piece in new[] { 57, 49 }) cell.Notes.Add(new TabNote { StringIndex = 0, Fret = piece, MidiValue = piece, Tied = tied });
        }
        var timeline = MidiTimelineBuilder.Build(song, new PlaybackOptions { StartBar = 0 });
        Assert.Equal(new[] { 49, 57 }, timeline.Notes.Select(n => n.Midi).OrderBy(m => m).ToArray());
        Assert.Equal(0, timeline.TieOrphans);
    }

    [Fact]
    public void Corpus_prowler_tied_cymbals_are_not_re_attacked()
    {
        var timeline = Corpus("iron_maiden/prowler_2.gp3", out var song);
        if (song is null) return;
        foreach (var bar in new[] { 34, 35 })
            Assert.Equal(12 * timeline.Bars.Count(b => b.Bar == bar), timeline.Notes.Count(n => n.TrackIndex == 3 && n.Bar == bar));   // the probe counts tracks from 1
    }

    [Fact]
    public void Before_beat_grace_trims_the_previous_bar_even_after_other_tracks_notes()
    {
        var song = new SongProject { Title = "T", Tempo = 120 };
        TrackModel Lane(string name, int channel)
        {
            var track = new TrackModel { Name = name, Kind = TrackKind.Guitar, MidiChannel = channel, StringTunings = new List<int> { 64, 59, 55, 50, 45, 40 }, Measures = TemplateFactory.Measures(2) };
            song.Tracks.Add(track);
            return track;
        }
        TabNote Note(TrackModel track, int fret, int str = 0, bool grace = false) => new() { StringIndex = str, Fret = fret, MidiValue = track.PitchOf(str, fret), IsGraceNote = grace };
        var busy = Lane("Busy", 0);
        var lead = Lane("Lead", 1);
        for (var bar = 0; bar < 2; bar++)   // the busy track fills both bars: far more than 16 notes come between the lead's two bars
            for (var i = 0; i < 16; i++) { busy.Measures[bar].Cells[i].DurationDenominator = 16; busy.Measures[bar].Cells[i].Notes.Add(Note(busy, 3)); busy.Measures[bar].Cells[i].Notes.Add(Note(busy, 5, 1)); }
        lead.Measures[0].Cells[0].DurationDenominator = 1; lead.Measures[0].Cells[0].Notes.Add(Note(lead, 5));   // a whole note, sounding to the bar end
        var next = lead.Measures[1].Cells[0];
        next.DurationDenominator = 4; next.IsGrace = true;
        next.Notes.Add(Note(lead, 3, grace: true)); next.Notes.Add(Note(lead, 7));
        var timeline = MidiTimelineBuilder.Build(song, new PlaybackOptions { StartBar = 0 });
        var held = timeline.Notes.Single(n => n.TrackIndex == 1 && n.Bar == 0);
        var grace = timeline.Notes.Single(n => n.TrackIndex == 1 && n.Bar == 1 && n.Fret == 3);
        Assert.True(held.EndMs <= grace.OnsetMs + 0.5);
    }

    [Theory]
    [InlineData(TrackKind.Drums, 0, true)]
    [InlineData(TrackKind.Guitar, 9, false)]
    [InlineData(TrackKind.Guitar, 0, false)]
    public void Drum_track_is_the_drum_kind_only(TrackKind kind, int channel, bool expected)
    {
        var song = new SongProject { Title = "T" };
        song.Tracks.Add(new TrackModel { Name = "T", Kind = kind, MidiChannel = channel, StringTunings = new List<int> { 64 } });
        Assert.Equal(expected, song.Tracks[0].IsDrumTrack);
        Assert.Equal(expected, ChannelAllocator.Assign(song)[0] == ChannelAllocator.PercussionChannel);
    }

    [Fact]
    public void Channel_ten_is_rejected_for_a_pitched_track_and_accepted_for_drums()
    {
        Assert.Equal("Channel 10 is for drums only.", TrackPropertiesPresenter.FieldError("29", "9", "24", "0", isDrumTrack: false));
        Assert.Null(TrackPropertiesPresenter.FieldError("0", "9", "24", "0", isDrumTrack: true));
        Assert.Null(TrackPropertiesPresenter.FieldError("29", "8", "24", "0", isDrumTrack: false));
    }

    [Fact]
    public void A_saved_guitar_on_channel_nine_loads_on_a_free_channel_and_plays_as_guitar()
    {
        var song = new SongProject { Title = "T", Tempo = 120 };
        var guitar = new TrackModel { Name = "Guitar", Kind = TrackKind.Guitar, MidiChannel = 9, StringTunings = new List<int> { 64, 59, 55, 50, 45, 40 }, Measures = TemplateFactory.Measures(1) };
        var drums = new TrackModel { Name = "Drums", Kind = TrackKind.Drums, MidiChannel = 9, StringTunings = new List<int> { 49, 46, 42, 38, 36 }, Measures = TemplateFactory.Measures(1) };
        song.Tracks.Add(guitar); song.Tracks.Add(drums);
        guitar.Measures[0].Cells[0].DurationDenominator = 4;
        guitar.Measures[0].Cells[0].Notes.Add(new TabNote { StringIndex = 0, Fret = 5, MidiValue = guitar.PitchOf(0, 5) });
        ProjectValidator.Validate(song);
        Assert.NotEqual(9, guitar.MidiChannel);
        Assert.Equal(9, drums.MidiChannel);
        var timeline = MidiTimelineBuilder.Build(song, new PlaybackOptions { StartBar = 0 });
        var note = timeline.Notes.Single(n => n.TrackIndex == 0);
        Assert.Equal(69, note.Midi);   // the pitch of the fret, not the raw fret as a drum key
        Assert.NotEqual(ChannelAllocator.PercussionChannel, note.Channel >= 0 ? note.Channel : guitar.MidiChannel);
    }

    [Fact]
    public void Legato_headroom_is_the_destinations_own_curve_peak()
    {
        ScoreTimeline Run(string? extra) => Build(new Cell(5, Legato: true), new Cell(7, Legato: true), new Cell(9, Legato: true), new Cell(11, Legato: true),
            new Cell(13, Legato: true), new Cell(15, Legato: true), new Cell(17, Extra: extra));
        Assert.Equal(new[] { 0 }, Attacks(Run(null)));
        Assert.Equal(new[] { 0, 6 }, Attacks(Run("SlideOutUp")));   // a 5-semitone fall on top of 12 would clip
        Assert.Equal(new[] { 0, 6 }, Attacks(Run("Vibrato")));
    }

    [Fact]
    public void Slide_and_legato_merge_work_in_an_8va_shifted_cell()
    {
        // The slide target is in the sounding frame: both cells play an octave up.
        Assert.Equal(new[] { 0 }, Attacks(Build(new Cell(5, Legato: true, Shift: 12), new Cell(7, Shift: 12))));
        Assert.Equal(new[] { 0 }, Attacks(Build(new Cell(5, Legato: true, SlideTo: 7, Shift: 12), new Cell(7, Shift: 12))));
    }

    public enum GraceShape { ImportedShared, ImportedSeparate, HandMade }

    // An on-beat grace on beat 3 (slot 3.0, 120 bpm: a slot is 125 ms) before a principal fret-5 note, written the way each source shapes the cell.
    private static ScoreTimeline BuildGrace(GraceShape shape, int graceSlot = 3)
    {
        var song = new SongProject { Title = "T", Tempo = 120 };
        var track = new TrackModel { Name = "Lead", Kind = TrackKind.Guitar, MidiChannel = 0, StringTunings = new List<int> { 64, 59, 55, 50, 45, 40 }, Measures = TemplateFactory.Measures(1) };
        song.Tracks.Add(track);
        var imported = shape != GraceShape.HandMade;
        TabNote Note(int fret, bool grace) => new() { StringIndex = 0, Fret = fret, MidiValue = track.PitchOf(0, fret), IsGraceNote = grace, GraceBeforeBeat = false, GraceDurationSlots = grace && imported ? 1 : 0 };
        var cells = track.Measures[0].Cells;
        var cell = cells[graceSlot];
        cell.DurationDenominator = 8;
        cell.Notes.Add(Note(3, true));
        if (shape == GraceShape.ImportedShared) cell.RhythmicPosition = graceSlot + 0.5;   // the cell sits at its principal note
        if (shape == GraceShape.HandMade) cell.Notes.Add(Note(5, false));
        else if (shape == GraceShape.ImportedShared) cell.Notes.Add(Note(5, false));
        else
        {
            cell.RhythmicPosition = graceSlot; cell.IsGrace = true;
            cells[graceSlot + 1].DurationDenominator = 8; cells[graceSlot + 1].RhythmicPosition = graceSlot + 0.5; cells[graceSlot + 1].Notes.Add(Note(5, false));
        }
        return MidiTimelineBuilder.Build(song, new PlaybackOptions { StartBar = 0 });
    }

    [Theory]
    [InlineData(GraceShape.ImportedShared)]
    [InlineData(GraceShape.ImportedSeparate)]
    [InlineData(GraceShape.HandMade)]
    public void On_beat_grace_sounds_on_the_beat_and_the_principal_half_a_slot_later_once(GraceShape shape)
    {
        var notes = BuildGrace(shape).Notes.OrderBy(n => n.OnsetMs).ToList();
        Assert.Equal(2, notes.Count);
        Assert.Equal(375.0, notes[0].OnsetMs, 0.5);
        Assert.Equal(437.5, notes[1].OnsetMs, 0.5);
    }

    [Theory]
    [InlineData(GraceShape.ImportedShared)]
    [InlineData(GraceShape.HandMade)]
    public void On_beat_grace_at_the_start_of_the_song_is_audible_and_delays_the_principal(GraceShape shape)
    {
        var notes = BuildGrace(shape, graceSlot: 0).Notes.OrderBy(n => n.OnsetMs).ToList();
        Assert.Equal(2, notes.Count);
        Assert.All(notes, n => Assert.True(n.OnsetMs >= 0));
        Assert.Equal(0.0, notes[0].OnsetMs, 0.5);
        Assert.Equal(62.5, notes[1].OnsetMs, 0.5);
    }

    [Theory]
    [InlineData(true, 1000.0)]    // a tie holds its 4 slots: 8 of 16 slots at 2 s a bar
    [InlineData(false, 500.0)]    // an annotation-only beat holds no time: 4 of 16 slots
    public void A_bar_ending_in_a_tie_only_or_annotation_only_beat_keeps_the_next_bar_on_time(bool tie, double nextBarMs)
    {
        var song = new SongProject { Title = "T", Tempo = 120, ImportedFrom = "x.gp5" };
        var track = new TrackModel { Name = "Lead", Kind = TrackKind.Guitar, MidiChannel = 0, StringTunings = new List<int> { 64, 59, 55, 50, 45, 40 }, Measures = TemplateFactory.Measures(2) };
        song.Tracks.Add(track);
        var cells = track.Measures[0].Cells;
        cells[0].DurationDenominator = 4; cells[0].Notes.Add(new TabNote { StringIndex = 0, Fret = 5, MidiValue = track.PitchOf(0, 5) });
        cells[4].DurationDenominator = 4;
        if (tie) cells[4].IsTied = true; else cells[4].Text = "verse";
        track.Measures[1].Cells[0].DurationDenominator = 4; track.Measures[1].Cells[0].Notes.Add(new TabNote { StringIndex = 0, Fret = 5, MidiValue = track.PitchOf(0, 5) });
        var timeline = MidiTimelineBuilder.Build(song, new PlaybackOptions { StartBar = 0 });
        Assert.Equal(nextBarMs, timeline.Bars[1].StartMs, 1.0);
    }

    private static ScoreTimeline Corpus(string relative, out SongProject? song)
    {
        AlphaTabWarmUp.EnsureReady();
        var dir = Environment.GetEnvironmentVariable("TABFORGE_SAMPLES_DIR");
        var path = string.IsNullOrWhiteSpace(dir) ? null : Path.Combine(dir, relative);
        if (path is null || !File.Exists(path)) { song = null; return new ScoreTimeline(); }
        song = GuitarProImporter.Import(path, new ImportContext());
        return MidiTimelineBuilder.Build(song, new PlaybackOptions { StartBar = 0 });
    }

    [Fact]
    public void Corpus_hallowed_legato_run_is_picked_once_per_string()
    {
        var timeline = Corpus("iron_maiden/hallowed_be_thy_name_17.gp4", out var song);
        if (song is null) return;
        Assert.Equal(4, timeline.Notes.Count(n => n.TrackIndex == 2 && n.Bar == 127));   // two strings, picked at fret 24 and again at fret 11 where the slide passes the wheel range (13 frets)
    }

    [Fact]
    public void Corpus_spit_out_the_bone_legato_run_is_not_re_picked()
    {
        var timeline = Corpus("metallica/metallica/spit_out_the_bone_2.gpx", out var song);
        if (song is null) return;
        var offenders = new List<string>();
        for (var t = 0; t <= 1; t++)
            for (var bar = 0; bar < song.Tracks[t].Measures.Count; bar++)
                offenders.AddRange(RePicked(timeline, song, t, bar).Select(x => $"t{t}b{bar}{x}"));
        Assert.True(offenders.Count == 0, string.Join(" ", offenders.Take(20)));
    }

    // Attacks on the written destination of a legato slide (same string, next cell, origin not a grace note): a chain must not re-pick them.
    private static List<string> RePicked(ScoreTimeline timeline, SongProject song, int trackIndex, int bar)
    {
        var cells = song.Tracks[trackIndex].Measures[bar].Cells;
        return timeline.Notes.Where(n => n.TrackIndex == trackIndex && n.Bar == bar && n.Cell > 0 && n.VoiceIndex == 0)
            .Where(n => cells[n.Cell - 1].Notes.Any(o => o.StringIndex == n.StringIndex && !o.IsGraceNote && o.Techniques.Contains("LegatoSlide")))
            .Select(n => $"c{n.Cell}/s{n.StringIndex}").ToList();
    }
}
