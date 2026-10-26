using System.Collections.Generic;
using System.Linq;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: a tab track written from the piano roll touching only the edited notes: the other beats stay the same objects with their marks, a moved
//   note carries its marks, a removed note leaves a rest of its length, a note inside a long beat splits it with ties, a compact beat list, the
//   second voice, tuplet beats never split, and a muted note stays written but silent.
// Does not own: the prompt and the undo step (PianoRollPresenterTests).
// Tests: PianoRollTabWriter, PianoRollTabSource, TabNote.Silent.
public sealed class PianoRollTabWriterTests
{
    private static TabNote Note(TrackModel track, int stringIndex, int fret, params string[] techniques)
    {
        var note = new TabNote { StringIndex = stringIndex, Fret = fret, MidiValue = track.PitchOf(stringIndex, fret), Velocity = 90 };
        foreach (var t in techniques) note.Techniques.Add(t);
        return note;
    }

    /// <summary>The blank song with bar 1 holding the given beats (slot, value) in voice 1, slot-indexed as the editor writes them.</summary>
    private static (SongProject Song, TrackModel Track) Bar(params (int Slot, int Denominator, TabNote? Note)[] beats)
    {
        var song = SongDocument.BlankProject();
        var track = song.Tracks[0];
        track.Measures[0].Cells = Enumerable.Range(0, 16).Select(_ => new TabCell()).ToList();
        foreach (var (slot, denominator, note) in beats)
            track.Measures[0].Cells[slot] = note is null ? new TabCell { DurationDenominator = denominator, IsRest = true } : new TabCell { DurationDenominator = denominator, Notes = { note } };
        return (song, track);
    }

    private static List<PianoRollNote> Read(SongProject song, TrackModel track) => new PianoRollTabSource().Read(song, track, PianoRollTimeMap.Written(song));

    private static bool Write(SongProject song, TrackModel track, IEnumerable<PianoRollNote> notes) =>
        new PianoRollTabSource().Write(song, track, PianoRollTimeMap.Written(song), notes.ToList());

    [Fact]
    public void MovingANoteLeavesTheOtherBeatsAsTheyWereAndCarriesItsMarks()
    {
        var song = SongDocument.BlankProject();
        var track = song.Tracks[0];
        (song, track) = Bar((0, 4, Note(track, 1, 1)), (4, 4, Note(track, 1, 3, "Vibrato")), (8, 4, Note(track, 2, 2, "PalmMute")));
        var cells = track.Measures[0].Cells;
        var (first, third) = (cells[0], cells[8]);
        var notes = Read(song, track);
        notes[1] = notes[1] with { Start = 3 };

        Assert.True(Write(song, track, notes));

        cells = track.Measures[0].Cells;
        var beats = MusicTime.BeatOnsets(cells).ToList();
        Assert.Same(first, beats.Single(b => b.Start == 0).Cell);
        Assert.Same(third, beats.Single(b => b.Start == 8).Cell);
        Assert.Contains("PalmMute", third.Notes.Single().Techniques);
        Assert.True(beats.Single(b => b.Start == 4).Cell.IsRest);   // the old place keeps its length as a rest
        var moved = beats.Single(b => b.Start == 12).Cell.Notes.Single();
        Assert.Contains("Vibrato", moved.Techniques);   // the moved note took its own mark along
        Assert.Equal(new[] { 0.0, 2.0, 3.0 }, Read(song, track).Select(n => n.Start).ToArray());
    }

    [Fact]
    public void ANoteInsideALongBeatSplitsItWithTiesAndTheLongNoteStillReadsAsOne()
    {
        var song = SongDocument.BlankProject();
        var track = song.Tracks[0];
        (song, track) = Bar((0, 1, Note(track, 1, 1)));
        var whole = track.Measures[0].Cells[0];
        var notes = Read(song, track);
        notes.Add(new PianoRollNote(2, 1, 64, 100));

        Assert.True(Write(song, track, notes));

        Assert.Equal(2, whole.DurationDenominator);   // the old beat is its own first piece, now a half
        var beats = MusicTime.BeatOnsets(track.Measures[0].Cells).ToList();
        Assert.Equal(new[] { 0.0, 8.0, 12.0 }, beats.Select(b => b.Start).ToArray());
        Assert.True(beats[1].Cell.Notes.Single(n => n.MidiValue == 60).Tied);
        Assert.Equal(new[] { (0.0, 4.0, 60), (2.0, 1.0, 64) }, Read(song, track).Select(n => (n.Start, n.Length, n.Pitch)).ToArray());
    }

