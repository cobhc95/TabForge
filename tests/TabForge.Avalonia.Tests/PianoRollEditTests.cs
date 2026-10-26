using System.Collections.Generic;
using System.Linq;
using TabForge.Documents;
using TabForge.Menus;
using TabForge.Presenters;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the piano roll's pure note edits: bulk move, copy, transpose, resize, velocity (set, shift, scale, ramp), mute, delete, split, duplicate,
//   paste, destructive quantize with strength and starts / ends, the info line's text formats, and the right-click rows.
// Does not own: the commit and undo (PianoRollPresenterTests) or storage (PianoRollSourceTests).
// Tests: PianoRollEdits, PianoRollPositionText, PianoRollMenus.
public sealed class PianoRollEditTests
{
    private static readonly IReadOnlyList<PianoRollNote> Three = new[]
    {
        new PianoRollNote(0, 1, 60, 80),
        new PianoRollNote(1.1, 0.5, 64, 100),
        new PianoRollNote(2.2, 1, 67, 120),
    };

    private static PianoRollTimeMap Map() => PianoRollTimeMap.Written(SongDocument.BlankProject());

    private static double[] R(IEnumerable<double> values) => values.Select(v => System.Math.Round(v, 6)).ToArray();

    private static (double, double) R(PianoRollNote n) => (System.Math.Round(n.Start, 6), System.Math.Round(n.Length, 6));

    [Fact]
    public void MovingSeveralNotesMovesThemAllAndNeverBeforeTheStartOrOutOfRange()
    {
        var moved = PianoRollEdits.Move(Three, new[] { 0, 2 }, 0.5, 2);
        Assert.Equal(new[] { 0.5, 1.1, 2.7 }, R(moved.Notes.Select(n => n.Start)));
        Assert.Equal(new[] { 62, 64, 69 }, moved.Notes.Select(n => n.Pitch).ToArray());
        Assert.Equal(new[] { 0, 2 }, moved.Selected);
        var early = PianoRollEdits.Move(Three, new[] { 0, 1 }, -5, 100);
        Assert.Equal(0, early.Notes[0].Start, 6);   // the earliest stops at beat 0, the others keep their distance
        Assert.Equal(1.1, early.Notes[1].Start, 6);
        Assert.Equal(127, early.Notes[1].Pitch);   // the highest stops at 127
    }

    [Fact]
    public void CopyLeavesTheOriginalsAndSelectsTheCopies()
    {
        var copy = PianoRollEdits.Move(Three, new[] { 1 }, 4, 0, copy: true);
        Assert.Equal(4, copy.Notes.Count);
        Assert.Equal(Three[1], copy.Notes[1]);
        Assert.Equal(new[] { 3 }, copy.Selected);
        Assert.Equal(5.1, copy.Notes[3].Start, 6);
    }

    [Fact]
    public void ResizeVelocityAndMuteActOnEverySelectedNote()
    {
        var all = new[] { 0, 1, 2 };
        Assert.Equal(new[] { 1.5, 1.0, 1.5 }, R(PianoRollEdits.Resize(Three, all, 0.5).Notes.Select(n => n.Length)));
        var fromStart = PianoRollEdits.Resize(Three, new[] { 2 }, 0.5, start: true).Notes[2];
        Assert.Equal((2.7, 0.5), R(fromStart));
        Assert.All(PianoRollEdits.Resize(Three, all, -9).Notes, n => Assert.Equal(PianoRollEdits.MinLength, n.Length, 9));
        Assert.Equal(new[] { 50, 50, 50 }, PianoRollEdits.SetVelocity(Three, all, 50).Notes.Select(n => n.Velocity).ToArray());
        Assert.Equal(new[] { 90, 110, 127 }, PianoRollEdits.ShiftVelocity(Three, all, 10).Notes.Select(n => n.Velocity).ToArray());
        Assert.Equal(new[] { 40, 50, 60 }, PianoRollEdits.ScaleVelocity(Three, all, 50).Notes.Select(n => n.Velocity).ToArray());
        Assert.Equal(new[] { 20, 70, 120 }, PianoRollEdits.Ramp(Three, all, 20, 120).Notes.Select(n => n.Velocity).ToArray());
        var muted = PianoRollEdits.ToggleMute(Three, new[] { 0, 1 });
        Assert.Equal(new[] { true, true, false }, muted.Notes.Select(n => n.Muted).ToArray());
        Assert.All(PianoRollEdits.ToggleMute(muted.Notes, new[] { 0, 1 }).Notes, n => Assert.False(n.Muted));
    }

