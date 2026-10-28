using System;
using System.Collections.Generic;
using System.Linq;
using TabForge.Models;
using TabForge.Presets;

namespace TabForge;

// Owns: the song the legacy-format export test writes: guitar, bass and drums with repeats, endings, a marker, key and time changes, a tempo change, a second voice and one bar per kind of mark.
// Does not own: the export or the comparison (SelfTestLegacyGpExport.cs).
// Tests: TestLegacyGpExport.
public static partial class SelfTest
{
    private static TabNote LgNote(TrackModel track, int stringIndex, int fret, params string[] techniques)
    {
        var note = new TabNote { StringIndex = stringIndex, Fret = fret, MidiValue = track.StringTunings[stringIndex] + fret };
        foreach (var name in techniques) note.Techniques.Add(name);
        return note;
    }

    private static TabCell LgPut(MeasureModel bar, int slot, int denominator, params TabNote[] notes)
    {
        var cell = bar.Cells[slot];
        cell.DurationDenominator = denominator;
        cell.Notes.AddRange(notes);
        return cell;
    }

    /// <summary>Six bars of guitar (every mark the formats keep), a bass line and a drum groove.</summary>
    private static SongProject BuildLegacyGpSong()
    {
        var song = new SongProject { Title = "Legacy copy", Artist = "Test artist", Album = "Test album", Tempo = 96, KeySignature = 2 };
        var guitar = new TrackModel { Name = "Lead guitar", Kind = TrackKind.Guitar, MidiProgram = 29, MidiChannel = 0, Capo = 2, Measures = TemplateFactory.Measures(6), Reverb = 40, Chorus = 16 };
        var bass = new TrackModel { Name = "Bass", Kind = TrackKind.Bass, MidiProgram = 34, MidiChannel = 1, StringTunings = new List<int> { 43, 38, 33, 28 }, Measures = TemplateFactory.Measures(6) };
        var drums = new TrackModel { Name = "Drums", Kind = TrackKind.Drums, MidiProgram = 0, MidiChannel = 9, StringTunings = new List<int> { 49, 46, 42, 38, 36 }, Measures = TemplateFactory.Measures(6) };
        song.Tracks.Add(guitar); song.Tracks.Add(bass); song.Tracks.Add(drums);
        var g = guitar.Measures;

        g[0].RepeatStart = true;                                                  // bar 1: quarters, a chord with a name, a ghost accent, a dead note
        LgPut(g[0], 0, 4, LgNote(guitar, 1, 0));
        LgPut(g[0], 4, 4, LgNote(guitar, 0, 3), LgNote(guitar, 2, 2)).ChordName = "G";
        LgPut(g[0], 8, 4, LgNote(guitar, 3, 5)).Notes[0].Ghost = true;
        g[0].Cells[8].Accent = 1;
        LgPut(g[0], 12, 4, LgNote(guitar, 4, 0)).Notes[0].Dead = true;

        LgPut(g[1], 0, 8, LgNote(guitar, 1, 5, "HOPO"));                          // bar 2: hammer-on, palm mute, tie, slide
        LgPut(g[1], 2, 8, LgNote(guitar, 1, 7));
        LgPut(g[1], 4, 4, LgNote(guitar, 2, 7, "PalmMute"));
        var tied = LgNote(guitar, 2, 7); tied.Tied = true;
        LgPut(g[1], 8, 4, tied);
        LgPut(g[1], 12, 8, LgNote(guitar, 3, 5, "LegatoSlide"));
        LgPut(g[1], 14, 8, LgNote(guitar, 3, 7));
        g[1].Cells[0].Text = "Lick";
        g[1].MidBarTempos = new List<TempoPoint> { new(8, 110) };                  // a tempo change half way through the bar
        for (var i = 0; i < 4; i++) g[0].Cells[i * 4].Lyrics = new[] { "Hel-", "lo", "wor-", "ld" }[i];

        g[2].TimeSigNum = 3; g[2].TimeSigDenom = 4;                                // bar 3: 3/4 with a triplet, a rest and a dotted note
        for (var i = 0; i < 3; i++)
        {
            var c = LgPut(g[2], i * 2, 8, LgNote(guitar, 2, 4 + i));
            c.TupletNumerator = 3; c.TupletDenominator = 2; c.IsTriplet = true;
        }
        g[2].Cells[6].DurationDenominator = 8; g[2].Cells[6].IsRest = true; g[2].Cells[6].WrittenRest = true;
        LgPut(g[2], 8, 4, LgNote(guitar, 4, 3)).Dots = 1;

        g[3].RepeatEnd = true; g[3].RepeatCount = 2;                               // bar 4: bend, vibrato, natural harmonic, staccato, a grace note
        LgPut(g[3], 0, 4, LgNote(guitar, 1, 7, "Bend", "Vibrato")).Notes[0].BendPoints = new List<BendPointModel> { new() { Offset = 0, Value = 0 }, new() { Offset = 15, Value = 4 }, new() { Offset = 60, Value = 4 } };
        LgPut(g[3], 4, 4, LgNote(guitar, 2, 12, "Harmonic"));
        LgPut(g[3], 8, 4, LgNote(guitar, 3, 7)).Staccato = true;
        var principal = LgPut(g[3], 12, 4, LgNote(guitar, 4, 5));
        principal.Notes.Insert(0, new TabNote { StringIndex = 4, Fret = 3, MidiValue = guitar.StringTunings[4] + 3, IsGraceNote = true, GraceBeforeBeat = true, GraceDurationSlots = 1 });

        song.Markers.Add(new MarkerModel { MeasureIndex = 4, Title = "Chorus" });  // bar 5: a marker, a tempo change, a second voice, a swing feel
        g[4].TempoChange = 140; g[4].KeySignature = -2;
        LgPut(g[4], 0, 2, LgNote(guitar, 1, 3));
        LgPut(g[4], 8, 4, LgNote(guitar, 2, 5), LgNote(guitar, 3, 5));
        var voice2 = g[4].CellsForVoice(1, create: true);
        voice2[0].DurationDenominator = 4; voice2[0].Notes.Add(LgNote(guitar, 5, 0));
        voice2[8].DurationDenominator = 4; voice2[8].Notes.Add(LgNote(guitar, 5, 3));

        g[5].AlternateEnding = 1; g[5].IsDoubleBar = true;                          // bar 6: an alternate ending, a double bar, shifted notes
        LgPut(g[5], 0, 4, LgNote(guitar, 0, 12));
        LgPut(g[5], 4, 4, LgNote(guitar, 0, 14));
        LgPut(g[5], 8, 2, LgNote(guitar, 1, 15));

        for (var b = 0; b < 6; b++)
        {
            LgPut(bass.Measures[b], 0, 4, LgNote(bass, 3, b % 5));
            LgPut(bass.Measures[b], 8, 4, LgNote(bass, 2, 2 + b % 3));
            LgPut(drums.Measures[b], 0, 4, new TabNote { StringIndex = 4, Fret = 36, MidiValue = 36 }, new TabNote { StringIndex = 2, Fret = 42, MidiValue = 42 });
            LgPut(drums.Measures[b], 4, 4, new TabNote { StringIndex = 2, Fret = 42, MidiValue = 42 });
            LgPut(drums.Measures[b], 8, 4, new TabNote { StringIndex = 3, Fret = 38, MidiValue = 38 }, new TabNote { StringIndex = 2, Fret = 42, MidiValue = 42 });
            if (b != 2) LgPut(drums.Measures[b], 12, 4, new TabNote { StringIndex = 2, Fret = 42, MidiValue = 42 });
        }
        bass.Measures[2].TimeSigNum = 3; bass.Measures[2].TimeSigDenom = 4; drums.Measures[2].TimeSigNum = 3; drums.Measures[2].TimeSigDenom = 4;
        return song;
    }
}
