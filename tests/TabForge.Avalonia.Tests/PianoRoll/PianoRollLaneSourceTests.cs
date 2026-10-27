using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presenters;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: what the piano roll edits on a track that has both written tab notes and MIDI takes: the tab until a take lane is chosen; a lane shows and edits
//   every take on it (each note goes back to the take that holds it, a new note inside a take into that take, one outside every take grows the nearest),
//   never touches the tab and never asks the tab question; choosing the tab asks once per visit as before; another track resets the choice.
// Does not own: the click on a take (TakeClickNeverSelectsNotesTests) or the plain tab / MIDI sources (PianoRollSourceTests).
// Tests: PianoRollPresenter, PianoRollSources, PianoRollClipSource, PianoRollGhosts.
public sealed class PianoRollLaneSourceTests
{
    /// <summary>The tab track (C4 at beat 0) with three MIDI takes: A (0 to 4 s) and B (4 to 8 s) on lane 0, C (0 to 4 s) on lane 1.</summary>
    private static SongDocument Hybrid()
    {
        var document = PianoRollSongs.Tab();
        var track = document.Project.Tracks[0];
        track.AudioClips.Add(Take("A", 0, 0, new ClipNote(0.5, 0.5, 60, 90)));
        track.AudioClips.Add(Take("B", 0, 4, new ClipNote(0.5, 0.5, 64, 100)));
        track.AudioClips.Add(Take("C", 1, 0, new ClipNote(1.0, 0.5, 67, 80)));
        ClipLanes.Ensure(track, 2);
        return document;
    }

    private static AudioClip Take(string name, int lane, double start, params ClipNote[] notes) =>
        new() { Name = name, Lane = lane, StartSec = start, SourceLengthSec = 4, FileLengthSec = 4, Notes = new List<ClipNote>(notes) };

    private static AudioClip Clip(SongDocument document, string name) => document.Project.Tracks[0].AudioClips.Single(c => c.Name == name);

    private static (PianoRollPresenter Presenter, List<string> Asked) Open(SongDocument document)
    {
        var asked = new List<string>();
        return (PianoRollSongs.Presenter(document, name => { asked.Add(name); return Task.FromResult(true); }), asked);
    }

    [Fact]
    public void ATrackWithNotesAndTakesStaysOnItsTabUntilALaneIsChosenAndOffersEachSource()
    {
        var (presenter, _) = Open(Hybrid());
        Assert.True(presenter.IsTab);
        Assert.Equal("Editing: Tab", presenter.SourceLabel);
        Assert.Equal(new[] { "Tab", "MIDI lane 1 (2 takes)", "MIDI lane 2 (1 take)" }, presenter.SourceOptions().Select(o => o.Label).ToArray());
    }

    [Fact]
    public async Task ALaneShowsEveryTakeOnItAndAnEditLandsInTheRightTakeWithoutAskingOrTouchingTheTab()
    {
        var document = Hybrid();
        var (presenter, asked) = Open(document);
        presenter.UseSource(PianoRollSourceChoice.OnLane(0));

        Assert.False(presenter.IsTab);
        Assert.Equal("Editing: MIDI lane 1 (2 takes)", presenter.SourceLabel);
        Assert.Equal(new[] { 60, 64 }, presenter.Notes.Select(n => n.Pitch).ToArray());   // both takes of the lane, not the other lane or the tab
        var tabNote = document.Project.Tracks[0].Measures[0].Cells[0].Notes.Single().MidiValue;
        var steps = document.Undo.UndoCount;

        presenter.Select(new[] { 1 });
        Assert.True(await presenter.TransposeAsync(2));

        Assert.Empty(asked);
        Assert.Equal(steps + 1, document.Undo.UndoCount);
        Assert.Equal(66, Clip(document, "B").Notes!.Single().Pitch);
        Assert.Equal(60, Clip(document, "A").Notes!.Single().Pitch);
        Assert.Equal(67, Clip(document, "C").Notes!.Single().Pitch);
        Assert.Equal(tabNote, document.Project.Tracks[0].Measures[0].Cells[0].Notes.Single().MidiValue);

        DocumentEdits.Undo(document);   // one step per source edit
        presenter.Reload();
        Assert.Equal(64, Clip(document, "B").Notes!.Single().Pitch);
    }

    [Fact]
    public async Task ANewNoteInsideATakeGoesIntoThatTakeAndOneOutsideEveryTakeGrowsTheNearestOneOfTheLane()
    {
        var document = Hybrid();
        var (presenter, asked) = Open(document);
        presenter.UseSource(PianoRollSourceChoice.OnLane(0));
        presenter.Select(new[] { 0 });
        presenter.Copy();

        Assert.True(await presenter.PasteAsync(10));   // beat 10 = 5 s: inside B (4 to 8 s)
        Assert.Equal(2, Clip(document, "B").Notes!.Count);
        Assert.Single(Clip(document, "A").Notes!);

        Assert.True(await presenter.PasteAsync(40));   // beat 40 = 20 s: outside both takes, so the nearest (B) grows to hold it
        Assert.Equal(3, Clip(document, "B").Notes!.Count);
        Assert.True(Clip(document, "B").EndSec >= 20);
        Assert.Equal(3, document.Project.Tracks[0].AudioClips.Count);   // no clip was created
        Assert.Empty(asked);
    }

    [Fact]
    public async Task ChoosingTheTabAsksOncePerVisitAsBefore()
    {
        var document = Hybrid();
        var (presenter, asked) = Open(document);
        presenter.UseSource(PianoRollSourceChoice.OnLane(0));
        presenter.UseSource(PianoRollSourceChoice.OnTab);
        Assert.True(presenter.IsTab);
        presenter.Select(new[] { 0 });

        Assert.True(await presenter.TransposeAsync(1));
        Assert.True(await presenter.TransposeAsync(1));

        Assert.Single(asked);
    }

    [Fact]
    public void AnotherTrackGoesBackToTheTrackDecidingAndTheOtherLaneShowsOnlyItsOwnTake()
    {
        var document = Hybrid();
        document.Project.Tracks.Add(document.Project.Tracks[0].ShallowCopy());
        var (presenter, _) = Open(document);
        presenter.UseSource(PianoRollSourceChoice.OnLane(1));
        Assert.Equal(new[] { 67 }, presenter.Notes.Select(n => n.Pitch).ToArray());

        presenter.Show(1);
        presenter.Show(0);

        Assert.Equal(default, presenter.Choice);
        Assert.True(presenter.IsTab);
    }

    [Fact]
    public void WhileATakeLaneIsEditedTheTabAndTheOtherLaneShowAsFaintGhostsOnly()
    {
        var document = Hybrid();
        var (presenter, _) = Open(document);
        presenter.UseSource(PianoRollSourceChoice.OnLane(0));

        var ghosts = PianoRollGhosts.Read(document.Project, 0, presenter.Map, presenter.Choice);

        Assert.Equal(new[] { 60, 67 }, ghosts.Select(n => n.Pitch).OrderBy(p => p).ToArray());   // the written C4 and the other lane take
    }
}
