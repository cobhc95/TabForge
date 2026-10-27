using System.Collections.Generic;
using System.Linq;
using TabForge.Documents;
using TabForge.Menus;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the piano roll's second set of features in Core: glue, legato, randomised velocity and humanised timing, chord stamps, swing and the
//   dotted grid, the scale highlight and the row folds, and the menus with no greyed "later" rows left.
// Does not own: the first set of edits (PianoRollEditTests) or the controller lanes (PianoRollControllerTests).
// Tests: PianoRollEdits (glue, legato, randomise, humanise, chord), PianoRollTimeMap swing, SnapCore dotted, PianoRollScale, PianoRollMenus.
public sealed class PianoRollMoreEditTests
{
    private static readonly IReadOnlyList<PianoRollNote> Notes = new[]
    {
        new PianoRollNote(0, 0.5, 60, 80), new PianoRollNote(1, 0.5, 60, 90), new PianoRollNote(2, 0.5, 64, 100), new PianoRollNote(3, 1, 60, 110),
    };

    private static PianoRollTimeMap Map() => PianoRollTimeMap.Written(SongDocument.BlankProject());

    [Fact]
    public void GlueJoinsTheSelectedNotesOfEachPitchAndTheGlueToolJoinsWithTheNext()
    {
        var glued = PianoRollEdits.Glue(Notes, new[] { 0, 1, 2 });
        Assert.Equal(3, glued.Notes.Count);
        Assert.Contains(new PianoRollNote(0, 1.5, 60, 80), glued.Notes);
        Assert.Contains(Notes[2], glued.Notes);
        Assert.Equal(2, glued.Selected.Count);
        var tool = PianoRollEdits.GlueAt(Notes, 1);
        Assert.Contains(new PianoRollNote(1, 3, 60, 90), tool.Notes);
        Assert.Equal(3, tool.Notes.Count);
        Assert.Equal(4, PianoRollEdits.GlueAt(Notes, 2).Notes.Count);   // no later note of its pitch
    }

    [Fact]
    public void LegatoStretchesEachNoteToTheNextStart()
    {
        var all = PianoRollEdits.Legato(Notes, new int[0]);
        Assert.Equal(new[] { 1.0, 1.0, 1.0, 1.0 }, all.Notes.Select(n => n.Length).ToArray());
        var one = PianoRollEdits.Legato(Notes, new[] { 2 });
        Assert.Equal(new[] { 0.5, 0.5, 1.0, 1.0 }, one.Notes.Select(n => n.Length).ToArray());
    }

    [Fact]
    public void RandomiseAndHumaniseStayInTheirRangeAndRepeatWithTheSeed()
    {
        var all = new[] { 0, 1, 2, 3 };
        var a = PianoRollEdits.RandomiseVelocity(Notes, all, 10, 7);
        Assert.Equal(a.Notes, PianoRollEdits.RandomiseVelocity(Notes, all, 10, 7).Notes);
        Assert.All(a.Notes.Zip(Notes), p => Assert.InRange(p.First.Velocity - p.Second.Velocity, -10, 10));
        var h = PianoRollEdits.Humanise(Notes, all, 0.05, 3);
        Assert.All(h.Notes.Zip(Notes), p => { Assert.InRange(p.First.Start - p.Second.Start, -0.0501, 0.0501); Assert.Equal(p.Second.End, p.First.End, 9); });
    }

    [Fact]
    public void AChordStampAddsTheChordsTonesAboveTheRoot()
    {
        Assert.Contains("Maj", PianoRollEdits.ChordNames);
        var chord = PianoRollEdits.Chord(new PianoRollNote(0, 1, 60, 100), "min7");
        Assert.Equal(new[] { 60, 63, 67, 70 }, chord.Select(n => n.Pitch).ToArray());
        Assert.Single(PianoRollEdits.Chord(new PianoRollNote(0, 1, 60, 100), "Off"));
        var added = PianoRollEdits.AddMany(Notes, chord);
        Assert.Equal(8, added.Notes.Count);
        Assert.Equal(new[] { 4, 5, 6, 7 }, added.Selected);
    }

