using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Menus;
using TabForge.Models;
using TabForge.Presenters;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the Reaper-style piano roll features of 26 Oct in the real window: the velocity lane sweep (paint; Shift ramps; only the selection
//   when there is one) as one step, the note's top-edge value drag, the key strip's note names (saved per track, drum names on a drum track), the
//   hover readout ("12.3.74 | F#3 54"), the "By lane" colour mode and the heat ramp tokens.
// Does not own: the earlier gestures (PianoRollGestureTests, PianoRollRealPathTests).
// Tests: PianoRollSurface (lanes, names, readout), PianoRollPositionText, PianoRollMenus.
public sealed class PianoRollReaperTests
{
    private static SongDocument Three()
    {
        var document = PianoRollSongs.Midi();
        document.Project.Tracks[0].AudioClips[0].Notes!.Add(new ClipNote(1.5, 0.5, 67, 110));
        return document;
    }

    private static (MainWindow Window, PianoRollView Roll) Open(SongDocument song)
    {
        var window = new MainWindow(first: song) { Width = 1600, Height = 1000 };
        window.Show();
        window.UpdateLayout();
        return (window, window.GetLogicalDescendants().OfType<PianoRollView>().Single());
    }

    private static Point W(MainWindow window, PianoRollSurface surface, double x, double y) => surface.TranslatePoint(new Point(x, y), window)!.Value;

    private static void Sweep(MainWindow window, Point from, Point to, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.MouseDown(from, MouseButton.Left, modifiers);
        for (var i = 1; i <= 12; i++) window.MouseMove(from + (to - from) * (i / 12.0), modifiers);
        window.MouseUp(to, MouseButton.Left, modifiers);
        Dispatcher.UIThread.RunJobs();
    }

    private static int[] Velocities(SongDocument song) => song.Project.Tracks[0].AudioClips[0].Notes!.Select(n => n.Velocity).ToArray();

    private static void Close(MainWindow window, SongDocument song)
    {
        song.MarkClean();
        window.Close();
    }

    [AvaloniaFact]
    public void ASweepPaintsEveryBarItPassesAsOneStepAndShiftDrawsARamp()
    {
        var song = Three();
        var (window, roll) = Open(song);
        var s = roll.Surface;
        Assert.True(s.LaneShown);
        var steps = song.Undo.UndoCount;
        Sweep(window, W(window, s, s.XOf(0.5), s.VelocityY(30)), W(window, s, s.XOf(3.5), s.VelocityY(30)));
        Assert.Equal(steps + 1, song.Undo.UndoCount);
        Assert.All(Velocities(song), v => Assert.InRange(v, 28, 32));

        var x0 = s.XOf(roll.Presenter.Notes[0].Start) + 1;
        var x2 = s.XOf(roll.Presenter.Notes[2].Start) + 1;
        Sweep(window, W(window, s, x0, s.VelocityY(20)), W(window, s, x2, s.VelocityY(120)), RawInputModifiers.Shift);
        var ramp = Velocities(song);
        Assert.InRange(ramp[0], 17, 23);
        Assert.InRange(ramp[1], 66, 74);   // half way along the straight line
        Assert.InRange(ramp[2], 117, 123);
        Close(window, song);
    }

    [AvaloniaFact]
    public void WithASelectionTheSweepShapesOnlyTheSelectedBars()
    {
        var song = Three();
        var (window, roll) = Open(song);
        var s = roll.Surface;
        roll.Presenter.Select(new[] { 0, 2 });
        Sweep(window, W(window, s, s.XOf(0.5), s.VelocityY(40)), W(window, s, s.XOf(3.5), s.VelocityY(40)));
        var v = Velocities(song);
        Assert.InRange(v[0], 38, 42);
        Assert.Equal(100, v[1]);   // not selected: untouched
        Assert.InRange(v[2], 38, 42);
        Close(window, song);
    }

    [AvaloniaFact]
    public void TheTopEdgeOfANoteDragsItsVelocityAndNeverMovesIt()
    {
        var song = Three();
        var (window, roll) = Open(song);
        var s = roll.Surface;
        var r = s.NoteRect(roll.Presenter.Notes[1]);
        Assert.Equal(PianoRollSurface.NotePart.Top, s.HitNote(new Point(r.Center.X, r.Top + 1)).Part);
        Sweep(window, W(window, s, r.Center.X, r.Top + 1), W(window, s, r.Center.X, r.Top - 19));
        var note = song.Project.Tracks[0].AudioClips[0].Notes![1];
        Assert.Equal(110, note.Velocity);   // 100 up by 20 px / 2
        Assert.Equal((1.0, 64), (note.StartSec, note.Pitch));
        Close(window, song);
    }

    [AvaloniaFact]
    public void TheKeyStripShowsNoteNamesSavedPerTrackAndDrumNamesOnADrumTrack()
    {
        var song = Three();
        var (window, roll) = Open(song);
        var steps = song.Undo.UndoCount;
        var keys = roll.Surface.GridLeft;
        roll.SetNoteNames(true);
        Assert.True(song.Project.Tracks[0].PianoRollNoteNames);
        Assert.Equal(steps + 1, song.Undo.UndoCount);
        Assert.True(roll.Surface.NoteNames);
        Assert.Equal(keys, roll.Surface.GridLeft);   // one strip width in both modes: the grid never shifts
        Assert.Equal(new[] { "Piano keys", "Note names" }, PianoRollMenus.KeyStripRows(true).Select(r => r.Header).ToArray());
        Assert.True(PianoRollMenus.KeyStripRows(true)[1].Checked);
        roll.SetNoteNames(false);
        Assert.False(song.Project.Tracks[0].PianoRollNoteNames);
        Assert.Equal("Acoustic Snare", PianoRollPositionText.KeyName(38, drums: true));
        Assert.Equal("D2", PianoRollPositionText.KeyName(38, drums: false));
        Assert.Equal("E7", PianoRollPositionText.KeyName(100, drums: true));   // no GM drum there: the note name
        Close(window, song);
    }

    [Fact]
    public void TheReadoutShowsPositionNoteAndVelocityAndTheLaneColourModeIsOffered()
    {
        var map = PianoRollTimeMap.Written(SongDocument.BlankProject());
        Assert.Equal("1.2.74", PianoRollPositionText.Short(map, 1.745));
        Assert.Equal("3.1.00", PianoRollPositionText.Short(map, 8));
        Assert.Equal("12.3.74 | F#3 54", PianoRollPositionText.Readout("12.3.74", 54, 54, false));
        Assert.Equal("C#5", PianoRollPositionText.Readout(73, null, false));
        Assert.Contains("Lane", PianoRollScale.ColourModes);
        var colours = PianoRollMenus.Rows(new PianoRollMenuState(false, false, false, ColourMode: "Lane")).Single(r => r.Header == "Note colours");
        Assert.True(colours.Children!.Single(c => c.Arg == "Lane").Checked);
    }
}
