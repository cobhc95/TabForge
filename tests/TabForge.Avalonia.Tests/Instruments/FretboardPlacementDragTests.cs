using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using TabForge.Avalonia.Platform;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Visualization;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: dragging the fretboard sideways with a real pointer: the board follows the pointer, the landing preview shows the place it would land in,
/// and the release saves that place as the fretboard's position. A press that does not drag still picks its fret (tested by FretboardInputAfterStatesTests).
/// Does not own: the place rule (FretboardGeometryRules.NearestPlacement), the board's drawing or the menu's Position rows.
/// Tests: TabForge.Avalonia.Tests.FretboardPlacementDragTests.
/// </summary>
public sealed class FretboardPlacementDragTests
{
    /// <summary>The fret the drag starts on: string 1 (index 0) at fret 3 of the demo guitar.</summary>
    private const int StartString = 0;
    private const int StartFret = 3;
    /// <summary>The pointer travels this far to the right, in steps, which is enough to land the board at the right.</summary>
    private const double TravelDips = 420;
    private const int TravelSteps = 12;

    [AvaloniaFact]
    public void DraggingTheBoardRightLandsItOnTheRightAndSavesIt()
    {
        var project = DemoSongFactory.Create();
        project.Tracks[0].Kind = TrackKind.Guitar;
        var score = new ScorePreviewView();
        score.Show(new SongDocument(() => null) { Project = project });
        var settings = new AppSettings();
        var saves = 0;
        var menu = new FretboardContextMenu(() => settings, () => saves++, () => { }, () => score.Editor.Track);
        var strip = new InstrumentPanelStrip(score, null, null, menu);
        var window = new Window { Width = 1100, Height = 400, Content = strip };
        window.Show();
        Settle(window);
        var panel = strip.Panel;
        var centre = panel.FretCentre(StartString, StartFret);
        var start = panel.TranslatePoint(new Point(centre.X, centre.Y), window)!.Value;

        window.MouseDown(start, MouseButton.Left);
        for (var step = 1; step <= TravelSteps; step++)
            window.MouseMove(new Point(start.X + TravelDips * step / TravelSteps, start.Y));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(FretboardHorizontalPosition.Right, panel.LandingPlace);

        window.MouseUp(new Point(start.X + TravelDips, start.Y), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Right", settings.Appearance.FretboardPosition);
        Assert.Equal(1, saves);
        Assert.Null(panel.LandingPlace);
        window.Close();
    }

    private static void Settle(Window window)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }
    }
}
