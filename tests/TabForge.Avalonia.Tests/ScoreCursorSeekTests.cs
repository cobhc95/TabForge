using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Transport;
using TabForge.Documents;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the test that a plain click on the score places playback at the pressed bar and cell on the press (stopped: the start, Play starts there),
/// that a drag places it at the press and again on release only when the range start moved, and that a Shift-click places nothing.
/// Does not own: the session's seek (AvaloniaPlaybackSession), the engine (AvaloniaPlaybackSessionTests.FakeEngine), the document-pane hook.
/// Tests: ScorePreviewView.CursorPlaced, AvaloniaPlaybackSession.Seek.
/// </summary>
public class ScoreCursorSeekTests
{
    private const int TargetBar = 6;   // bar 7 in the demo score

    /// <summary>A point in view coordinates over a cell of <see cref="TargetBar"/>, and that cell's index.</summary>
    private static (Point Point, int Cell) PointInTargetBar(ScorePreviewView score)
    {
        for (var y = 40.0; y < 700; y += 8)
            for (var x = 120.0; x < 1200; x += 8)
            {
                var (bar, cell, _) = score.Probe.HitTest(x, y);
                if (bar == TargetBar) return (new Point(x, y), cell);
            }
        throw new System.InvalidOperationException("No cell of bar 7 on the demo score.");
    }

    private static Point InWindow(ScorePreviewView score, Window window, Point viewPoint) =>
        score.TranslatePoint(viewPoint, window)!.Value;

    /// <summary>A score showing the demo song, as the window shows the active tab (a new ScorePreviewView starts blank).</summary>
    private static ScorePreviewView DemoScore()
    {
        var score = new ScorePreviewView();
        score.Show(new SongDocument(() => null) { Project = FullDemoSongFactory.Create() });
        return score;
    }

    [AvaloniaFact]
    public void ClickSeeksPlaybackToTheCellAndPlayStartsThere()
    {
        var engine = new AvaloniaPlaybackSessionTests.FakeEngine();
        using var session = new AvaloniaPlaybackSession(engine, new AvaloniaPlaybackSessionTests.FakeUiThread(), () => new TabForge.Models.SongProject());
        var score = DemoScore();
        var seen = new System.Collections.Generic.List<(int Bar, int Cell)>();
        score.CursorPlaced += (bar, cell) => { seen.Add((bar, cell)); session.Seek(bar, cell); };
        var window = new Window { Width = 1300, Height = 900, Content = score };
        window.Show();
        var (point, cell) = PointInTargetBar(score);
        var at = InWindow(score, window, point);

        window.MouseDown(at, MouseButton.Left);
        // The seek happens on the press: the button is still down here.
        Assert.Equal([(TargetBar, cell)], seen);
        window.MouseUp(at, MouseButton.Left);

        Assert.Equal([(TargetBar, cell)], seen);
        // Stopped, a seek moves the start position (the playhead shows it) and Play begins there.
        Assert.Equal((TargetBar, cell), (session.Transport.PlayheadBar, session.Transport.PlayheadCell));
        session.PlayOrPause();
        Assert.True(engine.Started);
        window.Close();
    }

    [AvaloniaFact]
    public void DragPlacesTheCursorOnPressAndRaisesNothingMoreWhileItMovesRight()
    {
        var score = DemoScore();
        var seen = new System.Collections.Generic.List<(int Bar, int Cell)>();
        score.CursorPlaced += (bar, cell) => seen.Add((bar, cell));
        var window = new Window { Width = 1300, Height = 900, Content = score };
        window.Show();
        var (point, cell) = PointInTargetBar(score);
        var from = InWindow(score, window, point);
        var to = new Point(from.X + 20, from.Y);

        window.MouseDown(from, MouseButton.Left);
        Assert.Equal([(TargetBar, cell)], seen);
        window.MouseMove(to);
        window.MouseUp(to, MouseButton.Left);

        // The range start did not move, so release adds no second event.
        Assert.Equal([(TargetBar, cell)], seen);
        window.Close();
    }

    [AvaloniaFact]
    public void ShiftClickExtendsTheSelectionWithoutPlacingPlayback()
    {
        var score = DemoScore();
        var seen = new System.Collections.Generic.List<(int Bar, int Cell)>();
        score.CursorPlaced += (bar, cell) => seen.Add((bar, cell));
        var window = new Window { Width = 1300, Height = 900, Content = score };
        window.Show();
        var (point, _) = PointInTargetBar(score);
        var at = InWindow(score, window, point);

        window.MouseDown(at, MouseButton.Left, RawInputModifiers.Shift);
        window.MouseUp(at, MouseButton.Left, RawInputModifiers.Shift);

        Assert.Empty(seen);
        window.Close();
    }
}
