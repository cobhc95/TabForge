using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using TabForge.Avalonia.Platform;
using TabForge.Documents;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the selection gestures and the actions on all selected tracks (see TrackMultiSelectTests.cs for the rig).
public sealed partial class TrackMultiSelectTests
{
    [AvaloniaFact]
    public void ShiftClickSelectsEveryTrackFromTheAnchor()
    {
        var rig = Open();
        Select(rig, 1);
        Select(rig, 3, RawInputModifiers.Shift);
        Assert.Equal(new[] { "T1", "T2", "T3" }, Picked(rig));
        Select(rig, 0, RawInputModifiers.Shift);   // the anchor stays on track 1
        Assert.Equal(new[] { "T0", "T1" }, Picked(rig));
        Assert.Equal(0, rig.Song.TrackIndex);
        rig.Close();
    }

    [AvaloniaFact]
    public void CtrlClickAddsAndRemovesOneTrack()
    {
        var rig = Open();
        Select(rig, 1);
        Select(rig, 4, RawInputModifiers.Control);
        Assert.Equal(new[] { "T1", "T4" }, Picked(rig));
        Select(rig, 1, RawInputModifiers.Control);
        Assert.Equal(new[] { "T4" }, Picked(rig));
        rig.Close();
    }

    [AvaloniaFact]
    public void PlainClickAndEscapeClearTheSelection()
    {
        var rig = Open();
        Select(rig, 1);
        Select(rig, 3, RawInputModifiers.Shift);
        Select(rig, 5);
        Assert.Equal(new[] { "T5" }, Picked(rig));
        Select(rig, 2, RawInputModifiers.Shift);
        Press(rig, Key.Escape);
        Assert.Empty(Picked(rig));
        rig.Close();
    }

    [AvaloniaFact]
    public void DeleteAsksOnceForAllSelectedAndOneUndoRestoresThem()
    {
        var rig = Open();
        var before = rig.Tracks.Select(t => t.Id).ToArray();
        rig.Tracks[2].Mute = true;
        Select(rig, 2);
        Select(rig, 3, RawInputModifiers.Shift);
        Press(rig, Key.Delete);

        var prompt = Prompt(rig)!;
        Assert.Contains(prompt.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text!.StartsWith("Delete 2 tracks: T2, T3?"));
        Assert.Equal(before.Length, rig.Tracks.Count);
        Answer(prompt, "Delete");
        Assert.Equal(before.Length - 2, rig.Tracks.Count);
        Assert.Null(Prompt(rig));

        Assert.NotNull(DocumentEdits.Undo(rig.Song));
        Assert.Equal(before, rig.Tracks.Select(t => t.Id).ToArray());
        Assert.True(rig.Tracks[2].Mute);
        rig.Close();
    }

    [AvaloniaFact]
    public void MuteSoloAndArmOnASelectedTrackSetAllSelectedToItsNewState()
    {
        var rig = Open();
        Select(rig, 1);
        Select(rig, 2, RawInputModifiers.Shift);
        Click(rig, CellPoint(rig, 1, TrackRowCells.Cell.Mute));
        Assert.Equal(new[] { 1, 2 }, Where(rig, t => t.Mute));
        Click(rig, CellPoint(rig, 2, TrackRowCells.Cell.Mute));   // the clicked track was muted, so all go unmuted
        Assert.Empty(Where(rig, t => t.Mute));
        Click(rig, CellPoint(rig, 2, TrackRowCells.Cell.Solo));
        Assert.Equal(new[] { 1, 2 }, Where(rig, t => t.Solo));
        Click(rig, CellPoint(rig, 1, TrackRowCells.Cell.Arm));
        Assert.Equal(new[] { 1, 2 }, Where(rig, t => t.RecordArm));
        rig.Close();
    }

    [AvaloniaFact]
    public void MuteOnATrackOutsideTheSelectionChangesOnlyThatTrack()
    {
        var rig = Open();
        Select(rig, 1);
        Select(rig, 2, RawInputModifiers.Shift);
        Click(rig, CellPoint(rig, 4, TrackRowCells.Cell.Mute));
        Assert.Equal(new[] { 4 }, Where(rig, t => t.Mute));
        rig.Close();
    }

    [AvaloniaFact]
    public void MuteOnAllSelectedIsOneUndoStep()
    {
        var rig = Open();
        Select(rig, 1);
        Select(rig, 3, RawInputModifiers.Shift);
        var steps = rig.Song.Undo.UndoCount;
        Click(rig, CellPoint(rig, 2, TrackRowCells.Cell.Mute));
        Assert.Equal(steps + 1, rig.Song.Undo.UndoCount);
        DocumentEdits.Undo(rig.Song);
        Assert.Empty(Where(rig, t => t.Mute));
        rig.Close();
    }

    [AvaloniaFact]
    public void SingleTrackBehaviourIsUnchanged()
    {
        var rig = Open();
        Select(rig, 2);
        Click(rig, CellPoint(rig, 2, TrackRowCells.Cell.Mute));
        Assert.Equal(new[] { 2 }, Where(rig, t => t.Mute));
        Press(rig, Key.Delete);
        var prompt = Prompt(rig)!;
        Assert.Contains(prompt.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text!.StartsWith("Delete the track 'T2'?"));
        Answer(prompt, "Delete");
        Assert.Equal(5, rig.Tracks.Count);
        rig.Close();
    }
}
