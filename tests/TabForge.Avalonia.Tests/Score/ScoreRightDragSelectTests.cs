using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using TabForge.Avalonia.Platform;
using TabForge.Documents;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the score's right-button drag: a deliberate sideways drag selects the same range a left drag does and sets no loop (SelectionReleased stays
//   quiet), a right click with a small or mostly vertical move still opens the context menu and changes no selection, the release of a drag opens
//   no menu, Escape mid-drag puts the earlier selection back, and the left drag still raises the loop event.
// Does not own: the loop link (SelectionLoopLinkTests), the menu rows (ScoreContextMenuTests), the left drag's selection rules (ScorePreviewEditorTests).
// Tests: ScorePreviewInput right drag, ScorePreviewView pointer and context menu.
public class ScoreRightDragSelectTests
{
    private sealed class Rig
    {
        public ScorePreviewView Score = new();
        public Window Window;
        public int Loops, Menus;
        public Point From, To;

        public Rig()
        {
            Score.Show(new SongDocument(() => null) { Project = FullDemoSongFactory.Create() });
            Window = new Window { Width = 1300, Height = 900, Content = Score };
            Window.Show();
            Score.SelectionReleased += () => Loops++;
            Score.AddHandler(Control.ContextRequestedEvent, (object? _, ContextRequestedEventArgs _) => Menus++, RoutingStrategies.Bubble, handledEventsToo: true);
            (From, To) = ThreeBars();
        }

        /// <summary>Window points on one row, the first in some bar and the second two bars on.</summary>
        private (Point, Point) ThreeBars()
        {
            for (var y = 40.0; y < 800; y += 8)
            {
                var firstX = new System.Collections.Generic.Dictionary<int, double>();
                for (var x = 120.0; x < 1200; x += 4)
                {
                    var (bar, _, _) = Score.Probe.HitTest(x, y);
                    if (bar < 0) continue;
                    firstX.TryAdd(bar, x);
                    if (firstX.TryGetValue(bar - 2, out var x0) && bar - 2 >= 0)
                        return (Score.TranslatePoint(new Point(x0, y), Window)!.Value, Score.TranslatePoint(new Point(x, y), Window)!.Value);
                }
            }
            throw new InvalidOperationException("No row of the demo score shows three bars.");
        }

        public void Drag(MouseButton button, Point from, Point to, RawInputModifiers held)
        {
            Window.MouseDown(from, button);
            Window.MouseMove(new Point((from.X + to.X) / 2, (from.Y + to.Y) / 2), held);
            Window.MouseMove(to, held);
            Window.MouseUp(to, button);
        }
    }

    [AvaloniaFact]
    public void RightDragAcrossThreeBarsSelectsTheLeftDragsRangeAndSetsNoLoop()
    {
        var left = new Rig();
        left.Drag(MouseButton.Left, left.From, left.To, RawInputModifiers.LeftMouseButton);
        var expected = left.Score.Editor.SelectionRange();
        Assert.True(left.Score.Editor.HasSelection);
        Assert.Equal(1, left.Loops);   // the left drag still loops
        left.Window.Close();

        var right = new Rig();
        right.Drag(MouseButton.Right, right.From, right.To, RawInputModifiers.RightMouseButton);

        Assert.True(right.Score.Editor.HasSelection);
        Assert.Equal(expected, right.Score.Editor.SelectionRange());
        Assert.Equal(2, expected.m2 - expected.m1);
        Assert.Equal(0, right.Loops);
        Assert.Equal(0, right.Menus);
        right.Window.Close();
    }

    [AvaloniaFact]
    public void RightClickWithFivePixelJitterOpensTheMenuAndChangesNoSelection()
    {
        var rig = new Rig();
        var before = rig.Score.Editor.SnapshotSelection();

        rig.Drag(MouseButton.Right, rig.From, new Point(rig.From.X + 5, rig.From.Y + 1), RawInputModifiers.RightMouseButton);

        Assert.False(rig.Score.Editor.HasSelection);
        Assert.Equal(0, rig.Loops);
        Assert.Equal(1, rig.Menus);
        Assert.Equal(before.Range, rig.Score.Editor.SnapshotSelection().Range);
        rig.Window.Close();
    }

    [AvaloniaFact]
    public void RightMovementThatIsMostlyVerticalStillOpensTheMenu()
    {
        var rig = new Rig();

        rig.Drag(MouseButton.Right, rig.From, new Point(rig.From.X + 30, rig.From.Y + 40), RawInputModifiers.RightMouseButton);

        Assert.False(rig.Score.Editor.HasSelection);
        Assert.Equal(0, rig.Loops);
        Assert.Equal(1, rig.Menus);
        rig.Window.Close();
    }

    [AvaloniaFact]
    public void EscapeMidRightDragRestoresTheEarlierSelectionAndOpensNoMenu()
    {
        var rig = new Rig();
        rig.Score.Editor.SelectRange(0, 0, 0, -1);   // an earlier selection: bar 1
        var before = rig.Score.Editor.SnapshotSelection();
        Assert.True(rig.Score.Editor.HasSelection);

        rig.Window.MouseDown(rig.From, MouseButton.Right);
        rig.Window.MouseMove(new Point(rig.From.X + 20, rig.From.Y), RawInputModifiers.RightMouseButton);
        rig.Window.MouseMove(rig.To, RawInputModifiers.RightMouseButton);
        Assert.NotEqual(before.Range, rig.Score.Editor.SnapshotSelection().Range);   // the drag moved the range
        rig.Window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Assert.Equal(before, rig.Score.Editor.SnapshotSelection());
        rig.Window.MouseMove(new Point(rig.To.X + 30, rig.To.Y), RawInputModifiers.RightMouseButton);   // the cancelled hold selects nothing more
        Assert.Equal(before, rig.Score.Editor.SnapshotSelection());
        rig.Window.MouseUp(rig.To, MouseButton.Right);

        Assert.Equal(before, rig.Score.Editor.SnapshotSelection());
        Assert.Equal(0, rig.Loops);
        Assert.Equal(0, rig.Menus);
        rig.Window.Close();
    }

    [AvaloniaFact]
    public void AfterARightDragAPlainRightClickStillOpensTheMenu()
    {
        var rig = new Rig();
        rig.Drag(MouseButton.Right, rig.From, rig.To, RawInputModifiers.RightMouseButton);

        rig.Window.MouseDown(rig.From, MouseButton.Right);
        rig.Window.MouseUp(rig.From, MouseButton.Right);

        Assert.Equal(1, rig.Menus);
        rig.Window.Close();
    }
}
