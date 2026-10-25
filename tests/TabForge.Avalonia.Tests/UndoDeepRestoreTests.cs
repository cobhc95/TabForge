using System;
using System.Collections.Generic;
using System.Linq;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: undo/redo deep restore (a sequence of edits undone in full returns the exact original song).
// Does not own: the undo stack (UndoController), the edit entry points (DocumentEdits) or the song model.
// Tests: this class. Headless, in-memory, no sleeps.
public sealed class UndoDeepRestoreTests
{
    private static (SongProject Song, UndoController Undo) BuildDocument()
    {
        var song = new SongProject
        {
            Title = "UndoDeep",
            Tempo = 120,
            TimeSignatureNumerator = 4,
            TimeSignatureDenominator = 4,
        };
        foreach (var (name, kind, channel, tuning) in new[]
                 {
                     ("Lead", TrackKind.Guitar, 0, new[] { 64, 59, 55, 50, 45, 40 }),
                     ("Bass", TrackKind.Bass, 1, new[] { 43, 38, 33, 28 }),
                 })
            song.Tracks.Add(new TrackModel
            {
                Name = name,
                Kind = kind,
                MidiChannel = channel,
                StringTunings = tuning.ToList(),
                Measures = TemplateFactory.Measures(3), // TemplateFactory.cs:117-119
            });
        PutNote(song.Tracks[0], 0, 0, 0, 3);
        PutNote(song.Tracks[0], 0, 8, 1, 2);
        PutNote(song.Tracks[1], 0, 0, 0, 0);
        PutNote(song.Tracks[0], 1, 0, 0, 5);
        return (song, new UndoController()); // UndoController.cs:21-31
    }

    private static void PutNote(TrackModel track, int bar, int cell, int stringIndex, int fret)
    {
        var beat = track.Measures[bar].Cells[cell];
        beat.DurationDenominator = 4;
        beat.Notes.Add(new TabNote
        {
            StringIndex = stringIndex,
            Fret = fret,
            MidiValue = track.PitchOf(stringIndex, fret), // SongProject.cs:151-153
        });
    }

    private static void Edit_AddNote(SongProject song)
    {
        var beat = song.Tracks[0].Measures[0].Cells[4];
        beat.DurationDenominator = 8;
        beat.Notes.Add(new TabNote { StringIndex = 0, Fret = 2, MidiValue = song.Tracks[0].PitchOf(0, 2) });
    }

    private static void Edit_ChangeDuration(SongProject song) =>
        song.Tracks[0].Measures[0].Cells[0].DurationDenominator = 8;

    private static void Edit_AddEffect(SongProject song) =>
        song.Tracks[0].Measures[0].Cells[0].Notes[0].Techniques.Add("PalmMute"); // GpEffects.All (SongProject.cs:491)

    private static void Edit_InsertBar(SongProject song)
    {
        foreach (var track in song.Tracks) track.Measures.Insert(1, new MeasureModel());
        foreach (var track in song.Tracks)
            for (var i = 0; i < track.Measures.Count; i++) track.Measures[i].Number = i + 1;
    }

    private static void Edit_ChangeTimeSignature(SongProject song) =>
        song.TimeSignatureNumerator = 3; // SongProject.cs:43

    private static readonly Action<SongProject>[] Edits =
    {
        Edit_AddNote, Edit_ChangeDuration, Edit_AddEffect, Edit_InsertBar, Edit_ChangeTimeSignature,
    };

    private static SongProject ApplyAllEdits(SongProject song, UndoController undo)
    {
        // DocumentEdits.cs:41-56 pattern: a transaction before the mutation, a commit after.
        foreach (var edit in Edits)
        {
            var transaction = undo.BeginTransaction(song); // UndoController.cs:63
            edit(song);
            undo.Commit(transaction); // UndoController.cs:67-75
        }
        return song;
    }

    private static SongProject UndoOnce(UndoController undo, SongProject song)
    {
        // DocumentEdits.Undo pattern (DocumentEdits.cs:93-105).
        var current = undo.Snapshot(song); // UndoController.cs:55
        Assert.True(undo.TryUndo(current, out var target)); // UndoController.cs:101-108
        return undo.Restore(target, song); // UndoController.cs:61
    }

    private static SongProject RedoOnce(UndoController undo, SongProject song)
    {
        // DocumentEdits.Redo pattern (DocumentEdits.cs:107-120).
        var current = undo.Snapshot(song);
        Assert.True(undo.TryRedo(current, out var target)); // UndoController.cs:110-117
        return undo.Restore(target, song);
    }

