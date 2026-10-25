using System;
using System.IO;
using System.Linq;
using TabForge.Models;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the Guitar Pro import rules found by the TuxGuitar parity audit (every notated note kept, every tie kept, graces, percussion by flag,
//   track names, tunings with a junk string, the first bar's tempo), each on the real file that showed it.
// Does not own: the audit run itself (ImportParityDump) or the export.
// Env: TABFORGE_SAMPLES_DIR = the sample folder (TabForge\Samples\corpus); without it the file tests return at once (the files are not in the repo).
public class GuitarProImportParityTests
{
    private static SongProject? Open(string relative)
    {
        var dir = Environment.GetEnvironmentVariable("TABFORGE_SAMPLES_DIR");
        var path = string.IsNullOrWhiteSpace(dir) ? null : Path.Combine(dir, relative);
        AlphaTabWarmUp.EnsureReady();
        return path is not null && File.Exists(path) ? GuitarProImporter.Import(path) : null;
    }

    private static int[] AllNotes(MeasureModel m) => m.Cells.Concat(m.Voice2Cells).Select(c => c.Notes.Count(n => !n.IsGraceNote)).ToArray();

    [Fact]
    public void A_drum_beat_listing_one_sound_twice_keeps_both_notes()   // aces_high.gp4 track 4 bar 144: TuxGuitar 7 notes
    {
        if (Open("children_of_bodom/aces_high.gp4") is not { } song) return;
        Assert.Equal(7, AllNotes(song.Tracks[3].Measures[143]).Sum());
    }

    [Fact]
    public void A_fifteen_beat_bar_keeps_every_note_and_a_simile_bar_stays_a_marked_rest()   // Ashen Meridian.gp track 1: bar 24 has 23 notes, bar 25 repeats it as a simile mark
    {
        if (Open("Ashen Meridian.gp") is not { } song) return;
        Assert.Equal(23, AllNotes(song.Tracks[0].Measures[23]).Sum());
        Assert.True(song.Tracks[0].Measures[24].SimileOneBar);
    }

    [Fact]
    public void Every_tied_drum_destination_keeps_its_own_sound()   // follow_the_reaper.gp4 track 5 bar 89: six ties
    {
        if (Open("children_of_bodom/follow_the_reaper.gp4") is not { } song) return;
        var cells = song.Tracks[4].Measures[88].Cells;
        Assert.Equal(6, cells.Sum(c => c.Notes.Count(n => n.Tied)));
        Assert.Equal(23, cells.Sum(c => c.Notes.Count));
    }

    [Fact]
    public void Two_graces_of_one_sound_before_a_beat_are_both_kept()   // el_dorado_3.gp5 track 6 bar 2
    {
        if (Open("iron_maiden/el_dorado_3.gp5") is not { } song) return;
        Assert.Equal(2, song.Tracks[5].Measures[1].Cells.Sum(c => c.Notes.Count(n => n.IsGraceNote)));
    }

    [Fact]
    public void Percussion_is_the_files_flag_not_the_track_name()   // paschendale_2.gp5: "Drum Fill (Bell)" is melodic, "Drums" is the kit
    {
        if (Open("iron_maiden/paschendale_2.gp5") is not { } song) return;
        Assert.NotEqual(TrackKind.Drums, song.Tracks[6].Kind);
        Assert.Equal(TrackKind.Drums, song.Tracks[7].Kind);
    }

    [Fact]
    public void A_track_name_padded_with_control_characters_imports()   // not_my_funeral.gp4: "Lead Guitar" + two DEL
    {
        if (Open("children_of_bodom/not_my_funeral.gp4") is not { } song) return;
        Assert.Contains(song.Tracks, t => t.Name == "Lead Guitar");
    }

    [Fact]
    public void Track_names_lose_control_characters_and_are_cut_to_the_title_limit()
    {
        Assert.Equal("Lead Guitar", GuitarProTrackConverter.TrackName("Lead Guitar\u007f\u007f"));
        Assert.Equal(InputLimits.MaxTitleLength, GuitarProTrackConverter.TrackName(new string('a', InputLimits.MaxTitleLength + 50)).Length);
        Assert.Equal("Imported Track", GuitarProTrackConverter.TrackName(null));
    }

    [Fact]
    public void A_junk_tuning_byte_keeps_the_string_count()   // for_whom_the_bell_tolls_4.gp3 track 6: six strings, the last one listed as 4
    {
        if (Open("metallica/metallica/for_whom_the_bell_tolls_4.gp3") is not { } song) return;
        Assert.Equal(6, song.Tracks[5].StringTunings.Count);
    }

    [Fact]
    public void The_first_bars_own_tempo_overrides_the_header_tempo()   // aces_high.gp4: header 145, first bar 160
    {
        if (Open("children_of_bodom/aces_high.gp4") is not { } song) return;
        Assert.Equal(160, song.Tempo);
        Assert.Null(song.Tracks[0].Measures[0].TempoChange);
    }

    [Fact]
    public void A_gp3_flat_key_signature_is_read_as_flats()   // brave_new_world.gp3: every bar is one flat (TuxGuitar -1)
    {
        if (Open("iron_maiden/brave_new_world.gp3") is not { } song) return;
        Assert.Equal(-1, song.Tracks[0].Measures[0].KeySignature);
        Assert.All(song.Tracks[0].Measures, m => Assert.Equal(-1, m.KeySignature ?? 0));
    }

    [Fact]
    public void A_gp3_track_keeps_its_channels_chorus_and_reverb_and_a_truncated_volume()   // brave_new_world.gp3 tracks 1,2,4: TuxGuitar volume 79/47/71, reverb 39/39/0, chorus 0/0/127
    {
        if (Open("iron_maiden/brave_new_world.gp3") is not { } song) return;
        Assert.Equal(new[] { 79, 47, 71 }, new[] { 0, 1, 3 }.Select(i => song.Tracks[i].Volume));
        Assert.Equal(new[] { 39, 39, 0 }, new[] { 0, 1, 3 }.Select(i => song.Tracks[i].Reverb));
        Assert.Equal(new[] { 0, 0, 127 }, new[] { 0, 1, 3 }.Select(i => song.Tracks[i].Chorus));
    }

    [Fact]
    public void A_gp5_track_with_full_sends_reads_them()   // aces_high_3.gp5 track 3: chorus 127, reverb 127
    {
        if (Open("children_of_bodom/aces_high_3.gp5") is not { } song) return;
        Assert.Equal(127, song.Tracks[2].Chorus);
        Assert.Equal(127, song.Tracks[2].Reverb);
        Assert.Equal(0, song.Tracks[0].Reverb);
    }

    [Fact]
    public void A_gp4_file_gets_no_heavy_accent_the_file_does_not_have()   // aces_high.gp4 track 1 bar 19: TuxGuitar has none, alphaTab reported one on the first note
    {
        if (Open("children_of_bodom/aces_high.gp4") is not { } song) return;
        Assert.DoesNotContain(song.Tracks[0].Measures[18].Cells, c => c.Accent == 2);
    }

    private sealed record FakeDrumNote(int PercussionArticulation, int Fret, int RealValue, bool IsTieDestination, object? TieOrigin);

    [Fact]
    public void A_drum_tie_with_its_own_sound_does_not_borrow_the_origins()
    {
        var origin = new FakeDrumNote(35, -1, 35, false, null);
        Assert.Equal(38, GuitarProImporter.DrumPitch(new FakeDrumNote(38, -1, 38, true, origin)));
        Assert.Equal(35, GuitarProImporter.DrumPitch(new FakeDrumNote(0, -1, 0, true, origin)));   // a tie with no sound continues the origin
    }
}