    [Fact]
    public void SwingMovesEverySecondLineLaterAndTheDottedGridIsOneAndAHalfValues()
    {
        var map = Map();
        Assert.Equal(0.5 + 0.5 / 3, map.NearestGrid(0.62, "1/8", 100), 6);   // full swing: the off-beat eighth at the triplet place
        Assert.Equal(1.0, map.NearestGrid(0.98, "1/8", 100), 6);   // the on-beat lines stay
        var swung = PianoRollEdits.Quantize(new[] { new PianoRollNote(0.55, 0.25, 60, 90) }, new int[0], map, "1/8", 1, true, false, swing: 50);
        Assert.Equal(0.5 + 0.5 / 6, swung.Notes[0].Start, 6);
        Assert.Contains("1/8D", SnapSettings.Grids);
        Assert.Equal(3.0, SnapCore.GridStepSlots("1/8D", 16), 9);
        Assert.Equal(1.5, map.NearestGrid(1.4, "1/8D"), 6);
    }

    [Fact]
    public void TheScaleHighlightAndTheFoldsPickTheRows()
    {
        var classes = PianoRollScale.PitchClasses("A Minor Pentatonic")!;
        Assert.Equal(new[] { 0, 2, 4, 7, 9 }, classes.OrderBy(c => c).ToArray());
        Assert.Null(PianoRollScale.PitchClasses(null));
        Assert.Null(PianoRollScale.PitchClasses("Nonsense"));
        Assert.Equal(128, PianoRollScale.Rows(PianoRollScale.FoldNone, classes, Notes).Count);
        var scaleRows = PianoRollScale.Rows(PianoRollScale.FoldScale, classes, Notes);
        Assert.All(scaleRows, p => Assert.Contains(p % 12, classes));
        // One or two used pitches never leave a blank strip: an octave either side shows (regression: one G4 row and nothing to draw on).
        var fewUsed = PianoRollScale.Rows(PianoRollScale.FoldUsed, classes, Notes);
        Assert.Equal(64 + 12, fewUsed[0]);
        Assert.Equal(60 - 12, fewUsed[^1]);
        Assert.Equal(new[] { 67 }, PianoRollScale.Rows(PianoRollScale.FoldUsed, null, new[] { new PianoRollNote(0, 2, 67, 100) }).Where(p => p == 67).ToArray());
        Assert.True(PianoRollScale.Rows(PianoRollScale.FoldUsed, null, new[] { new PianoRollNote(0, 2, 67, 100) }).Count > 12);
        var many = new[] { new PianoRollNote(0, 1, 60, 80), new PianoRollNote(1, 1, 64, 80), new PianoRollNote(2, 1, 67, 80) };
        Assert.Equal(new[] { 67, 64, 60 }, PianoRollScale.Rows(PianoRollScale.FoldUsed, null, many).ToArray());
        Assert.Equal(128, PianoRollScale.Rows(PianoRollScale.FoldUsed, null, new PianoRollNote[0]).Count);   // no notes: every row
    }

    [Fact]
    public void NoMenuRowIsLeftGreyedForALaterVersion()
    {
        var rows = PianoRollMenus.Rows(new PianoRollMenuState(OnNotes: true, CanPaste: true, InsideLoop: true));
        var flat = rows.SelectMany(r => r.Children ?? new List<MenuSpec> { r }).Where(r => !r.IsSeparator).ToList();
        Assert.All(flat, r => Assert.True(r.Enabled, r.Header));
        Assert.Contains(flat, r => r.Id == PianoRollMenus.Legato);
        Assert.Contains(flat, r => r.Id == PianoRollMenus.Glue);
        Assert.Contains(flat, r => r.Id == PianoRollMenus.Randomise);
        Assert.Contains(flat, r => r.Id == PianoRollMenus.Colours && r.Arg == "Velocity");
        var ruler = PianoRollMenus.RulerRows(new PianoRollMenuState(false, false, InsideLoop: true, OnSection: true));
        Assert.Equal(new[] { PianoRollMenus.MovePlayhead, PianoRollMenus.LoopBar, PianoRollMenus.LoopSection, null, PianoRollMenus.ClearLoop }, ruler.Select(r => r.Id).ToArray());
    }
}
