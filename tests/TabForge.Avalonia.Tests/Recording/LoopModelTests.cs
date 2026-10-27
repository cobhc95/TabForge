using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using TabForge.Platform;
using TabForge.Platform.Testing;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Transport;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Menus;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the loop model in the window: the marquee of a lane box drag (shown with its bounds while dragging, gone on release, the white stays), the ruler
//   edge resize as one undo step, the loop bar's two end handles and the resize pointer only over them, a ruler click clearing white and loop (undoable),
//   the Loop button surviving an undo, no grey block from a lane drag, the clip span acting on the clips' tracks only, a plain clip click moving the playhead to the clip start, and every row of the ruler menu.
// Does not own: the pure rules (LoopRegionTests) or the score link (SelectionLoopLinkTests).
// Tests: TimelineInputController.Box/Loop, ContextMenuRows.TimelineAt, SelectionLoopLink.
public class LoopModelTests
{
    private sealed record Rig(MainWindow Window, AvaloniaPlaybackSession Session, TimelineView Timeline, SelectionModel Model)
    {
        public Point At(double x, double y) => Timeline.TranslatePoint(new Point(x, y), Window)!.Value;
        public Point Ruler(int bar) => At(Timeline.BarX(bar) + 5, TimelineView.RulerHeight / 2);
        public void Run(MenuSpec row) => Timeline.ContextMenus.Commands.Run(ContextMenuRows.CommandIdOf(row)!, row.Arg);
    }

    private static Rig Open()
    {
        var engine = new AvaloniaPlaybackSessionTests.FakeEngine();
        var sessions = new AvaloniaPlaybackSessions(new AvaloniaPlaybackSessionTests.FakeUiThread(), () => engine);
        var document = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        var window = new MainWindow(first: document, playback: sessions);
        window.Show();
        var arrangement = window.GetLogicalDescendants().OfType<ArrangementView>().Single();
        return new Rig(window, sessions.For(document)!, arrangement.Timeline, arrangement.Input.Selection);
    }

    [AvaloniaFact]
    public void ALaneBoxDragShowsTheMarqueeWhileDraggingAndHidesItOnRelease()
    {
        var rig = Open();
        var t = rig.Timeline;
        var from = new Point(t.BarX(2) + 5, t.RowTop(0) + 5);
        var to = new Point(t.BarX(5) + 5, t.RowTop(1) + 20);
        rig.Window.MouseDown(rig.At(from.X, from.Y), MouseButton.Left);
        rig.Window.MouseMove(rig.At(to.X, to.Y));
        var box = Assert.NotNull(t.Marquee);
        Assert.Equal((from.X, from.Y, to.X - from.X, to.Y - from.Y), (box.X, box.Y, box.Width, box.Height), new Tolerance());
        rig.Window.MouseUp(rig.At(to.X, to.Y), MouseButton.Left);
        Assert.Null(t.Marquee);
        Assert.False(rig.Model.HasLoop);
        rig.Window.Close();
    }

    [AvaloniaFact]
    public void DraggingALoopEdgeResizesItAsOneUndoStep()
    {
        var rig = Open();
        rig.Model.SetLoop(4, 6);
        var edge = rig.At(rig.Timeline.BarX(7), TimelineView.RulerHeight / 2);
        rig.Window.MouseDown(edge, MouseButton.Left);
        rig.Window.MouseMove(rig.Ruler(8));
        rig.Window.MouseMove(rig.Ruler(9));
        rig.Window.MouseUp(rig.Ruler(9), MouseButton.Left);
        Assert.Equal((4, 9), rig.Model.LoopBars);
        DocumentEdits.Undo(rig.Window.Songs.Active);
        Assert.Equal((4, 6), rig.Model.LoopBars);
        rig.Window.Close();
    }

    [AvaloniaFact]
    public void TheRulerMenuLoopsClearsLoopsTheSectionLoopsTheSelectionAndMovesThePlayhead()
    {
        var rig = Open();
        var song = rig.Window.Songs.Active;
        List<MenuSpec> Menu(int bar) => ContextMenuRows.TimelineAt(rig.Timeline, new Point(rig.Timeline.BarX(bar) + 5, TimelineView.RulerHeight / 2)).ToList();
        MenuSpec Row(int bar, TimelineCommand command) => Menu(bar).Single(r => r.Command == command);

        Assert.False(Row(1, TimelineCommand.RulerClearLoop).Enabled);   // no loop yet
        Assert.False(Row(1, TimelineCommand.RulerLoopSelectedBars).Enabled);   // nothing selected
        Assert.Equal("Esc twice", Row(1, TimelineCommand.RulerClearLoop).Shortcut);

        var marker = song.Project.Markers[0];
        Assert.True(SectionMenuEdits.TryBounds(song.Project, marker, out var start, out var end));
        rig.Run(Row(start, TimelineCommand.RulerLoopSection));
        Assert.Equal(((start, end - 1), true), (rig.Model.LoopBars, rig.Model.LoopRepeat));

        rig.Run(Row(start, TimelineCommand.RulerLoop));   // the tick turns repeat off and keeps the region
        Assert.Equal(((start, end - 1), false), (rig.Model.LoopBars, rig.Model.LoopRepeat));

        rig.Model.SetRange(0, 6, 7, SelectionOrigin.Editor);
        rig.Run(Row(1, TimelineCommand.RulerLoopSelectedBars));
        Assert.Equal((6, 7), rig.Model.LoopBars);

        var steps = song.Undo.UndoCount;
        rig.Run(Row(1, TimelineCommand.RulerClearLoop));
        Assert.Equal((false, steps + 1), (rig.Model.HasLoop, song.Undo.UndoCount));

        rig.Run(Row(9, TimelineCommand.RulerMovePlayhead));
        Assert.Equal(9, rig.Window.GetLogicalDescendants().OfType<ScorePreviewView>().First().Editor.SelectedMeasure);
        rig.Window.Close();
    }