    // Deep equality: every bar, beat, note, duration, effect and time signature, not just counts.
    private static bool SongsEqual(SongProject a, SongProject b)
    {
        if (a.Tempo != b.Tempo || a.TimeSignatureNumerator != b.TimeSignatureNumerator ||
            a.TimeSignatureDenominator != b.TimeSignatureDenominator) return false;
        if (a.Tracks.Count != b.Tracks.Count) return false;
        for (var t = 0; t < a.Tracks.Count; t++)
        {
            var at = a.Tracks[t];
            var bt = b.Tracks[t];
            if (at.Name != bt.Name || at.Kind != bt.Kind || at.Capo != bt.Capo || at.MidiChannel != bt.MidiChannel) return false;
            if (!at.StringTunings.SequenceEqual(bt.StringTunings)) return false;
            if (at.Measures.Count != bt.Measures.Count) return false;
            for (var m = 0; m < at.Measures.Count; m++)
            {
                var am = at.Measures[m];
                var bm = bt.Measures[m];
                if (am.Number != bm.Number || am.TimeSigNum != bm.TimeSigNum ||
                    am.TimeSigDenom != bm.TimeSigDenom || am.TempoChange != bm.TempoChange) return false;
                if (am.Cells.Count != bm.Cells.Count) return false;
                for (var c = 0; c < am.Cells.Count; c++)
                {
                    var ac = am.Cells[c];
                    var bc = bm.Cells[c];
                    if (ac.DurationDenominator != bc.DurationDenominator || ac.Dots != bc.Dots ||
                        ac.IsRest != bc.IsRest || ac.IsTied != bc.IsTied) return false;
                    if (ac.Notes.Count != bc.Notes.Count) return false;
                    for (var n = 0; n < ac.Notes.Count; n++)
                    {
                        var an = ac.Notes[n];
                        var bn = bc.Notes[n];
                        if (an.StringIndex != bn.StringIndex || an.Fret != bn.Fret || an.MidiValue != bn.MidiValue ||
                            an.Tied != bn.Tied || an.Dead != bn.Dead || an.Ghost != bn.Ghost) return false;
                        if (!an.Techniques.SetEquals(bn.Techniques)) return false;
                    }
                }
            }
        }
        return true;
    }

    [Fact]
    public void FiveEdits_ProduceFiveUndoLevels()
    {
        var (song, undo) = BuildDocument();
        ApplyAllEdits(song, undo);

        Assert.Equal(Edits.Length, undo.UndoCount);
    }

    [Fact]
    public void UndoAll_RestoresSongDeepEqualToOriginal()
    {
        var (song, undo) = BuildDocument();
        var original = undo.Restore(undo.Snapshot(song), null);
        ApplyAllEdits(song, undo);
        Assert.False(SongsEqual(song, original));

        for (var i = 0; i < Edits.Length; i++) song = UndoOnce(undo, song);

        Assert.True(SongsEqual(song, original));
        Assert.False(undo.CanUndo);
    }

    [Fact]
    public void RedoAll_RestoresSongDeepEqualToEditedState()
    {
        var (song, undo) = BuildDocument();
        ApplyAllEdits(song, undo);
        var edited = undo.Restore(undo.Snapshot(song), null);

        for (var i = 0; i < Edits.Length; i++) song = UndoOnce(undo, song);
        for (var i = 0; i < Edits.Length; i++) song = RedoOnce(undo, song);

        Assert.True(SongsEqual(song, edited));
        Assert.False(undo.CanRedo);
    }

    [Fact]
    public void SingleUndo_RemovesOnlyTheLastEdit()
    {
        var (song, undo) = BuildDocument();
        ApplyAllEdits(song, undo);

        song = UndoOnce(undo, song);

        Assert.Equal(4, song.TimeSignatureNumerator);
        Assert.Equal(4, song.Tracks[0].Measures.Count); // inserted bar is still there
        Assert.Single(song.Tracks[0].Measures[0].Cells[4].Notes); // added note is still there
    }

    [Fact]
    public void NewEditAfterUndo_ClearsRedo()
    {
        var (song, undo) = BuildDocument();
        ApplyAllEdits(song, undo);
        song = UndoOnce(undo, song);
        Assert.True(undo.CanRedo);

        // UndoController.cs:71-72: committing a new step clears the redo stack.
        var transaction = undo.BeginTransaction(song);
        song.Title = "Changed after undo";
        undo.Commit(transaction);

        Assert.False(undo.CanRedo);
    }

    [Fact]
    public void DeepEqual_DetectsAnEffectOnlyDifference()
    {
        var (song, undo) = BuildDocument();
        var copy = undo.Restore(undo.Snapshot(song), null);

        Assert.True(SongsEqual(song, copy));
        copy.Tracks[0].Measures[0].Cells[0].Notes[0].Techniques.Add("PalmMute");

        Assert.False(SongsEqual(song, copy));
    }
}
