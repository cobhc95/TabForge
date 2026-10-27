using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: where the piano roll's notes live: a MIDI track's clips (read and written back on the performed clock, the clip growing to hold a note, a
//   new clip for a MIDI track with none, a muted note kept but never played) and a tab track's first voice (velocity and mute in place, an added
//   note written with its string and fret picked, a note over a bar line tied on, unchanged notes keeping their techniques); the saved Muted flag
//   and view choice, and the Auto view.
// Does not own: the commit and the prompt (PianoRollPresenterTests).
// Tests: PianoRollClipSource, PianoRollTabSource, PianoRollViewRules, ClipNote.Muted, TrackModel.EditorView.
public sealed class PianoRollSourceTests
{
    [Fact]
    public void AClipTrackReadsBackUnchangedAndAWriteOfTheSameNotesChangesNothing()
    {
        var song = PianoRollSongs.Midi().Project;
        var track = song.Tracks[0];
        var source = PianoRollSources.For(track);
        Assert.IsType<PianoRollClipSource>(source);
        var map = source.Map(song);
        var notes = source.Read(song, track, map);
        Assert.Equal(new[] { (1.0, 1.0, 60), (2.0, 1.0, 64) }, notes.Select(n => (Math.Round(n.Start, 6), Math.Round(n.Length, 6), n.Pitch)).ToArray());
        Assert.False(source.Write(song, track, map, notes));
    }

    [Fact]
    public void ANoteAfterTheClipGrowsItAndAMidiTrackWithNoClipGetsOne()
    {
        var song = PianoRollSongs.Midi().Project;
        var track = song.Tracks[0];
        var source = PianoRollSources.For(track);
        var map = source.Map(song);
        var notes = source.Read(song, track, map).Append(new PianoRollNote(20, 1, 67, 100)).ToList();
        Assert.True(source.Write(song, track, map, notes));
        var clip = track.AudioClips.Single();
        Assert.True(clip.EndSec >= 10.5 - 1e-6);
        Assert.Equal(3, clip.Notes!.Count);

        var empty = TabForge.Documents.SongDocument.BlankProject();
        var midiTrack = empty.Tracks[0];
        midiTrack.AudioInput = AudioInputs.Midi;
        Assert.True(PianoRollViewRules.UsesClips(midiTrack));
        var fresh = PianoRollSources.For(midiTrack);
        Assert.True(fresh.Write(empty, midiTrack, fresh.Map(empty), new[] { new PianoRollNote(2, 1, 60, 90) }));
        var created = midiTrack.AudioClips.Single();
        Assert.True(created.IsMidi);
        Assert.Equal(1.0, created.StartSec, 6);
    }

    [Fact]
    public void AMutedNoteStaysInTheClipButDoesNotPlay()
    {
        int NoteOns(SongProject song, int pitch) =>
            MidiTimelineBuilder.Build(song, new PlaybackOptions { RespectMuteSolo = false }).Events.Count(e => (e.Status & 0xF0) == 0x90 && e.Data2 > 0 && e.Data1 == pitch);
        Assert.Equal(1, NoteOns(PianoRollSongs.Midi().Project, 64));
        var muted = PianoRollSongs.Midi(mutedSecond: true).Project;
        Assert.Equal(0, NoteOns(muted, 64));
        Assert.Equal(1, NoteOns(muted, 60));
        var notes = PianoRollSources.For(muted.Tracks[0]).Read(muted, muted.Tracks[0], PianoRollTimeMap.Performed(muted));
        Assert.True(notes[1].Muted);
    }

    [Fact]
    public void TabVelocityAndMuteChangeTheNoteInPlaceAndMuteLeavesTheTabAsWritten()
    {
        var song = PianoRollSongs.Tab().Project;
        var track = song.Tracks[0];
        var source = PianoRollSources.For(track);
        Assert.IsType<PianoRollTabSource>(source);
        var map = source.Map(song);
        var notes = source.Read(song, track, map);
        Assert.Equal(new PianoRollNote(0, 1, 60, 90), notes.Single());
        var written = track.Measures[0].Cells[0].Notes[0];
        Assert.True(source.Write(song, track, map, new[] { notes[0] with { Velocity = 40, Muted = true } }));
        Assert.Same(written, track.Measures[0].Cells[0].Notes[0]);
        Assert.Equal(40, written.Velocity);
        Assert.True(written.Silent);
        Assert.False(written.Dead);   // the written tab is unchanged: no dead note
    }

    [Fact]
    public void AnAddedTabNoteGetsItsStringAndFretAndTheOldNoteKeepsItsTechniques()
    {
        var song = PianoRollSongs.Tab().Project;
        var track = song.Tracks[0];
        track.Measures[0].Cells[0].Notes[0].Techniques.Add("PalmMute");
        var source = PianoRollSources.For(track);
        var map = source.Map(song);
        var notes = source.Read(song, track, map).Append(new PianoRollNote(1, 1, 64, 100)).ToList();

        Assert.True(source.Write(song, track, map, notes));

        var cells = track.Measures[0].Cells;
        Assert.Equal(4, cells[0].DurationDenominator);
        Assert.Contains("PalmMute", cells[0].Notes.Single().Techniques);
        var added = cells[4].Notes.Single();
        Assert.Equal((0, 0, 64), (added.StringIndex, added.Fret, added.MidiValue));   // E4: the open high string
        Assert.Equal(new[] { (0.0, 60), (1.0, 64) }, source.Read(song, track, map).Select(n => (n.Start, n.Pitch)).ToArray());
    }

    [Fact]
    public void ATabNoteOverTheBarLineIsTiedOnAndReadsBackAsOneNote()
    {
        var song = PianoRollSongs.Tab().Project;
        var track = song.Tracks[0];
        var source = PianoRollSources.For(track);
        var map = source.Map(song);
        Assert.True(source.Write(song, track, map, new[] { new PianoRollNote(3, 2, 60, 90) }));
        Assert.Empty(track.Measures[0].Cells[0].Notes);
        Assert.Equal(4, track.Measures[0].Cells[12].DurationDenominator);
        Assert.True(track.Measures[1].Cells[0].Notes.Single().Tied);
        var back = source.Read(song, track, map).Single();
        Assert.Equal((3.0, 2.0), (back.Start, back.Length));
    }

    [Fact]
    public void TheMutedFlagAndTheViewChoiceAreSavedAndOldFilesLoadTheDefaults()
    {
        Assert.DoesNotContain("Muted", JsonSerializer.Serialize(new ClipNote(0, 1, 60, 90)));
        Assert.False(JsonSerializer.Deserialize<ClipNote>("{\"StartSec\":0,\"LengthSec\":1,\"Pitch\":60,\"Velocity\":90}")!.Muted);
        var song = PianoRollSongs.Midi(mutedSecond: true).Project;
        song.Tracks[0].EditorView = TrackEditorView.Score;
        var folder = Path.Combine(Path.GetTempPath(), "tf-pianoroll-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "song.tforge");
            ProjectService.Save(path, song);
            var loaded = ProjectService.Load(path);
            Assert.Equal(TrackEditorView.Score, loaded.Tracks[0].EditorView);
            Assert.Equal(new[] { false, true }, loaded.Tracks[0].AudioClips[0].Notes!.Select(n => n.Muted).ToArray());
            var plain = PianoRollSongs.Tab().Project;
            ProjectService.Save(path, plain);
            Assert.Equal(TrackEditorView.Auto, ProjectService.Load(path).Tracks[0].EditorView);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void MidiTracksOpenInThePianoRollAndTabTracksInTheScoreUnlessChosen()
    {
        var midi = PianoRollSongs.Midi().Project.Tracks[0];
        var tab = PianoRollSongs.Tab().Project.Tracks[0];
        Assert.True(PianoRollViewRules.ShowsPianoRoll(midi));
        Assert.False(PianoRollViewRules.ShowsPianoRoll(tab));
        tab.EditorView = TrackEditorView.PianoRoll;
        midi.EditorView = TrackEditorView.Score;
        Assert.True(PianoRollViewRules.ShowsPianoRoll(tab));
        Assert.False(PianoRollViewRules.ShowsPianoRoll(midi));
        var audio = new TrackModel { Kind = TrackKind.Audio, EditorView = TrackEditorView.PianoRoll };
        Assert.False(PianoRollViewRules.ShowsPianoRoll(audio));   // no MIDI take: nothing to show
    }
}