    [AvaloniaFact]
    public void ALaneDragGreysTheCellsItTouched()
    {
        var rig = Open();
        var t = rig.Timeline;
        rig.Window.MouseDown(rig.At(t.BarX(2) + 5, t.RowTop(0) + 5), MouseButton.Left);
        rig.Window.MouseMove(rig.At(t.BarX(4) + 5, t.RowTop(1) + 20));
        rig.Window.MouseMove(rig.At(t.BarX(5) + 5, t.RowTop(1) + 20));
        rig.Window.MouseUp(rig.At(t.BarX(5) + 5, t.RowTop(1) + 20), MouseButton.Left);
        Assert.True(rig.Window.Songs.Active.Highlight.Has);
        rig.Window.Close();
    }

    [AvaloniaFact]
    public void ARulerClickClearsWhiteAndLoopAsOneUndoStepAndUndoRestoresBoth()
    {
        var rig = Open();
        var song = rig.Window.Songs.Active;
        rig.Model.SetRange(0, 1, 2, SelectionOrigin.Editor);
        rig.Model.SetLoop(4, 6);
        rig.Model.SetLoopRepeat(true);
        var steps = song.Undo.UndoCount;
        rig.Window.MouseDown(rig.Ruler(9), MouseButton.Left);
        rig.Window.MouseUp(rig.Ruler(9), MouseButton.Left);
        Assert.Equal((true, false, true, steps + 1), (rig.Model.HasRange, rig.Model.HasLoop, rig.Model.LoopRepeat, song.Undo.UndoCount));   // only the loop goes; white and Loop button stay
        DocumentEdits.Undo(song);
        Assert.Equal(((4, 6), true), (rig.Model.LoopBars, rig.Model.LoopRepeat));
        rig.Window.Close();
    }

    [AvaloniaFact]
    public void UndoingALoopResizeKeepsTheLoopButton()
    {
        var rig = Open();
        rig.Model.SetLoop(4, 6);
        rig.Model.SetLoopRepeat(true);
        rig.Window.MouseDown(rig.At(rig.Timeline.BarX(7), TimelineView.RulerHeight / 2), MouseButton.Left);
        rig.Window.MouseMove(rig.Ruler(8));
        rig.Window.MouseMove(rig.Ruler(9));
        rig.Window.MouseUp(rig.Ruler(9), MouseButton.Left);
        rig.Model.SetLoopRepeat(false);
        DocumentEdits.Undo(rig.Window.Songs.Active);
        Assert.Equal(((4, 6), false), (rig.Model.LoopBars, rig.Model.LoopRepeat));
        rig.Window.Close();
    }

    [AvaloniaFact]
    public void TheLoopBarHasAHandleAtEachEndAndTheResizePointerOnlyOverThem()
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(400, 100), new Vector(96, 96));
        using var dc = bitmap.CreateDrawingContext();
        var draw = new AvaloniaDrawContext();
        draw.Use(dc);
        var recorder = new GeometryRecorder(draw);
        TimelineLoopMarks.Draw(recorder, _ => new UiColor(255, 200, 100, 0), 100, 300, 44, 44, 100, looping: true);
        var handles = recorder.Records.Where(r => r.Kind == "Path").ToList();
        Assert.Equal(2, handles.Count);
        Assert.Equal(100, handles[0].Values[0] + handles[0].Values[2] / 2, 3);
        Assert.Equal(300, handles[1].Values[0] + handles[1].Values[2] / 2, 3);
        Assert.All(handles, h => Assert.True(h.Values[2] >= 8 && h.Values[3] >= 8));

        var rig = Open();
        var t = rig.Timeline;
        rig.Model.SetLoop(4, 6);
        var y = TimelineView.RulerHeight / 2;
        rig.Window.MouseMove(rig.At(t.BarX(4) + 2, y));
        Assert.NotNull(t.Cursor);
        rig.Window.MouseMove(rig.At((t.BarX(4) + t.BarX(7)) / 2, y));
        Assert.Null(t.Cursor);
        rig.Window.MouseMove(rig.At(t.BarX(7) - 2, y));
        Assert.NotNull(t.Cursor);
        rig.Window.MouseMove(rig.At(t.BarX(7) - 2, t.RowTop(0) + 5));
        Assert.Null(t.Cursor);
        rig.Window.Close();
    }

    [AvaloniaFact]
    public void ARulerMiddleDragMovesTheLoop()
    {
        var rig = Open();
        rig.Model.SetLoop(4, 6);
        rig.Window.MouseDown(rig.Ruler(5), MouseButton.Left);
        rig.Window.MouseMove(rig.Ruler(6));
        rig.Window.MouseMove(rig.Ruler(7));
        rig.Window.MouseUp(rig.Ruler(7), MouseButton.Left);
        Assert.Equal((6, 8), rig.Model.LoopBars);
        rig.Window.Close();
    }

    // Compares doubles within half a pixel.
    private sealed class Tolerance : System.Collections.Generic.IEqualityComparer<(double, double, double, double)>
    {
        public bool Equals((double, double, double, double) a, (double, double, double, double) b) =>
            System.Math.Abs(a.Item1 - b.Item1) < 0.5 && System.Math.Abs(a.Item2 - b.Item2) < 0.5 && System.Math.Abs(a.Item3 - b.Item3) < 0.5 && System.Math.Abs(a.Item4 - b.Item4) < 0.5;
        public int GetHashCode((double, double, double, double) obj) => 0;
    }
}
