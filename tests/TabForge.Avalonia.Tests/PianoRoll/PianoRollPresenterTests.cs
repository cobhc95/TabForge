using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presenters;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>Songs the piano roll tests share: a MIDI track (one MIDI take) and a tab track (one written note), both on the blank 4-bar song at 120 bpm.</summary>
internal static class PianoRollSongs
{
    /// <summary>A MIDI track: one clip at 0 s holding C4 at 0.5 s (beat 1) and E4 at 1 s (beat 2), each 0.5 s long.</summary>
    public static SongDocument Midi(bool mutedSecond = false)
    {
        var project = SongDocument.BlankProject();
        project.Tracks[0].AudioClips.Add(new AudioClip
        {
            Name = "take", StartSec = 0, SourceLengthSec = 8, FileLengthSec = 8,
            Notes = new List<ClipNote> { new(0.5, 0.5, 60, 90), new(1.0, 0.5, 64, 100, mutedSecond) },
        });
        return new SongDocument(() => null) { Project = project };
    }

    /// <summary>A tab track: one quarter-note C4 (B string, fret 1) at the start of bar 1, velocity 90.</summary>
    public static SongDocument Tab()
    {
        var project = SongDocument.BlankProject();
        var track = project.Tracks[0];
        var measure = track.Measures[0];
        measure.Cells = Enumerable.Range(0, 16).Select(_ => new TabCell()).ToList();
        measure.Cells[0] = new TabCell { DurationDenominator = 4, Notes = { new TabNote { StringIndex = 1, Fret = 1, MidiValue = track.PitchOf(1, 1), Velocity = 90 } } };
        return new SongDocument(() => null) { Project = project };
    }

    public static PianoRollPresenter Presenter(SongDocument document, Func<string, Task<bool>>? confirm = null)
    {
        var presenter = new PianoRollPresenter(() => document, confirm ?? (_ => Task.FromResult(true)));
        presenter.Show(0);
        return presenter;
    }
}

// Owns: the piano roll presenter's commit rules: one undo step per finished edit (and Undo brings the notes back), the tab prompt once per visit
//   for pitch and timing edits only (Cancel applies nothing and leaves no step), and the selection kept on the edited notes.
// Does not own: the pure edits (PianoRollEditTests) or the storage (PianoRollSourceTests).
// Tests: PianoRollPresenter.
public sealed class PianoRollPresenterTests
{
    [Fact]
    public async Task AFinishedMoveIsOneUndoStepAndUndoBringsTheNotesBack()
    {
        var document = PianoRollSongs.Midi();
        var presenter = PianoRollSongs.Presenter(document);
        Assert.False(presenter.IsTab);
        Assert.Equal(new[] { 1.0, 2.0 }, presenter.Notes.Select(n => System.Math.Round(n.Start, 6)).ToArray());
        presenter.Select(new[] { 0, 1 });
        var steps = document.Undo.UndoCount;

        Assert.True(await presenter.MoveAsync(1, 2));

        Assert.Equal(steps + 1, document.Undo.UndoCount);
        var clip = document.Project.Tracks[0].AudioClips[0];
        Assert.Equal(new[] { (1.0, 62), (1.5, 66) }, clip.Notes!.Select(n => (System.Math.Round(n.StartSec, 6), n.Pitch)).ToArray());
        Assert.Equal(2, presenter.Selected.Count);   // the moved notes stay selected

        DocumentEdits.Undo(document);
        presenter.Reload();
        Assert.Equal(steps, document.Undo.UndoCount);
        Assert.Equal(new[] { (0.5, 60), (1.0, 64) }, document.Project.Tracks[0].AudioClips[0].Notes!.Select(n => (System.Math.Round(n.StartSec, 6), n.Pitch)).ToArray());
    }

    [Fact]
    public async Task AQuantizeOfEveryNoteIsOneStep()
    {
        var document = PianoRollSongs.Midi();
        document.Project.Tracks[0].AudioClips[0].Notes![0] = new ClipNote(0.56, 0.5, 60, 90);   // beat 1.12
        var presenter = PianoRollSongs.Presenter(document);
        var steps = document.Undo.UndoCount;
        Assert.True(await presenter.QuantizeAsync("1/4", 1, starts: true, ends: false));
        Assert.Equal(steps + 1, document.Undo.UndoCount);
        Assert.Equal(0.5, document.Project.Tracks[0].AudioClips[0].Notes![0].StartSec, 6);
    }

    [Fact]
    public async Task ATabTrackAsksOncePerVisitForPitchAndTimingOnly()
    {
        var document = PianoRollSongs.Tab();
        var asked = new List<string>();
        var presenter = PianoRollSongs.Presenter(document, name => { asked.Add(name); return Task.FromResult(true); });
        Assert.True(presenter.IsTab);
        presenter.Select(new[] { 0 });

        Assert.True(await presenter.SetVelocityAsync(50));   // velocity: no prompt
        Assert.Empty(asked);
        Assert.True(await presenter.ToggleMuteAsync());   // mute: no prompt
        Assert.Empty(asked);
        Assert.True(await presenter.TransposeAsync(2));
        Assert.Single(asked);
        Assert.True(await presenter.TransposeAsync(1));
        Assert.Single(asked);   // once per visit

        presenter.BeginVisit();
        Assert.True(await presenter.TransposeAsync(1));
        Assert.Equal(2, asked.Count);
        Assert.Equal(document.Project.Tracks[0].Name, asked[0]);
    }

    [Fact]
    public async Task CancellingTheTabPromptAppliesNothingAndLeavesNoStep()
    {
        var document = PianoRollSongs.Tab();
        var presenter = PianoRollSongs.Presenter(document, _ => Task.FromResult(false));
        presenter.Select(new[] { 0 });
        var steps = document.Undo.UndoCount;

        Assert.False(await presenter.MoveAsync(1, 0));

        Assert.Equal(steps, document.Undo.UndoCount);
        Assert.False(presenter.TabConfirmed);
        var cell = document.Project.Tracks[0].Measures[0].Cells[0];
        Assert.Equal(60, cell.Notes.Single().MidiValue);
    }

    [Fact]
    public async Task AnotherTrackIsANewVisit()
    {
        var document = PianoRollSongs.Tab();
        document.Project.Tracks.Add(document.Project.Tracks[0].ShallowCopy());
        var asked = 0;
        var presenter = PianoRollSongs.Presenter(document, _ => { asked++; return Task.FromResult(true); });
        presenter.Select(new[] { 0 });
        await presenter.TransposeAsync(1);
        presenter.Show(1);
        presenter.Show(0);
        presenter.Select(new[] { 0 });
        await presenter.TransposeAsync(1);
        Assert.Equal(2, asked);
    }

    [Fact]
    public void TheTabPromptNamesTheTrackAndOffersEditOrCancel()
    {
        var spec = PianoRollPresenter.TabPrompt("Rhythm Gtr");
        Assert.Equal("Edit the tab of “Rhythm Gtr”?", spec.Title);
        Assert.Equal("Edit the tab", spec.YesText);
        Assert.Equal("Cancel", spec.NoText);
        Assert.Contains("Velocity and mute changes never ask", spec.Message);
    }
}