    [Fact]
    public void SplitDeleteDuplicateAndPaste()
    {
        var split = PianoRollEdits.Split(Three, 0, 0.25);
        Assert.Equal(4, split.Notes.Count);
        Assert.Equal((0.0, 0.25), R(split.Notes[0]));
        Assert.Equal((0.25, 0.75), R(split.Notes[3]));
        Assert.Equal(3, PianoRollEdits.Split(Three, 0, 1.5).Notes.Count);   // a cut outside the note does nothing
        Assert.Equal(new[] { 64 }, PianoRollEdits.Delete(Three, new[] { 0, 2 }).Notes.Select(n => n.Pitch).ToArray());
        var duplicate = PianoRollEdits.Duplicate(Three, new[] { 0, 1 });
        Assert.Equal(new[] { 1.6, 2.7 }, R(duplicate.Selected.Select(i => duplicate.Notes[i].Start)));   // right after the selection's span (1.6 beats)
        var clip = PianoRollEdits.Copy(Three, new[] { 1, 2 });
        Assert.Equal(new[] { 0.0, 1.1 }, R(clip.Select(n => n.Start)));
        var pasted = PianoRollEdits.Paste(Three, clip, 8);
        Assert.Equal(new[] { 8.0, 9.1 }, R(pasted.Selected.Select(i => pasted.Notes[i].Start)));
    }

    [Fact]
    public void QuantizeMovesStartsByTheStrengthAndKeepsLengths()
    {
        var map = Map();
        var notes = new[] { new PianoRollNote(1.2, 0.9, 60, 90), new PianoRollNote(2.9, 0.4, 62, 90) };
        var full = PianoRollEdits.Quantize(notes, new int[0], map, "1/4", 1, starts: true, ends: false);
        Assert.Equal(new[] { 1.0, 3.0 }, R(full.Notes.Select(n => n.Start)));
        Assert.Equal(new[] { 0.9, 0.4 }, R(full.Notes.Select(n => n.Length)));
        Assert.Empty(full.Selected);   // no selection: every note, nothing selected after
        var half = PianoRollEdits.Quantize(notes, new[] { 0 }, map, "1/4", 0.5, starts: true, ends: false);
        Assert.Equal(1.1, half.Notes[0].Start, 6);
        Assert.Equal(2.9, half.Notes[1].Start, 6);   // not selected: untouched
        var ends = PianoRollEdits.Quantize(notes, new[] { 0 }, map, "1/4", 1, starts: true, ends: true);
        Assert.Equal((1.0, 1.0), R(ends.Notes[0]));   // start to 1, end 1.9 to 2
        var triplet = PianoRollEdits.Quantize(new[] { new PianoRollNote(0.3, 0.2, 60, 90) }, new int[0], map, "1/8T", 1, true, false);
        Assert.Equal(1.0 / 3, triplet.Notes[0].Start, 6);
    }

    [Fact]
    public void TheInfoLineReadsAndWritesBarBeatSixteenthTick()
    {
        var map = Map();
        Assert.Equal("2.1.1.0", PianoRollPositionText.Position(map, 4));
        Assert.Equal("1.2.3.60", PianoRollPositionText.Position(map, 1.625));
        Assert.Equal(1.625, PianoRollPositionText.ParsePosition(map, "1.2.3.60")!.Value, 6);
        Assert.Equal(4, PianoRollPositionText.ParsePosition(map, "2")!.Value, 6);
        Assert.Null(PianoRollPositionText.ParsePosition(map, "99.1"));
        Assert.Equal("1.2.0", PianoRollPositionText.Length(1.5));
        Assert.Equal(1.5, PianoRollPositionText.ParseLength("1.2")!.Value, 6);
        Assert.Equal("C4", PianoRollPositionText.PitchName(60));
        Assert.Equal(61, PianoRollPositionText.ParsePitch("C#4"));
        Assert.Equal(58, PianoRollPositionText.ParsePitch("Bb3"));
        Assert.Equal(40, PianoRollPositionText.ParsePitch("40"));
    }

    [Fact]
    public void TheMenusOfferTheSwitchBackToTheScoreAndClearLoopOnlyInsideTheLoop()
    {
        var note = PianoRollMenus.Rows(new PianoRollMenuState(OnNotes: true, CanPaste: false, InsideLoop: false));
        Assert.Equal(PianoRollMenus.ShowInScoreId, note[^1].Id);
        Assert.Equal(PianoRollMenus.ShowInScore, note[^1].Header);
        Assert.DoesNotContain(note, row => row.Id == PianoRollMenus.ClearLoop);
        Assert.False(note.Single(row => row.Id == PianoRollMenus.Paste).Enabled);
        Assert.Contains(note, row => row.Header == "Transpose" && row.Children!.Any(c => c.Id == PianoRollMenus.OctaveUp));
        Assert.Contains(note, row => row.Header == "Velocity" && row.Children!.Any(c => c.Id == PianoRollMenus.VelocityRamp));
        var inside = PianoRollMenus.Rows(new PianoRollMenuState(OnNotes: false, CanPaste: true, InsideLoop: true));
        Assert.Equal(PianoRollMenus.ClearLoop, inside[0].Id);
        Assert.Equal(PianoRollMenus.ShowInScoreId, inside[^1].Id);
        var score = ScoreMenus.WithPianoRollRow(new[] { new MenuSpec { Id = "x" } }).ToList();
        Assert.Equal(ScoreMenus.ShowInPianoRollId, score[^1].Id);
        Assert.Equal("Show in piano roll (this track)", score[^1].Header);
    }
}