    [Fact]
    public void ACompactBeatListKeepsItsBeatsWhenANoteIsShortened()
    {
        var song = SongDocument.BlankProject();
        var track = song.Tracks[0];
        track.Measures[0].Cells = new List<TabCell>
        {
            new() { DurationDenominator = 4, Notes = { Note(track, 1, 1) } }, new() { DurationDenominator = 4, Notes = { Note(track, 1, 3) } },
            new() { DurationDenominator = 4, Notes = { Note(track, 1, 5) } }, new() { DurationDenominator = 4, Notes = { Note(track, 0, 1) } },
        };
        var (a, c, d) = (track.Measures[0].Cells[0], track.Measures[0].Cells[2], track.Measures[0].Cells[3]);
        var notes = Read(song, track);
        notes[1] = notes[1] with { Length = 0.5 };

        Assert.True(Write(song, track, notes));

        Assert.Equal(new[] { (0.0, 1.0), (1.0, 0.5), (2.0, 1.0), (3.0, 1.0) }, Read(song, track).Select(n => (n.Start, n.Length)).ToArray());
        var beats = MusicTime.BeatOnsets(track.Measures[0].Cells).ToList();
        Assert.Same(a, beats.Single(b => b.Start == 0).Cell);
        Assert.Same(c, beats.Single(b => b.Start == 8).Cell);
        Assert.Same(d, beats.Single(b => b.Start == 12).Cell);
        Assert.True(beats.Single(b => b.Start == 6).Cell.IsRest);
    }

    [Fact]
    public void ADeletedNoteLeavesARestAndNothingMoves()
    {
        var song = SongDocument.BlankProject();
        var track = song.Tracks[0];
        (song, track) = Bar((0, 4, Note(track, 1, 1)), (4, 4, Note(track, 1, 3)));
        var notes = Read(song, track);
        Assert.True(Write(song, track, notes.Take(1)));
        Assert.True(track.Measures[0].Cells[4].IsRest);
        Assert.Equal(4, track.Measures[0].Cells[4].DurationDenominator);
        Assert.Equal(new[] { 0.0 }, Read(song, track).Select(n => n.Start).ToArray());
    }

    [Fact]
    public void TheSecondVoiceIsReadAndANoteMovedThereStaysInIt()
    {
        var song = SongDocument.BlankProject();
        var track = song.Tracks[0];
        (song, track) = Bar((0, 2, Note(track, 1, 1)));
        var voice2 = track.Measures[0].CellsForVoice(1, create: true);
        voice2[0] = new TabCell { DurationDenominator = 2, Notes = { Note(track, 4, 3) } };
        var notes = Read(song, track);
        Assert.Equal(new[] { 0, 1 }, notes.OrderBy(n => n.Pitch).Select(n => n.Voice).Reverse().ToArray());
        var low = notes.Single(n => n.Voice == 1);
        notes[notes.IndexOf(low)] = low with { Start = 2 };

        Assert.True(Write(song, track, notes));

        Assert.Single(track.Measures[0].Cells[0].Notes);   // voice 1 untouched
        Assert.Equal((2.0, 1), Read(song, track).Where(n => n.Voice == 1).Select(n => (n.Start, n.Voice)).Single());
    }

    [Fact]
    public void ATupletBeatIsNeverSplit()
    {
        var song = SongDocument.BlankProject();
        var track = song.Tracks[0];
        var cells = track.Measures[0].Cells = Enumerable.Range(0, 16).Select(_ => new TabCell()).ToList();
        for (var i = 0; i < 3; i++)
            cells[i] = new TabCell { DurationDenominator = 8, TupletNumerator = 3, TupletDenominator = 2, IsTriplet = true, Notes = { Note(track, 1, 1 + i) }, RhythmicPosition = i * 4.0 / 3 };
        var tuplets = cells.Take(3).ToList();
        var notes = Read(song, track);
        notes.Add(new PianoRollNote(0.5, 0.25, 72, 100));   // starts inside the second triplet eighth

        Assert.True(Write(song, track, notes));

        var beats = MusicTime.BeatOnsets(track.Measures[0].Cells).Where(b => MusicTime.ConsumesTime(b.Cell)).ToList();
        Assert.All(tuplets, t => Assert.Contains(beats, b => ReferenceEquals(b.Cell, t)));
        Assert.Contains(tuplets[1].Notes, n => n.MidiValue == 72);   // the note edge moved to the tuplet beat's own edge
    }

    [Fact]
    public void AMutedTabNoteIsStillWrittenButNeverPlays()
    {
        var song = SongDocument.BlankProject();
        var track = song.Tracks[0];
        (song, track) = Bar((0, 4, Note(track, 1, 1)));
        int NoteOns() => MidiTimelineBuilder.Build(song, new PlaybackOptions { RespectMuteSolo = false }).Events.Count(e => (e.Status & 0xF0) == 0x90 && e.Data2 > 0 && e.Data1 == 60);
        Assert.Equal(1, NoteOns());
        var notes = Read(song, track);
        Assert.True(Write(song, track, new[] { notes[0] with { Muted = true } }));
        song.MarkTimelineChanged();
        var written = track.Measures[0].Cells[0].Notes.Single();
        Assert.True(written.Silent);
        Assert.False(written.Dead);
        Assert.Equal(1, written.Fret);
        Assert.Equal(0, NoteOns());
        Assert.True(Read(song, track).Single().Muted);
        Assert.True(written.Clone().Silent);
    }
}
