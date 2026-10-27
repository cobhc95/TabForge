using System;
using System.Linq;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using TabForge.Avalonia.Platform;
using TabForge.Documents;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the slider, clipboard and reorder actions on all selected tracks (see TrackMultiSelectTests.cs for the rig).
public sealed partial class TrackMultiSelectTests
{
    [AvaloniaFact]
    public void VolumeDragMovesAllSelectedByTheSameDeltaWithOneUndoStepAndOneLivePush()
    {
        var rig = Open();
        rig.Tracks[3].Volume = 125;   // clamps at the top while the others still move the full delta
        var before = rig.Tracks.Select(t => t.Volume).ToArray();
        Select(rig, 1);
        Select(rig, 3, RawInputModifiers.Shift);
        var pushes = 0;
        rig.View.Input.TrackLevelChanged += () => pushes++;
        var steps = rig.Song.Undo.UndoCount;
        var start = CellPoint(rig, 1, TrackRowCells.Cell.Volume, 0.2);
        var end = new Point(start.X + 20, start.Y);

        rig.Window.MouseDown(start, MouseButton.Left);
        rig.Window.MouseMove(end);
        rig.Window.MouseUp(end, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        var delta = rig.Tracks[1].Volume - before[1];
        Assert.NotEqual(0, delta);
        Assert.Equal(before[2] + delta, rig.Tracks[2].Volume);
        Assert.Equal(Math.Min(127, before[3] + delta), rig.Tracks[3].Volume);
        Assert.Equal(before[0], rig.Tracks[0].Volume);
        Assert.Equal(before[4], rig.Tracks[4].Volume);
        Assert.Equal(steps + 1, rig.Song.Undo.UndoCount);
        Assert.Equal(1, pushes);
        rig.Close();
    }

    [AvaloniaFact]
    public void ResetSetsEverySelectedSliderToItsDefaultInOneStep()
    {
        var rig = Open();
        Select(rig, 1);
        Select(rig, 2, RawInputModifiers.Shift);
        rig.Tracks[1].Pan = 100;
        rig.Tracks[2].Pan = 10;
        var pushes = 0;
        rig.View.Input.TrackLevelChanged += () => pushes++;
        var steps = rig.Song.Undo.UndoCount;

        rig.View.Input.ResetSlider(1, TrackRowCells.Cell.Pan);

        Assert.Equal(64, rig.Tracks[1].Pan);
        Assert.Equal(64, rig.Tracks[2].Pan);
        Assert.Equal(steps + 1, rig.Song.Undo.UndoCount);
        Assert.Equal(1, pushes);
        rig.Close();
    }

    [AvaloniaFact]
    public void CopyAndPasteTakeAllSelectedTracksAfterTheActiveOne()
    {
        var rig = Open();
        Select(rig, 1);
        Select(rig, 2, RawInputModifiers.Shift);
        var steps = rig.Song.Undo.UndoCount;
        Press(rig, Key.C, KeyModifiers.Control);
        Assert.True(rig.Clipboard.HasTrack);
        Assert.Equal(steps, rig.Song.Undo.UndoCount);

        Press(rig, Key.V, KeyModifiers.Control);
        WaitUntil(() => rig.Tracks.Count == 8);
        Assert.Equal(new[] { "T0", "T1", "T2", "T1 copy", "T2 copy", "T3" }, Names(rig).Take(6).ToArray());
        Assert.Equal(steps + 1, rig.Song.Undo.UndoCount);
        DocumentEdits.Undo(rig.Song);
        Assert.Equal(6, rig.Tracks.Count);
        rig.Close();
    }

    [AvaloniaFact]
    public void CutCopiesAllSelectedAfterOneQuestionAndIsOneUndoStep()
    {
        var rig = Open();
        Select(rig, 4);
        Select(rig, 5, RawInputModifiers.Shift);
        Press(rig, Key.X, KeyModifiers.Control);
        var prompt = Prompt(rig)!;
        Assert.False(rig.Clipboard.HasTrack);   // nothing is copied until OK
        Answer(prompt, "Delete");

        Assert.Equal(4, rig.Tracks.Count);
        Assert.Equal(2, rig.Clipboard.TryGetTracks().Count);
        Assert.NotNull(DocumentEdits.Undo(rig.Song));
        Assert.Equal(6, rig.Tracks.Count);
        rig.Close();
    }

    [AvaloniaFact]
    public void DraggingASelectedTrackMovesTheWholeGroupWithOneUndoStep()
    {
        var rig = Open();
        Select(rig, 1);
        Select(rig, 2, RawInputModifiers.Shift);
        var steps = rig.Song.Undo.UndoCount;
        var from = NamePoint(rig, 1);
        var to = NamePoint(rig, 4);

        rig.Window.MouseDown(from, MouseButton.Left);
        rig.Window.MouseMove(new Point(from.X, from.Y + 12));
        rig.Window.MouseMove(to);
        rig.Window.MouseUp(to, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new[] { "T0", "T3", "T4", "T5", "T1", "T2" }, Names(rig));
        Assert.Equal(steps + 1, rig.Song.Undo.UndoCount);
        DocumentEdits.Undo(rig.Song);
        Assert.Equal(new[] { "T0", "T1", "T2", "T3", "T4", "T5" }, Names(rig));
        rig.Close();
    }
}
